# Diseño E2 — Liquidación al repartidor (B4), rangos de cliente (B3) y rentabilidad (B7)

**24/09/2026 · Etapa E2 del Anexo I §5 ("Rangos y liquidación": B3, B4, B7).** Fase de gobernanza: este documento se escribe **antes del código** y fija qué se construye. Las decisiones de negocio van en `acta_sistema.md` (changelog 4.21); acá está el diseño. Entra por los ciclos de operación de acta §9.3, no por el disparador de §9.2:
- **Semanal:** pago al repartidor (B4).
- **Mensual:** costos fijos, rentabilidad y liquidación por período (B4, B7).
- **Trimestral:** recálculo de rango de cliente (B3).

Los puntos marcados **[confirmado 24/09/2026]** tienen un valor por defecto que se construye si nadie lo cambia al revisar este documento.

---

## 1. Definiciones del Anexo I §10.2 que se cierran (24/09/2026)

| Ref | Decisión de la Empresa | Efecto en el sistema |
|---|---|---|
| **C** (de D6) | El **porcentaje mínimo** de entregas exitosas para el bono y la **lista de motivos de fallo imputables** al repartidor son **configurables** desde el sistema. | Valores en la base, editables por administración (Anexo §6: configuración, no cambio de alcance). **Sin valor de fábrica para el porcentaje** (acta §13: un número inventado es peor que un casillero vacío); la lista arranca con `otro` y `zona_inaccesible`. |
| **J** | Rango y tarifa se relacionan como **modificador**, opción (i): el rango aplica un % de descuento sobre el precio de la zona. | Un término nuevo en la fórmula de precio (§4). La tabla de tarifas **no** suma la dimensión rango. |
| **F** (de D5) | "Respeto y buen trato" se mide por **ajuste manual** de administración: **±1 rango como máximo**, con **justificación escrita obligatoria** y **fecha de vencimiento**. | Columnas de ajuste en `clientes` e historial en `cliente_rangos` (§3). |
| **D4** | Recálculo **trimestral**, tal como fue decidido; se **rechaza la contrapropuesta** asimétrica de §10.3 (bajada inmediata por mora). | Subida y bajada solo en el recálculo trimestral. **Riesgo aceptado a sabiendas** (el que §10.3 describía): un cliente puede acumular deuda un trimestre entero conservando su descuento. El corte por deuda de D11 sigue actuando igual; lo que no cambia hasta el recálculo es la tarifa. |
| **B7** | La estructura económica objetivo (60-65% / 10-15% / 20-30%) **se define después**. | Tramos con nombre libre y porcentajes configurables; el sistema no trae ninguno cargado. |

---

## 2. B4 — Liquidación al repartidor

### 2.1 Parámetros (`parametros_liquidacion`, tabla nueva)

Una fila por tipo de vehículo (`camioneta` | `moto`) con vigencia, igual que `tarifas`:

| Columna | Tipo | Nota |
|---|---|---|
| `tipo_vehiculo` | text | mismo check que `tarifas.tipo_vehiculo` |
| `pago_por_entrega` | numeric(12,2) ≥ 0 | |
| `bono_ruta` | numeric(12,2) ≥ 0 | |
| `pct_minimo_exitosas` | numeric(5,2), 0–100 | definición C |
| `motivos_imputables` | text[] | subconjunto de `PruebaEntrega:MotivosFallo`, validado en la aplicación |
| `vigente_desde` / `vigente_hasta` | date | sin solapamiento por tipo (mismo criterio que `tarifas`) |

Sin fila vigente para el tipo de vehículo de la ruta, **el pago se sigue tipeando a mano como hoy**. Un casillero vacío no se completa con un número inventado.

### 2.2 Cálculo al cerrar la ruta (`RutasController.Cerrar`)

- **Entrega:** una parada de tipo `entrega` en estado `completada`. **[confirmado 24/09/2026]** Se paga **por parada, no por pedido**: una parada consolidada con tres pedidos al mismo destino es un viaje, no tres (RF-14).
- **Fallida imputable:** una parada `fallida` cuyo motivo, tomado de sus `pruebas_entrega`, está en `motivos_imputables`. Las fallidas no imputables y las paradas `cancelada` **no entran al cálculo**: ni suman ni restan.
- **% de éxito:** entregas / (entregas + fallidas imputables). Sin ninguna parada que cuente, el % es 100. Aun así, una ruta sin ninguna entrega no cobra bono: el bono es por ruta completa (D6).
- **Pago:** `entregas × pago_por_entrega`, más `bono_ruta` si el % de éxito ≥ `pct_minimo_exitosas`. Solo se paga con la ruta cerrada (D6): el cálculo corre en el propio cierre.
- **Parámetros aplicados:** los vigentes en `rutas.fecha`, según el tipo del vehículo de la ruta.

**Qué queda guardado en `rutas`** (el desglose congelado del cálculo):
- `liq_entregas`, `liq_fallidas_imputables`, `liq_pct_exito`, `liq_pago_entregas`, `liq_bono`.
- `PagoRepartidor` = el total propuesto.

**Si administración paga otro monto:** puede cambiarlo, con **motivo escrito obligatorio** (`pago_ajuste_motivo`). Es el mismo patrón que `NotasCierre` frente a la declaración del repartidor (RF-26): el número calculado y el pagado quedan uno al lado del otro.

El margen de la ruta (`CalcularResultadoAsync`) no cambia de fórmula: usa `PagoRepartidor`, que ahora llega calculado.

### 2.3 Liquidación por período (`liquidaciones`, tabla nueva)

- **Qué es:** el comprobante que se le entrega al repartidor por un período. Tiene repartidor, período (`desde`, `hasta`), `total`, `emitida_en` y `emitida_por`.
- **Qué incluye:** las rutas `cerrada` del repartidor dentro del período que todavía no están en otra liquidación.
- **Cómo se vincula cada ruta:** con `rutas.liquidacion_id` (FK). No hay tabla de ítems, porque el detalle de cada ruta ya está en sus columnas `liq_*`.
- **Una vez emitida no se edita:** lo impide el trigger `trg_liquidaciones_inmutable`, con el mismo criterio que `trg_facturas_inmutable`. Una ruta ya liquidada tampoco cambia de liquidación ni de `PagoRepartidor`.
- **Correcciones:** ajuste manual en la liquidación siguiente. **[confirmado 24/09/2026]** No hay notas de crédito al repartidor.

### 2.4 Endpoints y pantallas (solo Administración)

- `GET/POST/PUT /api/parametros-liquidacion`: ABM con vigencia. Se muestra dentro de `/tarifas`.
- `GET /api/liquidaciones/previsualizacion?repartidorId&desde&hasta`: lo que entraría en la liquidación, sin escribir nada.
- `POST /api/liquidaciones`: emite.
- `GET /api/liquidaciones` (paginado) y `GET /api/liquidaciones/{id}`.
- `GET /api/liquidaciones/{id}/csv`: comprobante, con el escritor de `ExportarController` (escape contra CSV injection incluido).
- Pantallas: `/liquidaciones` (listado, previsualizar y emitir) y `/liquidaciones/[id]` (comprobante imprimible).
- **Cierre de ruta:** `/rutas/[id]/cierre` muestra el desglose calculado y pide motivo si el monto pagado cambia.

`RepartidoresController` sigue sin mostrar importes (acta changelog 4.6): la liquidación es una pantalla aparte.

---

## 3. B3 — Rangos de cliente

### 3.1 Configuración (`rangos`, tabla nueva, cinco filas fijas)

| Columna | Nota |
|---|---|
| `codigo`, `nombre`, `orden` | `sin_rango`(0), `bronce`(1), `plata`(2), `oro`(3), `empresa`(4). Las filas son fijas: agregar un rango es cambio de alcance (Anexo §6) |
| `min_envios_trimestre` | pedidos entregados en el trimestre |
| `min_facturacion_trimestre` | suma de `factura_items` aprobados del trimestre |
| `min_antiguedad_meses` | desde `clientes.creado_en` |
| `min_semanas_activas` | frecuencia: semanas del trimestre con al menos un envío entregado |
| `min_pct_pagos_en_termino` | % de facturas vencidas en el trimestre que quedaron cubiertas a su vencimiento (FIFO, D12, mismo cálculo que `v_facturas_saldo`) |
| `descuento_pct` | definición J, 0–100 |
| `limite_credito` | nullable |

- **Umbrales:** todos nullable, sin valores de fábrica (acta §13). Un umbral vacío no exige nada.
- **Un rango sin ningún umbral cargado** no se alcanza por cálculo: solo por ajuste manual. Así arranca "Empresa". **[confirmado 24/09/2026]**
- **Rango calculado:** el más alto cuyos umbrales cargados se cumplen **todos**.

### 3.2 Estado en `clientes`

- **Rango actual:** `rango_codigo` (FK a `rangos`, default `sin_rango`) y `rango_calculado_en`.
- **Ajuste manual (definición F):**
  - `rango_ajuste` smallint, −1 / 0 / +1.
  - `rango_ajuste_motivo` (obligatorio si ≠ 0).
  - `rango_ajuste_vence` (fecha, obligatoria si ≠ 0).
  - `rango_ajuste_por`, `rango_ajuste_en`.
  - Check de conjunto, mismo patrón que `ck_clientes_corte_suspendido`.
- **Rango efectivo** = el calculado, movido por el ajuste si está vigente, sin bajar de `sin_rango` ni subir de `empresa`.

### 3.3 Historial (`cliente_rangos`, tabla nueva, solo inserción)

- **Una fila por cambio:** cliente, rango anterior y nuevo, origen (`recalculo` | `ajuste`), `criterios jsonb` (lo medido: envíos, facturación, semanas, % en término, antigüedad), motivo, autor y fecha.
- **Inmutable:** trigger `trg_cliente_rangos_inmutable`.
- Reemplaza a la `cliente_scores` que acta §4 dejaba fuera del modelo inicial.

### 3.4 Recálculo trimestral (D4)

- **Quién lo dispara:** administración, a mano. No hay scheduler (mismo criterio que el cierre de ciclo de facturación, acta changelog 4.2).
  - `POST /api/rangos/recalculo/previsualizacion?trimestre=2026-T3`: qué rango le toca a cada cliente y con qué números, sin escribir.
  - `POST /api/rangos/recalculo`: aplica. Escribe `clientes.rango_codigo` y una fila de historial por cliente que cambió.
- **Qué trimestre mide:** el trimestre calendario cerrado que se indica. Recalcular dos veces el mismo trimestre da el mismo resultado.
- **Ajuste manual vencido:** se ignora en el cálculo, y el recálculo lo limpia (queda registrado en el historial).
- Cargar un ajuste manual (`PUT /api/clientes/{id}/rango/ajuste`) registra historial en el acto.

### 3.5 Efectos del rango

1. **Precio (J):** la fórmula de `construccion_v1.md` §6 suma `descuento_rango = precio_base × descuento_pct` del rango efectivo del cliente al momento de cotizar. Se congela con el resto del precio (P1), así que un cambio de rango posterior no mueve un precio ya congelado.
   - **[confirmado 24/09/2026]** Aplica **solo sobre la lista general** de tarifas. Un cliente con lista propia (`tarifas.cliente_id`) ya tiene su precio negociado y no suma descuento por rango.
   - `DesglosePrecio` suma `DescuentoRango`, visible en el detalle del pedido. El cliente ve su descuento, pero **nunca los números internos con que se calculó su rango** (RF-33).
2. **Límite de crédito:** **[confirmado 24/09/2026]** solo **avisa**. Si el saldo pendiente del cliente más el pedido nuevo supera `limite_credito`, el alta responde con una advertencia visible para el back-office. El bloqueo sigue siendo solo el corte por deuda vencida de D11; D1, que decía "el límite solo avisa", fue derogada por D11, y esto no la resucita como bloqueo.
3. **Prioridad de asignación:** **[confirmado 24/09/2026; choca con acta §7 y se resuelve así]** acta §7 dice *"al buen cliente se lo premia con acceso, nunca con prioridad… nunca reordenando la ruta a su favor"*.
   - **Propuesta:** la prioridad se limita al **orden en que aparecen los pedidos pendientes en el armado** (quién entra primero cuando la capacidad no alcanza para todos). **Nunca** cambia el orden de paradas dentro de una ruta, que sigue siendo geográfico.
   - Si la Empresa entiende que ni eso corresponde, el efecto se descarta y `prioridad` sale del diseño.
4. **Visible para el cliente:** el nombre de su rango y su descuento en "Mi plan". **[confirmado 24/09/2026]**

### 3.6 Pantallas

- `/clientes/[id]`: rango efectivo, calculado y ajuste (cargar o quitar, con motivo y vencimiento), más el historial.
- `/clientes/rangos`: previsualizar y aplicar el recálculo trimestral.
- `/tarifas`: la tabla de rangos (umbrales, descuento y límite).
- Todo solo Administración.

---

## 4. Fórmula de precio resultante

```
total = precio_base + recargo_km + recargo_urgencia − descuento_ruta − descuento_rango + peajes
descuento_rango = precio_base × rangos.descuento_pct / 100   (solo lista general; 0 si el rango no tiene descuento)
```

Es el cambio de fórmula que el Anexo §6 llama "agregar un término"; queda cubierto por la definición J, cerrada.

---

## 5. B7 — Costos fijos y rentabilidad mensual

### 5.1 Datos (dos tablas nuevas)

**`costos_fijos`:**
- Columnas: `mes` (date, día 1), `categoria` (texto libre), `descripcion`, `monto` ≥ 0, más quién lo cargó y cuándo.
- Se carga mes a mes. Hay un botón "copiar del mes anterior" para no retipear lo fijo de verdad.

**`objetivos_rentabilidad`:** los tramos de la estructura objetivo, que se definen después (§1).
- Columnas: `nombre`, `pct_min`, `pct_max`, `orden`.
- `fuentes text[]`: qué suma cada tramo, entre:
  - `pago_repartidor`, `combustible`, `peajes`, `otros_costos` (de `rutas` cerradas);
  - `fijos` (todos) o `fijos:<categoría>`;
  - `margen`.

### 5.2 Cálculo (`GET /api/rentabilidad?mes=2026-09`, solo Administración)

- **Ingresos del mes:** `factura_items` aprobados con `creado_en` en el mes (lo facturable nace en el momento en que se vuelve facturable: entrega o cancelación cobrada). No depende de cuándo se emitió la factura.
- **Costos variables:** las columnas de costo de las rutas `cerrada` con `fecha` en el mes.
- **Costos fijos:** `costos_fijos` del mes.
- **Margen:** ingresos − variables − fijos.
- **Por tramo:** el % sobre ingresos de la suma de sus fuentes, contra `pct_min`–`pct_max`: dentro, debajo o encima.
- **Sin tramos cargados:** se muestran igual los % de cada fuente sobre ingresos, sin comparación contra un objetivo.

### 5.3 Pantalla

`/rentabilidad`: selector de mes, resumen (ingresos, costos, margen), tabla de tramos contra objetivo, carga de costos fijos del mes y edición de los tramos.

---

## 6. Techo de tablas

Pasa de **19 a 25**, con estas seis tablas nuevas:
- `parametros_liquidacion` y `liquidaciones` (B4).
- `rangos` y `cliente_rangos` (B3).
- `costos_fijos` y `objetivos_rentabilidad` (B7).

Cada una entra por un ciclo de §9.3, no por catálogo (P6). La justificación de cada una queda en acta changelog 4.21. `liquidaciones` sale de "fuera del modelo inicial" (acta §4); `cliente_scores` se realiza como `cliente_rangos`.

---

## 7. Orden de construcción y pruebas

1. **B4:** migración `AgregarLiquidacion`, `Servicios/LiquidacionService.cs`, cierre de ruta, liquidaciones y pantallas.
2. **B3:** migración `AgregarRangos`, `Servicios/RangoClienteService.cs`, término nuevo en `PrecioService`, pantallas.
3. **B7:** migración `AgregarRentabilidad`, `RentabilidadController` y pantalla.

**Pruebas unitarias** (`backend/Logistica.Tests`): el cálculo de pago y bono (con fallidas imputables y no imputables, sin parámetros, ruta sin paradas), el rango calculado (umbrales vacíos, todos, ajuste vigente y vencido, topes), el % de pagos en término por FIFO y el cálculo de tramos.

**Verificación con `curl`/`psql`** contra la base de desarrollo:
- Los triggers de inmutabilidad (una liquidación emitida no se edita: 409).
- Un precio congelado no se mueve al cambiar de rango.
- Permisos: operación y repartidor reciben 403 en todo lo de E2.

---

## 8. Fuera de este diseño

- Notas de crédito al repartidor.
- Pago efectivo al repartidor: el sistema emite el comprobante, no transfiere.
- Condiciones contractuales del repartidor (acta §11.2).
- Ausencias declaradas: la tabla `repartidores` sigue fuera (acta §4).
- Tablero de indicadores (B2/E3): va en su propio diseño.
- Bajada inmediata de rango por mora (rechazada, D4).
- Tarifa por rango como dimensión (rechazada, J).
