# Estado de implementación — relevamiento de código

**Generado:** 13/09/2026, actualizado puntualmente el 15/09/2026 (monitor de jornada, changelog 4.4), el 16/09/2026 (deliverys/recargo por km, changelog 4.5) y el 17/09/2026 (disponibilidad de repartidores, changelog 4.6; y en una segunda pasada del mismo día, la fase de gobernanza de changelog 4.7 — ninguna de las cuatro es una repasada completa del resto) · **Rama:** `demo-d` · **Fuente:** lectura directa del código (backend ASP.NET Core 8 + PostgreSQL, frontend Next.js), cruzado contra `acta_sistema.md` v4.7 y `Anexo_I_Alcance_V2.docx`.

**Regla de este documento, explícita desde la pasada de changelog 4.7:** acá se cuenta lo que existe en el repositorio. Lo diseñado y acordado pero no escrito se nombra como tal y **no suma a ningún conteo** — para eso están `acta_sistema.md` (alcance) y `construccion_v1.md` (diseño). Es la diferencia que hace auditable este inventario.

Este documento no reemplaza a `acta_sistema.md` (reglas de negocio) ni a `construccion_v1.md` (especificación técnica). Es un inventario de qué existe hoy en el repositorio, con la referencia a qué requisito/decisión de las actas cubre cada pieza, para poder auditar alcance sin releer código.

---

## 1. Resumen

| | |
|---|---|
| Controladores API | 21 (suma `MiJornadaController`, changelog 4.7, y `NovedadesController`, changelog 4.8) |
| Endpoints | 107 acciones `[Http*]` en `Controllers/*.cs` — conteo mecánico y reproducible: `grep -cE '^\s*\[Http(Get\|Post\|Put\|Patch\|Delete)' backend/Logistica/Controllers/*.cs`. Diez más que las 97 de changelog 4.6: retiro, cierre de jornada, alta y acuse de novedades del repartidor (4), `NovedadesController` (4), contacto de pedido y `interrumpir` ruta |
| Pantallas frontend (`page.tsx`) | 34 (suma `/hoy/retiro` y `/hoy/cierre` —changelog 4.7— y `/hoy/problema` —4.8—; recontado con `find frontend/app -name page.tsx`, no adelantado) |
| Entidades / tablas núcleo | 21 entidades / 18 tablas núcleo (suma `Novedad`, changelog 4.8: cruza el techo de 17 con justificación en acta §4.1) |
| Migraciones aplicadas | 14 (`Inicial` → `AgregarNovedades`; suman `AgregarRetiroDeRuta`, changelog 4.7, y `AgregarNovedades`, 4.8) |
| Roles | administracion, operacion, repartidor (personal interno) + cliente (`clientes_usuarios`, tabla separada) |
| Última etapa cerrada | **E1 — Cuenta corriente y facturación** (Anexo I §5, changelog acta 4.2). El monitor de jornada (4.4), los deliverys/recargo por km (4.5) y el panel de repartidores (4.6) son ampliaciones sobre etapas ya cerradas (§9.3, D14/§10.2-N y RF-15/§9.3 respectivamente), no etapas nuevas del Anexo I. |

---

## 2. Roles y control de acceso

Definidos en `Program.cs` como políticas de autorización:

| Política | Roles que la cumplen | Uso típico |
|---|---|---|
| `Administracion` | administracion | Tarifas, usuarios, vehículos (ABM), clientes, facturación, cuenta corriente, precio manual |
| `BackOffice` | administracion + operacion | Alta de pedidos, armado de rutas, catálogos operativos, deliverys (`DeliverysController`, changelog 4.5), disponibilidad de repartidores (`RepartidoresController`, changelog 4.6) |
| `Operacion` | operacion | (reservada, sin uso exclusivo hoy) |
| `Repartidor` | repartidor | `/api/mis-paradas` — superficie de escritura de la PWA |
| `Cliente` | cliente (tabla `clientes_usuarios`) | `/api/mi-cuenta`, `/mis-envios` |
| `Recorrido` | administracion + operacion + repartidor | Ruteo real por calles (OSRM) |

Regla de diseño repetida en varios controladores (`ClientesController`, `UsuariosController`, `VehiculosController`, `PruebasEntregaController`): **sin `[Authorize]` de clase cuando conviven acciones con público distinto**, porque ASP.NET Core combina el atributo de clase y el de acción con AND, no lo reemplaza. El recíproco también vale y se aplica en `JornadaController` y `RepartidoresController`: cuando todas las acciones comparten una sola política, se declara una vez a nivel de clase.

---

## 3. Módulos implementados

### 3.1 Autenticación (`AuthController`)
`POST /login`, `/refresh`, `/logout`, `GET /yo`. Refresh token en cookie httpOnly, rate limiting en login. Cubre RNF-08 (control de acceso por rol) y la auditoría de seguridad del 31/08/2026 citada en el Anexo I §2.

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
RF-18 a RF-24: lista de paradas del repartidor autenticado (`GET /dia`, bundle único de RNF-07 armado por `Servicios/JornadaService.cs`), registro de llegada (`POST /{paradaId}/llegada`) y cierre de parada con prueba de entrega (foto, receptor, posición, hora — `POST /{paradaId}/cierre`, multipart atómico, idempotente por `device_uuid` con respuesta `duplicado: true` distinguible de un conflicto real). Fotos servidas solo autenticadas, nunca por `wwwroot` estático (RNF-09). Ruteo real por calles vía OSRM (`RecorridoController`, changelog 3.4). Verificación de identidad: hoy solo el booleano `pruebas_entrega.identidad_verificada`, sin imagen — es RF-23 **en su redacción anterior** a la del changelog 4.7.

**Construido desde changelog 4.7/4.8:** gate del retiro (sin retiro firmado, `/llegada` y `/cierre` devuelven `409`), `POST /api/mi-jornada/retiro` y `/cierre` (`MiJornadaController`), `SiguienteParadaId` en el cierre de parada, y las novedades (§3.13). **Sin implementar:** modo sin conexión (B10/RNF-01) — confirmado ausente, no hay cola local ni sincronización diferida. Imagen del documento con retención y purga (RF-23 reescrito): diseñada en acta 4.7, **sin código** (`identidad_verificada` sigue siendo solo el booleano).

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

## 4. Pantallas del frontend (34)

**El conteo pasó de 31 a 34 cuando el código existió, no antes:** `/hoy/retiro` y `/hoy/cierre` (changelog 4.7) y `/hoy/problema` (4.8). Recontado con `find frontend/app -name page.tsx`. Este documento cuenta lo que existe, no lo que está acordado — y que exista no es que se haya probado en un teléfono: ninguna se verificó en un navegador real.

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
| `/mis-envios` | Portal de consulta del cliente |
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

21 entidades (`Entidades/*.cs`), agrupadas según §4 de `acta_sistema.md`:

| Bloque | Tablas |
|---|---|
| Geografía | `zonas`, `localidades`, `ubicaciones` |
| Comercial | `clientes`, `tarifas` |
| Núcleo | `pedidos`, `pedido_eventos` |
| Operación | `rutas`, `ruta_paradas`, `parada_pedidos`, `pruebas_entrega`, `vehiculos`, `novedades` |
| Registro sin maquinaria | `tipos_evento_cliente`, `eventos_cliente` |
| Cuenta corriente (E1) | `facturas`, `factura_items`, `pagos` |
| Usuarios | `usuarios`, `clientes_usuarios`, `refresh_tokens` |

DDL completo en `docs/schema_v3.sql`. Historial de migraciones (14, cronológico; las dos últimas son `AgregarRetiroDeRuta`, changelog 4.7, y `AgregarNovedades`, 4.8):
`Inicial` → `ReglasDeBaseDeDatos` → `AgregarVehiculos` → `AgregarKmZonas` → `SepararUsuariosCliente` → `AgregarOrigenRuta` → `AgregarCatalogoDepositos` → `AgregarTipoVehiculo` → `AgregarPrecioManual` → `AgregarCuentaCorriente` → `AgregarTipoEventoAvisoCobranza` → `AgregarPrecioPorKm`.

Nota: `AgregarTipoEventoAvisoCobranza` (changelog 4.3, una fila de catálogo) faltaba en esta lista desde su propia versión — el conteo de "10" en §1 antes de esta pasada ya estaba desactualizado; quedó corregido acá de paso, no es parte del alcance de changelog 4.5.

---

## 6. Fuera de alcance / no construido (confirmado por lectura de código)

Coincide con lo que el Anexo I declara en §4/§7 como brecha o exclusión — se lista acá solo lo verificado en esta pasada, no una copia del Anexo:

- **Modo sin conexión de la PWA (B10 / RNF-01 / H2-E5):** no hay cola local ni sincronización diferida, ni `manifest.json`, ni service worker, ni Dexie en `package.json` — verificado archivo por archivo. Lo que sí existe y la cola no tiene que reinventar: `device_uuid` generado en la captura y persistido (`lib/captura/dispositivo.ts`), compresión de la foto antes de enviar (`lib/captura/foto.ts`) e idempotencia del lado del servidor. **Las pantallas de `/hoy` sí están construidas** desde changelog 3.4 — `construccion_v1.md` §4.2 decía "sin construir" hasta la corrección de 1.21. Lo que mantiene H2/E5 abierto es solo la cola: sin ella no se puede correr la prueba de modo avión (criterio de aceptación 3 del acta), y por decisión del 17/09/2026 se construye **después** de la tanda de changelog 4.7, para envolver los cinco caminos de escritura de una sola vez en vez de migrarlos de a uno.
- **Imagen del documento con retención y purga (RF-23 reescrito, changelog 4.7):** diseñada y acordada, **sin código** — ni segunda foto en el cierre de parada, ni `documento_numero`, ni purgado, ni endpoint de la imagen. Bloqueada por la consulta legal de acta §11.2. El retiro firmado, el cierre en dos actores y las novedades (changelog 4.7/4.8) sí están construidos: ver §3.6, §3.7 y §3.13.
- **Tablero de indicadores (B2):** sigue sin construir. `/jornada` (changelog 4.4) agrega contadores y `/repartidores` (changelog 4.6) suma paradas por rango, pero ninguno calcula las 10 métricas que B2 prevé (entregas/día, km, tiempo, margen, NPS, ocupación de flota) ni compara períodos entre sí ni arma series: el acumulado de `/repartidores` es una sumatoria recalculada en cada request, sin persistir, para decidir a quién asignar — mismo encuadre que `/cobranza` (changelog 4.3). `ExportarController` sigue siendo la única vía de análisis histórico (CSV).
- **Motor de rango de cliente (B3):** los tres indicadores (`ColorPago/Trato/Oper`) son manuales, no hay cálculo periódico ni efecto sobre tarifa/prioridad/crédito.
- **Liquidación al repartidor (B4):** no hay controlador ni tabla de liquidación; el pago se sigue tipeando a mano en el cierre de ruta. `RepartidoresController` (changelog 4.6) **no** lo acerca: no lee ni escribe `rutas.pago_repartidor` y no expone un solo importe, ni en el listado ni en el detalle.
- **Portal de carga del cliente (B5):** `MiCuentaController` es de solo lectura; no hay endpoint de alta de pedido para el rol cliente.
- **Notificaciones (B6):** sigue sin construir *como sistema de notificaciones*, pero la línea previa ("sin integración de correo/WhatsApp en el código") quedó desactualizada en changelog 4.3 y no se había corregido acá. Lo que sí existe, y solo para el aviso de vencimiento del panel de cobranza: `Servicios/EmailService.cs` (HttpClient tipado contra la API REST de Resend, best-effort, nunca propaga excepción), `Servicios/AvisosCobranzaService.cs`, `Servicios/PlantillasAviso.cs` y `Dominio/EnlaceWhatsApp.cs` (link `wa.me` con el mensaje precargado — **sin API de WhatsApp**, Anexo I R7). Lo que no existe: notificación al cliente por cambio de estado del pedido, aviso al repartidor, y cualquier envío que no lo dispare una persona desde la pantalla — el sistema sigue sin scheduler.
- **Costos fijos y rentabilidad objetivo (B7):** el margen se calcula por ruta, no hay tabla de costos fijos ni prorrateo.

---

## 7. Fuentes

- `docs/acta_sistema.md` v4.7 (reglas de negocio vigentes)
- `docs/Anexo_I_Alcance_V2.docx` v1.0 (alcance comercial, brechas, decisiones D1-D15, definiciones §10.2)
- `docs/construccion_v1.md` (especificación técnica, changelog detallado)
- `docs/schema_v3.sql` (DDL)
- Lectura directa de `backend/Logistica/{Controllers,Entidades,Dominio,Datos}` y `frontend/app` en la rama `demo-d`, 13/09/2026 (§3.11 y los conteos de §1/§4/§5 actualizados puntualmente el 16/09/2026 para changelog 4.5; §3.12, los conteos de §1/§4 y tres ítems de §6 —B2, B4, B6— actualizados el 17/09/2026 para changelog 4.6, con `RepartidoresController` releído línea por línea — no es una repasada completa del resto del documento). Segunda pasada del 17/09/2026 para la fase de gobernanza de changelog 4.7: §3.6, §3.7, la nota de §4, el ítem de modo sin conexión de §6 y la regla del encabezado, con `MisParadasController`, `JornadaService`, `AlmacenamientoFotos`, `app/hoy/*` y `app/rutas/[id]/cierre` releídos — **ningún conteo se movió, porque no se escribió código en esa pasada**
- Pasada del 18/09/2026 (changelog 4.8, `next build` incluido — pasa): §1, §3.6, §3.7, §3.13 nuevo, §4, §5 y §6, con `MiJornadaController`, `NovedadesController`, `MisParadasController`, `RutasController`, `PedidosController`, `JornadaController` y `frontend/app/hoy/*` releídos. Conteos recontados mecánicamente. **Las pantallas nuevas pasan `tsc`, `eslint` y compilan bajo `next dev`, pero no se abrieron en un navegador**; los flujos de API sí se probaron con `curl` y `psql`.
