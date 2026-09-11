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
| Detalle de pedido | admin + operación | Datos, desglose de precio congelado, historial completo de estados (actor, motivo, hora — RF-28), botones de transición según `Dominio/TransicionesPedido.cs`. | 02, 28; criterios de aceptación 5 y 7 |
| Rutas | admin + operación | Listado por fecha. Acción por fila según estado: *Armar* (`planificada`), *Cerrar* (`en_curso`, solo admin), *Ver cierre* (`cerrada`, solo admin). | 10 |
| Nueva ruta | admin + operación | Alta mínima (solo fecha) → redirige a armar. | — |
| Armar ruta | admin + operación | Candidatos confirmados de la fecha, agrupados por zona; consolidación automática por destino (RF-14); punto de partida de la jornada — combobox con el catálogo de depósitos (`GET /api/ubicaciones/depositos`) más "Otra dirección…" (mismo selector localidad+calle y misma geocodificación con debounce del alta de pedido); ya no hay opción "por default", el planificador siempre elige, `CerrarPlanificacion` lo exige (acta changelog 3.8); botón "sugerir orden" (`lib/dominio/ruteo.ts`); reordenamiento manual con flechas ↑/↓ y anclar/desanclar (RF-12/13, no drag & drop — decisión tomada, ver más abajo); contador contra `capacidad_paradas` (RF-16, aviso, nunca bloqueo); seleccionar vehículo del catálogo y repartidor; cerrar planificación. | 11–17 |
| Cierre de ruta | solo admin | Km, combustible, peajes, otros costos, pago al repartidor → margen del día y conteo de entregas efectivas/fallidas/reprogramadas. Solo disponible una vez cerrada la planificación (`Estado == "en_curso"`). | 26, 27 |
| Clientes | solo admin | Alta, datos, tres colores (semáforos, RF-32/33), tarifas por zona. | 09, 31–33 |
| Tarifas | solo admin | Lista general de precios por zona (`tarifas.cliente_id` null), con el rango de km de cada zona editable al lado. Encima, "Localidades sin zona": pendientes de asignar, con zona sugerida por distancia al depósito (acta changelog 3.10). | 09 |
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

---

## 5. Máquina de estados

```
borrador ──confirmar──► confirmado ──asignar a ruta──► en_ruta
   │                         │                            │
cancelado                cancelado              ┌─────────┴─────────┐
                                                ▼                   ▼
                                           entregado             fallido
                                                                    │
                                                     ┌──────────────┴───────────┐
                                                     ▼                          ▼
                                               reprogramado                 devuelto
                                                     │                          │
                                            confirmado (nueva fecha)    pedido tipo 'retorno'
```

| Transición | Condición |
|---|---|
| `borrador → confirmado` | Precio calculado y congelado — **exclusivamente** en `RutasController.CerrarPlanificacion` (RF-17), nunca a mano por `POST /api/pedidos/{id}/estado`: cotizar exige conocer el tipo de vehículo real de la ruta (acta changelog 3.11), que no existe hasta ese momento. `TransicionesPedido.Permitidas[Borrador]` no incluye `Confirmado` a propósito — dejarlo confirmaría un pedido con precio en null. Dirección apta o el trigger la rechaza al rutear. |
| `confirmado → en_ruta` | Existe fila en `parada_pedidos`. |
| `en_ruta → entregado` | Prueba de entrega sincronizada. |
| `en_ruta → fallido` | Motivo obligatorio de lista cerrada. |
| `fallido → reprogramado` | Primer reintento: sin cargo, misma fila, nueva fecha. |
| `fallido → devuelto` | Genera pedido nuevo `tipo='retorno'` con `pedido_origen_id`. |
| Segundo reintento | Pedido nuevo `tipo='reintento'`, precio propio. |

**Actualizado tras H0/H1:** la fuente de verdad es el backend, no el frontend. `Dominio/TransicionesPedido.cs` (`Permitida(actual, nuevo)`, `MotivoObligatorio(nuevo)`) es quien decide, aplicada en `POST /api/pedidos/{id}/estado`; `lib/dominio/estados.ts` es un espejo de solo lectura que el frontend usa únicamente para decidir qué botones mostrar. Un intento inválido lo rechaza el servidor igual, con o sin ese espejo. La razón del cambio: sin Supabase no hay lógica de negocio confiable del lado del navegador (§1), el servidor tiene que ser quien imponga.

Implementadas hoy todas las transiciones **excepto** `en_ruta → entregado`, que depende de una prueba de entrega sincronizada (H2/F3, sin construir). `fallido → devuelto` genera el pedido `tipo='retorno'` con precio propio cotizado en el momento; el segundo reintento (`tipo='reintento'`) todavía no tiene camino en la UI.

---

## 6. Cálculo de precio

```
precio_base       = tarifa_vigente(cliente_id, zona_id, fecha)
recargo_urgencia  = urgente ? precio_base × factor_urgencia : 0
descuento_ruta    = pedido agregado a ruta existente ? precio_base × factor_desc : 0
peajes            = valor cargado
total             = precio_base + recargo_urgencia − descuento_ruta + peajes
```

Los dos factores son parámetros de configuración, no constantes en el código. Al confirmar, los cinco valores quedan escritos en la fila y `precio_congelado_en` se sella. El trigger `trg_congelar_pedido` impide tocarlos después.

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
- [ ] RF-08: definir si los `borrador` del día siguiente se cancelan o se postergan a las 18:00 (hoy no aplica: `PedidosController.Crear` da de alta todo pedido directo en `confirmado`, no existe camino a `borrador` — el job de cierre de carga se retoma cuando exista uno, ver §2)
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
