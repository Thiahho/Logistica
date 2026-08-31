# Documento de construcción — Sistema de gestión logística

**v1.1 · 29/08/2026**
Reemplaza y deja sin efecto `mvp_especificacion.md`.

---

## 0. Cómo se usa este documento

Hay tres documentos y ninguno repite al otro. Si algo está en dos lugares, uno de los dos está desactualizado y no vas a saber cuál.

| Documento | Responde | Se toca cuando |
|---|---|---|
| `acta_sistema_v3.md` | Qué hace el sistema y por qué. Alcance, reglas de negocio, criterios de aceptación. | Cambia el negocio |
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
                               # TarifaService — sin interfaces, inyectados por tipo concreto
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
    /hoy                                           # rol repartidor — solo lectura, sin cola offline
    /mis-envios                                    # rol cliente
    /login
  /components
    Shell.tsx                 # nav lateral filtrada por rol (back-office)
    CabeceraSesion.tsx
    /ui                       # shadcn CLI v4, style base-nova, sobre Base UI (no Radix)
  /lib
    /auth                     # AuthProvider (fetchConSesion), RequireRole, types
    /dominio
      tipos.ts                 # tipos compartidos que reflejan los DTOs del backend
      estados.ts                # espejo de solo lectura de Dominio/TransicionesPedido.cs (backend)
      geo.ts                    # haversine
      ruteo.ts                  # nearest-neighbor + 2-opt, respeta paradas ancladas
    /api
      errores.ts                # parseo de ProblemDetails
  proxy.ts                    # gate de sesión por cookie (el rol se resuelve en RequireRole)
```

**Pendiente de construir:** `/parada/[id]` y toda la capa offline (`lib/offline/`: Dexie, cola, compresión de fotos — sección 7), el job de cierre de carga de las 18:00 (RF-08, deferido a propósito — ver §10).

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
| Alta de pedido | admin + operación | Cliente (selector sin colores ni tarifas), referencia, destinatario, teléfono, dirección, localidad, bultos, fecha, urgente. Geocodifica al salir del campo dirección. Precio en vivo. | 01–07, 09 |
| Detalle de pedido | admin + operación | Datos, desglose de precio congelado, historial completo de estados (actor, motivo, hora — RF-28), botones de transición según `Dominio/TransicionesPedido.cs`. | 02, 28; criterios de aceptación 5 y 7 |
| Rutas | admin + operación | Listado por fecha. Acción por fila según estado: *Armar* (`planificada`), *Cerrar* (`en_curso`, solo admin), *Ver cierre* (`cerrada`, solo admin). | 10 |
| Nueva ruta | admin + operación | Alta mínima (solo fecha) → redirige a armar. | — |
| Armar ruta | admin + operación | Candidatos confirmados de la fecha, agrupados por zona; consolidación automática por destino (RF-14); botón "sugerir orden" (`lib/dominio/ruteo.ts`); reordenamiento manual con flechas ↑/↓ y anclar/desanclar (RF-12/13, no drag & drop — decisión tomada, ver más abajo); contador contra `capacidad_paradas` (RF-16, aviso, nunca bloqueo); seleccionar vehículo del catálogo y repartidor; cerrar planificación. | 11–17 |
| Cierre de ruta | solo admin | Km, combustible, peajes, otros costos, pago al repartidor → margen del día y conteo de entregas efectivas/fallidas/reprogramadas. Solo disponible una vez cerrada la planificación (`Estado == "en_curso"`). | 26, 27 |
| Clientes | solo admin | Alta, datos, tres colores (semáforos, RF-32/33), tarifas por zona. | 09, 31–33 |
| Tarifas | solo admin | Lista general de precios por zona (`tarifas.cliente_id` null), con el rango de km de cada zona editable al lado. | 09 |
| Usuarios | solo admin | Alta, edición, activo/inactivo, reset de contraseña. Sin esto no se puede dar de alta al repartidor. | RNF-08 |
| Vehículos | solo admin | Alta, edición, activo/inactivo. Ficha de flota: patente, descripción, marca/modelo/año, km, vencimientos de VTV y seguro, costo/km, capacidad de paradas (prellena la de la ruta al elegirlo en Armar ruta). | — |
| Exportar | solo admin | CSV de pedidos, rutas y resultados por rango. Reemplaza el módulo de reportes. | 30 |
| Mis envíos | rol cliente | Solo lectura de los propios pedidos, sin importes internos ni datos de otros clientes. | RNF-08 |
| Login | todos | Sesión de dos tokens: access en memoria, refresh en cookie httpOnly. Nunca pedir contraseña en la calle. | — |

**Decisión tomada sobre RF-12:** el reordenamiento usa flechas subir/bajar + anclar, no una lista arrastrable. El frontend no tenía ninguna dependencia de interacción compleja (todo hand-rolled con `fetch` + `useState`) y se mantuvo esa línea en vez de sumar `@dnd-kit` u otra librería de drag & drop. RF-12 no exige que sea arrastrable, solo que el reordenamiento manual esté siempre disponible.

**Simplificación consciente sobre RF-17:** "cierre de ruta como planificada, queda disponible para el dispositivo del repartidor" se implementó como la acción explícita `POST /api/rutas/{id}/cerrar-planificacion`, que transiciona `Ruta.Estado` de `planificada` a `en_curso` *y*, en la misma escritura, todos sus pedidos de `Confirmado` a `EnRuta` (condición ya documentada en §5: *"confirmado → en_ruta si existe fila en parada_pedidos"*). Esto adelanta a esta fase la transición que el acta asocia al retiro físico de las 07:30 (F3, sin construir) — no hay otro actor todavía que la dispare.

### 4.2 PWA — repartidor (sin construir, H2/F3)

| Pantalla | Contenido | RF |
|---|---|---|
| Hoy | Paradas en orden, contador hechas/pendientes, indicador de cola pendiente. Funciona con la app cerrada desde ayer y sin señal. | 18, 25 |
| Parada | Dirección, referencia, destinatario, botón de llamar, bultos, observaciones. | 19, 22 |
| Cierre de parada | Entregado (foto obligatoria + receptor + posición) o fallido (motivo de lista cerrada). Guarda local y sincroniza cuando puede. | 20, 21, 23, 24 |

`/hoy` existe hoy pero es de **solo lectura** (`GET /api/mis-paradas`, filtrado por `Ruta.Estado == "en_curso"` — antes de eso el repartidor no ve nada de una ruta todavía en armado). No hay cierre de parada, no hay cola offline, `/parada/[id]` no existe.

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
| `borrador → confirmado` | Precio calculado y congelado. Dirección apta o el trigger la rechaza al rutear. |
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
| `Deposito:CalleNumero/Localidad/Lat/Lng` | backend, `appsettings.json` | Origen fijo de todo pedido y punto de partida del orden sugerido (`GET /api/ubicaciones/deposito`) |

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
