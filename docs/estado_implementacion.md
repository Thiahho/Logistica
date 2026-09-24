# Estado de implementación — relevamiento de código

**Generado:** 13/09/2026, actualizado puntualmente el 15/09/2026 (monitor de jornada, changelog 4.4), el 16/09/2026 (deliverys/recargo por km, changelog 4.5), el 17/09/2026 (disponibilidad de repartidores, changelog 4.6; y en una segunda pasada del mismo día, la fase de gobernanza de changelog 4.7), el 18/09/2026 (novedades de la calle, changelog 4.8 — §3.13), el 21/09/2026 (identidad visual "BF Transportes", `construccion_v1.md` changelog 1.23 — cambio puramente visual, no suma ninguna fila a los conteos de este documento; ver nota debajo de §1), el 22/09/2026 (portal de carga y recepción, changelog 4.9 — §3.14, nueva) una segunda pasada del mismo día (detalle de envío, envío en curso y "mis clientes", changelog 4.10 — §3.15, nueva) y el 23/09/2026 (changelog 4.11 a 4.18 — §3.16 a §3.22, nuevas: zonas automáticas, ubicación por link de Maps, "Mi plan", bultos y armado rápido, detalle de ruta y Maps, flujo del repartidor con DNI, y la auditoría de validación/seguridad/carga; ninguna de estas pasadas es una repasada completa del resto) y el 24/09/2026 (preparación del despliegue en Render + Vercel, changelog 4.19 — §3.23, nueva; no mueve ningún conteo) · **Rama:** `main` (desde el merge squash de `demo-d`, commit `22c6735`, 23/09/2026; `demo-d` queda como historial de trabajo) · **Fuente:** lectura directa del código (backend ASP.NET Core 8 + PostgreSQL, frontend Next.js), cruzado contra `acta_sistema.md` v4.20 y `Anexo_I_Alcance_V2.docx`.

**Regla de este documento, explícita desde la pasada de changelog 4.7:** acá se cuenta lo que existe en el repositorio. Lo diseñado y acordado pero no escrito se nombra como tal y **no suma a ningún conteo** — para eso están `acta_sistema.md` (alcance) y `construccion_v1.md` (diseño). Es la diferencia que hace auditable este inventario.

Este documento no reemplaza a `acta_sistema.md` (reglas de negocio) ni a `construccion_v1.md` (especificación técnica). Es un inventario de qué existe hoy en el repositorio, con la referencia a qué requisito/decisión de las actas cubre cada pieza, para poder auditar alcance sin releer código.

---

## 1. Resumen

| | |
|---|---|
| Controladores API | 21 (sin cambio desde changelog 4.8 — 4.11 a 4.18 extienden controladores existentes, ninguno nuevo) |
| Endpoints | **125** acciones `[Http*]` en `Controllers/*.cs` — conteo mecánico y reproducible: `grep -cE '^\s*\[Http(Get\|Post\|Put\|Patch\|Delete)' backend/Logistica/Controllers/*.cs`, coincide con las 125 operaciones que ahora lista Swagger. Ocho más que las 117 de changelog 4.10: `GET /localidades/con-zona`, `PUT /localidades/{id}/zona/automatica`, `POST /localidades/recalcular`, `GET /localidades/{id}/precio-sugerido` y `GET /mi-cuenta/localidades/{id}/precio-sugerido` (4.11), `POST /ubicaciones/desde-mapa` y su espejo `POST /mi-cuenta/ubicaciones/desde-mapa` (4.14) y `GET /mi-cuenta/plan-del-dia` (4.12) |
| Pantallas frontend (`page.tsx`) | 38 (**sin pantallas nuevas desde changelog 4.10**: 4.11 a 4.18 modifican y extienden las existentes; recontado con `find frontend/app -name page.tsx`, no adelantado) |
| Entidades / tablas núcleo | 22 entidades / 19 tablas núcleo (**sin cambio**: `localidades` suma `lat`, `lng`, `distancia_km_deposito`, `distancia_fuente` y `zona_manual` — 4.11 — y `pruebas_entrega` suma `documento_numero` y `sin_documento_motivo` — 4.17; ninguna tabla ni entidad nueva. `ls Entidades/*.cs` da 23 archivos porque incluye el enum `EstadoPedido.cs`) |
| Migraciones aplicadas | **19** (`Inicial` → `IndiceBusquedaPedidos`; suman `ZonasAutomaticas` (4.11), `AgregarDocumentoAPruebaEntrega` (4.17) e `IndiceBusquedaPedidos` (4.18, requiere la extensión `pg_trgm`)) |
| Roles | administracion, operacion, repartidor (personal interno) + cliente (`clientes_usuarios`, tabla separada) |
| Última etapa cerrada | **E1 — Cuenta corriente y facturación** (Anexo I §5, changelog acta 4.2). El monitor de jornada (4.4), los deliverys/recargo por km (4.5), el panel de repartidores (4.6) y el portal de carga/recepción/"mis clientes" (4.9/4.10) son ampliaciones sobre etapas ya cerradas o adelantadas puntualmente (§9.3, D14/§10.2-N, RF-15/§9.3 y Anexo I §5/E4 respectivamente), no un cierre completo de etapa nueva del Anexo I. |

**Identidad visual "BF Transportes" (`construccion_v1.md` changelog 1.23, 21/09/2026):** rediseño de marca (paleta, tipografía, logo provisorio, navegación mobile) sobre tres referencias en `docs/` — `Paleta.png`, `Logo.png`, `MobileVistaOperador.png`. No suma ni resta un solo controlador, endpoint, pantalla, entidad o migración: por eso no mueve ninguna cifra de la tabla de arriba. Detalle técnico completo en `construccion_v1.md` §1 y changelog 1.23, no repetido acá — es la diferencia de alcance entre los dos documentos (regla del encabezado).

---

## 2. Roles y control de acceso

Definidos en `Program.cs` como políticas de autorización:

| Política | Roles que la cumplen | Uso típico |
|---|---|---|
| `Administracion` | administracion | Tarifas, usuarios, vehículos (ABM), clientes, facturación, cuenta corriente, precio manual |
| `BackOffice` | administracion + operacion | Alta de pedidos, armado de rutas, catálogos operativos, deliverys (`DeliverysController`, changelog 4.5), disponibilidad de repartidores (`RepartidoresController`, changelog 4.6), recepción de pedidos de portal (`PedidosController`, changelog 4.9) |
| `Operacion` | operacion | (reservada, sin uso exclusivo hoy) |
| `Repartidor` | repartidor | `/api/mis-paradas` — superficie de escritura de la PWA |
| `Cliente` | cliente (tabla `clientes_usuarios`) | `/api/mi-cuenta`, `/mis-envios` |
| `Recorrido` | administracion + operacion + repartidor | Ruteo real por calles (OSRM) |

Regla de diseño repetida en varios controladores (`ClientesController`, `UsuariosController`, `VehiculosController`, `PruebasEntregaController`): **sin `[Authorize]` de clase cuando conviven acciones con público distinto**, porque ASP.NET Core combina el atributo de clase y el de acción con AND, no lo reemplaza. El recíproco también vale y se aplica en `JornadaController` y `RepartidoresController`: cuando todas las acciones comparten una sola política, se declara una vez a nivel de clase.

---

## 3. Módulos implementados

### 3.1 Autenticación (`AuthController`)
`POST /login`, `/refresh`, `/logout`, `GET /yo`. Refresh token en cookie httpOnly, rate limiting en login. Cubre RNF-08 (control de acceso por rol) y la auditoría de seguridad del 31/08/2026 citada en el Anexo I §2. **Desde changelog 4.19:** en producción el frontend llama a `/api/*` en su propio origen (reenvío de `next.config.ts`, §3.23) y `AuthProvider` trata cualquier falla del refresco inicial como "sin sesión". **Desde changelog 4.20:** detrás del proxy, el límite de intentos ve la IP real del usuario (`UseForwardedHeaders` + `Web/IpClienteDesdeProxy.cs`, `auditoria_seguridad.md` hallazgo 14, resuelto).

### 3.2 Geografía (`ZonasController`, `UbicacionesController`)
- Zonas A/B/C/D con `km_desde`/`km_hasta` editables (`PUT /api/zonas/{id}/km`) — changelog acta 3.2.
- Catálogo de localidades con búsqueda propia + OSM/Nominatim como fallback, alta automática sin zona (changelog 3.9), y pantalla de asignación de zona por distancia sugerida (changelog 3.10) — `GET /api/localidades/pendientes`, `PUT /api/localidades/{id}/zona`.
- Catálogo de depósitos con nombre único, ABM completo (changelog 3.8) — `GET/POST/PUT/DELETE /api/ubicaciones/depositos`.

### 3.3 Comercial (`ClientesController`, `TarifasController`)
- ABM de clientes, indicadores internos RF-32/RF-33 (`ColorPago`/`ColorTrato`/`ColorOper`, nunca expuestos al rol cliente).
- Tarifas generales por zona (`TarifasController`) y tarifas por cliente (`ClientesController` `PUT /{id}/tarifas/{zonaId}`) — RF-09, comparten `TarifaService` para no duplicar la regla de vigencia.
- Registro de eventos de cliente (`GET/POST /{id}/eventos`) — RF-31, tabla `eventos_cliente`/`tipos_evento_cliente`.
- Login de cliente en tabla separada `clientes_usuarios` (`GET/POST /{id}/usuarios`, activar, cambiar password) — decisión acta 3.3, Anexo I §2 ("dos tablas separadas, no cuatro roles en una").

### 3.4 Pedidos y tarificación (`PedidosController`)
- Alta completa con geocodificación, resolución automática de zona, marca de dirección dudosa (RF-01 a RF-07).
- Cotización en vivo por tipo de vehículo (camioneta/moto) antes de confirmar — changelog 3.11.
- **B9 (Anexo I):** zona sin tarifa → `RequiereCotizacion = true` en vez de rechazar; `PUT /{id}/precio-manual` fija el precio a mano (Administracion), protegido por el mismo trigger de congelamiento que el resto (P1) — changelog 4.1.
- Máquina de estados Borrador → Confirmado → EnRuta, con transición real disparada recién al cerrar planificación de ruta (no al alta) — changelog 3.11.
- **D13 (Anexo I):** tres reprogramaciones gratis por pedido; la 4ta genera un pedido nuevo facturado aparte (`ReintentoCreado`).
- **§10.2-I:** cancelación gratis en Borrador, factura el 100% del precio congelado si ya estaba Confirmado.
- **B16:** ajuste de bultos al retiro, tope de 3 sin cargo, el 4to exige `CargoGestion` obligatorio como ítem de factura separado — flujo de aprobación/rechazo por Administracion (`PUT /{id}/ajustes/{ajusteId}/aprobar|rechazar`).
- **D11/§10.2-L1:** el corte de servicio por deuda vencida solo bloquea altas nuevas (`POST /api/pedidos`), consultando `DeudaVencidaAsync` y `Cliente.CorteSuspendidoHasta` (plan de cuotas, §10.2-L4).
- **§10.2-N (changelog 4.5):** `Cotizar` resuelve un recargo por km (`Servicios/DistanciaService.cs`) cuando el request trae `DestinoUbicacionId` — opcional, el estimado no cambia si no viene. Con `Precio:TramosKm` vacío (default) el recargo siempre da 0.

### 3.5 Planificación y rutas (`RutasController`)
RF-10 a RF-17: candidatos por zona (`GET /api/pedidos/candidatos-ruta`), armado (`PUT /{id}/paradas` reemplaza el set completo mientras la ruta está `planificada`), consolidación de retiros en la misma dirección vía `parada_pedidos`, asignación de vehículo/repartidor, validación de capacidad en paradas (P7, avisa sin bloquear — RF-16), origen de ruta variable (depósito del catálogo u "otra dirección", changelog 3.6/3.8).
`POST /{id}/cerrar-planificacion` (RF-17) transiciona la ruta a `en_curso` y en bloque sus pedidos a Confirmado/EnRuta, congelando ahí el precio final (changelog 3.11).

**Monitor de jornada (changelog 4.4):** `GET /{id}/jornada` expone el mismo bundle de paradas/estado/horarios que la PWA (`Servicios/JornadaService.cs`, extraído de `MisParadasController.Dia`), para cualquier estado de ruta — hasta esta versión una ruta `en_curso` no tenía ninguna vista de back-office. `PUT /{id}/repartidor` (reasignar, con la ruta `planificada` o `en_curso`) y `PUT /{id}/paradas/orden` (reordenar pendientes, RF-12) son las dos acciones nuevas sobre una ruta ya en curso. `GET /api/jornada/resumen` (`JornadaController`, nuevo) agrega contadores por estado de ruta/parada para una fecha — no es el tablero B2 (§6).

### 3.6 Ejecución en calle (`MisParadasController`, `RecorridoController`, `PruebasEntregaController`)
RF-18 a RF-24: lista de paradas del repartidor autenticado (`GET /dia`, bundle único de RNF-07 armado por `Servicios/JornadaService.cs`), registro de llegada (`POST /{paradaId}/llegada`) y cierre de parada con prueba de entrega (foto, receptor, posición, hora — `POST /{paradaId}/cierre`, multipart atómico, idempotente por `device_uuid` con respuesta `duplicado: true` distinguible de un conflicto real). Fotos servidas solo autenticadas, nunca por `wwwroot` estático (RNF-09). Ruteo real por calles vía OSRM (`RecorridoController`, changelog 3.4). Verificación de identidad: número de DNI del receptor o motivo de ausencia (changelog 4.17), con `identidad_verificada` derivado en el servidor; sin imagen del documento — es una versión reducida de RF-23 (ver §3.21).

**Construido desde changelog 4.7/4.8:** gate del retiro (sin retiro firmado, `/llegada` y `/cierre` devuelven `409`), `POST /api/mi-jornada/retiro` y `/cierre` (`MiJornadaController`), `SiguienteParadaId` en el cierre de parada, y las novedades (§3.13). **Sin implementar:** modo sin conexión (B10/RNF-01) — confirmado ausente, no hay cola local ni sincronización diferida. Imagen del documento con retención y purga (RF-23 reescrito): diseñada en acta 4.7, **sin código**; desde 4.17 se guarda solo el número (§3.21).

### 3.7 Cierre y trazabilidad (`RutasController.Cierre`, triggers de base)
RF-26/RF-27: cierre económico con km, combustible, peajes, costos, resultado disponible el mismo día. **Dos actores, desde changelog 4.7 (construido):** el repartidor declara km final, combustible y peajes (`POST /api/mi-jornada/cierre`, inmutable por `trg_congelar_declaracion_repartidor`); `RutasController.Cerrar` exige la declaración salvo `SinDeclaracionDelRepartidor`, pide `NotasCierre` si algún valor difiere del declarado, sella `cerrada_por` y `ResultadoRuta.Aprobacion` marca `tal_cual`/`corregido`/`sin_declaracion` derivado en lectura. RF-28/RNF-04: trazabilidad automática por trigger de base de datos (`trg_*`, ver `schema_v3.sql`), no por código de aplicación — verificado en los comentarios de `Factura`/`Pago` (`trg_facturas_inmutable`, `trg_pagos_inmutable`). `rutas` **no** tiene log de eventos propio: reasignar repartidor, reordenar paradas y cerrar no quedan registrados en ninguna tabla (gap aceptado por escrito en `construccion_v1.md` §4.3); la única huella del actor de cierre es `cerrada_por`. Lo que sí queda registrado desde 4.8 es lo que el repartidor y operación se dijeron durante la jornada (`novedades`).

### 3.8 Cuenta corriente y facturación — E1 (`FacturasController`, `ClientesController`, `Dominio/CiclosFacturacion.cs`, `Servicios/CuentaCorrienteService.cs`)
Última etapa cerrada (Anexo I §5, acta changelog 4.2):
- Tablas `facturas`/`factura_items`/`pagos`, libros de solo inserción con triggers de inmutabilidad; el saldo se deriva por FIFO en lectura (`v_facturas_saldo`), nunca se persiste.
- **D2 revisada / §10.2-A cerrada:** dos ciclos por cliente (quincenal, vencimiento 7 días; mensual, vencimiento 10 días), fechas fijas de calendario (día 15 y último día del mes) — `Dominio/CiclosFacturacion.cs`.
- Cierre de ciclo manual (`POST /api/facturas/cierre`, con dry-run en `GET /cierre/previsualizacion`) — sin scheduler en el proyecto, decisión explícita documentada en el código.
- **D12:** el cliente no elige la imputación de pagos; FIFO contra la factura más antigua, sobrante como saldo a favor.
- **D11/R5 (resuelto):** corte de servicio automático por deuda vencida, sin días de gracia, se levanta solo al pagar el total; plan de cuotas vía `Cliente.CorteSuspendidoHasta/Motivo/Por/En`.
- Registro de pagos (`POST /api/clientes/{id}/pagos`) y extensión manual del corte suspendido (`PUT /{id}/corte-suspendido`), ambos Administracion.
- Portal de cliente de solo lectura sobre su propia cuenta (`MiCuentaController`), resuelve el cliente desde el claim de sesión, nunca desde la URL — nunca expone los semáforos internos.

**Pendiente dentro de E1, declarado sin resolver en la propia acta:** §10.2-L(2) — qué pasa con mercadería ya retirada al momento del corte (R12 del Anexo I) requiere dictamen legal; hoy el sistema no retiene nada.

### 3.9 Exportación (`ExportarController`)
RF-30: CSV de pedidos, rutas y resultados por rango de fechas — "reemplaza el módulo de reportes" (no hay tablero/dashboard, eso es B2, fuera de alcance hasta E3).

### 3.10 Catálogos de soporte
`VehiculosController` (ABM de flota, patente única, vencimientos VTV/seguro, costo/km, capacidad de paradas — changelog 3.1), `UsuariosController` (ABM de personal interno), `TiposEventoClienteController` (catálogo de motivos de evento de cliente).

### 3.11 Deliverys / urgencias y recargo por km (`DeliverysController`, `Servicios/DistanciaService.cs`) — changelog acta 4.5
Resuelve Anexo I D14 (servicio punto a punto ad-hoc, sin retiro programado) y §10.2-N (recargo proporcional al km recorrido — el propio Anexo lo declara cambio de alcance según su §6, no config). No es una entidad nueva: un `Pedido` con `Tipo="delivery"`, sigue en 20 entidades / 17 tablas núcleo (`Migrations/AgregarPrecioPorKm`).
- `GET /api/deliverys` (listado, mismo contrato paginado que `PedidosController.Listar` acotado a `tipo='delivery'`), `POST /api/deliverys/cotizar` (un solo `DesglosePrecio` — a diferencia de `PedidosController.Cotizar`, acá el tipo de vehículo ya está elegido), `POST /api/deliverys` (alta).
- **Vehículo elegido en la alta, no al armar ruta:** a diferencia de un pedido programado (changelog 3.11: precio congelado recién en `CerrarPlanificacion`), un delivery nace directo en `Confirmado` — no hay ruta que lo lleve. Zona sin tarifa (B9) exige `PrecioManual` en el mismo request, sin dejar un Borrador a medio cotizar.
- **Origen arbitrario:** `OrigenUbicacionId` del request, resuelto por el mismo `POST /api/ubicaciones` que cualquier dirección — no usa `OrigenRutaService.PrincipalParaPedidosAsync` (el depósito por default de un pedido programado).
- **`DistanciaService`:** cascada `km_manual` (gana siempre) → `RuteoService`/OSRM (`Distancia:Fuente="ruta"`, default) → `Dominio/Geo.cs` haversine → `null` (⇒ `recargo_km = 0`). Nunca tira — un proveedor externo caído no bloquea una cotización.
- **Fórmula (`Servicios/PrecioService.cs`):** `recargo_km = precio_base × factor_km(km_cobrados)`, tramos de `Precio:TramosKm` (vacío por defecto, sin valores de fábrica). Con tramos vacíos o sin distancia resuelta, el total da igual que antes de esta versión — verificado con curl contra una ruta existente.
- Reusado también por `PedidosController.Cotizar` (opcional, vía `DestinoUbicacionId`) y `RutasController.CerrarPlanificacion` (siempre, por pedido, dentro del mismo bloque que ya cotiza todo antes de escribir nada).
- **Congelamiento:** `km_cobrados`/`recargo_km`/`km_fuente`/`km_manual` quedan protegidos por `fn_congelar_pedido` (P1) una vez confirmado — mismo mecanismo que `precio_manual` (changelog 4.1).
- **Pendiente, no construido en esta versión:** la "ventana de urgencias" del acta §7 (inserción de una urgencia en una ruta ya en curso, una sola ventana a las 13:00, tope de 3 paradas desplazadas) — caso distinto del delivery ad-hoc de D14, sigue sin código propio.

### 3.12 Disponibilidad y carga de repartidores (`RepartidoresController`) — RF-34, changelog acta 4.6

`GET /api/repartidores?desde=&hasta=` y `GET /api/repartidores/{id}?desde=&hasta=` (BackOffice de clase). Una fila por usuario con rol `repartidor` — **incluidos los inactivos** — con su disponibilidad de hoy (`inactivo` > `en_ruta` > `asignado` > `libre`, derivada de `usuarios.activo` y del estado de la ruta de hoy) y el acumulado de paradas del rango. Rango opcional, default últimos 7 días; `400` si viene invertido; `404` en el detalle si el usuario existe pero no es repartidor.

Round-trips constantes, no crecen con la cantidad de repartidores ni de rutas — mismo patrón anti-N+1 de `JornadaController.Resumen` (varias queries planas + composición en memoria). **Listado: 5** (usuarios repartidor · rutas de hoy · paradas de hoy agrupadas por ruta · rutas del rango agrupadas por repartidor · paradas del rango agrupadas por repartidor). **Detalle: 3, o 4 si el rango no incluye hoy** (la disponibilidad es siempre la de hoy y en ese caso cuesta una query extra acotada a `Fecha == hoy`).

Lo que agrega sobre lo que ya existía: `/jornada` parte de las rutas del día y filtra `RepartidorId is not null`, así que **el repartidor sin ruta asignada no aparecía en ninguna pantalla** — justo el que hace falta ver para asignar. Acá la query arranca de `usuarios`.

**Cero tablas, entidades, migraciones e índices nuevos** — sigue en 20 entidades / 17 tablas núcleo. No hay ausencias declaradas (franco/vacaciones/licencia): un repartidor de vacaciones figura `libre`, decisión documentada en acta §4. **No es el tablero B2 (§6) ni liquidación al repartidor (B4/E2, §6)** — no toca `rutas.pago_repartidor` ni muestra importes.

---

### 3.13 Novedades de la calle (`MiJornadaController`, `NovedadesController`) — RF-36/RF-37, changelog acta 4.8

Tabla nueva `novedades` (la 18ª): cinco tipos en una sola fila-forma — `incidencia_ruta`, `problema_carga`, `cambio_propuesto` (origen repartidor) y `cambio_operacion`, `cancelacion` (origen operación); check de coherencia origen/tipo; `trg_novedades_inmutable` impide editar lo informado (solo estado, resolución y `visto_en` se completan). Repartidor: `POST /api/mi-jornada/novedades` (multipart, idempotente por `device_uuid`) y `POST .../{id}/visto`. Back-office: `GET /api/novedades`, `GET /api/rutas/{id}/novedades`, `GET /api/novedades/{id}/foto`, `PUT /api/novedades/{id}/resolver`. Complementos: `PUT /api/pedidos/{id}/contacto` (corrección de teléfono, nombre y observaciones; destino y precio siguen congelados por P1), aviso y parada `cancelada` al cancelar un pedido en ruta (`PedidosController.CambiarEstado`), y `POST /api/rutas/{id}/interrumpir` (`RutasController`). `JornadaController.Resumen` suma `NovedadesAbiertas` por ruta y en total. **Verificado con `curl`/SQL contra la API real** (idempotencia, 404 por parada/pedido ajenos, whitelist de campos, aceptar aplica el cambio, doble resolución 409, cancelación con parada consolidada, interrumpir, cierre posterior de la ruta, trigger de inmutabilidad). Las pantallas **no** se probaron en un navegador real.

### 3.14 Portal de carga del cliente y recepción en depósito (`MiCuentaController`, `PedidosController`) — B5/B13, RF-38/RF-39, changelog acta 4.9

`MiCuentaController` deja de ser de solo lectura: `POST /api/mi-cuenta/pedidos` da de alta un pedido con el `ClienteId` resuelto del claim de sesión (nunca del body), cotiza con `PrecioService.CotizarAsync` usando el tipo de vehículo elegido por el propio cliente y, si la zona tiene tarifa, graba el total cotizado en `precio_manual`/`precio_manual_por`/`precio_manual_en` como precio **vinculante** — no un estimado (mecanismo de B9, changelog 4.1, reusado con un significado nuevo). Zona sin tarifa (B9): el pedido nace igual, sin precio, `requiereCotizacion:true`. Corte horario configurable (`Opciones/OpcionesPortal.cs`, `HoraCorte` = 16:00 provisional) que solo bloquea un pedido cuya `fechaEntrega` es hoy o antes — `409` con la fecha de mañana sugerida; cargar para un día futuro no espera al corte. `MiCuentaController` también suma `POST /ubicaciones`, `GET /localidades/buscar` y `POST /localidades`: espejo de `UbicacionesController`/`LocalidadesController` bajo la policy `Cliente`, necesario porque esos dos controllers son `BackOffice` de clase (regla §2 de este documento) y el rol `cliente` no podía llegar a ellos.

**Hallazgo de esquema corregido en esta misma pasada, no en el diseño previo:** `precio_manual_por` tenía FK a `usuarios` (personal interno); escribir ahí el id de un `clientes_usuarios` (tabla separada desde changelog acta 3.3) violaba esa FK. Migración `AgregarRecepcionPortal` la elimina (`DropForeignKey`) — la columna sigue siendo `uuid`, ahora sin FK, resuelta a mano contra las dos tablas al mostrarla (`PedidosController.Detalle`). Por el mismo motivo, el alta desde el portal llama `GuardarComoAsync(usuarioId: null, ...)`: con el actor en null, `fn_log_estado_pedido` anota `actor_tipo='sistema'` en `pedido_eventos` en vez de intentar la misma FK sobre `actor_usuario_id`.

`PedidosController` suma la recepción (B13): `GET /recepcion-pendiente` (cola de pedidos de portal cargados **hoy** — por `creado_en`, no por `fecha_entrega` — sin recepción confirmada) y `POST /{id}/recepcion` (404 si el pedido no es de portal; 409 si ya salió de `Borrador`, apuntando al ajuste existente, `POST /{id}/ajustes`; 409 si la recepción ya estaba confirmada — idempotencia explícita, mismo criterio que `MiJornadaController.Retiro`; 400 si el conteo difiere sin nota). No hay estado nuevo de pedido ni trigger de inmutabilidad nuevo: la recepción es una edición dentro de `Borrador`, y `bultos_declarados_cliente` (snapshot que graba `MiCuentaController.CrearPedido` una sola vez) nunca se vuelve a exponer en ningún endpoint de edición.

Frontend: `/mis-envios/nuevo` (selector de tipo de vehículo — no existe en el alta interna, que cotiza los dos tipos a la vez — y aviso de corte con botón "Cargar para mañana") y `/recepcion` (cola con confirmación en lote para lo que coincide, corrección con nota fila por fila para lo que no). `SelectorDireccion`/`SelectorLocalidad` suman un prop `basePath` para apuntar al espejo de `MiCuentaController` en vez de a los endpoints de back-office.

**Verificado con `curl`/`psql` contra la base real:** alta con precio vinculante grabado y actor `sistema` sin violar ninguna FK; B9 sin tarifa; corte horario (409 sobre hoy, 201 sobre una fecha futura en el mismo minuto — corregido durante esta misma verificación, la primera versión bloqueaba cualquier fecha, no solo la de hoy); cola de recepción; confirmación sin diferencia, con diferencia sin nota (400), con nota (200); reintento sobre una recepción ya confirmada (409); pedido de origen interno (404). `dotnet build`, `tsc --noEmit`, `eslint` y `next build` limpios. **Las dos pantallas no se probaron en un navegador real** (sin herramienta de navegador disponible en la sesión) — se verificó que compilan y sirven sin error de servidor bajo `next dev`.

### 3.15 Portal del cliente: detalle de envío, envío en curso y "mis clientes" (`MiCuentaController`, `PedidosController`) — RF-40, changelog acta 4.10

Tabla nueva `clientes_destinatarios` (la 19ª — cruza el techo de 18, reversión explícita de la
decisión de acta 3.5, a pedido del cliente, con el dictamen legal pendiente de acta §11.2 cubriéndola
también). `MiCuentaController` suma el ABM: `GET/POST /destinatarios`, `PUT/DELETE
/destinatarios/{id:guid}` — mismo criterio de alcance que el resto del controller (`ClienteId`
siempre del claim), 404 si el id no es de este cliente. Sin trigger de inmutabilidad ni FK desde
`pedidos` — el alta de un envío copia los valores del contacto elegido, no guarda su id.

**Detalle de envío y "en camino ahora" no tocan el backend más allá de una corrección de datos**
(ver abajo): `GET /api/pedidos/{id}` y el filtro `?estado=EnRuta` de `Listar` ya estaban alcanzables
por el rol `cliente` sobre sus propios pedidos desde antes de esta versión — solo faltaba la
pantalla. **Brecha de datos preexistente, cerrada en esta misma pasada, no introducida por ella:**
`PedidosController.Detalle` devolvía el nombre real de un usuario interno en `historial[].actorNombre`
y en `precioManualInfo.fijadoPor` — alcanzable por un `cliente` sobre su propio pedido desde E1, pero
nadie lo notaba porque ninguna pantalla de cliente llamaba a este endpoint hasta ahora. Fix: un
caller `cliente` ve `"Empresa"` en vez del nombre real cuando el actor es de `usuarios`; sigue viendo
el nombre real si el actor es su propia cuenta de `clientes_usuarios`. Administración, sobre el
mismo pedido, sigue viendo el nombre real sin cambios.

Frontend: `components/PedidoDetalleCliente.tsx` (nuevo, no reusa `PedidoDetalleContenido.tsx` — ese
tiene acciones de escritura sin chequeo de rol; el precio a mostrar es `precioManual?.precio ??
total`, no solo `total`, porque un pedido de portal en Borrador tiene el precio vinculante en
`precioManual` — bug real encontrado al probar contra un pedido de portal real), `app/mis-envios/[id]/page.tsx`,
`app/mis-envios/contactos/page.tsx` (mismo patrón que `/depositos`). `components/EstadoBadge.tsx`
suma `EstadoPedidoBadge`. `app/mis-envios/page.tsx`: filas de la lista pasan a `Link` al detalle,
sección "En camino ahora", botón "Mis clientes". `app/mis-envios/nuevo/page.tsx`: combobox "Usar un
cliente guardado" que prellena destinatario y dirección sin geocodificar de nuevo.

**Verificado con `curl`/`psql` contra la base real:** ABM completo de un contacto y 404 sobre un id
ajeno; alta de pedido reusando la dirección de un contacto guardado; `GET /api/pedidos/{id}` sobre
un pedido con historial de un actor `usuario` real, comparando la respuesta como cliente
(`"Empresa"`) y como administración (nombre real) sobre el mismo pedido; filtro `?estado=EnRuta`.
`dotnet build`, migración aplicada, `tsc --noEmit`, `eslint` y `next build` limpios. **Las tres
pantallas nuevas no se probaron en un navegador real** (sin herramienta de navegador disponible en
la sesión) — se verificó que sirven sin error de servidor bajo `next dev`.

### 3.16 Zona automática de las localidades y precio sugerido (`ZonaLocalidadService`, `LocalidadesController`, `MiCuentaController`) — changelog acta 4.11

Revierte a propósito el criterio "la zona nunca se infiere sola" (changelog 3.9/3.10). `Localidad` guarda su centro (`lat`/`lng`, geocodificado **en el servidor** por nombre y partido — nunca aportado por el cliente del portal), la distancia al depósito principal (0 km = el propio depósito; `DistanciaService`: ruta OSRM con caída a línea recta) y `zona_manual`. `Servicios/ZonaLocalidadService.cs` mide y asigna la zona cuyo rango `[km_desde, km_hasta)` cubre esa distancia (rangos que sigue cargando administración en `/tarifas`, sin valores de fábrica); sin depósito, sin coordenadas o fuera de todo rango la localidad queda sin zona ("pendientes"). Una zona fijada a mano (`zona_manual`) no se pisa nunca; las zonas que ya existían quedaron como manuales en la migración `ZonasAutomaticas`. Cambiar un rango reasigna las automáticas con el km ya guardado (sin red); cambiar de depósito exige `POST /api/localidades/recalcular` (Nominatim admite ~1 req/s, así que es lento a propósito). Precio sugerido al elegir la localidad: solo tarifa de la zona (sin recargos), camioneta y moto, con la lista propia del cliente (`GET /api/mi-cuenta/localidades/{id}/precio-sugerido`; espejo interno con `?clienteId=`). Frontend: `/tarifas` (nota "0 km = el depósito", botón "Recalcular distancias y zonas", localidades sin zona con su motivo, lista con etiqueta automática/manual y "Volver a automática"), `components/PrecioSugeridoLocalidad.tsx` en `/mis-envios/nuevo`. **Verificado con `curl` contra Nominatim y OSRM reales**: 4 localidades sin zona quedaron asignadas en ~9 s, alta de una localidad nueva con zona automática, zona manual sobreviviendo al recálculo, "volver a automática", precio sugerido con tarifa propia. **Hallazgo de datos:** las zonas cargadas empezaban en 5 km (hueco 0–5 km): para que el depósito y las localidades cercanas reciban zona, la primera debe empezar en 0 — el aviso ya existía, la carga sigue siendo de administración.

### 3.17 Ubicación exacta por link de Google Maps y lectura de la dirección (`EnlaceMapaService`, `DireccionDesdeMapaService`, `UbicacionService`) — changelog acta 4.13/4.14

Campo opcional "Link de Google Maps" al cargar una dirección (portal, "Mis clientes" y alta interna). `Servicios/EnlaceMapaService.cs` saca las coordenadas de un link largo (pin `!3d/!4d` sobre `@`, `q`, `ll`, `query`, `destination`), de uno corto (`maps.app.goo.gl`: el servidor sigue el redirect **a mano**, con tope de saltos, timeout y solo hacia hosts de Google — sin SSRF) o de un "lat, lng" pegado; cualquier URL que no sea https de Google se rechaza antes de interpretarla. `UbicacionService.ResolverConMapaAsync` guarda el punto (`geo_proveedor='google_maps'`, `geo_confianza='alta'`) sin pasar por Nominatim, **nunca tumba el alta** (link inservible, fuera de la Argentina o a más de 50 km del centro de la localidad → geocodificación normal más `urlMapaError`) y no mueve una ubicación `Verificada` por administración. `DireccionDesdeMapaService` (servicio aparte para evitar una dependencia circular con `OrigenRutaService`) convierte el punto en calle, número y localidad (geocodificación inversa de Nominatim), busca la localidad en el catálogo —"Buenos Aires" de OSM se resuelve a "CABA"— o la da de alta con zona automática. En el portal el cliente **confirma o corrige** lo leído antes de que la dirección quede resuelta ("Verificá la dirección de tu link"); la alta interna (`/pedidos/nuevo`) solo toma el punto, sin autocompletar. **Verificado con `curl`** (todos los formatos, rechazos, respeto de `Verificada`, puntos reales de CABA/Morón/Tigre/Pilar) y un duplicado real de localidad detectado y corregido en la misma pasada. **No se probó el link corto real ni la pantalla de confirmación en un navegador.**

### 3.18 Portal del cliente "Mi plan" (`MiCuentaController`, `PedidosController.Detalle`) — changelog acta 4.12

`/mis-envios` pasa a "Mi plan": los envíos de hoy con su línea de tiempo de estados (`components/LineaTiempoEstados.tsx`, sondeo cada 30 s sobre `GET /api/mi-cuenta/plan-del-dia` — sin nombres de actor, RF-33), cuenta compacta y listado completo con botón de orden (más nuevos/más viejos primero, el `orden=fecha|-fecha` que ya soportaba `Listar`). Tocar un envío abre el detalle en un popup (`components/DetalleEnvioSheet.tsx`: hoja inferior en el teléfono, modal desde `md`); `PedidoDetalle` suma `destinoLat`/`destinoLng` y el detalle muestra la ubicación con mapa y "Abrir en Google Maps" (`components/UbicacionEnvio.tsx`). El rol cliente suma barra de navegación inferior (Mi plan, Cargar envío, Mis clientes) mediante `ShellConBarra`, que reemplaza la barra duplicada del repartidor. **Verificado con `curl`** (`plan-del-dia`, coordenadas del detalle); **las pantallas no se abrieron en un navegador real.**

### 3.19 Bultos, armado rápido y ruta del día siguiente (`PedidosController`, `RutasController`) — changelog acta 4.15

`PedidoResumen.Bultos` y `RutaResumen.CantidadBultos` (suma sobre `parada_pedidos`, una subconsulta por fila): columna de bultos en `/pedidos` y `/rutas`, y en el armado totales por zona, por parada y de lo seleccionado, "Seleccionar toda la zona"/"Seleccionar todos" (solo direcciones aptas) y preselección con `?todos=1`. `/rutas` suma "Preparar la ruta del día siguiente": pedidos y bultos sin ruta para una fecha (sondeo cada 30 s) y "Preparar ruta", que abre la planificada de esa fecha o la crea. **Corrección de un defecto del seed de desarrollo:** `DatosSemilla` recreaba la ruta de demostración cada vez que no había rutas y fallaba con más de un repartidor (bloqueaba el arranque del backend tras una limpieza de datos); ahora solo la siembra en una base recién creada. **Verificado con `curl`**: 3 envíos de cliente → bultos en el listado, ruta preparada con 10 bultos, candidatos restantes.

### 3.20 Detalle de ruta para el teléfono y ruta en Google Maps (`lib/dominio/googleMaps.ts`, `components/RutaEnGoogleMaps.tsx`) — changelog acta 4.16

`/rutas/[id]` se reordena para mobile (acciones en grilla de 44 px, resumen con paradas/pedidos/bultos, lista de paradas antes que el mapa —lado a lado desde `md`—, filas de tres niveles con "Cómo llegar" y "Llamar", diálogos con pie apilable). `enlacesRutaGoogleMaps` arma links de Maps con el punto de salida y las paradas en orden (`origin` + `waypoints` + `destination`), con coordenadas o, si faltan, la dirección como texto; parte la ruta en tramos de hasta 10 paradas (los puntos intermedios están limitados por Maps) y, con la ruta en curso, solo incluye las pendientes desde la ubicación actual. **Verificado**: 16 casos del generador con un script de Node (1, 10, 11 y 25 paradas, sin coordenadas, sin origen, en curso, cerrada, desordenadas) y con el JSON real de `/api/rutas/{id}/jornada`. **El límite de 10 por tramo sale de la documentación de Maps y no se probó en un teléfono; las pantallas no se revisaron visualmente.**

### 3.21 Flujo del repartidor: empezar ruta, toda la ruta en Maps, terminar la ruta y DNI (`MisParadasController`, `app/hoy/*`) — changelog acta 4.17

"Empezar ruta" es el retiro firmado de siempre (RF-35), presentado como el arranque, con la hora de salida visible; `/hoy` suma `RutaEnGoogleMaps` (salida + todas las paradas), un bloque "Terminé la ruta" que indica cuántas paradas faltan y `AvisoParadaHecha` ("Parada N cerrada · quedan M"). En `/hoy/cierre` el botón final aparecía apagado sin explicación hasta cargar el kilometraje (se reportó como "no pasa nada"): ahora dice qué falta. **RF-23, versión reducida:** `pruebas_entrega` suma `documento_numero` y `sin_documento_motivo`; cada entrega exige el DNI del receptor (6 a 9 dígitos, se aceptan puntos) **o** un motivo escrito de por qué no lo dio, además de foto y nombre; `identidad_verificada` se deriva en el servidor. **Sin imagen del documento** (sigue bloqueada por la consulta legal de acta §11.2). El DNI solo lo ve administración (`GET /api/pedidos/{id}/prueba-entrega`, tarjeta "Prueba de entrega" en el detalle del pedido); operación y cliente reciben 403. **Verificado con `curl`** (DNI válido, con puntos, inválido, corto, sin foto, sin DNI con y sin motivo, reintento duplicado, cierre sin retiro 409, permisos por rol) **y en un Chrome real** (`puppeteer-core`, viewport de 390 px): `/hoy` → "Terminé la ruta" → `/hoy/cierre` → kilometraje → "Jornada cerrada" (`POST /api/mi-jornada/cierre` 200). Un defecto propio de esta tanda —el formulario dejó de enviar `receptorNombre`— se detectó por el reporte de uso, no por las pruebas con `curl`, que enviaban el campo a mano.

### 3.22 Auditoría de validación, seguridad y carga (`Web/LimitesDeEntrada.cs`, `Program.cs`, `CalentamientoService`) — changelog acta 4.18

Detalle completo y cifras en `auditoria_seguridad.md` (sección del 23/09/2026) y `resultados_test_sistema.md` (§10). Cambios en el código: tope de paginación (100 por página, 500 sin paginar) en los seis listados paginados; filtro global que rechaza campos de texto desmedidos, más límites y ventana de fechas en las altas de pedido; tope de 5 MB por body; cabeceras de seguridad en la API y en el frontend (`next.config.ts`); límite de tasa `"geo"` en los endpoints que consultan Nominatim/OSRM; compresión Brotli/Gzip; calentamiento al arrancar; búsqueda de pedidos con `ILIKE` sobre un índice de trigramas (migración `IndiceBusquedaPedidos`); exportes acotados a 366 días; y Swagger (`/swagger`), que devolvía 500 por nombres de DTO repetidos, vuelve a funcionar.

### 3.23 Preparación del despliegue (`backend/Logistica/Dockerfile`, `frontend/proxy.ts`, `lib/auth/AuthProvider.tsx`) — changelog acta 4.19 y 4.20

**Actualización 4.20 (24/09/2026):** el reenvío de `/api/*` pasó de `next.config.ts` a `proxy.ts`, que agrega la IP del usuario y un secreto compartido; el backend suma `UseForwardedHeaders` y `Web/IpClienteDesdeProxy.cs` (hallazgo 14). `RuteoService` admite OpenRouteService (`Ruteo:Proveedor`, hallazgo de OSRM en producción). Las fotos pueden ir a Cloudinary (`AlmacenamientoFotosCloudinary`, `Almacenamiento:Proveedor`, hallazgo 15) y el contenedor corre sin `root` (hallazgo 16). Existe por primera vez un proyecto de pruebas, `backend/Logistica.Tests` (xUnit, 14 pruebas). Sigue sin existir: aplicación de migraciones o alta del primer administrador en producción, configuración de Render/Vercel versionada y CI. Ningún conteo de §1 cambia. Lo que sigue es el texto de la 4.19.

Lo que existe en el repositorio: un `Dockerfile` de dos etapas para el backend (`sdk:8.0` → `aspnet:8.0`, escucha en `http://+:10000`, el puerto de Render) con su `.dockerignore` (fuera de la imagen: `bin/`, `obj/`, `almacenamiento/`, `appsettings.Development.json`, `*.http`); en el frontend, `rewrites()` que con `BACKEND_URL` reenvía `/api/*` al backend, y `AuthProvider` con `API_URL` vacío por defecto (mismo origen) y el refresco inicial dentro del `try`. Fuera de `Development` el backend no expone Swagger ni corre el seed. **Lo que no existe:** montaje de un disco persistente o almacenamiento externo para fotos y firmas (`RaizFotos` sigue siendo una carpeta local del contenedor), `UseForwardedHeaders`, aplicación de migraciones al arrancar o un script para hacerlo, un mecanismo para crear el primer administrador en una base vacía, configuración de Render/Vercel versionada (`render.yaml`, `vercel.json`) y cualquier pipeline de CI. Detalle y lista de pasos en `construccion_v1.md` §9/§10 (changelog 1.33) y riesgos en `auditoria_seguridad.md` (hallazgos 14 a 17). **Sin endpoints, pantallas, entidades ni migraciones nuevas — los conteos de §1 no cambian.** No se construyó la imagen ni se probó el reenvío en Vercel en esta pasada: el despliegue real no está verificado.

## 4. Pantallas del frontend (38)

**El conteo pasó de 36 a 38 cuando el código existió, no antes:** `/mis-envios/[id]` y `/mis-envios/contactos` (changelog 4.10). Recontado con `find frontend/app -name page.tsx`. **Sin pantallas nuevas en 4.11–4.18**: `/mis-envios` ahora es "Mi plan" (4.12), `/rutas` suma "Preparar la ruta del día siguiente" y columna de bultos, `/rutas/[id]` se reordena para mobile y suma el botón de Maps (4.16), `/rutas/[id]/armar` suma totales y selección masiva (4.15), `/hoy`, `/hoy/retiro`, `/hoy/cierre` y `/hoy/parada/[paradaId]` cambian según el flujo del repartidor (4.17) y `/tarifas` suma la gestión de zonas automáticas (4.11). Este documento cuenta lo que existe, no lo que está acordado — y que exista no es que se haya probado en un teléfono: de las pantallas de esta ronda solo el recorrido `/hoy` → `/hoy/cierre` se abrió en un Chrome real (headless, 390 px); el resto se verificó con `tsc`, `eslint`, `next build` y la API.

| Ruta | Pantalla |
|---|---|
| `/` | Home |
| `/login` | Login |
| `/pedidos`, `/pedidos/nuevo`, `/pedidos/[id]` | Carga y detalle de pedidos |
| `/deliverys`, `/deliverys/nuevo` | Deliverys/urgencias punto a punto — alta con origen y destino propios, precio en vivo con recargo por km (changelog 4.5) |
| `/jornada` | Monitor del día en curso: contadores por estado, rutas del día, panel por repartidor (changelog 4.4) |
| `/repartidores`, `/repartidores/[id]` | Disponibilidad de hoy y carga acumulada por rango; incluye al repartidor sin ruta, que `/jornada` no muestra (changelog 4.6) |
| `/rutas`, `/rutas/nueva`, `/rutas/[id]`, `/rutas/[id]/armar`, `/rutas/[id]/cierre` | Planificación, detalle (cualquier estado, changelog 4.4) y cierre de rutas |
| `/hoy`, `/hoy/parada/[paradaId]` | PWA del repartidor — mapa, recorrido, avisos de operación con acuse (sondeo cada 20 s), cierre de parada con foto y salto a la siguiente, "Corregir un dato". Navegación propia del repartidor en `Shell` (barra inferior en pantalla chica, sidebar desde `md`). Sin cola offline (H2/E5 abierto) |
| `/hoy/retiro`, `/hoy/cierre`, `/hoy/problema` | Retiro con conteo firmado (RF-35), cierre de jornada del repartidor (RF-26, declaración pendiente de revisión) y reporte de un problema (RF-36) |
| `/mis-envios`, `/mis-envios/nuevo`, `/mis-envios/[id]` | Portal del cliente: consulta de cuenta, alta de pedido propio con precio vinculante (B5, changelog 4.9) y detalle de un envío (changelog 4.10) |
| `/mis-envios/contactos` | "Mis clientes" — libreta de destinatarios que el cliente registra a mano, reutilizable al cargar un envío (RF-40, changelog 4.10) |
| `/recepcion` | Cola de conciliación de bultos declarados por el cliente vs. cargados, para pedidos de portal (B13, changelog 4.9) |
| `/clientes`, `/clientes/nuevo`, `/clientes/[id]` | ABM de clientes |
| `/usuarios`, `/usuarios/nuevo` | ABM de usuarios internos |
| `/vehiculos`, `/vehiculos/nuevo`, `/vehiculos/[id]` | ABM de flota |
| `/tarifas` | Tarifas + localidades sin zona |
| `/depositos` | Catálogo de depósitos |
| `/facturas` | Facturación / cuenta corriente (E1) |
| `/cobranza` | Panel de riesgo de cuenta corriente y aviso de vencimiento (changelog 4.3) |
| `/exportar` | Exportación CSV |

---

## 5. Modelo de datos

22 entidades (`Entidades/*.cs`), agrupadas según §4 de `acta_sistema.md`:

| Bloque | Tablas |
|---|---|
| Geografía | `zonas`, `localidades`, `ubicaciones` |
| Comercial | `clientes`, `tarifas`, `clientes_destinatarios` |
| Núcleo | `pedidos`, `pedido_eventos` |
| Operación | `rutas`, `ruta_paradas`, `parada_pedidos`, `pruebas_entrega`, `vehiculos`, `novedades` |
| Registro sin maquinaria | `tipos_evento_cliente`, `eventos_cliente` |
| Cuenta corriente (E1) | `facturas`, `factura_items`, `pagos` |
| Usuarios | `usuarios`, `clientes_usuarios`, `refresh_tokens` |

DDL completo en `docs/schema_v3.sql`. Historial de migraciones (19, cronológico; las tres últimas son `ZonasAutomaticas` (4.11 — agrega las columnas de medición a `localidades` y marca como manuales las zonas ya cargadas), `AgregarDocumentoAPruebaEntrega` (4.17) e `IndiceBusquedaPedidos` (4.18 — extensión `pg_trgm` más un índice GIN sobre `pedidos.destinatario_nombre`); antes de ellas, las cuatro últimas eran `AgregarRetiroDeRuta` (4.7), `AgregarNovedades` (4.8), `AgregarRecepcionPortal` (4.9 — también elimina la FK de `pedidos.precio_manual_por`, no solo agrega columnas) y `AgregarClienteDestinatarios` (4.10)):
`Inicial` → `ReglasDeBaseDeDatos` → `AgregarVehiculos` → `AgregarKmZonas` → `SepararUsuariosCliente` → `AgregarOrigenRuta` → `AgregarCatalogoDepositos` → `AgregarTipoVehiculo` → `AgregarPrecioManual` → `AgregarCuentaCorriente` → `AgregarTipoEventoAvisoCobranza` → `AgregarPrecioPorKm` → `AgregarRetiroDeRuta` → `AgregarNovedades` → `AgregarRecepcionPortal` → `AgregarClienteDestinatarios` → `ZonasAutomaticas` → `AgregarDocumentoAPruebaEntrega` → `IndiceBusquedaPedidos`.

Nota: `AgregarTipoEventoAvisoCobranza` (changelog 4.3, una fila de catálogo) faltaba en esta lista desde su propia versión — el conteo de "10" en §1 antes de esta pasada ya estaba desactualizado; quedó corregido acá de paso, no es parte del alcance de changelog 4.5.

---

## 6. Fuera de alcance / no construido (confirmado por lectura de código)

Coincide con lo que el Anexo I declara en §4/§7 como brecha o exclusión — se lista acá solo lo verificado en esta pasada, no una copia del Anexo:

- **Modo sin conexión de la PWA (B10 / RNF-01 / H2-E5):** no hay cola local ni sincronización diferida, ni `manifest.json`, ni service worker, ni Dexie en `package.json` — verificado archivo por archivo. Lo que sí existe y la cola no tiene que reinventar: `device_uuid` generado en la captura y persistido (`lib/captura/dispositivo.ts`), compresión de la foto antes de enviar (`lib/captura/foto.ts`) e idempotencia del lado del servidor. **Las pantallas de `/hoy` sí están construidas** desde changelog 3.4 — `construccion_v1.md` §4.2 decía "sin construir" hasta la corrección de 1.21. Lo que mantiene H2/E5 abierto es solo la cola: sin ella no se puede correr la prueba de modo avión (criterio de aceptación 3 del acta), y por decisión del 17/09/2026 se construye **después** de la tanda de changelog 4.7, para envolver los cinco caminos de escritura de una sola vez en vez de migrarlos de a uno.
- **Imagen del documento con retención y purga (RF-23 reescrito, changelog 4.7):** diseñada y acordada, **sin código** — ni segunda foto en el cierre de parada, ni purgado, ni endpoint de la imagen. Bloqueada por la consulta legal de acta §11.2. **Desde el changelog 4.17 sí existe la versión reducida**: el número de documento (`documento_numero`) o el motivo por el que no se dio, sin imagen, visible solo para administración (§3.21). El retiro firmado, el cierre en dos actores y las novedades (changelog 4.7/4.8) también están construidos: ver §3.6, §3.7 y §3.13.
- **Tablero de indicadores (B2):** sigue sin construir. `/jornada` (changelog 4.4) agrega contadores y `/repartidores` (changelog 4.6) suma paradas por rango, pero ninguno calcula las 10 métricas que B2 prevé (entregas/día, km, tiempo, margen, NPS, ocupación de flota) ni compara períodos entre sí ni arma series: el acumulado de `/repartidores` es una sumatoria recalculada en cada request, sin persistir, para decidir a quién asignar — mismo encuadre que `/cobranza` (changelog 4.3). `ExportarController` sigue siendo la única vía de análisis histórico (CSV).
- **E2 — B3, B4 y B7: diseñados y acordados el 24/09/2026 (acta 4.21, `diseño_e2_rangos_liquidacion.md`), sin código.** No suman a ningún conteo hasta que existan. Lo que sigue describe el código actual.
- **Motor de rango de cliente (B3):** los tres indicadores (`ColorPago/Trato/Oper`) son manuales, no hay cálculo periódico ni efecto sobre tarifa/prioridad/crédito.
- **Liquidación al repartidor (B4):** no hay controlador ni tabla de liquidación; el pago se sigue tipeando a mano en el cierre de ruta. `RepartidoresController` (changelog 4.6) **no** lo acerca: no lee ni escribe `rutas.pago_repartidor` y no expone un solo importe, ni en el listado ni en el detalle.
- **Notificaciones (B6):** sigue sin construir *como sistema de notificaciones*, pero la línea previa ("sin integración de correo/WhatsApp en el código") quedó desactualizada en changelog 4.3 y no se había corregido acá. Lo que sí existe, y solo para el aviso de vencimiento del panel de cobranza: `Servicios/EmailService.cs` (HttpClient tipado contra la API REST de Resend, best-effort, nunca propaga excepción), `Servicios/AvisosCobranzaService.cs`, `Servicios/PlantillasAviso.cs` y `Dominio/EnlaceWhatsApp.cs` (link `wa.me` con el mensaje precargado — **sin API de WhatsApp**, Anexo I R7). Lo que no existe: notificación al cliente por cambio de estado del pedido, aviso al repartidor, y cualquier envío que no lo dispare una persona desde la pantalla — el sistema sigue sin scheduler.
- **Costos fijos y rentabilidad objetivo (B7):** el margen se calcula por ruta, no hay tabla de costos fijos ni prorrateo.

---

## 7. Fuentes

- `docs/acta_sistema.md` v4.20 (reglas de negocio vigentes)
- `docs/Anexo_I_Alcance_V2.docx` v1.0 (alcance comercial, brechas, decisiones D1-D15, definiciones §10.2)
- `docs/construccion_v1.md` (especificación técnica, changelog detallado)
- `docs/schema_v3.sql` (DDL)
- Lectura directa de `backend/Logistica/{Controllers,Entidades,Dominio,Datos}` y `frontend/app` en la rama `demo-d`, 13/09/2026 (§3.11 y los conteos de §1/§4/§5 actualizados puntualmente el 16/09/2026 para changelog 4.5; §3.12, los conteos de §1/§4 y tres ítems de §6 —B2, B4, B6— actualizados el 17/09/2026 para changelog 4.6, con `RepartidoresController` releído línea por línea — no es una repasada completa del resto del documento). Segunda pasada del 17/09/2026 para la fase de gobernanza de changelog 4.7: §3.6, §3.7, la nota de §4, el ítem de modo sin conexión de §6 y la regla del encabezado, con `MisParadasController`, `JornadaService`, `AlmacenamientoFotos`, `app/hoy/*` y `app/rutas/[id]/cierre` releídos — **ningún conteo se movió, porque no se escribió código en esa pasada**
- Pasada del 18/09/2026 (changelog 4.8, `next build` incluido — pasa): §1, §3.6, §3.7, §3.13 nuevo, §4, §5 y §6, con `MiJornadaController`, `NovedadesController`, `MisParadasController`, `RutasController`, `PedidosController`, `JornadaController` y `frontend/app/hoy/*` releídos. Conteos recontados mecánicamente. **Las pantallas nuevas pasan `tsc`, `eslint` y compilan bajo `next dev`, pero no se abrieron en un navegador**; los flujos de API sí se probaron con `curl` y `psql`.
- Pasada del 21/09/2026 (`construccion_v1.md` changelog 1.23, identidad visual): nota bajo §1 de este documento, más `docs/schema_v3.sql`, `docs/acta_sistema.md` y los conteos §1/§4/§5 revisados por si el rediseño tocaba alguno — no tocó ninguno, es CSS/componentes de presentación sobre pantallas y endpoints que ya existían. Fuentes de diseño nuevas: `docs/Paleta.png`, `docs/Logo.png`, `docs/MobileVistaOperador.png` (agregadas al repositorio en el mismo commit). **No se abrió ninguna pantalla en un navegador real** — se revisó el diff de `frontend/app/globals.css`, `frontend/components/Shell.tsx` y los componentes nuevos (`LogoBF`, `CabeceraRepartidor`, `OrdenMovil`, `ui/drawer.tsx`) contra las tres referencias.
- Pasada del 22/09/2026 (portal de carga y recepción, `construccion_v1.md` changelog 1.24, acta 4.9 — RF-38/RF-39): §1, §3.14 nueva, §4, §5 y la fila de B5 retirada de §6, con `MiCuentaController`, `PedidosController`, `PedidoConfiguration`, `Dominio/Reloj.cs` y `frontend/app/{mis-envios,recepcion}/*` releídos. Conteos recontados mecánicamente (los mismos comandos de §1). **Verificado con `curl` y `psql` contra la base real de desarrollo**, incluida la migración aplicada (`dotnet ef database update`) y el hallazgo de la FK de `precio_manual_por` corregido en la misma pasada, no detectado antes de escribir código. **Ninguna de las dos pantallas nuevas se abrió en un navegador real** — sin herramienta de navegador disponible en la sesión; se verificó que sirven sin error bajo `next dev` y que `next build` las compila.
- Segunda pasada del 22/09/2026 (detalle de envío, envío en curso y "mis clientes", `construccion_v1.md` changelog 1.25, acta 4.10 — RF-40): §1, §3.15 nueva, §4, §5, con `MiCuentaController`, `PedidosController.Detalle`, `EstadoBadge.tsx` y `frontend/app/mis-envios/*` releídos. Conteos recontados mecánicamente. **Verificado con `curl`/`psql` contra la base real**, incluida la migración aplicada y la brecha de nombres de actor interno en `PedidosController.Detalle` (preexistente, cerrada en esta misma pasada — comparado explícitamente el mismo pedido como cliente y como administración). **Ninguna de las tres pantallas nuevas se abrió en un navegador real** — sin herramienta de navegador disponible en la sesión; se verificó que sirven sin error bajo `next dev` y que `next build` las compila.
- Pasada del 23/09/2026 (changelog 4.11 a 4.18, `construccion_v1.md` changelog 1.26 a 1.32): §1, §3.16 a §3.22 nuevas, §3.6, §4, §5 y el ítem de RF-23 de §6, con `ZonaLocalidadService`, `EnlaceMapaService`, `DireccionDesdeMapaService`, `UbicacionService`, `MiCuentaController`, `PedidosController`, `RutasController`, `MisParadasController`, `Program.cs`, `Web/LimitesDeEntrada.cs` y las pantallas de `frontend/app/{hoy,rutas,mis-envios,tarifas,pedidos}` releídos. Conteos recontados mecánicamente (125 endpoints, 38 pantallas, 22 entidades/19 tablas, 19 migraciones). **Verificado con `curl`/`psql`/Node contra la base real y contra una base sintética de 100.000 pedidos**, `dotnet build` sin advertencias, `tsc --noEmit`, `eslint` (todo el proyecto) y `next build` limpios. **Solo el recorrido `/hoy` → `/hoy/cierre` del repartidor se recorrió en un navegador real** (Chrome headless controlado con `puppeteer-core`); las demás pantallas nuevas o modificadas no se revisaron visualmente.
- Pasada del 24/09/2026 (preparación del despliegue, `construccion_v1.md` changelog 1.33, acta 4.19), ya sobre la rama `main`: encabezado, §3.1, §3.23 nueva y esta lista, con los commits `6d004a9`, `8833cbb` y `958957c` releídos más `Program.cs`, `AuthController`, `AlmacenamientoFotos`, `appsettings.json` y `frontend/next.config.ts`. **Solo lectura de código y configuración**: no se construyó la imagen Docker, no se compiló el frontend ni se probó nada contra Render o Vercel. Los conteos de §1 no se recontaron porque ninguno de esos commits toca controladores, pantallas, entidades ni migraciones.
- Segunda pasada del 24/09/2026 (infraestructura de producción, `construccion_v1.md` changelog 1.34, acta 4.20): §3.1 y §3.23. `dotnet build` sin advertencias, `dotnet test` (14/14), `tsc` y `eslint` limpios; verificado en vivo con el backend y `next dev` (límite de login detrás del proxy, recorrido con OSRM y con `ninguno`). **No verificado:** OpenRouteService y Cloudinary contra cuentas reales, y la imagen Docker. Ningún controlador, pantalla, entidad ni migración nueva: los conteos de §1 no cambian.
