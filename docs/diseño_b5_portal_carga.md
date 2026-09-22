# Diseño B5 — Portal de carga del cliente
 
**22/09/2026.** Etapa E4 (Anexo I §5), adelantada por decisión del cliente respecto del orden
original (E2/E3 quedan para después — ver nota de secuencia en `acta_sistema.md` cuando se
formalice el changelog). Este documento cubre el formulario de carga; `diseno_b13_recepcion_portal.md`
cubre la conciliación posterior, ya diseñada y dependiente de que este formulario exista
(`bultos_declarados_cliente` se completa acá).
 
---
 
## 1. Anexo I, definición D (de D8) — resuelto 22/09/2026: precio vinculante
 
**Decisión del cliente:** el precio que el portal cotiza es el precio que se cobra. Motivo
explícito: con facturación por ciclo (cuenta corriente, no pago contra entrega), un precio
estimativo que después cambia lo descubre el cliente semanas más tarde, en la factura — sin nadie
en el medio para absorber esa fricción, a diferencia de la carga interna que hoy maneja una
persona por teléfono.
 
**Mecanismo (sin tabla ni columna ni trigger nuevo):** reusa el campo `precio_manual` que ya existe
para B9, junto con sus dos acompañantes `precio_manual_por`/`precio_manual_en`
(`acta_sistema.md` §4.1 — "quién y cuándo, porque es un criterio subjetivo con efecto sobre el
precio"). Al confirmar la carga, el precio cotizado con el tipo de vehículo que el cliente eligió
se graba en `precio_manual`, y `precio_manual_por` queda con el `usuario_id` del propio cliente
(tabla `clientes_usuarios`) en vez del de un admin. **No hace falta una columna nueva para
distinguir "admin" de "portal_vinculante"**: alcanza con cruzar `precio_manual_por` contra
`clientes_usuarios` (si el id está ahí, fue el cliente) — el propio campo ya resuelve la auditoría
que iba a resolver con una columna extra. Corrección sobre la primera versión de este documento,
que sí proponía esa columna sin necesidad. `RutasController.CerrarPlanificacion` ya sabe respetar
`precio_manual` cuando está seteado (no recalcula) — es el mismo camino que hoy usa B9, aplicado a
un caso distinto.
 
**Riesgo asumido, dicho explícitamente (decisión del cliente, no mía):** si el planificador
necesita asignar un pedido de portal a un vehículo más caro que el elegido (por capacidad o por
ruta), la diferencia la absorbe la Empresa — el precio ya quedó fijo del lado del cliente.
 
---
 
## 2. Autenticación y alcance de datos
 
Reusa `clientes_usuarios` (login ya construido, changelog acta 3.3) y el patrón de
`MiCuentaController`: el cliente autenticado solo ve y carga para su propio `ClienteId`,
**resuelto del claim de sesión, nunca del body ni de la URL** — mismo criterio que ya aplica el
portal de solo lectura.
 
## 3. Gate de corte de servicio (D11)
 
Antes de aceptar la carga: valida `DeudaVencidaAsync(clienteId)`, la misma función que ya usa
`PedidosController.Crear` para bloquear altas internas de un cliente con deuda vencida. Un cliente
cortado no puede cargar desde el portal — mismo mensaje que ya existe, no uno nuevo.
 
## 4. Formulario — campos
 
Mismos datos que la carga interna (RF-01 a RF-07), reusando componentes existentes en vez de
duplicarlos:
 
| Campo | Reusa |
|---|---|
| Destinatario (nombre, teléfono obligatorio) | — |
| Dirección de destino | `SelectorDireccion` + geocodificación (extraído de `/pedidos/nuevo`) |
| Bultos | — (este valor pasa a `bultos_declarados_cliente`, §3 de `diseno_b13_recepcion_portal.md`) |
| Tipo de vehículo (camioneta/moto) | mismo selector de alta interna — **ahora también define el precio final** (§1), no solo el estimado |
| Observaciones | opcional |
 
**Fuera del formulario, por diseño (no por omisión):**
- **Dirección de retiro:** fija, depósito por defecto. El cliente no elige de dónde se retira su
  mercadería — B12/P (Anexo I) sigue "a determinar", no se construye acá.
- **Peso:** B8 sigue sin construir el control por peso: se guarda si ya existe el campo, no
  participa del cálculo.
## 5. Cotización y B9 (zona sin tarifa) — con precio vinculante
 
Reusa `PrecioService.CotizarAsync`. Con el precio ahora vinculante (§1), esto se vuelve más
estricto que antes: si la zona no tiene tarifa cargada (`RequiereCotizacion = true`, B9), el portal
**no puede mostrar ni fijar ningún precio** — no hay nada que congelar. El pedido se guarda igual,
`precio_manual` queda `null`, con aviso visible para el cliente ("pendiente de cotización, te
contactamos"), y aparece para Administración en la misma cola que ya usa para este caso. **Hasta
que Administración fije el precio a mano, el pedido no es confirmable desde el portal** — el
cliente no puede aceptar un precio vinculante que todavía no existe.
 
## 6. Corte horario del portal (16:00, `diseno_b13_recepcion_portal.md` §1)
 
- El backend valida la hora del servidor **al confirmar**, no solo al mostrar el formulario —
  evita que alguien deje la pantalla abierta y confirme después de las 16:00.
- Pasado el corte: `409`, mensaje claro ("la carga de hoy cerró a las 16:00, esto se carga para
  mañana"), y el formulario ofrece cargarlo directamente para el día siguiente en vez de solo
  bloquear.
## 7. Confirmación — qué estado queda
 
El pedido nace en `Borrador`, `origen_carga = 'portal'` (RF-06, ya existe como discriminador), con
`precio_manual`/`precio_manual_por`/`precio_manual_en` ya fijados (§1). **No pasa a `Confirmado`
automáticamente** — ese estado sigue reservado a `cerrar-planificacion`, mismo mecanismo que la
carga interna (changelog 3.11); lo que cambia con este diseño es que el *precio* ya no se toca ahí,
aunque el *estado* del pedido sí siga el mismo camino. El cliente ve "cargado, pendiente de
retiro" en su portal, con el precio ya visible como definitivo, no estimado.
 
## 8. Endpoint
 
`POST /api/mi-cuenta/pedidos` — política `Cliente`, extiende `MiCuentaController` (hoy solo
lectura). Mismo cuerpo que `POST /api/pedidos`, pero `ClienteId` sale del claim de sesión, no del
request, y el handler escribe `precio_manual`/`precio_manual_por`/`precio_manual_en` al cotizar en
vez de dejarlos en null como hace hoy el alta interna.
 
## 9. Pantalla
 
`/mis-envios/nuevo` — mismo namespace que el portal de solo lectura ya construido. Reusa el
formulario de `/pedidos/nuevo` quitando lo que no aplica (selector de cliente, selector de
origen), agregando el aviso de corte horario (§6) y mostrando el precio como **definitivo**, no
estimado — con una línea aclarando que puede diferir solo si la ruta termina usando otro vehículo
(§1), costo que absorbe la Empresa.
 
---
 
## 10. Fuera de este diseño
 
- Dirección de retiro por pedido (B12/P) — reabre el módulo de recolección, no contemplado.
- Importación masiva (B11) — pantalla distinta, no este formulario.
- El tablero (B2) sigue como siguiente punto, sin depender de esto.