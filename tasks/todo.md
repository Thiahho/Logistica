# Ciclo completo del repartidor — lista de tareas

Plan en [plan.md](plan.md). Absorbe el plan de la jornada del repartidor (acta 4.7) y suma las
novedades de la calle (acta 4.8, RF-36/RF-37). **No cierra H2/E5**: la cola offline es el plan
siguiente y último.

Estado al **18/09/2026**. `[x]` = escrito **y** verificado como se indica. Las pantallas se verificaron
con `tsc`, `eslint` y compilación bajo `next dev`, **no en un navegador real** (no había herramienta
de navegador): RNF-06 y el trazo del canvas de firma están sin probar en un teléfono.

**Actualización del 23/09/2026:** este archivo sigue el ciclo del repartidor (acta 4.7/4.8). El trabajo
posterior (acta 4.11 a 4.18: zonas automáticas, ubicación por link de Maps, "Mi plan", armado rápido,
detalle de ruta y Maps, flujo del repartidor con DNI, auditoría) se lista al final, en
"Tandas posteriores (22–23/09/2026)". Cambios de estado en este archivo desde el 18/09: **F5** y **F8**
tienen ahora un recorrido verificado en un Chrome real (ver sus ítems), **F7** quedó **parcial** (solo el
número de DNI, sin imagen) y la base de desarrollo se vació de datos de prueba (ver la última sección).

**Nota posterior (21/09/2026):** las pantallas de abajo se volvieron a tocar, sin cambio de contrato,
en `construccion_v1.md` changelog 1.23 (identidad "BF Transportes" — `Shell.tsx` con barra inferior +
drawer, `CabeceraRepartidor.tsx` nuevo en `/hoy/*`). Los `[ ]` de "sin verificar en un teléfono real"
de F5/F6/F8 siguen abiertos y ahora cubren esa UI, no la de esta fecha.

## Fase 0 — Gobernanza de 4.7 (hecha el 17/09/2026)

- [x] Acta 4.7, construcción 1.21, `estado_implementacion.md` (sin adelantar conteos)

## F1 — Retiro firmado (RF-35)

- [x] Migración `AgregarRetiroDeRuta` + `trg_congelar_declaracion_repartidor` (commit `c60d1c6`)
- [x] `MiJornadaController.Retiro` (commit `c60d1c6`)
- [x] Gate `409` en `/llegada` y `/cierre` — verificado con `curl` (`/cierre`: después de la idempotencia)
- [x] `FirmaCanvas` (fondo blanco antes de dibujar), `/hoy/retiro`, tarjeta bloqueante en `/hoy`
- [x] `curl`: sin firma 400 · discrepancia sin observación 400 · completo 200 · repetido `duplicado` · otro uuid 409

## F2 — Siguiente parada

- [x] `CierreResultado.SiguienteParadaId` (también en `duplicado`), `router.replace`, "Cerrar la jornada"
- [x] `curl`: entrega devuelve la siguiente por orden; el reintento devuelve la misma; la última devuelve `null`

## F3 — Cierre de jornada en dos actores (RF-26)

- [x] `POST /api/mi-jornada/cierre` — `curl`: pendientes 409 · km final < inicial 400 · ok 200 · repetido `duplicado` · otro uuid 409
- [x] `/hoy/cierre`; `/hoy` "jornada cerrada / pendiente de revisión"
- [x] Administración: `RutaDetalle` con la declaración, `SinDeclaracionDelRepartidor` (M2), `NotasCierre` obligatorio si difiere (M3), `cerrada_por` (M4), prellenado y columna "Declarado" con delta (M1/M5), `ResultadoRuta.Aprobacion`
- [x] `curl`: sin declaración 409 · corregir sin notas 400 con el delta · aprobar tal cual 200 `tal_cual`
- [x] **Trigger:** `update` a mano sobre `cierre_repartidor_km_final` y `retiro_bultos_contados` → excepción
- [x] `JornadaController.Resumen` + `/jornada`: retiro, declaraciones pendientes, discrepancia de bultos

## F4 — Novedades, backend (acta 4.8)

- [x] Migración `AgregarNovedades`: tabla, checks, `trg_novedades_inmutable`, `ruta_paradas.estado` admite `cancelada`, vista con `pedido_estado`
- [x] `MiJornadaController`: `POST /novedades` (idempotente, whitelist, 404 por ajeno) y `POST /novedades/{id}/visto`
- [x] `NovedadesController`: listar, por ruta, foto, resolver (aceptar aplica el cambio; 409 si el dato cambió)
- [x] `PUT /api/pedidos/{id}/contacto`; cancelar en ruta deja aviso y pasa la parada a `cancelada`
- [x] `MisParadasController.Cerrar` saltea pedidos cancelados
- [x] `POST /api/rutas/{id}/interrumpir` — y la ruta interrumpida se cierra después (repartidor y administración)
- [x] `curl`/SQL: duplicado, categoría inválida, campo no editable, pedido de otra ruta, foto, doble resolución, trigger de novedades

## F5 — PWA del repartidor

- [x] `/hoy` con `useSondeo`, avisos con acuse, "Reportar un problema"
- [x] `/hoy/problema`, "Corregir un dato" en la parada, pedido cancelado tachado, parada `cancelada`
- [x] **Recorrido verificado en un Chrome real el 23/09/2026** (headless, viewport de 390 px, controlado con `puppeteer-core`): login del repartidor → `/hoy` → "Terminé la ruta" → `/hoy/cierre` → kilometraje → "Jornada cerrada" (`POST /api/mi-jornada/cierre` 200)
- [ ] **Sigue sin verificar en un teléfono real** (un dedo, sin scroll, botones ≥ 48 px, canvas de firma) y el formulario de entrega con foto en el navegador

## F6 — Back-office responde

- [x] `PanelNovedades` en `/rutas/[id]` (aceptar/rechazar/resolver, foto, "Interrumpir ruta")
- [x] Contador en `/jornada` y en la navegación; contacto y novedades en el detalle de pedido
- [ ] **Sin verificar en navegador real**

## F7 — Documento del receptor (RF-23 reescrito) — **PARCIAL: número sí (23/09/2026), imagen NO**

La **imagen** del documento está diferida a propósito: la consulta legal de acta §11.2 la bloquea y no afecta a F1–F6.
Lo que sí se hizo el 23/09/2026 (acta 4.17), sin imagen y por lo tanto sin retención ni purga:
- [x] Migración `AgregarDocumentoAPruebaEntrega` (`documento_numero`, `sin_documento_motivo`); entregar exige el DNI (6 a 9 dígitos) **o** un motivo escrito; `identidad_verificada` se deriva en el servidor
- [x] Solo administración lo ve (`GET /api/pedidos/{id}/prueba-entrega`; operación y cliente → 403) y tarjeta "Prueba de entrega" en el detalle del pedido
- [x] `curl`: DNI válido y con puntos 200 · inválido y corto 400 · sin foto 400 · sin DNI y sin motivo 400 · sin DNI con motivo 200 · reintento `duplicado`
Lo que sigue **sin hacerse** (necesita el dictamen legal):
- [ ] Migración `AgregarFotoDocumento`, segunda foto en `Cerrar` (3 MB), `OpcionesPruebaEntrega.RetencionDocumentoDias`
- [ ] Purga idempotente + contador de vencidas, `foto-documento` solo `Administracion`
- [ ] UI en la parada, en el detalle de pedido y aviso en `/jornada`

## F8 — Gobernanza y cierre

- [x] Acta 4.8 (RF-36, RF-37, 18 tablas, changelog), construcción 1.22, `estado_implementacion.md` recontado (34 pantallas, 21 entidades, 107 endpoints, 14 migraciones)
- [x] `dotnet build` 0 errores · `tsc --noEmit` limpio · `eslint app components lib` limpio
- [x] `next build` (pasa)
- [ ] Jornada completa en un teléfono real
- [x] 23/09/2026: `dotnet build` 0 advertencias · `tsc --noEmit` limpio · `eslint .` (todo el proyecto) limpio · `next build` pasa

## Tandas posteriores (22–23/09/2026) — acta 4.9 a 4.18

Detalle técnico en `construccion_v1.md` changelog 1.24 a 1.32 y cifras en `estado_implementacion.md` §3.14 a §3.22.
`[x]` = escrito **y** verificado como se indica; las pantallas, salvo el recorrido del repartidor de arriba,
se verificaron con `tsc`, `eslint`, `next build` y la API, **no en un navegador**.

### Hechas

- [x] **4.9/4.10 (22/09)** — portal de carga del cliente y recepción en depósito; detalle de envío, envío en curso y "mis clientes" (`curl`/`psql`)
- [x] **4.11 Zona automática y precio sugerido** — migración `ZonasAutomaticas`; `curl` contra Nominatim/OSRM reales (4 localidades asignadas en ~9 s, zona manual sobrevive al recálculo)
- [x] **4.12 "Mi plan"** — `plan-del-dia`, línea de tiempo, popup de detalle con mapa y "Abrir en Google Maps", navegación inferior del cliente, botón de orden (`curl`)
- [x] **4.13/4.14 Ubicación por link de Maps** — todos los formatos, rechazos (host ajeno, fuera de Argentina, > 50 km, sin coordenadas), respeto de `Verificada`; lectura de calle y localidad con confirmación del cliente (`curl`; duplicado "Buenos Aires"/"CABA" detectado y corregido)
- [x] **4.15 Bultos, armado rápido y día siguiente** — `curl`: 3 envíos → ruta preparada con 10 bultos; **seed de desarrollo corregido** (no recrea la ruta de demostración en una base ya usada)
- [x] **4.16 Detalle de ruta mobile y ruta en Maps** — 16 casos del generador de links con Node y con el JSON real de `/jornada`
- [x] **4.17 Flujo del repartidor y DNI** — empezar ruta, toda la ruta en Maps, "Terminé la ruta", DNI o motivo por entrega (`curl` completo + Chrome real en `/hoy` → `/hoy/cierre`). Un defecto propio (el formulario dejó de enviar `receptorNombre`) y un botón apagado sin explicación se corrigieron a partir de los reportes de uso
- [x] **4.18 Auditoría de validación, seguridad y carga** — batería de 38 pruebas (30 → 38 correctas), matriz de 125 endpoints, aislamiento entre clientes, base sintética de 100.000 pedidos, 100 usuarios simultáneos; correcciones y migración `IndiceBusquedaPedidos`

### Pendientes (nada de esto está hecho)

- [ ] **Revisión visual en navegador o teléfono real** de: "Mi plan" y su popup, el campo de link de Maps con su confirmación, "Preparar ruta", el armado con totales, el detalle de ruta mobile, el formulario de entrega con DNI y foto, y `/tarifas` con zonas automáticas
- [ ] **Link corto real** (`maps.app.goo.gl`) — solo se probaron formatos largos y texto `lat, lng`; no hay forma de generar uno desde la sesión
- [ ] **Límite de 10 paradas por tramo de Google Maps** — sale de la documentación; probarlo en un teléfono con una ruta de 8 a 10 paradas
- [ ] **Repetir la prueba de concurrencia (100 usuarios) con el código nuevo** — el sistema bloqueó la ejecución en la sesión; las mejoras se deducen de latencias individuales y de `explain analyze`
- [ ] **Medir el efecto real** de la compresión de respuestas y del calentamiento al arrancar (implementados, sin cifra)
- [ ] **Cargar la primera zona desde 0 km** en `/tarifas` (dato de administración: las zonas cargadas empiezan en 5 km, así que 0–5 km quedan sin zona)
- [ ] **Pendientes de `auditoria_seguridad.md`:** contraseña de Postgres en `appsettings.Development.json` (2), formato de email/CUIT (3), política de contraseña (4), límite de tasa del envío masivo de avisos (6), CSP estricta del frontend (9), `shadcn` en `pnpm audit` (13)
- [ ] **Reiniciar backend y frontend** para que tomen el código nuevo: las migraciones `ZonasAutomaticas`, `AgregarDocumentoAPruebaEntrega` e `IndiceBusquedaPedidos` ya están aplicadas a `bd_logistica`; `IndiceBusquedaPedidos` se aplicó con el mismo SQL que genera EF y su fila en `__EFMigrationsHistory` (la herramienta no podía correr con el backend activo)
- [ ] **Imagen del documento (F7)** y la **cola offline** (siguiente sección): sin cambios

## Después: la cola offline

Plan aparte. Envuelve **siete** caminos de escritura de una vez: llegada, cierre de parada, retiro,
cierre de jornada, novedad, acuse y (con F7) la segunda foto. Hasta que pase la prueba de modo avión,
**H2/E5 sigue abierto**.

## Datos de prueba en la base de desarrollo

**Actualizado el 23/09/2026:** a pedido del usuario se eliminaron todos los datos de prueba (pedidos, rutas, paradas,
pruebas de entrega, facturas, pagos, novedades, eventos de cliente, sesiones y direcciones de prueba); se
conservaron usuarios, clientes, usuarios de cliente, zonas, tarifas, localidades, tipos de evento, vehículos, contactos
guardados y el depósito. Los pedidos `TEST-CICLO` de abajo (ids 45–52) ya no existen. Lo que hay hoy en la base son
pruebas manuales del propio usuario (2 pedidos, 1 ruta). Las pasadas de prueba posteriores crearon sus datos con el prefijo
`ZZ` y los borraron al terminar (con `session_replication_role = replica` dentro de una transacción, porque los triggers
de inmutabilidad impiden borrar `pedido_eventos` de otro modo).

**Histórico (18/09/2026):**

8 pedidos marcados `TEST-CICLO` (ids 45–52, estados terminales). No se pudieron borrar: `pedido_eventos`
es de solo inserción. Las rutas 25 y 26, sus paradas, novedades, pruebas de entrega e ítems de factura
de prueba sí se borraron.
