# Estado de implementación — relevamiento de código

**Generado:** 13/09/2026, actualizado puntualmente el 15/09/2026 (monitor de jornada, changelog 4.4 — no es una repasada completa del resto) · **Rama:** `demo-d` · **Fuente:** lectura directa del código (backend ASP.NET Core 8 + PostgreSQL, frontend Next.js), cruzado contra `acta_sistema.md` v4.4 y `Anexo_I_Alcance_V2.docx`.

Este documento no reemplaza a `acta_sistema.md` (reglas de negocio) ni a `construccion_v1.md` (especificación técnica). Es un inventario de qué existe hoy en el repositorio, con la referencia a qué requisito/decisión de las actas cubre cada pieza, para poder auditar alcance sin releer código.

---

## 1. Resumen

| | |
|---|---|
| Controladores API | 17 (suma `JornadaController`) |
| Endpoints | ~75 |
| Pantallas frontend (`page.tsx`) | 24 (suma `/jornada` y `/rutas/[id]`) |
| Entidades / tablas núcleo | 20 |
| Migraciones aplicadas | 10 (`Inicial` → `AgregarCuentaCorriente`) — el monitor de jornada (4.4) no agrega ninguna |
| Roles | administracion, operacion, repartidor (personal interno) + cliente (`clientes_usuarios`, tabla separada) |
| Última etapa cerrada | **E1 — Cuenta corriente y facturación** (Anexo I §5, changelog acta 4.2). El monitor de jornada (4.4) es tooling operativo del ciclo diario (§9.3), no una etapa nueva del Anexo I. |

---

## 2. Roles y control de acceso

Definidos en `Program.cs` como políticas de autorización:

| Política | Roles que la cumplen | Uso típico |
|---|---|---|
| `Administracion` | administracion | Tarifas, usuarios, vehículos (ABM), clientes, facturación, cuenta corriente, precio manual |
| `BackOffice` | administracion + operacion | Alta de pedidos, armado de rutas, catálogos operativos |
| `Operacion` | operacion | (reservada, sin uso exclusivo hoy) |
| `Repartidor` | repartidor | `/api/mis-paradas` — superficie de escritura de la PWA |
| `Cliente` | cliente (tabla `clientes_usuarios`) | `/api/mi-cuenta`, `/mis-envios` |
| `Recorrido` | administracion + operacion + repartidor | Ruteo real por calles (OSRM) |

Regla de diseño repetida en varios controladores (`ClientesController`, `UsuariosController`, `VehiculosController`, `PruebasEntregaController`): **sin `[Authorize]` de clase cuando conviven acciones con público distinto**, porque ASP.NET Core combina el atributo de clase y el de acción con AND, no lo reemplaza.

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

### 3.5 Planificación y rutas (`RutasController`)
RF-10 a RF-17: candidatos por zona (`GET /api/pedidos/candidatos-ruta`), armado (`PUT /{id}/paradas` reemplaza el set completo mientras la ruta está `planificada`), consolidación de retiros en la misma dirección vía `parada_pedidos`, asignación de vehículo/repartidor, validación de capacidad en paradas (P7, avisa sin bloquear — RF-16), origen de ruta variable (depósito del catálogo u "otra dirección", changelog 3.6/3.8).
`POST /{id}/cerrar-planificacion` (RF-17) transiciona la ruta a `en_curso` y en bloque sus pedidos a Confirmado/EnRuta, congelando ahí el precio final (changelog 3.11).

**Monitor de jornada (changelog 4.4):** `GET /{id}/jornada` expone el mismo bundle de paradas/estado/horarios que la PWA (`Servicios/JornadaService.cs`, extraído de `MisParadasController.Dia`), para cualquier estado de ruta — hasta esta versión una ruta `en_curso` no tenía ninguna vista de back-office. `PUT /{id}/repartidor` (reasignar, con la ruta `planificada` o `en_curso`) y `PUT /{id}/paradas/orden` (reordenar pendientes, RF-12) son las dos acciones nuevas sobre una ruta ya en curso. `GET /api/jornada/resumen` (`JornadaController`, nuevo) agrega contadores por estado de ruta/parada para una fecha — no es el tablero B2 (§6).

### 3.6 Ejecución en calle (`MisParadasController`, `RecorridoController`, `PruebasEntregaController`)
RF-18 a RF-24: lista de paradas del repartidor autenticado (`GET /dia`), registro de llegada (`POST /{paradaId}/llegada`) y cierre de parada con prueba de entrega (foto, receptor, posición, hora — `POST /{paradaId}/cierre`). Fotos servidas solo autenticadas, nunca por `wwwroot` estático (RNF-09). Ruteo real por calles vía OSRM (`RecorridoController`, changelog 3.4).
**Sin implementar:** modo sin conexión (B10/RNF-01) — confirmado ausente, no hay cola local ni sincronización diferida en el código revisado.

### 3.7 Cierre y trazabilidad (`RutasController.Cierre`, triggers de base)
RF-26/RF-27: cierre económico con km, combustible, peajes, costos, resultado disponible el mismo día. RF-28/RNF-04: trazabilidad automática por trigger de base de datos (`trg_*`, ver `schema_v3.sql`), no por código de aplicación — verificado en los comentarios de `Factura`/`Pago` (`trg_facturas_inmutable`, `trg_pagos_inmutable`).

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

---

## 4. Pantallas del frontend (24)

| Ruta | Pantalla |
|---|---|
| `/` | Home |
| `/login` | Login |
| `/pedidos`, `/pedidos/nuevo`, `/pedidos/[id]` | Carga y detalle de pedidos |
| `/jornada` | Monitor del día en curso: contadores por estado, rutas del día, panel por repartidor (changelog 4.4) |
| `/rutas`, `/rutas/nueva`, `/rutas/[id]`, `/rutas/[id]/armar`, `/rutas/[id]/cierre` | Planificación, detalle (cualquier estado, changelog 4.4) y cierre de rutas |
| `/hoy`, `/hoy/parada/[paradaId]` | PWA del repartidor |
| `/mis-envios` | Portal de consulta del cliente |
| `/clientes`, `/clientes/nuevo`, `/clientes/[id]` | ABM de clientes |
| `/usuarios`, `/usuarios/nuevo` | ABM de usuarios internos |
| `/vehiculos`, `/vehiculos/nuevo`, `/vehiculos/[id]` | ABM de flota |
| `/tarifas` | Tarifas + localidades sin zona |
| `/depositos` | Catálogo de depósitos |
| `/facturas` | Facturación / cuenta corriente (E1) |
| `/exportar` | Exportación CSV |

---

## 5. Modelo de datos

20 entidades (`Entidades/*.cs`), agrupadas según §4 de `acta_sistema.md`:

| Bloque | Tablas |
|---|---|
| Geografía | `zonas`, `localidades`, `ubicaciones` |
| Comercial | `clientes`, `tarifas` |
| Núcleo | `pedidos`, `pedido_eventos` |
| Operación | `rutas`, `ruta_paradas`, `parada_pedidos`, `pruebas_entrega`, `vehiculos` |
| Registro sin maquinaria | `tipos_evento_cliente`, `eventos_cliente` |
| Cuenta corriente (E1) | `facturas`, `factura_items`, `pagos` |
| Usuarios | `usuarios`, `clientes_usuarios`, `refresh_tokens` |

DDL completo en `docs/schema_v3.sql`. Historial de migraciones (10, cronológico):
`Inicial` → `ReglasDeBaseDeDatos` → `AgregarVehiculos` → `AgregarKmZonas` → `SepararUsuariosCliente` → `AgregarOrigenRuta` → `AgregarCatalogoDepositos` → `AgregarTipoVehiculo` → `AgregarPrecioManual` → `AgregarCuentaCorriente`.

---

## 6. Fuera de alcance / no construido (confirmado por lectura de código)

Coincide con lo que el Anexo I declara en §4/§7 como brecha o exclusión — se lista acá solo lo verificado en esta pasada, no una copia del Anexo:

- **Modo sin conexión de la PWA (B10 / RNF-01):** no hay cola local, compresión de fotos ni sincronización diferida en `MisParadasController` ni en el frontend de `/hoy`.
- **Tablero de indicadores (B2):** sigue sin construir. `/jornada` (changelog 4.4) agrega contadores, pero no calcula ninguna de las 10 métricas que B2 prevé (entregas/día, km, tiempo, margen, NPS, ocupación de flota) ni acumula/compara períodos — es un monitor del día en curso, mismo encuadre que `/cobranza` (changelog 4.3). `ExportarController` sigue siendo la única vía de análisis histórico (CSV).
- **Motor de rango de cliente (B3):** los tres indicadores (`ColorPago/Trato/Oper`) son manuales, no hay cálculo periódico ni efecto sobre tarifa/prioridad/crédito.
- **Liquidación al repartidor (B4):** no hay controlador ni tabla de liquidación; el pago se sigue tipeando a mano en el cierre de ruta.
- **Portal de carga del cliente (B5):** `MiCuentaController` es de solo lectura; no hay endpoint de alta de pedido para el rol cliente.
- **Notificaciones (B6):** sin integración de correo/WhatsApp en el código.
- **Costos fijos y rentabilidad objetivo (B7):** el margen se calcula por ruta, no hay tabla de costos fijos ni prorrateo.

---

## 7. Fuentes

- `docs/acta_sistema.md` v4.2 (reglas de negocio vigentes)
- `docs/Anexo_I_Alcance_V2.docx` v1.0 (alcance comercial, brechas, decisiones D1-D15, definiciones §10.2)
- `docs/construccion_v1.md` (especificación técnica, changelog detallado)
- `docs/schema_v3.sql` (DDL)
- Lectura directa de `backend/Logistica/{Controllers,Entidades,Dominio,Datos}` y `frontend/app` en la rama `demo-d`, 13/09/2026
