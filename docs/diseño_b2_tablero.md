# Diseño B2 — Tablero de indicadores (E3)

**24/09/2026 · Etapa E3 del Anexo I §5 ("Tablero de control", B2), que depende de E1 y E2, ya cerradas.**
- Las 10 métricas del Anexo I §4 con corte por período, cliente, zona, vehículo y repartidor.
- Solo lectura: no guarda nada ni agrega tablas. El techo sigue en 25.
- Acta: RF-44, changelog 4.24.

**No es un criterio de aceptación** (acta §8: "que tenga tablero, que muestre métricas agregadas" no lo es). Es la función de lectura que la Empresa contrató para la etapa E3.

---

## 1. Filtros

- **Período:** `desde` / `hasta`, hasta 366 días, mismo tope que los exportes.
- **Cliente** y **zona:** filtran lo que es de un pedido.
- **Vehículo** y **repartidor:** filtran lo que es de una ruta. Sobre los pedidos, filtran por la ruta que los llevó.

**Lo que no se hace:** el costo de una ruta **no se prorratea** entre sus clientes ni entre sus zonas. Con un filtro de cliente o de zona, los indicadores de ruta (km, tiempo, costo, margen, ocupación) no se muestran y la pantalla dice por qué. Inventar un criterio de reparto sería un número con cara de dato.

## 2. Definiciones

| Indicador | Definición |
|---|---|
| **Entregas por día** | Pedidos entregados con fecha de entrega en el período, divididos por los días con al menos una entrega. Además, una serie por día. |
| **Km por entrega** | Suma de (`km_final` − `km_inicial`) de las rutas cerradas del período, dividida por sus paradas completadas. |
| **Tiempo por entrega** | Promedio, en minutos, de `salida_en` − `llegada_en` de las paradas completadas de las rutas del período. Descarta tiempos negativos. |
| **Facturación por cliente** | Suma de los `factura_items` aprobados nacidos en el período (el mismo criterio que B7 y los rangos). Se muestran los 10 primeros clientes y el resto agrupado en "Otros". |
| **Facturación por rango** | La misma suma, agrupada por el rango **efectivo de hoy** de cada cliente: no hay historial de rango por fecha para repartirla, y se aclara en pantalla. |
| **Cancelaciones** | Pedidos cancelados sobre los pedidos con fecha de entrega en el período, excluidos los que siguen en borrador. |
| **Costo por entrega / por ruta** | Costos de las rutas cerradas (combustible, peajes, otros y pago al repartidor), divididos por sus paradas completadas o por la cantidad de rutas. |
| **Margen por ruta / por día** | Ingresos de la ruta (total de sus pedidos entregados) menos sus costos, como `CalcularResultadoAsync`. Promedio por ruta y serie por día. |
| **Ocupación de flota** | Suma de paradas de entrega sobre la suma de `capacidad_paradas` de las rutas en curso o cerradas del período. |
| **NPS** | **Sin datos.** El Anexo I §7 prevé el indicador pero deja afuera el mecanismo de encuesta. La tarjeta existe y lo dice. |

## 3. Endpoint y pantalla

- `GET /api/tablero?desde&hasta&clienteId&zonaId&vehiculoId&repartidorId`, solo Administración: incluye márgenes (RNF-08).
- **Pantalla `/tablero`:**
  - una fila de tarjetas con los indicadores;
  - un gráfico de columnas con las entregas por día;
  - un gráfico de margen por día sobre una línea base: azul si es positivo, naranja si es negativo;
  - barras horizontales de facturación por cliente y por rango.
- Cada gráfico tiene su tabla y un tooltip al pasar el mouse. Paleta validada con el validador de la skill de visualización, en modo claro y oscuro.

## 4. Fuera de este diseño

- Comparación contra el período anterior.
- Series guardadas o calculadas de noche: el sistema no tiene scheduler y todo se calcula en cada consulta.
- Prorrateo de costos por cliente o zona.
- La encuesta de NPS.
- Exportar el tablero: para eso están los CSV de `/exportar`.
