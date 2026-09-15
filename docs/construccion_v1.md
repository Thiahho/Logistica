# Documento de construcción — Sistema de gestión logística

**v1.1 · 29/08/2026**
Reemplaza y deja sin efecto `mvp_especificacion.md`.

---

## 0. Cómo se usa este documento

Hay tres documentos y ninguno repite al otro. Si algo está en dos lugares, uno de los dos está desactualizado y no vas a saber cuál.

| Documento | Responde | Se toca cuando |
|---|---|---|
| `acta_sistema.md` | Qué hace el sistema y por qué. Alcance, reglas de negocio, criterios de aceptación. | Cambia el negocio |
| `schema_v3.sql` | Cómo se guardan los datos. Tablas, triggers, RLS. | Cambia el modelo |
| **Este documento** | Cómo se construye. Estructura, pantallas, contratos, orden de trabajo. | Cambia la implementación |

Regla de corte: acá no se justifica ninguna decisión de negocio. Si aparece un "por qué" que no es técnico, va al acta.

---

## 1. Stack

**Actualizado tras H0/H1: se reemplazó Supabase por una API .NET propia.** Sin RLS ni `auth.uid()` disponibles fuera de Supabase, el control de acceso pasó a políticas de autorización de ASP.NET Core (`Program.cs`) y el actor de los triggers pasó a viajar como GUC de sesión (`app.usuario_id`, ver `Datos/EscrituraDominio.cs`) en vez de `auth.uid()`. La razón no es de negocio — va acá, no al acta.

| Capa | Elección | Nota |
|---|---|---|
| Framework | Next.js (App Router) + TypeScript | Un lenguaje para admin y PWA |
| Backend | ASP.NET Core 8 + Entity Framework Core | API propia; reemplaza Supabase Data API |
| Datos | Postgres (Docker local en desarrollo) | Mismo esquema de `schema_v3.sql`; triggers y funciones aplicados vía migración EF (`ReglasDeBaseDeDatos`), no desde el editor SQL de Supabase |
| Auth | JWT propio (access token de 15 min + refresh token opaco en cookie httpOnly), cuatro roles | Reemplaza Supabase Auth. Control de acceso por políticas de autorización (`BackOffice`, `Administracion`, `Operacion`, `Repartidor`, `Cliente`), no por RLS |
| UI | Tailwind + shadcn/ui (style `base-nova`, sobre Base UI, no Radix) | |
| Offline | Dexie (IndexedDB) + Service Worker + cola propia | Sección 7 — sin construir todavía |
| Geocodificación | Nominatim (OpenStreetMap), **solo desde el servidor**, cacheada permanente en `ubicaciones` | Reemplaza Google Geocoding API: sin key ni facturación, suficiente para desarrollo. `geo_proveedor` ya distingue el proveedor, así que cambiarlo después no toca el resto del sistema |
| Orden de paradas | Nearest-neighbor + 2-opt sobre haversine, en el navegador (`lib/dominio/ruteo.ts` + `geo.ts`) | Sin API de matriz. Reordenamiento manual con flechas subir/bajar + anclar, no drag & drop — ver §4.1 |
| Mapa | Leaflet + `react-leaflet`, tiles de OpenStreetMap | Sin key ni facturación, mismo criterio que Nominatim. Se monta sin SSR (`next/dynamic`, `ssr: false`): Leaflet toca `window` en el import |
| Recorrido por calles | OSRM, **solo desde el servidor**, cacheado en memoria (`RuteoService`) | El navegador nunca llama a un proveedor externo (regla §3.2). En desarrollo contra el demo público (`router.project-osrm.org`); su política de uso no admite producción — ahí exige contenedor propio |
| Hosting | A definir | Vercel servía para el plan original con Next.js full-stack; con backend .NET separado, el back-office necesita su propio hosting |

**Restricción de disciplina:** las 13 tablas del esquema son el techo hasta el tercer cliente. Cada tabla nueva necesita una línea en el acta antes que una línea en el SQL. **Excepción registrada:** `clientes_usuarios` (acta §11.1, changelog 3.3) sube el conteo a 14 — el login de cliente no puede compartir tabla con el personal interno, no es crecimiento por catálogo (P6).

**Techo en 17, alcanzado (E1, 11/09/2026):** reservado desde E0 (acta v4.0/v4.1, `cuentas_cobrar` saliendo de "fuera del modelo inicial" por el ciclo semanal de §9.3, no por catálogo — P6). E1 lo materializa con `facturas`, `factura_items` y `pagos` (migración `AgregarCuentaCorriente`) — sin una cuarta tabla de "imputaciones": el saldo y el FIFO de pagos parciales se derivan en lectura (`v_facturas_saldo`, ver §6), no se persisten. `factura_items` es la tabla puente que reemplaza a un `pedidos.factura_id`, porque una factura admite ítems que no son un pedido (ajustes de B16, notas de crédito). **`cuentas_cobrar` como tabla propia nunca se construyó** — el concepto de negocio del acta §4 se realiza con estas tres tablas juntas, no con una tabla que lleve ese nombre.

---

## 2. Estructura del repositorio

**Actualizada tras H0/H1.** El plan original era Next.js full-stack sobre Supabase; lo que se construyó separa un backend .NET del frontend. La sección 1 explica por qué.

```
/backend/Logistica            # ASP.NET Core 8 + EF Core
  /Auth
    TokenService.cs           # emite access (JWT) y refresh tokens
    AuthService.cs            # login, rotación de refresh, hashing de contraseñas
    CurrentUser.cs             # extensiones sobre ClaimsPrincipal
  /Controllers                # un controller por recurso (pedidos, rutas, clientes, usuarios,
                               # tarifas, zonas, localidades, ubicaciones, vehiculos, exportar,
                               # mis-paradas). El ABM del login de cliente (api/clientes/{id}/usuarios)
                               # vive anidado en ClientesController, no en UsuariosController — ese
                               # es solo personal interno (administracion, operacion, repartidor).
  /Datos
    LogisticaDbContext.cs
    EscrituraDominio.cs        # GuardarComoAsync: publica el actor como GUC antes de guardar,
                                # reemplaza a auth.uid() (no hay Supabase)
    DatosSemilla.cs            # seed de desarrollo (incluye una ruta de ejemplo cerrable)
    /Configuraciones           # IEntityTypeConfiguration por entidad
  /Dominio
    TransicionesPedido.cs      # máquina de estados (§5), fuente de verdad del backend
  /Entidades                  # una clase por tabla, en español, coincide con el esquema
  /Migrations                 # EF Core; ReglasDeBaseDeDatos aplica los triggers/funciones/vista
                               # de schema_v3.sql como SQL crudo (EF no los expresa)
  /Servicios                  # PrecioService, UbicacionService, GeocodificacionService (Nominatim),
                               # RuteoService (OSRM, cacheado en IMemoryCache), TarifaService — sin
                               # interfaces, inyectados por tipo concreto
  /Web
    ManejadorExcepciones.cs   # traduce triggers/checks de Postgres a ProblemDetails (regla 3)
  Program.cs                 # políticas de autorización, CORS, pipeline

/frontend                     # Next.js (App Router) + TypeScript
  /app
    /pedidos, /pedidos/nuevo, /pedidos/[id]        # administración + operación
    /rutas, /rutas/nueva, /rutas/[id]/armar        # administración + operación
    /rutas/[id]/cierre                             # solo administración
    /clientes, /clientes/[id], /clientes/nuevo     # solo administración — clientes/[id] incluye
                                                    # el ABM del login de ese cliente
    /usuarios, /usuarios/nuevo                     # solo administración — solo personal interno,
                                                    # el rol "cliente" no aparece acá
    /vehiculos, /vehiculos/nuevo, /vehiculos/[id]  # solo administración
    /tarifas, /exportar                            # solo administración
    /depositos                                      # solo administración — ABM del catálogo de
                                                    # depósitos (acta changelog 3.8)
    /hoy, /hoy/parada/[paradaId]                   # rol repartidor — mapa + cierre de parada,
                                                    # sin cola offline todavía
    /mis-envios                                    # rol cliente
    /login
  /components
    Shell.tsx                 # nav lateral filtrada por rol (back-office)
    CabeceraSesion.tsx
    ComboboxBusqueda.tsx       # combobox con búsqueda (Cliente/Localidad), filtra en memoria
    SugerenciaDestinatario.tsx # autocomplete de destinatario sobre el historial del cliente
    SelectorDireccion.tsx      # localidad+calle+geocodificación, extraído de pedidos/nuevo;
                               # reusado por el punto de partida de una ruta (armar ruta)
    /mapa                      # Mapa.tsx (Leaflet) + MapaDinamico.tsx (next/dynamic, ssr: false)
    /ui                       # shadcn CLI v4, style base-nova, sobre Base UI (no Radix) —
                               # incluye autocomplete.tsx escrito a mano (no está en el registry)
  /lib
    /auth                     # AuthProvider (fetchConSesion), RequireRole, types
    /dominio
      tipos.ts                 # tipos compartidos que reflejan los DTOs del backend
      estados.ts                # espejo de solo lectura de Dominio/TransicionesPedido.cs (backend)
      geo.ts                    # haversine
      ruteo.ts                  # nearest-neighbor + 2-opt, respeta paradas ancladas
    /captura                  # foto.ts (compresión a canvas), dispositivo.ts (deviceUuid por parada)
    /api
      errores.ts                # parseo de ProblemDetails
      recorrido.ts               # POST /api/recorrido, compartido entre armar y /hoy
  proxy.ts                    # gate de sesión por cookie (el rol se resuelve en RequireRole)
```

**Pendiente de construir:** la capa offline (`lib/offline/`: Dexie, cola, compresión de fotos — sección 7), el job de cierre de carga de las 18:00 (RF-08, deferido a propósito — ver §10).

**Convención de nombres:** el dominio va en español y coincide letra por letra con el esquema (`pedidos`, `ruta_paradas`, `pruebas_entrega`) tanto en SQL como en las entidades de EF. Lo técnico va en inglés (`useState`, `fetchAll`, `queue`). Traducir el dominio genera un diccionario mental que se rompe a las tres semanas.

---

## 3. Reglas de implementación no negociables

1. **El precio se calcula en el servidor.** `PrecioService.CotizarAsync` resuelve contra la función Postgres `tarifa_vigente`; `PedidosController` nunca acepta un `total` que vino del navegador, ni siquiera del operador. (El plan original hablaba de `fn_calcular_precio` + RPC `crear_pedido` de Supabase; la función de tarifa sigue en la base, el cálculo del total vive en el servicio .NET.)
2. **Ninguna credencial de proveedor externo va al bundle del navegador.** Hoy no aplica (Nominatim no requiere key); si se vuelve a Google Geocoding, la key va en configuración del servidor, nunca `NEXT_PUBLIC_`.
3. **Nada de lógica de negocio duplicada entre trigger y aplicación.** Si el trigger lo impide, la app muestra el error, no lo previene por su cuenta — `ManejadorExcepciones` traduce la excepción de Postgres a un 409/400 con el mensaje del trigger, en vez de reimplementar la validación en C#.
4. **El repartidor consulta `v_paradas_repartidor`, nunca `pedidos`.** Esa vista no tiene importes.
5. **Toda escritura desde la PWA pasa por la cola**, incluso con señal. Un solo camino de escritura, probado siempre. (Sin construir todavía — ver "pendiente" arriba.)
6. **Migraciones aplicadas no se editan.** Se agrega una nueva.
7. **Todo evento de dominio se escribe con `EscrituraDominio.GuardarComoAsync`**, nunca `SaveChangesAsync` directo — es lo único que publica el actor para que `fn_log_estado_pedido` lo registre (RNF-04). La única excepción documentada es la rotación de sesión en `AuthService`, que no es un evento de dominio.
8. **`[Authorize]` de clase y de acción se combinan con AND, no se reemplazan** (comportamiento de ASP.NET Core, no obvio). Un controller con `[Authorize(Policy="Administracion")]` a nivel de clase no se puede "abrir" a operación en una acción puntual con `[Authorize(Policy="BackOffice")]`: el resultado sigue exigiendo Administracion. La regla, ya aplicada en `ClientesController` y `UsuariosController`: si un controller tiene alguna acción que necesita un público más amplio que el resto, **no lleva `[Authorize]` de clase** — cada acción declara la suya. Si en cambio una acción necesita ser *más estricta* que el resto (caso de `RutasController.Cerrar`/`Resultado`, que agregan `Administracion` sobre una clase `BackOffice`), la combinación AND funciona como se espera y sí se puede dejar el atributo de clase.

---

## 4. Pantallas y a qué requisito responden

**Construidas hasta ahora: H0, H1 y H3** (administración y operación, escritorio). **H2 — la PWA del repartidor — sigue sin construir**; §8 explica por qué se invirtió el orden que este documento recomienda.

División de roles entre administración y operación (no estaba resuelta en la v1.0 de este documento): administración es superset de operación — ve y hace todo lo de operación, más clientes/tarifas/semáforos, usuarios, cierre económico de ruta y exportar. Operación no ve márgenes ni tarifas.

### 4.1 Administración y operación — escritorio

| Pantalla | Rol | Contenido | RF |
|---|---|---|---|
| Pedidos del día | admin + operación | Filtro fecha/estado. Marca roja en direcciones dudosas. Cada fila linkea al detalle. | 05, 10 |
| Alta de pedido | admin + operación | Cliente (combobox con búsqueda, sin colores ni tarifas), referencia, destinatario (autocompletado sobre el historial del propio cliente — elegir una sugerencia prellena teléfono, localidad y dirección, reusando la ubicación ya geocodificada), dirección de entrega (localidad como combobox con búsqueda, calle), bultos, fecha, urgente. Geocodifica con debounce al terminar de tipear la dirección o al cambiar la localidad (ya no solo `onBlur`), con el error visible si falla. Precio en vivo. | 01–07, 09 |
| Detalle de pedido | admin + operación | Datos, desglose de precio congelado, historial completo de estados (actor, motivo, hora — RF-28), botones de transición según `Dominio/TransicionesPedido.cs`. Mientras el pedido está en Borrador y su zona no tiene tarifa cargada (B9, Anexo I §4), admin ve un input de precio manual con quién y cuándo lo fijó. | 02, 28; criterios de aceptación 5 y 7 |
| Jornada (`/jornada`) | admin + operación | Monitor del día en curso (jornada §9.3 del acta — no confundir con el tablero B2, sigue fuera de alcance): contadores por estado de ruta y de parada para una fecha (default hoy, con selector), lista de rutas del día con avance, y un panel por repartidor con su ruta asignada, progreso y horarios. Sondea `GET /api/jornada/resumen` cada 20s (`lib/hooks/useSondeo.ts`, primer polling del proyecto). | — |
| Rutas | admin + operación | Listado por fecha. Cada fila linkea a *Ver* (`/rutas/{id}`, cualquier estado). Acción adicional por fila según estado: *Armar* (`planificada`), *Cerrar* (`en_curso`, solo admin), *Ver cierre* (`cerrada`, solo admin). | 10 |
| Detalle de ruta (`/rutas/[id]`) | admin + operación | Único punto de la app que muestra las paradas de una ruta en cualquier estado — hasta esta versión `armar/page.tsx` se bloqueaba apenas la ruta dejaba `planificada` y no había sustituto. Cabecera, mapa, lista de paradas con estado/horarios/pedidos (`GET /api/rutas/{id}/jornada`, mismo motor que `/api/mis-paradas/dia` — `Servicios/JornadaService.cs`). Sondea a 20s solo mientras `en_curso`; en `planificada`/`cerrada` carga una vez. Acciones (ruta no `cerrada`): reasignar repartidor y reordenar paradas pendientes — ver §4.3. | 10, 18 |
| Armar ruta | admin + operación | Candidatos confirmados de la fecha, agrupados por zona; consolidación automática por destino (RF-14); punto de partida de la jornada — combobox con el catálogo de depósitos (`GET /api/ubicaciones/depositos`) más "Otra dirección…" (mismo selector localidad+calle y misma geocodificación con debounce del alta de pedido); ya no hay opción "por default", el planificador siempre elige, `CerrarPlanificacion` lo exige (acta changelog 3.8); botón "sugerir orden" (`lib/dominio/ruteo.ts`); reordenamiento manual con flechas ↑/↓ y anclar/desanclar (RF-12/13, no drag & drop — decisión tomada, ver más abajo); contador contra `capacidad_paradas` (RF-16, aviso, nunca bloqueo); marca sobre los candidatos cuya zona no tiene tarifa (B9); seleccionar vehículo del catálogo y repartidor; cerrar planificación. | 11–17 |
| Cierre de ruta | solo admin | Km, combustible, peajes, otros costos, pago al repartidor → margen del día y conteo de entregas efectivas/fallidas/reprogramadas. Solo disponible una vez cerrada la planificación (`Estado == "en_curso"`). | 26, 27 |
| Clientes | solo admin | Alta, datos, tres colores (semáforos, RF-32/33), tarifas por zona. | 09, 31–33 |
| Tarifas | solo admin | Lista general de precios por zona (`tarifas.cliente_id` null), con el rango de km de cada zona editable al lado. Encima, "Localidades sin zona": pendientes de asignar, con zona sugerida por distancia al depósito (acta changelog 3.10). Aviso no bloqueante de tramos de km sin ninguna zona activa que los cubra (auditoría §7). | 09 |
| Usuarios | solo admin | Alta, edición, activo/inactivo, reset de contraseña. Sin esto no se puede dar de alta al repartidor. | RNF-08 |
| Depósitos | solo admin | ABM del catálogo de depósitos: alta (nombre + mismo selector localidad+calle del alta de pedido), cambio de nombre, baja. Ninguno es "el" default — renombrar o dar de baja no reescribe rutas ya cerradas — acta changelog 3.8. | RNF-08 |
| Vehículos | solo admin | Alta, edición, activo/inactivo. Ficha de flota: patente, descripción, marca/modelo/año, km, vencimientos de VTV y seguro, costo/km, capacidad de paradas (prellena la de la ruta al elegirlo en Armar ruta). | — |
| Exportar | solo admin | CSV de pedidos, rutas y resultados por rango. Reemplaza el módulo de reportes. | 30 |
| Mis envíos | rol cliente | Solo lectura de los propios pedidos, sin importes internos ni datos de otros clientes. | RNF-08 |
| Login | todos | Sesión de dos tokens: access en memoria, refresh en cookie httpOnly. Nunca pedir contraseña en la calle. | — |

**Decisión tomada sobre RF-12:** el reordenamiento usa flechas subir/bajar + anclar, no una lista arrastrable. El frontend no tenía ninguna dependencia de interacción compleja (todo hand-rolled con `fetch` + `useState`) y se mantuvo esa línea en vez de sumar `@dnd-kit` u otra librería de drag & drop. RF-12 no exige que sea arrastrable, solo que el reordenamiento manual esté siempre disponible.

**Simplificación consciente sobre RF-17:** "cierre de ruta como planificada, queda disponible para el dispositivo del repartidor" se implementó como la acción explícita `POST /api/rutas/{id}/cerrar-planificacion`, que transiciona `Ruta.Estado` de `planificada` a `en_curso` *y*, en la misma escritura, todos sus pedidos de `Confirmado` a `EnRuta` (condición ya documentada en §5: *"confirmado → en_ruta si existe fila en parada_pedidos"*). Esto adelanta a esta fase la transición que el acta asocia al retiro físico de las 07:30 (F3, sin construir) — no hay otro actor todavía que la dispare.

### 4.2 PWA — repartidor (sin construir, H2/F3)

| Pantalla | Contenido | RF |
|---|---|---|
| Hoy | Mapa (marcadores numerados por orden + recorrido OSRM) y, debajo, paradas en orden con contador hechas/pendientes. Indicador de cola pendiente: sin construir todavía. | 18, 25 |
| Parada (`/hoy/parada/[paradaId]`) | Dirección, referencia, destinatario, botón de llamar, bultos, observaciones. | 19, 22 |
| Cierre de parada | Entregado (foto obligatoria + receptor + posición) o fallido (motivo de lista cerrada). Escribe directo contra la API; guarda-local-y-sincroniza (cola offline) sigue sin construir. | 20, 21, 23, 24 |

`/hoy` migró de `GET /api/mis-paradas` (solo lectura, sin coordenadas) a `GET /api/mis-paradas/dia`, que trae lat/lng por parada, el origen de la ruta (`Origen`, antes `Deposito`: es el depósito o la dirección elegida al armar — acta changelog 3.6) y el bundle completo de la jornada en un solo request (RNF-07). `/hoy/parada/[paradaId]` y el cierre de parada (con foto, receptor y posición) ya están construidos y escriben directo contra la API — lo que sigue sin construir es la cola offline (`lib/offline/`: Dexie, compresión de fotos, sincronización diferida — sección 7) y la prueba de modo avión.

**Regla de diseño de la PWA (RNF-06):** ningún flujo pasa de tres toques con una mano. Si una pantalla necesita scroll para completarse, está mal. Botones de 48 px mínimo, contraste alto, legible al sol.

### 4.3 Monitor de jornada (`/jornada` y `/rutas/[id]`) — acta changelog 4.4

Entra por §9.3 del acta (ciclo diario), no por B2 (tablero de indicadores, sigue en E3). Detalle técnico:

- **`Servicios/JornadaService.cs`** extrae el núcleo de `MisParadasController.Dia` (agrupar `v_paradas_repartidor` por parada, contar completadas/fallidas, resolver origen, trazar recorrido) parametrizado por `rutaId` en vez de "la ruta en_curso del repartidor autenticado". `MisParadasController.Dia` pasa a delegar en él; el contrato de `GET /api/mis-paradas/dia` no cambia.
- **`GET /api/rutas/{id}/jornada`** (`RutasController`, BackOffice) expone ese mismo bundle para el back-office. Sin importes, igual que la vista de origen.
- **`GET /api/jornada/resumen?fecha=`** (`JornadaController`, nuevo, BackOffice) — contadores de rutas por estado y de paradas por estado para una fecha, más una fila por ruta y una por repartidor con su avance. Dos queries (rutas de la fecha + paradas agrupadas por ruta), sin N+1.
- **`PUT /api/rutas/{id}/repartidor`** — reasigna repartidor con la ruta `planificada` o `en_curso` (no `cerrada`). Valida rol `repartidor` activo y que no tenga ya otra ruta `en_curso` (si no, `MisParadasController.Dia` — `FirstOrDefault` — le escondería una de las dos). No toca vehículo ni precio.
- **`PUT /api/rutas/{id}/paradas/orden`** — reordena las paradas `pendiente` de una ruta no `cerrada` (RF-12). Las `completada`/`fallida` conservan su `Orden` — la secuencia recibida se vuelca sobre las posiciones que hoy ocupan las pendientes, no sobre 1..N. Valida conjunto exacto (sin faltantes ni repetidos). Transacción explícita, mismo motivo que `GuardarParadas`: el unique `(ruta_id, orden)` es DEFERRABLE.
- **`GET /api/rutas/{id}/paradas`** ahora incluye `id`, `orden`, `estado`, `llegadaEn`, `salidaEn` además de lo que ya traía — cambio aditivo, el armado no los usa.
- **`GET /api/rutas`** suma el filtro `repartidorId`.
- **Descartado a propósito:** cambiar vehículo de una ruta en curso (el tipo de vehículo ya fijó la tarifa congelada, acta 3.11) y marcar una parada a mano desde el back-office (saltea la prueba de entrega de RF-20, contra RF-28).
- **Sin auditoría propia:** reasignar y reordenar no quedan en ningún log — no hay tabla de eventos de ruta y crearla cruzaría el techo de tablas. Gap aceptado, no un olvido.
- **`lib/hooks/useSondeo.ts`** — primer polling del frontend. Pausa en pestaña oculta, refresca al volver al foco, `AbortController` por ciclo, y un error transitorio no borra los datos ya cargados.

---

## 5. Máquina de estados

```
borrador ──confirmar──► confirmado ──asignar a ruta──► en_ruta ──cancelar──► cancelado
   │                         │                            │
cancelado                cancelado              ┌─────────┴─────────┐
                                                ▼                   ▼
                                           entregado             fallido
                                                                    │
                                                     ┌──────────────┴───────────┐
                                                     ▼                          ▼
                                               reprogramado                 devuelto
                                                (hasta 3 veces)                 │
                                                     │                          │
                                            confirmado (nueva fecha)    pedido tipo 'retorno'
                                                     │
                                         (4ta reprogramación)
                                                     ▼
                                          pedido tipo 'reintento'
```

| Transición | Condición |
|---|---|
| `borrador → confirmado` | Precio calculado y congelado — **exclusivamente** en `RutasController.CerrarPlanificacion` (RF-17), nunca a mano por `POST /api/pedidos/{id}/estado`: cotizar exige conocer el tipo de vehículo real de la ruta (acta changelog 3.11), que no existe hasta ese momento. `TransicionesPedido.Permitidas[Borrador]` no incluye `Confirmado` a propósito — dejarlo confirmaría un pedido con precio en null. Dirección apta o el trigger la rechaza al rutear. Si la zona no tiene tarifa cargada (B9, Anexo I §4) y el pedido tampoco tiene `precio_manual`, cotizar rechaza y `CerrarPlanificacion` devuelve 400 sin tocar nada — admin tiene que fijar el precio manual primero. |
| `confirmado → en_ruta` | Existe fila en `parada_pedidos`. |
| `en_ruta → entregado` | Prueba de entrega sincronizada — acuña el `factura_items` de la entrega (E1, `CuentaCorrienteService.AgregarItemDePedido`, `MisParadasController.Cerrar`). |
| `en_ruta → fallido` | Motivo obligatorio de lista cerrada. |
| `en_ruta → cancelado` | **E1 (§10.2-I).** `CerrarPlanificacion` pasa cada pedido de Confirmado a EnRuta en la misma escritura, así que Confirmado nunca queda observable para cancelarlo ahí — EnRuta es el primer estado externamente alcanzable con precio ya congelado. Cancelar desde acá factura el 100% del precio (mismo mecanismo que una entrega). Cancelar desde Borrador sigue siendo gratis (no genera `factura_items`). |
| `fallido → reprogramado` | **3 gratis por pedido** (E1/D13, antes era 1) — sin cargo, misma fila, nueva fecha. Contador derivado de `pedido_eventos` (insert-only), no una columna. |
| `fallido → devuelto` | Genera pedido nuevo `tipo='retorno'` con `pedido_origen_id`. |
| 4ta reprogramación | **E1.** No reprograma: genera un pedido nuevo `tipo='reintento'` con `pedido_origen_id`, mismo destino (no invierte origen/destino como `retorno`), precio propio a cotizar en su propia ruta. El original queda en `Fallido`. `PedidosController.CambiarEstado` devuelve `200 + ReintentoCreado` en vez del `204` habitual en este único camino. |

**Actualizado tras H0/H1:** la fuente de verdad es el backend, no el frontend. `Dominio/TransicionesPedido.cs` (`Permitida(actual, nuevo)`, `MotivoObligatorio(nuevo)`) es quien decide, aplicada en `POST /api/pedidos/{id}/estado`; `lib/dominio/estados.ts` es un espejo de solo lectura que el frontend usa únicamente para decidir qué botones mostrar. Un intento inválido lo rechaza el servidor igual, con o sin ese espejo. La razón del cambio: sin Supabase no hay lógica de negocio confiable del lado del navegador (§1), el servidor tiene que ser quien imponga.

`fallido → devuelto` genera el pedido `tipo='retorno'` con precio propio cotizado en el momento. El reintento (`tipo='reintento'`) ya tiene camino completo en la UI desde E1 — antes de esto el check constraint lo preveía pero ningún controller lo usaba.

---

## 6. Cálculo de precio

```
precio_base       = pedidos.precio_manual ?? tarifa_vigente(cliente_id, zona_id, fecha, tipo_vehiculo)
recargo_urgencia  = urgente ? precio_base × factor_urgencia : 0
descuento_ruta    = pedido agregado a ruta existente ? precio_base × factor_desc : 0
peajes            = valor cargado
total             = precio_base + recargo_urgencia − descuento_ruta + peajes
```

Los dos factores son parámetros de configuración, no constantes en el código. Al confirmar, los cinco valores (`precio_base`, `recargo_urgencia`, `descuento_ruta`, `peajes`, `total`) quedan escritos en la fila y `precio_congelado_en` se sella. El trigger `trg_congelar_pedido` impide tocarlos después.

**`precio_manual` (B9, Anexo I §4, "+40 km → Cotización"):** sustituye solo el origen de `precio_base` cuando la zona no tiene tarifa cargada en ningún tipo de vehículo — la fórmula de arriba no cambia, sigue aplicando recargo/descuento/peajes encima igual. Se fija a mano desde el detalle del pedido (`PUT /api/pedidos/{id}/precio-manual`, solo Administracion, solo en Borrador) y queda protegido por `trg_congelar_pedido` una vez confirmado, igual que el resto del precio. `PrecioService.CotizarAsync` lo recibe como parámetro opcional; si viene, no consulta `tarifa_vigente`.

---

## 6.1 Cuenta corriente y facturación (E1)

**Libro contable derivado, no un motor de estados.** `facturas` y `pagos` son insert-only
(`trg_facturas_inmutable`/`trg_pagos_inmutable`); el saldo, y qué factura está pendiente, parcial,
pagada o vencida, se calcula en lectura vía la vista `v_facturas_saldo` (window function FIFO
sobre `facturas.fecha_emision`, restando `pagos.monto` acumulado) — nunca se persiste como
columna mutable. Un pago **no** tiene `factura_id`: D12 (Anexo I §10.2-B) dice que el cliente no
elige a qué factura se imputa, y no hay ningún escritor legítimo de esa columna.

**`saldo_cliente(cliente_id)` vs. `deuda_vencida_cliente(cliente_id, fecha)` — no son
intercambiables.** El primero es para pantalla (incluye deuda todavía no vencida). El segundo es
el único que gatea el corte de servicio (`PedidosController.Crear`, antes de resolver la
dirección): un cliente con una factura recién emitida y no vencida tiene `saldo_cliente > 0` pero
`deuda_vencida_cliente = 0`, y tiene que poder seguir cargando pedidos (§10.2-L3). Usar el
primero ahí habría cortado a cualquier cliente con una factura abierta normal.

**Qué acuña un `factura_items`.** Todo lo facturable nace como ítem en el momento exacto en que
se vuelve facturable — nunca se "descubre" escaneando `pedidos` al cerrar el ciclo:
- Entrega: `MisParadasController.Cerrar`, por pedido (una parada consolidada por RF-14 puede
  traer más de uno).
- Cancelación de un pedido ya `EnRuta` (§10.2-I): `PedidosController.CambiarEstado`, 100% del
  precio congelado. Cancelar desde `Borrador` sigue gratis.
- Ajuste aprobado (B16, ver abajo).

Único punto de escritura de estos tres: `Servicios/CuentaCorrienteService.AgregarItemDePedido` —
sin él, los dos controllers que lo llaman se desincronizarían en la primera corrección.
`ux_factura_items_pedido` (único parcial, `tipo='pedido'`) garantiza a nivel de base que un
pedido se factura una sola vez.

**Ajustes (B16, sumado a E1 por decisión del usuario).** Al retiro físico, si los bultos reales
no coinciden con los declarados, operación lo solicita (`POST /api/pedidos/{id}/ajustes`,
`FacturaItem` con `Estado='pendiente'`, `Monto=null`). Un admin lo aprueba fijando el monto a
mano — mismo patrón que `precio_manual` (B9) — o lo rechaza. **Tope de 3 ajustes por pedido sin
cargo extra**; el 4to exige que el admin fije además un `cargoGestion`, que entra como un
**segundo** `factura_items` separado (no sumado al primero) — sin porcentaje inventado, se tipea
a mano.

**Reprogramaciones con tope de 3 (D13, §10.2-M) y el reintento en la 4ta.** El contador se deriva
de `pedido_eventos` (`count(estado_nuevo='reprogramado')`), no de una columna — el log ya es
insert-only. Al 4to intento de `Fallido → Reprogramado`, `CambiarEstado` no reprograma: genera un
pedido `tipo='reintento'` (mismo patrón que `Devuelto`/`tipo='retorno'`, pero **sin** invertir
origen/destino — es un segundo intento al mismo domicilio) y el original queda en `Fallido`. Este
único camino responde `200` con el pedido nuevo en vez del `204` habitual — ver §5.

**Cierre de ciclo, manual, sin scheduler.** No hay Hangfire/Quartz/`IHostedService` en el
proyecto. `Dominio/CiclosFacturacion.cs` enumera los cierres pendientes de un cliente entre su
último `periodo_hasta` y la fecha pedida, en vez de preguntar "¿cierra hoy?" — así correr
`POST /api/facturas/cierre` tarde (o saltearse un día) se recupera solo. Fechas fijas de
calendario (§10.2-A): quincenal cierra el 15 y el último día del mes; mensual, solo el último.
`fecha_vencimiento = periodo_hasta + plazo` (7 días quincenal, 10 mensual) — **nunca**
`fecha_emision + plazo`, para que correr el cierre tarde no regale días de crédito.
`ux_facturas_periodo` (único `cliente_id, periodo_hasta`) hace el cierre idempotente a nivel de
base. `GET /api/facturas/cierre/previsualizacion` hace el mismo cómputo y revierte la
transacción — imprescindible: una factura emitida es inmutable, no hay deshacer.

**Plan de cuotas (§10.2-L4), sin tabla propia.** `clientes.corte_suspendido_hasta/motivo/por/en`
— el gate ignora la deuda vencida mientras `corte_suspendido_hasta >= hoy`. Admin extiende la
fecha con cada cuota que entra; si una no entra, el corte vuelve solo, sin job. **No guarda el
cronograma** (cuántas cuotas, de cuánto) — con dos clientes cerrados eso es papel (P6). Si hace
falta un cronograma real, se agrega una tabla `planes_pago` después, sin migrar nada de esto.

**`Dominio/Reloj.cs`, único punto de conversión a hora local.** Todo el resto del sistema calcula
"hoy" con `DateTime.UtcNow`; para el gate de corte y el barrido del cierre eso alcanzaba a mover
una entrega de las 22:00 (AR) de un período al siguiente. `Reloj.HoyLocal()`/`ALaFechaLocal()`
sobre `America/Argentina/Buenos_Aires` es la única conversión de todo el backend.

**Lo que §10.2-L2 deja pendiente.** El alcance de qué pasa con mercadería de terceros ya retirada
al momento del corte requiere dictamen legal de la Empresa — no se construyó nada de retención.
Como el corte solo alcanza altas nuevas (§10.2-L1), el punto queda inerte por ahora: la mercadería
ya retirada se entrega igual porque su pedido ya está confirmado.

Detalle técnico completo (DDL, controllers, casos de prueba) en el changelog 1.18.

---

## 7. Sincronización offline — el 60% del riesgo técnico

```
Acción en la calle
  └─► Dexie: fila en `capturas` con device_uuid + capturada_en + blob de foto
       └─► marca visible "pendiente" en la UI, la app sigue andando
            └─► al haber red: por cada captura, en orden
                 ├─ sube la foto a Storage en ruta determinista
                 │    pruebas/{pedido_id}/{device_uuid}.jpg  (upsert)
                 ├─ inserta la fila en `pruebas_entrega`
                 │    unique (pedido_id, device_uuid) → el duplicado falla y se descarta
                 └─ actualiza estado de parada y pedido
                      └─► borra la captura local
```

**Tablas Dexie**

| Tabla | Contenido |
|---|---|
| `paradas` | La ruta del día completa, descargada a las 07:30 con direcciones, coordenadas y teléfonos |
| `capturas` | Cierres de parada pendientes de subir, con blob de foto |
| `meta` | Fecha de la ruta descargada, última sincronización, id de dispositivo |

**Decisiones que no se improvisan**

- `device_uuid` se genera **en el momento de la captura**, no al sincronizar. Es lo único que hace idempotente el reintento (RNF-02).
- La hora que vale es `capturada_en`, del dispositivo. Se guardan las dos (RNF-03).
- La foto se comprime **antes** de encolar: lado largo 1280 px, JPEG calidad 0.7, objetivo menos de 200 KB. Ocho fotos de 3 MB llenan la cuota de IndexedDB y la jornada se pierde.
- `desvio_metros` se calcula en el dispositivo con la coordenada del destino que ya viene descargada (RF-29).
- La ruta se descarga **completa antes de salir**. Después de las 07:30 la PWA no necesita red para nada excepto vaciar la cola (RNF-07).
- Reintento con backoff y tope. Una captura que falla cinco veces se marca en rojo en la pantalla, no se descarta en silencio.

**Prueba que define si el hito está terminado:** modo avión activado *antes* de abrir la app, jornada completa con foto en cada entrega, cerrar la app, reabrirla, restaurar la conexión. Todo llega, sin duplicados, con la hora real de captura. Es el criterio de aceptación 3 del acta.

---

## 8. Orden de construcción

Cada hito termina cuando pasa su prueba, no cuando el código está escrito.

| Hito | Se construye | Prueba de salida | Estado |
|---|---|---|---|
| **H0** | Proyecto, esquema + triggers, seed de zonas/localidades/tarifas, auth con los cuatro roles | Un usuario de cada rol entra y ve exactamente lo que le corresponde | **Hecho** (sobre ASP.NET Core + Postgres local, no Supabase — §1) |
| **H1** | Alta de pedido con geocodificación, precio y congelamiento. Listado del día, detalle, historial y transiciones de estado. | Pedido cargado en menos de 30 s, con evento en el log. Intentar cambiar el precio falla. Dirección inexistente queda marcada. | **Hecho** |
| **H2** | **PWA completa con cola offline. Nada más este hito.** | Prueba de modo avión (sección 7) | **Sin construir** |
| **H3** | Armado de ruta con orden sugerido, reordenamiento manual, anclaje de urgentes, capacidad. Cierre de ruta con margen. | Ruta de 8 paradas armada en menos de 15 min. Margen disponible el mismo día. | **Hecho** (flechas subir/bajar en vez de drag & drop — §4.1) |
| **H4** | Solo lo que rompió la calle. Exportar CSV. | Tres rutas reales cerradas con costos reales pagados | Exportar CSV hecho; el resto depende de operar rutas reales — fuera del alcance de este documento |

**Desvío consciente del orden que este mismo documento recomienda.** Esta sección decía, y sigue siendo cierto: *"H2 es el único que importa (...) es la única pieza sin sustituto manual. Si no pasa la prueba de modo avión, no se avanza."* Se construyó H3 antes que H2 igual: se priorizó cerrar el ciclo de administración y operación (H0/H1/H3) para tener el sistema usable de punta a punta desde el escritorio, dejando la PWA offline para el final. El costo asumido es exactamente el que esta sección advierte: el dato de la calle — el único irrecuperable — todavía se captura en papel, no en el sistema.

**Regla de H3 y H4:** ninguna funcionalidad nueva mientras haya una ruta que se ejecutó mal. Los errores de la calle tienen prioridad absoluta sobre la lista de mejoras.

**En paralelo, no después:** consulta legal (contrato del repartidor, límite de responsabilidad por bulto, datos del destinatario), costo real por km de la camioneta, y conversión de al menos un prospecto.

### 8.1 Hitos (H) y etapas contractuales (E) — dos vocabularios, un mapeo

`H0`–`H4` son el orden de construcción de *este* documento, pensado antes de que existiera el Anexo I. `E0`–`E5` (`Anexo_I_Alcance_V2.docx` §5) son el cronograma que la Empresa firmó. No son la misma lista — E0 no tenía hito propio (es deuda de auditoría y nomenclatura, no una pantalla nueva) y H2 es, palabra por palabra, el contenido de E5.

| Hito | Etapa equivalente | Nota |
|---|---|---|
| H0, H1, H3 | — (anteriores al Anexo) | Administración y operación de escritorio, ya hechas. El Anexo §3 las lista como "alcance ya construido y verificado". |
| — | **E0** | Sin hito propio: nomenclatura, hallazgos de auditoría (pisos numéricos, coherencia de km), B9. Ver §1 (techo de tablas) y §4.1/§6 (precio manual) de este documento. |
| — | **E1** | **Hecha.** Sin hito propio tampoco — cuenta corriente y facturación (B1). Ver §6.1 de este documento. |
| H2 | **E5** | Mismo contenido exacto: PWA con cola offline. Sigue sin construir en las dos numeraciones. |
| — | E2–E4 | Sin hito asignado todavía — construidas etapa por etapa según el Anexo, no por el orden H de este documento. Dependen de E1, ya cerrada. |

---

## 9. Configuración

**Actualizada tras H0/H1** (§1: no hay Supabase, las variables de abajo reemplazan a las originales).

| Variable | Dónde | Nota |
|---|---|---|
| `NEXT_PUBLIC_API_URL` | frontend, `.env.local` | URL del backend (`http://localhost:5190` en desarrollo) |
| `ConnectionStrings:Postgres` | backend, `appsettings.Development.json` | Cadena de conexión a Postgres local |
| `Jwt:Key` | backend, user-secrets en desarrollo / variable de entorno en producción | Nunca en `appsettings.json`. `dotnet user-secrets set "Jwt:Key" "<valor>"` |
| `Frontend:Origin` | backend, `appsettings.json` | Origen permitido por CORS (`http://localhost:3000` en desarrollo) |
| `Precio:FactorUrgencia`, `Precio:FactorDescuentoRuta` | backend, `appsettings.json` | Valores provisionales (0.20 / 0.10) — la decisión comercial real sigue pendiente (acta §13) |
| `Deposito:CalleNumero/Localidad/Lat/Lng` | backend, `appsettings.json` | Semilla del primer depósito del catálogo: `DatosSemilla` crea con esto la `Ubicacion` con `NombreDeposito = "Depósito"` (§4.1 del acta), solo si el catálogo está vacío. No se vuelve a leer en runtime — el catálogo completo vive en `ubicaciones.nombre_deposito` y se administra desde `/depositos` (acta changelog 3.8). Sigue siendo el origen por default de todo `Pedido` nuevo (`OrigenRutaService.PrincipalParaPedidosAsync`); una ruta ya no tiene default, el planificador elige siempre a mano. |

---

## 10. Antes de la primera ruta

- [ ] `tarifas`: una fila por zona con `cliente_id` null, más las de cada cliente si difieren
- [ ] `localidades`: área de cobertura con su zona asignada — es decisión comercial, cada localidad zonificada es un lugar al que te comprometés a llegar
- [ ] `capacidad_paradas`: número real, hoy está en 24 por defecto
- [ ] RF-08: definir si los `borrador` del día siguiente se cancelan o se postergan a las 18:00 (desactualizado desde el changelog 1.10: `PedidosController.Crear` da de alta todo pedido en `borrador`, no directo en `confirmado` — el camino a `borrador` sí existe hoy, lo que falta es el job de cierre de carga en sí, ver §2)
- [ ] `FACTOR_URGENCIA` y `FACTOR_DESCUENTO_RUTA`
- [ ] Motivos de entrega fallida: lista cerrada, escrita
- [ ] Respaldo diario configurado **y una restauración probada** (RNF-10)
- [ ] Las cinco decisiones abiertas del acta, sección 11.2

---

## 11. Lo que este documento no define

Diseño visual, textos de interfaz, esquema de pruebas automatizadas, monitoreo y estrategia de despliegue. Se resuelven al llegar al hito que los necesita.

---

## 12. Control de versiones

| Versión | Fecha | Cambios |
|---|---|---|
| 1.0 | 28/08/2026 | Documento inicial. Reemplaza `mvp_especificacion.md`, alineado al Acta v3.0 y a `schema_v3.sql`. |
| **1.1** | **29/08/2026** | **Refleja la implementación real de H0, H1 y H3** (administración y operación completas desde el escritorio; H2, la PWA offline, sigue sin construir — desvío consciente del orden que este documento recomienda, ver §8). Cambios: stack real sin Supabase (§1–§3, §9); regla nueva sobre combinación de `[Authorize]` de clase y de acción (§3.8); pantallas divididas por rol tal como quedaron construidas, con la decisión de reordenamiento manual sin drag & drop (§4); fuente de verdad de la máquina de estados movida al backend (§5); estado hito por hito (§8); variables de configuración reales (§9). |
| **1.2** | **30/08/2026** | Separación de `usuarios` (personal interno) y `clientes_usuarios` (login de cliente) en dos tablas — acta §11.1/changelog 3.3. Excepción al techo de 13 tablas (§1); `UsuariosController` queda solo para staff, el ABM del login de cliente pasa a `ClientesController` anidado (§2); pantallas actualizadas (`/usuarios` sin el rol cliente, `clientes/[id]` con su login). |
| **1.3** | **31/08/2026** | Módulo de mapa (Leaflet + OSM) y ruteo por calles (`RuteoService` sobre OSRM, cacheado en memoria) — acta changelog 3.4 (§1, stack). `/hoy` migra a `GET /api/mis-paradas/dia`; `/hoy/parada/[paradaId]` y el cierre de parada quedan construidos, sin cola offline todavía (§4.2). |
| **1.4** | **31/08/2026** | Alta de pedido: combobox con búsqueda para cliente y localidad, autocompletado de destinatario sobre el historial del cliente (`GET /api/pedidos/destinatarios-frecuentes`, agregación sobre `pedidos` — acta changelog 3.5). El formulario separa "Destinatario" de "Dirección de entrega" (§4.1). La geocodificación pasa de `onBlur` a un efecto con debounce + `AbortController` (mismo patrón que la cotización), e informa el error en pantalla en vez de tragárselo. Fix de datos: `UbicacionService` normaliza `calle_numero` (`Trim` + colapso de espacios) antes de buscar y de guardar — `ux_ubicaciones_calle_localidad` es case-insensitive pero no whitespace-insensitive, y cada variante de espaciado venía creando una ubicación duplicada. |
| **1.5** | **31/08/2026** | Punto de partida variable de la ruta (acta changelog 3.6). Columna nueva `rutas.origen_ubicacion_id` (nullable, `null` = depósito: sin backfill, las rutas existentes no cambian de comportamiento) y migración `AgregarOrigenRuta`. `OrigenRutaService` pasa a ser el único lugar que resuelve el origen efectivo — antes `UbicacionesController` leía la fila `ubicaciones.referencia='deposito'` y `MisParadasController` leía `OpcionesDeposito` de `appsettings`, dos fuentes de verdad que podían discrepar; `OpcionesDeposito` queda solo como semilla. `PUT /api/rutas/{id}` acepta `origenUbicacionId` (no se agregó endpoint propio: es cabecera de ruta, mismo guard de `planificada`, misma llamada que ya hacía el armado); una dirección nueva se resuelve antes con `POST /api/ubicaciones`, igual que el destino de un pedido. `GET /api/mis-paradas/dia` renombra `deposito` → `origen` y devuelve también la calle/localidad, para que el repartidor lea desde dónde sale. Nuevo `components/SelectorDireccion.tsx` extraído del alta de pedido (que **no** se migró todavía: `aplicarSugerencia` escribe ese estado desde afuera y necesita una variante controlada — deuda anotada, §4.1). Fix de ruteo: `sugerirOrden` dejó de darle a las paradas sin geocodificar la coordenada del depósito como fallback (las empujaba al principio del recorrido) — ahora se tratan como ancladas y mantienen su posición. |
| **1.6** | **31/08/2026** | Depósito editable (acta changelog 3.7). `PUT /api/ubicaciones/deposito` (`Authorize(Policy="Administracion")`, más estricto que el `BackOffice` del resto del controller — es decisión de empresa, no operativa del día a día) resuelve-o-crea la nueva dirección (`UbicacionService`, mismo camino que el destino de un pedido) y mueve la marca `ubicaciones.referencia='deposito'` con `OrigenRutaService.MarcarComoDepositoAsync` — nunca edita la fila vieja, así que cualquier ruta o pedido que ya apuntaba a esa dirección por id la conserva intacta. `RutasController.Cerrar` (cierre económico) es el punto de congelamiento: si `origen_ubicacion_id` seguía en `null`, ahí se materializa al id concreto que resolvía "depósito" en ese momento — antes de esto, cambiar el depósito reescribía en silencio el origen mostrado de cualquier ruta planificada, en curso **o ya cerrada**. Pantalla nueva `/deposito` (solo admin, nav en `Shell.tsx`), con `SelectorDireccion` prellenado desde `GET /api/ubicaciones/deposito`. Sin tabla nueva. **Superada por 1.7**: `/deposito` y el endpoint singular no existen más. |
| **1.7** | **31/08/2026** | Catálogo de depósitos (acta changelog 3.8), reemplaza el depósito único de 1.6. Columna nueva `ubicaciones.nombre_deposito` (nullable, única entre no-nulos vía índice parcial `HasFilter`) — no-nula marca qué fila es depósito y con qué nombre; se agrega en vez de reusar `Referencia`, que ya significa la nota de parada que ve el repartidor (`v_paradas_repartidor`). Migración `AgregarCatalogoDepositos`, con data-fix embebido: promueve la fila legado `referencia='deposito'` al catálogo nombrado y backfillea toda `ruta.origen_ubicacion_id` que estuviera en `null` a ese id — el fallback implícito "`null` = el depósito" se retira en esta misma versión, así que ninguna ruta puede quedar sin resolver. `OrigenRutaService` pasa de un único `DepositoAsync()` a `ListarDepositosAsync`/`CrearDepositoAsync`/`RenombrarDepositoAsync`/`DesactivarDepositoAsync` (ABM completo) más `PrincipalParaPedidosAsync` — este último resolver, separado y sin equivalente para rutas, es el único lugar que conserva un default implícito (el depósito más antiguo por id), porque el alta de pedido todavía no tiene pantalla propia para elegir origen (RF-11, fuera de alcance de esta versión). **Decisión explícita del usuario: ninguna ruta tiene depósito "principal"** — `CerrarPlanificacion` (RF-17) bloquea con 400 si `origen_ubicacion_id` sigue en `null`; antes de esto el default lo resolvía `Cerrar` en el cierre económico, ahora ese freeze es innecesario porque el origen concreto ya quedó fijado antes de pasar a en curso. `UbicacionesController` cambia `GET/PUT /deposito` por `GET/POST /depositos` y `PUT/DELETE /depositos/{id}` (alta y renombre en `Administracion`, lectura en `BackOffice`). Frontend: `/deposito` → `/depositos` (ABM completo: lista con renombrar/sacar del catálogo, alta con nombre + `SelectorDireccion`); `armar/page.tsx` cambia el toggle "Depósito"/"Otra dirección" por un `ComboboxBusqueda` sobre el catálogo más el sentinel "Otra dirección…"; `Shell.tsx` y `proxy.ts` actualizados. Sin tabla nueva. |
| **1.8** | **31/08/2026** | Descubrimiento de localidades (acta changelog 3.9): `localidades` deja de ser un catálogo fijo de 5 filas cargadas en el seed. `GeocodificacionService.BuscarLocalidadesAsync` agrega una búsqueda nueva contra Nominatim (`featureType=settlement`, filtra a ciudades/pueblos en vez de calles o comercios) que extrae `city ?? town ?? village ?? municipality` como nombre y `county ?? state` como partido. `LocalidadesController` suma `GET /api/localidades/buscar?q=` (catálogo propio por ILIKE + sugerencias de Nominatim en paralelo con `Task.WhenAll` — en serie, la espera de red de Nominatim (~1s) se sumaba encima de una consulta local que ya era instantánea) y `POST /api/localidades` (resolver-o-crear, mismo patrón que `UbicacionService`; nace siempre con `zona_id = null`). Frontend: `components/SelectorLocalidad.tsx` (nuevo) reemplaza los dos comboboxes de localidad que cargaban la lista completa en memoria (`SelectorDireccion.tsx` y `pedidos/nuevo/page.tsx`, que tenían implementaciones duplicadas) por un combobox server-driven sobre el endpoint de búsqueda; elegir una sugerencia la da de alta sola. Con `textoBusqueda` controlado, el input ya no se sincroniza solo desde `value` (a diferencia del modo en memoria de `ComboboxBusqueda`) — el componente sincroniza el texto a mano ante una localidad asignada desde afuera, con el patrón "ajustar estado durante el render" (sin efecto) para no pisar el set-state-in-effect. Sin tabla ni columna nueva. |
| **1.9** | **31/08/2026** | Asignación de zona a localidades pendientes (acta changelog 3.10), cierra el hueco que dejó 1.8: una localidad descubierta quedaba sin zona y sin pantalla para asignarla. `LocalidadesController` suma `GET /api/localidades/pendientes` (`Authorize(Policy="Administracion")`, mismo criterio que el alta de depósito — fija precio, es decisión de empresa) y `PUT /api/localidades/{id}/zona`. La sugerencia de zona reutiliza `Dominio.Geo.DistanciaMetros` (el mismo haversine que ya usa la prueba de entrega, RF-29) contra el depósito de `OrigenRutaService.PrincipalParaPedidosAsync`, y busca qué `Zona` activa con `KmDesde` cargado cubre esa distancia — si el depósito no tiene coordenada, o la localidad no tiene todavía ninguna `Ubicacion` geocodificada, no hay sugerencia y el combobox queda en blanco para elegir a mano. Frontend: `/tarifas` suma la sección "Localidades sin zona" arriba de la tabla existente, reutilizando el mismo `GET /api/tarifas` para poblar el combobox de zonas (ya trae `zonaId`/`zonaCodigo`/`zonaNombre` de todas, sin pedir `/api/zonas` aparte). Sin tabla ni columna nueva. |
| **1.10** | **31/08/2026** | Precio por tipo de vehículo (acta changelog 3.11) — el cambio de mayor alcance sobre la máquina de estados desde H0/H1. Migración `AgregarTipoVehiculo`: `vehiculos.tipo` (default `'camioneta'`, backfill automático de la flota existente), `tarifas.tipo_vehiculo` (mismo backfill), `tarifa_vigente()` gana un cuarto parámetro y el índice `ux_tarifas_vigencia` lo suma a la clave. `precio_base`/`recargo_urgencia`/`descuento_ruta`/`total`/`precio_congelado_en` de `pedidos` pasan a nullable — `peajes` no, es un dato de entrada que no depende del vehículo. **`PedidosController.Crear` deja de saltear `Borrador`:** crea el pedido en `Borrador`, sin cotizar (antes iba directo a `Confirmado` con el precio ya calculado); `POST /api/pedidos/cotizar` pasa a devolver `{ camioneta, moto }` — un estimado, nunca lo que se persiste. `CandidatosRuta` filtra `Borrador` en vez de `Confirmado`; `RutasController.GuardarParadas` exige lo mismo. **`RutasController.CerrarPlanificacion` es donde se cierra el círculo:** ya exigía vehículo asignado (RF-17); ahora, antes de pasar a `en_curso`, cotiza cada pedido `Borrador` de la ruta con el `Tipo` de ese vehículo (`PrecioService.CotizarAsync`, con `tipoVehiculo`) — si a alguno le falta tarifa para esa zona × tipo, se rechaza *todo* con 400 antes de escribir nada (ruta y pedidos quedan exactamente como estaban). Si cotiza, dos `SaveChangesAsync` bajo una única transacción y un único contexto de actor (`EscrituraDominio.PublicarActorAsync`, extraído de `GuardarComoAsync` para este caso — dos `GuardarComoAsync` habrían abierto dos transacciones separadas, con ventana de fallo parcial entre medio): el primero fija precio y pasa `Borrador → Confirmado`, el segundo pasa todo a `EnRuta` — dos eventos de historial reales (RF-28, "sin huecos"), no un salto directo. `CambiarEstado` (rama `Devuelto`) deja de cotizar en el acto al crear el pedido de retorno: nace en `Borrador` como cualquier alta, mismo motivo (su vehículo tampoco se conoce hasta que se arme su propia ruta). Frontend: `/tarifas` y `/clientes/[id]` muestran precio camioneta y moto por zona (dos columnas, mismo patrón de inputs+Guardar); `/vehiculos` suma el selector Tipo; `pedidos/nuevo` muestra el estimado de los dos tipos y el botón "Crear pedido" deja de depender de tener una cotización resuelta; `pedidos/page.tsx` y `pedidos/[id]/page.tsx` muestran "Pendiente de armado" mientras el total es `null`. Verificado end-to-end con Playwright: pedido nace Borrador con estimado camioneta/moto correcto, se arma con un vehículo moto, `CerrarPlanificacion` fija el precio de moto (no el de camioneta) y dos transiciones de historial separadas; y el caso de falla (zona sin tarifa de moto) rechaza con 400 sin tocar ruta ni pedido. Cero tablas nuevas. |
| **1.11** | **31/08/2026** | Paginación, filtros y orden en `/pedidos` (RF-10) — sin cambio de negocio, no suma línea al acta. `PedidosController.Listar` gana `q` (id o destinatario), `fechaDesde`/`fechaHasta` (reemplazan al filtro de fecha exacta, que seguía existiendo por compatibilidad), `clienteId`, `orden` (id/fecha/total/estado, con prefijo `-` para descendente) y `pagina`/`tamanioPagina` — todos opcionales: sin `tamanioPagina` devuelve todo sin recortar, a propósito, para no romper `GET /api/pedidos` de `/mis-envios` (rol 'cliente', que lo pide sin ningún query param). La respuesta pasa de un array a `{ items, total }` (`ListaPaginada<T>`, tipo genérico reusable) — `mis-envios/page.tsx` se ajusta con un cambio de una línea (`.then(r => r.items)`). Orden por defecto ahora es `fecha` descendente con `id` descendente como desempate — antes el desempate entre pedidos de la misma fecha quedaba librado al orden físico de la tabla, ni estable ni predecible. Frontend: encabezados de columna clickeables (alternan asc/desc, con indicador ▲/▼), selector de tamaño de página (10/15/20) y controles Anterior/Siguiente con "Mostrando X–Y de Z". `PedidoResumen` suma `clienteRazonSocial` (ya se hacía el join para filtrar por cliente; mostrar el nombre en la fila es gratis). Sin tabla ni columna nueva. |
| **1.12** | **31/08/2026** | Mismo patrón de 1.11 aplicado a `/rutas` — sin cambio de negocio, no suma línea al acta. `ListaPaginada<T>` se extrae de `PedidosController` a `Dominio/ListaPaginada.cs` para que `RutasController` la reuse sin duplicar el mismo record genérico. `RutasController.Listar` gana `q` (id, patente o nombre de repartidor), `fechaDesde`/`fechaHasta`, `estado` (sin enum: `Ruta.Estado` ya era texto plano con check constraint, no hace falta parsear), `orden` (id/fecha/estado/paradas) y `pagina`/`tamanioPagina` — mismo criterio opcional que en pedidos, sin otro consumidor de `GET /api/rutas` que romper (`rutas/nueva/page.tsx` solo hace `POST`). Ordenar por `paradas` usa la misma subconsulta `COUNT` que ya se proyectaba para mostrar la columna, ahora también en el `ORDER BY`. Frontend: mismos encabezados clickeables, selector 10/15/20 y Anterior/Siguiente que en `/pedidos`. Sin tabla ni columna nueva. |
| **1.13** | **31/08/2026** | Rediseño de la guía del repartidor (`/hoy` y `/hoy/parada/[paradaId]`) — sin cambio de negocio ni de API, no suma línea al acta: la jornada seguía siendo `GET /api/mis-paradas/dia`, el cierre de parada seguía siendo el mismo `POST /api/mis-paradas/{id}/cierre`. `/hoy` suma: barra de progreso (completadas/fallidas/pendientes, antes solo un texto "X de Y"); tarjeta "Próxima parada" (la primera pendiente, destacada, con botón "Cómo llegar" — enlace directo a Google Maps con `lat/lng` — y acceso directo al detalle, para no tener que buscarla en la lista); lista de paradas rediseñada como tarjetas (badge numerado por color de estado, pill de estado, contador de bultos con ícono); mapa con botón para expandir a una superposición de pantalla completa. El mapa chico se **saca del árbol** (no solo se oculta) mientras está expandido: los controles propios de Leaflet (zoom, atribución) traen su propio z-index >1000 en la hoja de estilos de la librería, por encima de cualquier overlay razonable de la página — con los dos mapas vivos a la vez, los controles del chico se colaban por encima del modal. `/hoy/parada/[paradaId]` suma: botón "Cómo llegar" (mismo enlace a Maps, que antes no existía — la única acción de navegación era "Llamar"); preview de la foto de entrega antes de confirmar (antes el `<input type=file>` no mostraba nada hasta enviar); iconos en todos los botones de acción. El preview usa `URL.createObjectURL` calculado en el render vía `useMemo` (no en un efecto) — evita `react-hooks/set-state-in-effect`, que si bloqueaba tanto el `setState` de reseteo como el de creación de la URL; el efecto que queda solo hace la limpieza (`revokeObjectURL`), que no dispara esa regla porque no llama `setState`. |
| **1.14** | **31/08/2026** | Detalle de pedido en dialog en vez de navegación — sin cambio de API. El contenido de `pedidos/[id]/page.tsx` (Datos, Precio, Cambiar estado, Historial) se extrae a `components/PedidoDetalleContenido.tsx`, reusado por la página standalone (que sigue existiendo, para acceso directo por URL) y por un `Dialog` nuevo en `/pedidos`: clic en cualquier parte de la fila (antes solo ID y Destinatario eran `<Link>`) abre el detalle sin abandonar la lista — filtros, orden y página actual quedan intactos al cerrar. `components/ui/dialog.tsx` (nuevo) envuelve `@base-ui/react/dialog` con el mismo criterio visual que `combobox.tsx`/`select.tsx` (animaciones `data-open`/`data-closed`, radios y sombras ya usados en el resto del sistema). El link "Retorno de #X"/"Reintento de #X" pasa a ser un botón que cambia de pedido *dentro* del mismo dialog (`onAbrirPedidoOrigen`) cuando está embebido, y sigue siendo un link a `/pedidos/{id}` en la página standalone. `key={pedidoId}` en `PedidoDetalleContenido` fuerza un remonte completo al cambiar de pedido — todo el estado de la transición en curso se resetea solo, sin un efecto manual (que hubiera disparado `react-hooks/set-state-in-effect`). |
| **1.15** | **31/08/2026** | Fix: `Borrador → Confirmado` ya no es una transición manual. Encontrado al probar 1.14: `TransicionesPedido.Permitidas[Borrador]` todavía incluía `Confirmado` desde antes de acta changelog 3.11, así que `POST /api/pedidos/{id}/estado` dejaba confirmar (y por lo tanto rutear) un pedido con `precio_base`/`total` en `null` — el trigger `fn_congelar_pedido` no lo impide porque no valida que haya precio, solo que no cambie una vez fuera de Borrador. `TransicionesPedido.Permitidas[Borrador]` pasa a `[Cancelado]` únicamente; `lib/dominio/estados.ts` (espejo) igual. La única vía real a `Confirmado` desde `Borrador` sigue siendo `RutasController.CerrarPlanificacion`, que ya cotiza con el tipo de vehículo real antes de escribir el estado. Verificado que el botón "Confirmar" ya no aparece en el dialog/página de un pedido en Borrador, y que `POST /api/pedidos/{id}/estado` con `estadoNuevo=Confirmado` sobre un pedido en Borrador devuelve 400 llamado directo por curl. |
| **1.16** | **31/08/2026** | Rate limiting en `/api/auth/login` — hallazgo de una auditoría de seguridad pedida por el usuario sobre el sistema completo: sin límite de intentos, era fuerza bruta viable contra el login (nada de negocio lo frenaba). `Program.cs` agrega `AddRateLimiter` con una policy `"login"` — `FixedWindowRateLimiter`, 5 intentos por minuto, particionada **por IP** (`HttpContext.Connection.RemoteIpAddress`), no por email: particionar por email dejaría que cualquiera bloqueara el login de otro con solo mandar intentos fallidos a su nombre (un DoS disfrazado de "protección"). `AuthController.Login` lleva `[EnableRateLimiting("login")]`. El rechazo (429) devuelve un `ProblemDetails` con `Retry-After: 60` vía el mismo `IProblemDetailsService` que usa `ManejadorExcepciones` — mismo formato que ya sabe leer `leerError` del frontend, sin caso especial. `AuthProvider.login` distingue 429 del resto de los `!ok` (que siguen mostrando el genérico "Email o contraseña incorrectos", a propósito: no hay que filtrar si el email existe o no) y propaga el detalle real; `login/page.tsx` deja de pisarlo con un mensaje hardcodeado en el `catch`. Verificado con curl: intentos 1-5 pasan, 6to en adelante devuelve 429 con `Retry-After`, y pasado el minuto un login válido vuelve a funcionar normal. |
| **1.17** | **11/09/2026** | **E0 completa** (`Anexo_I_Alcance_V2.docx` §5) — ver §8.1 para el mapeo de hitos/etapas. Tres piezas: **B9** ("+40 km → Cotización", Anexo I §4): `pedidos` gana `precio_manual`/`precio_manual_por`/`precio_manual_en` (migración `AgregarPrecioManual`) — precio fijado a mano cuando la zona no tiene tarifa cargada en ningún tipo de vehículo, sustituye solo el origen de `precio_base` en `PrecioService.CotizarAsync` (§6), protegido por `fn_congelar_pedido` una vez confirmado igual que el resto del precio (P1). `PUT /api/pedidos/{id}/precio-manual`, solo `Administracion`, solo en Borrador — mismo criterio de "más estricto que la clase" que `ZonasController.ActualizarKm` (regla §3.8). `Cotizar`, `Crear`, `Listar` y `CandidatosRuta` exponen `requiereCotizacion` para que se vea antes del 400 al cerrar planificación, no después. **Pisos numéricos** (hallazgos de auditoría — ver nota más abajo): `[Range]` en los request records que aceptaban negativos sin control (`RutasController.CerrarRutaRequest`/`ActualizarRutaRequest`, `VehiculosController`, `PedidosController.CrearPedidoRequest`, `TarifasController`/`ClientesController.FijarTarifaRequest`, `ZonasController.ActualizarKmRequest`) más un chequeo cruzado explícito donde `[Range]` no alcanza (`RutasController.Cerrar`: km final ≥ inicial). **Importante para quien repita el patrón:** el atributo va directo sobre el parámetro posicional del record (`[Range(...)] decimal X`), **no** con el target `[property: Range(...)]` — ASP.NET Core devuelve un 400 en runtime ("validation metadata... must be associated with the constructor parameter") si la metadata queda solo en la propiedad autogenerada. `Program.cs` suma `ApiBehaviorOptions.InvalidModelStateResponseFactory` para que un `[Range]` fallido salga como `ProblemDetails` con `Detail` en español (antes: `ValidationProblemDetails` sin `detail`, `leerError` caía al `title` en inglés). **Coherencia de km**: `ZonasController.ActualizarKm` rechaza con 400 si el rango nuevo se solapa con otra zona activa (semiabierto `[desde, hasta)`, mismo criterio que la frontera de `UbicacionesController.LocalidadesPendientes`, ahora corregida de inclusiva en los dos extremos a semiabierta); el hueco entre zonas **no** bloquea, solo se reporta (`TarifasController.CalcularHuecos`, `GET /api/tarifas` cambia de array a `{ zonas, huecos }` — frontend ajustado). Nomenclatura: `frontend/lib/dominio/tipos.ts` suma `etiquetaTipoVehiculo()` (`camioneta` → "Auto" en pantalla, el valor de base no se toca) usado en `/tarifas`, `/clientes/[id]`, `/vehiculos`, alta y detalle de pedido. Techo de tablas 14 → 17, ver §1. Verificado end-to-end con curl: `Cotizar`/`Crear`/`Detalle` en una zona sin tarifa marcan `requiereCotizacion`; `PUT precio-manual` en 403 para operación/repartidor, 400 con precio ≤0, 204 con precio válido, 409 sobre un pedido ya confirmado; overlap de zonas rechaza con 400 y el mensaje nombra la zona en conflicto, un hueco no bloquea el guardado; `Cerrar` de ruta rechaza km/montos negativos y km final menor al inicial, todos con mensaje en español. |
| **1.18** | **11/09/2026** | **E1 completa** (Anexo I §5, B1) — cuenta corriente y facturación. Bloqueada por seis definiciones del Anexo §10.2 (A residual, B, I, L, M, N), todas resueltas por decisión explícita del usuario **excepto §10.2-L(2)**, que sigue pendiente de dictamen legal (retención de mercadería de terceros ante el corte — inerte por ahora, el corte solo alcanza altas nuevas). Detalle técnico completo en §6.1 de este mismo documento; acá solo la superficie nueva. **Datos:** techo de tablas alcanzado en 17 (`facturas`, `factura_items`, `pagos`, migración `AgregarCuentaCorriente`) + `clientes.ciclo_facturacion`/`corte_suspendido_*`. **Backend nuevo:** `Dominio/{CiclosFacturacion,Reloj}.cs`, `Servicios/CuentaCorrienteService.cs`, `Controllers/{Facturas,MiCuenta}Controller.cs`. **Backend modificado:** `PedidosController` (gate de corte en `Crear`, ramas de cancelación al 100% y reprogramación con tope de 3 + reintento en `CambiarEstado`, 4 endpoints de ajustes), `MisParadasController.Cerrar` (ítem de entrega), `ClientesController` (cuenta-corriente, pagos, corte-suspendido, ciclo), `TransicionesPedido` (`EnRuta → Cancelado` nueva — ver §5). **Frontend nuevo:** `lib/hooks/useListadoPaginado.ts` + `components/ControlesPaginacion.tsx` (extraídos de `/pedidos` y `/rutas`, que ya duplicaban ~120 líneas literales — regla de tres, tercera copia habría sido `/facturas`), `app/facturas/page.tsx` (listado + cierre de ciclo con previsualización obligatoria), `components/FacturaDetalleContenido.tsx`. **Frontend modificado:** `PedidoDetalleContenido.tsx` (Card "Facturación y ajustes", aviso de cancelación al 100%, manejo del `200 + body` del reintento), `app/clientes/[id]/page.tsx` (Card `CuentaCorrienteCliente`, ciclo en `DatosCliente`), `app/mis-envios/page.tsx` (Card "Mi cuenta", sin nav nueva — el rol `cliente` no recibe `Shell`, construirla es alcance de E4). Verificado end-to-end con curl (roles, triggers de inmutabilidad, FIFO de pagos parciales, idempotencia del cierre) y `next build` limpio. |
