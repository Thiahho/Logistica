# Diseño B13 — Recepción y conciliación de pedidos cargados por el cliente
 
**22/09/2026.** Decisión de negocio del cliente en esta fecha: se adopta el modelo **G-i** del Anexo
I (§10.2-G) — *el pedido manda*. El cliente carga desde el portal; al retiro de la mercadería,
operación confirma lo recibido contra lo declarado, o lo corrige si hay un error.
 
Este documento diseña B13 (Anexo I, "no incluido en ninguna etapa") apoyándose en lo que **ya existe**
(B16 — ajuste de bultos, `estado_implementacion.md` §3.4) para no duplicar mecanismo.
 
---
 
## 1. §10.2-E — corte del portal (resuelto parcialmente, 22/09/2026)
 
**Decisión tomada:** corte del portal a las **16:00**, global (no por cliente), dos horas antes del
corte de carga general (18:00, `acta_sistema.md` §7).
 
**Sigue sin definirse por completo — anotado a propósito, no una decisión cerrada:**
- Si 16:00 queda como valor fijo en código o como parámetro de configuración editable —
  mismo tratamiento que el resto de los parámetros comerciales del sistema, que no llevan
  valores de fábrica reales hasta confirmación explícita (§13 del acta; mismo caso que
  `Precio:FactorUrgencia`, todavía provisional en 0,20).
- Si en algún momento hace falta diferenciarlo por cliente (la pregunta original del Anexo
  incluía esa opción y quedó sin cerrar).
**Efecto sobre el diseño:** con el portal cerrando dos horas antes de la carga general, sí queda
un bloque de tiempo dedicado (16:00–18:00) para que operación revise lo que llegó contra lo
declarado, además de poder ir confirmando pedido por pedido durante el día a medida que la
mercadería llega físicamente al depósito.
 
---
 
## 2. Por qué este diseño no agrega un estado nuevo al pedido
 
El Anexo I asume que B13 "implica un estado nuevo en el pedido y una pantalla de recepción". Revisando
el ciclo ya documentado, no hace falta:
 
- El pedido queda en `Borrador` hasta que **cierra la planificación de ruta** (`cerrar-planificacion`),
  no al cargarse — `estado_implementacion.md` §3.4. Mientras es `Borrador`, es editable sin
  restricciones; no hay trigger de congelamiento todavía.
- Con el corte del portal a las 16:00 (§1), la recepción cae siempre dentro de la ventana en que
  el pedido sigue siendo `Borrador` — sea que se confirme durante el día o en el bloque 16:00–18:00.
- Conclusión: la recepción es una **edición dentro de Borrador**, no una transición de estado. No
  hace falta cruzar el techo de tablas ni tocar la máquina de estados (`TransicionesPedido`).
**B16 no se toca.** Sigue exactamente como está, para lo que ya cubre: discrepancias descubiertas
*después* de que el pedido se congela (por ejemplo, en el retiro del repartidor a la mañana, RF-35,
que es un evento distinto de la recepción en depósito).
 
| | Antes de las 18:00 (pedido en `Borrador`) | Después (pedido `Confirmado`) |
|---|---|---|
| Discrepancia bultos declarados vs. recibidos | **Nuevo (este diseño):** edición directa en el pedido, con nota obligatoria | **Ya existe:** ajuste B16, tope de 3 gratis, cargo de gestión al 4to |
| Efecto sobre precio | Ninguno (P3: precio depende de zona, no de bultos) | Ninguno directo — el cargo de gestión es tarifa aparte, no recálculo |
| Aprobación | Ninguna — queda registrado quién y cuándo | Administración aprueba o rechaza cada ajuste |
 
---
 
## 3. Modelo de datos propuesto (sin tabla nueva)
 
Columnas nuevas en `pedidos` (cero migraciones de tabla, extiende la existente):
 
- `bultos_declarados_cliente` (int, nullable) — snapshot de lo que el cliente cargó en el portal.
  Se completa una sola vez, al alta, solo si `origen_carga = 'portal'` (campo que **ya existe**,
  RF-06). Inmutable después de escrito — mismo patrón que `retiro_bultos_contados` (RF-35).
- `recepcion_confirmada_en` (timestamp, nullable)
- `recepcion_confirmada_por` (usuario_id, nullable)
Si al confirmar recepción `pedidos.bultos` (el valor operativo) coincide con
`bultos_declarados_cliente`, solo se sellan las dos columnas de arriba. Si no coincide, operación
edita `pedidos.bultos` ahí mismo — todavía en Borrador, sin necesidad de pasar por B16 — dejando una
nota obligatoria si difiere, mismo patrón que `NotasCierre` en el cierre de ruta (RF-26).
 
---
 
## 4. Endpoint propuesto
 
`POST /api/pedidos/{id}/recepcion` — política `BackOffice`.
 
- 404 si el pedido no es `origen_carga = 'portal'`.
- 409 si el pedido ya no está en `Borrador` (el corte de carga de las 18:00 ya pasó) — el mensaje de
  error apunta al endpoint de ajustes existente (B16, `POST /api/pedidos/{id}/ajustes`) en vez de
  fallar en seco.
- Body: `{ bultosConfirmados: int, nota?: string }`. `nota` obligatoria si `bultosConfirmados !=
  bultos` actual.
- El corte del propio portal (16:00, §1) lo aplica el endpoint de alta del cliente, no este — este
  endpoint solo valida contra el estado del pedido (`Borrador`), no contra el reloj del portal.
## 5. Pantalla propuesta
 
`/recepcion` — nueva, cola de trabajo (no el detalle de un pedido a la vez): lista los pedidos de
`origen_carga = 'portal'` del día con `recepcion_confirmada_en IS NULL`, ordenados por hora de
carga. Cada fila: bultos declarados, campo para confirmar o corregir, nota si corrige. Confirmar en
lote los que coinciden sin abrir cada uno — la alternativa (abrir `/pedidos/[id]` uno por uno) no
escala si el volumen del portal crece, que es justo el objetivo de negocio de B5.
 
---
 
## 6. Fuera de este diseño
 
- Si 16:00 queda hardcodeado o como parámetro editable, y si en el futuro se necesita por cliente —
  pendiente de definición completa (§1).
- Todo lo demás de B5 (formulario de carga, cotización en vivo, validaciones de alta) — diseño
  aparte, este documento cubre solo la conciliación en recepción (B13).
- Vinculancia del precio mostrado en el portal (Anexo I, definición D) — no afecta a B13, pero
  bloquea parte de B5.