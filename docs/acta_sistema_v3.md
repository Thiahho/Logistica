# Acta del Sistema de Gestión Logística
**Versión 3.0 — Documento consolidado · 27/08/2026**

Reemplaza y deja sin efecto: Acta de alcance v1.0, v1.1 y v2.0, y el Acta funcional v1.0.
Documento único de referencia. Toda discusión de alcance se resuelve contra este texto.

---

## 1. Qué es

Sistema de gestión operativa para una empresa de logística tercerizada multi-cliente. Administra el ciclo completo del pedido: carga, tarificación, agrupamiento en ruta, ejecución en calle, prueba de entrega y cierre económico de la jornada.

**No es** un ERP, un sistema de stock, un e-commerce, una plataforma de facturación ni un sistema de cobranza.

**Estado del negocio al momento de esta versión:** dos clientes cerrados, un vehículo utilitario propio, un repartidor, cuatro prospectos en conversación. El sistema se dimensiona para eso, con previsiones estructurales para multi-vehículo (sección 10).

---

## 2. Qué NO hace

| No hace | Por qué | Se revisa |
|---|---|---|
| Control de inventario o stock | Movemos bultos, no administramos productos. Si el sistema muestra "productos", el cliente espera cuadrar contra su inventario y el desvío pasa a ser problema propio. | Nunca |
| Emisión de comprobantes fiscales | El sistema calcula y registra importes. La emisión ocurre fuera. | Cuando el volumen haga inviable facturar a mano |
| Cobro contrareembolso | Efectivo en la calle es arqueo, riesgo de robo, seguro distinto y responsabilidad por fondos de terceros. Otro negocio. | Nunca |
| Almacenamiento de imagen de documento de identidad | Dato personal sensible de alguien sin relación contractual con la empresa. | Solo con dictamen legal y exigencia contractual |
| Ruteo automático sin revisión humana | El operador conoce restricciones que el algoritmo no ve. | Nunca |
| Optimización con ventanas horarias | Problema de otra clase, requiere solver dedicado. | A partir de 3 vehículos simultáneos |
| Tracking público para el destinatario | El destinatario no paga y no reclama. | Sin fecha |
| Aplicación nativa | La PWA cubre el caso de uso. | Sin fecha |
| Clasificación automática de clientes | No hay historial. Cualquier fórmula hoy sería inventada. | 6 meses de operación registrada |
| Portal de carga para el cliente | Ver sección 9. | Cuando el cliente lo pida explícitamente |

---

## 3. Principios estructurales

Cambiarlos después implica migración de datos, no refactor.

**P1. El precio se congela en el pedido.** Snapshot al confirmar. Nunca se recalcula contra la lista vigente.

**P2. Todo cambio de estado queda registrado automáticamente**, con actor, motivo y hora, por trigger de base de datos. No por decisión de la aplicación.

**P3. El precio depende de la zona; la ruta depende de la coordenada.** El importe se calcula sobre la zona del destino. Las coordenadas existen para ordenar paradas y detectar direcciones dudosas, nunca para tarifar.

**P4. Registrar es gratis, reconstruir es imposible.** Todo evento relevante se guarda desde el día uno aunque hoy no se use.

**P5. Ningún dato de la operación se ingresa dos veces.** Lo que se carga en la calle no se vuelve a tipear.

**P6. Se construye por dolor, no por catálogo.** Ningún módulo se construye antes de que su ausencia cueste tiempo medible.

**P7. La capacidad se mide en paradas, no en bultos.** El límite del vehículo son las horas del repartidor: 20 a 28 paradas por jornada en conurbano. El volumen de carga casi nunca es la restricción activa.

---

## 4. Modelo de datos

**Nueve tablas núcleo.** Detalle completo de DDL en el documento de especificación técnica.

| Bloque | Tablas |
|---|---|
| Geografía | `zonas`, `localidades`, `ubicaciones` |
| Comercial | `clientes`, `tarifas` |
| Núcleo | `pedidos`, `pedido_eventos` |
| Operación | `rutas`, `ruta_paradas`, `parada_pedidos`, `pruebas_entrega` |
| Registro sin maquinaria | `tipos_evento_cliente`, `eventos_cliente` |

**Fuera del modelo inicial:** `listas_precio`, `reglas_precio`, `contratos_dedicados`, `vehiculos`, `repartidores`, `liquidaciones`, `cuentas_cobrar`, `cliente_scores`, `condiciones_comerciales`.

Dos entidades concentran las relaciones: `pedidos` (qué se prometió y a qué precio) y `ruta_paradas` (qué pasó en la calle). Toda tabla que no se conecte a alguna de las dos está fuera de alcance por definición.

### 4.1 Campos incorporados en esta versión

| Campo | Tabla | Para qué |
|---|---|---|
| `salida_en` | `ruta_paradas` | Junto a `llegada_en`, da el tiempo real de servicio por parada. Es el dato que revela qué cliente es caro de atender. Sin él, los clientes caros consumen margen en silencio. |
| `desvio_metros` | `pruebas_entrega` | Distancia entre la coordenada de la prueba y la del destino. Detecta entrega en dirección equivocada y sostiene la evidencia ante un reclamo. |
| `capacidad_paradas` | `rutas` | Presupuesto de paradas de la jornada. El sistema avisa cuando la ruta lo excede (P7). |
| `tipo` con valor previsto `deposito` | `ruta_paradas` | Previsión de arquitectura, sección 10.2. No se usa hoy. |

---

## 5. Requisitos funcionales

### 5.1 Carga y tarificación

- **RF-01** Alta de pedido completo en menos de 30 segundos.
- **RF-02** Congelamiento del precio al confirmar, sin recálculo posterior.
- **RF-03** Resolución automática de zona a partir de la localidad de destino.
- **RF-04** Obtención y persistencia de coordenadas al momento de la carga, reutilizadas en cargas posteriores del mismo destino.
- **RF-05** Marcado visible de toda dirección no localizable, de baja confianza o fuera de zona cubierta, bloqueada para corrección manual antes de entrar a una ruta.
- **RF-06** Registro del origen de carga de cada pedido (interno, importado, portal, integración).
- **RF-07** Teléfono del destinatario como campo obligatorio.
- **RF-08** Cierre automático de la carga a la hora de corte, congelando el conjunto del día siguiente.
- **RF-09** Precio por cliente y zona, con la lista general como default.

### 5.2 Planificación

- **RF-10** Listado de pedidos confirmados del día agrupados por zona.
- **RF-11** Orden de paradas sugerido automáticamente, minimizando recorrido total desde el punto de retiro.
- **RF-12** Reordenamiento manual siempre disponible sobre la sugerencia.
- **RF-13** Anclaje de paradas urgentes en su posición, ordenando el resto alrededor.
- **RF-14** Consolidación en una sola parada de todos los retiros en la misma dirección.
- **RF-15** Asignación de vehículo y repartidor.
- **RF-16** Validación de capacidad en paradas y alerta al excederla (P7).
- **RF-17** Cierre de la ruta como planificada, quedando disponible para el dispositivo del repartidor.

### 5.3 Ejecución en calle

- **RF-18** Lista de paradas del día en el orden planificado, con progreso visible.
- **RF-19** Cambio de estado por parada, en el momento y en el lugar.
- **RF-20** Prueba de entrega con foto, nombre del receptor, posición y hora de captura.
- **RF-21** Motivo de entrega fallida desde lista cerrada de opciones.
- **RF-22** Contacto telefónico directo con el destinatario desde la parada.
- **RF-23** Verificación de identidad registrada sin almacenar imagen del documento.
- **RF-24** Registro de hora de llegada y de salida por parada.
- **RF-25** Operación completa sin conexión (ver RNF-01).

### 5.4 Cierre y trazabilidad

- **RF-26** Cierre de ruta con kilómetros, combustible, peajes, entregas efectivas, fallidas y reprogramadas.
- **RF-27** Resultado económico de cada ruta disponible el mismo día.
- **RF-28** Registro inmutable de toda transición de estado.
- **RF-29** Cálculo del desvío entre la prueba de entrega y el destino, con marcado sobre umbral.
- **RF-30** Exportación tabular de pedidos, rutas y resultados por rango de fechas.

### 5.5 Registro de comportamiento

- **RF-31** Registro desde el día uno de todo evento relevante del cliente: pagos, rechazos, cambios sobre ruta armada, direcciones erróneas, reclamos.
- **RF-32** Tres indicadores independientes por cliente (pago, trato, operación), asignados manualmente hasta que haya volumen suficiente.
- **RF-33** Indicador de uso interno, nunca visible para el cliente.

**Sobre RF-32:** un indicador único obliga a promediar dimensiones que llevan a decisiones opuestas. El cliente que más factura y peor paga es simultáneamente el más valioso y el más riesgoso, y el promedio destruye esa información. Default para cliente nuevo: pago en rojo, trato y operación en amarillo. Un cliente sin historial no es neutro, es desconocido, y el costo del error es asimétrico.

---

## 6. Requisitos no funcionales

| ID | Garantía | Por qué |
|---|---|---|
| **RNF-01** | Operación completa sin conexión con cola de sincronización diferida | Toda la información nace de una persona en la vereda apretando un botón. Si esa pantalla falla, el resto del sistema son números inventados con mejor tipografía. **Requisito de la primera versión, no mejora posterior.** |
| **RNF-02** | Idempotencia de la sincronización | En la calle, ante una pantalla que no responde, el botón se toca dos veces. Siempre. |
| **RNF-03** | La hora válida es la de captura; ambas se almacenan | Una entrega hecha a las 10 y sincronizada a las 17 debe figurar a las 10. |
| **RNF-04** | Trazabilidad automática por trigger, no por aplicación | Si el log depende de que la aplicación se acuerde, en tres semanas hay huecos, y aparecen justo en los casos conflictivos. |
| **RNF-05** | Pedido en menos de 30 segundos; ruta en menos de 15 minutos | Si es más lento que la planilla, se vuelve a la planilla. |
| **RNF-06** | Flujo del repartidor operable con una mano, máximo tres toques, sin scroll | Se usa parado, apurado, con el bulto en la otra mano. |
| **RNF-07** | Degradación segura: la ruta se descarga completa antes de salir | Una caída durante la jornada no detiene la operación. |
| **RNF-08** | Control de acceso por rol: administración, operación, repartidor, consulta de cliente | El repartidor no ve precios; el cliente no ve otros clientes. |
| **RNF-09** | Datos personales acotados y con retención definida | Los destinatarios no son clientes y no consintieron nada. |
| **RNF-10** | Respaldo diario con restauración verificada | Un respaldo que nunca se probó no es un respaldo. |

---

## 7. Reglas operativas que el sistema impone

Codificadas en el software, no libradas al criterio del día.

**Estructura de la jornada**

| Hora | Evento | Responsable |
|---|---|---|
| 18:00 (D-1) | Cierre de carga. Los pedidos se congelan. | Sistema |
| 18:00–19:00 (D-1) | Zonificación, orden de paradas, asignación de vehículo | Administración |
| 07:30 | Retiro con conteo de bultos contra lista y firma | Repartidor |
| 08:00–13:00 | Primera vuelta | Repartidor |
| 13:00 | Ventana de reagrupamiento. Único ingreso de urgencias. | Administración |
| 13:00–17:00 | Segunda vuelta, urgencias, reintentos | Repartidor |
| 17:30 | Cierre de ruta | Ambos |

**Reglas**

- **El corte de carga no se cede.** Sin conjunto cerrado no hay planificación; sin planificación no hay densidad; sin densidad la operación es mensajería cara. Es la primera regla que un cliente va a pedir excepcionar.
- **Las urgencias entran en una sola ventana**, solo si desplazan menos de tres paradas, siempre con recargo. Una urgencia fuera de ventana no cuesta el recargo no cobrado: cuesta las entregas que llegaron tarde.
- **Ninguna ruta sale sin conteo firmado.** El primer conflicto serio no será por un retraso sino por un bulto faltante, y sin firma contra lista esa discusión se pierde siempre.
- **Primer reintento por ausente sin cargo; el segundo genera envío nuevo.** Sin regla escrita cada caso se negocia, y el cliente siempre tiene más urgencia de discutir.
- **El bulto no entregado vuelve y el retorno se registra como servicio.** Si no, cada devolución es un viaje regalado.
- **La hora de cierre es fija.** Lo que no salió se reprograma. Estirar la jornada convierte a la empresa en el proveedor al que se le puede pedir cualquier cosa.
- **La planificación no la hace quien manejó.** La franja de las 18:00 define si la ruta rinde.
- **Al buen cliente se lo premia con acceso, nunca con prioridad.** Mejores plazos, acceso a urgencias, acceso a vehículo dedicado. Nunca reordenando la ruta a su favor: eso destruye la densidad que hace rentable la operación para todos.

---

## 8. Criterios de aceptación

| # | Prueba | Resultado esperado |
|---|---|---|
| 1 | Cargar un pedido cronometrado | Menos de 30 segundos, precio congelado, evento registrado |
| 2 | Armar la ruta de una jornada cronometrada | Menos de 15 minutos |
| 3 | **Prueba de modo avión**: modo avión antes de abrir la app, jornada completa con foto en cada entrega, cerrar app, reabrir, restaurar conexión | Todo llega completo, sin duplicados, con la hora real de captura |
| 4 | Cerrar una ruta | Resultado económico disponible ese mismo día |
| 5 | Auditar un pedido cualquiera | Historia completa de estados, sin huecos |
| 6 | Cargar una dirección inexistente | Queda marcada y bloqueada antes de entrar a una ruta |
| 7 | Intentar modificar el precio de un pedido confirmado | El sistema lo impide |
| 8 | Operar tres rutas reales consecutivas | Las tres cierran con resultado positivo, con costos reales pagados |

**No son criterios de aceptación:** que se vea bien, que tenga tablero, que muestre métricas agregadas.

**El criterio 8 no se resuelve programando.** Está acá a propósito: los siete anteriores se cumplen enteros en una empresa que pierde plata en cada viaje.

---

## 9. Etapas

### 9.1 Primera versión — solo lo que no tiene sustituto manual

PWA del repartidor con prueba de entrega offline, registro de estados, alta y tarificación de pedidos, armado y cierre de ruta.

**Criterio:** a bajo volumen casi todo se hace en planilla. La evidencia de una entrega en la calle no se hace en ninguna otra parte, y el dato que no se captura hoy no se recupera.

**Orden de construcción:** primero la PWA. Es la única pieza sin sustituto y la más difícil de hacer bien; se construye cuando hay tiempo, no cuando ya se está operando. El importador de archivos va al final: a bajo volumen un formulario es más rápido que depurar un mapeo de columnas.

### 9.2 Etapas siguientes — habilitadas por hecho, no por fecha

| Funcionalidad | Disparador |
|---|---|
| Importación masiva de archivos | El tipeo se vuelve cuello de botella |
| Seguimiento de solo lectura para el cliente | Las consultas por estado son interrupción diaria |
| Liquidación al repartidor | Segundo repartidor |
| Asignación masiva parada-vehículo | Tercer vehículo simultáneo |
| Registro de cobranzas y semáforo de pago automático | El volumen de comprobantes hace inviable el seguimiento manual |
| Recálculo de zonas por costo real de servicio | 3 meses de tiempos de servicio registrados |
| Formato único de importación e integración directa | Quinto cliente |
| Clasificación automática de clientes | 6 meses de eventos registrados |

Ninguna se construye antes, aunque sobre tiempo. Cada función que existe es una función que hay que mantener.

---

## 10. Previsiones de arquitectura

Cosas que no se construyen hoy pero cuyo lugar se deja reservado, porque incorporarlas después cuesta migración.

### 10.1 Multi-vehículo

Con un vehículo el problema es en qué orden visitar. **Con tres o más, el problema caro es qué parada va a qué vehículo**, y una asignación mala no la corrige ningún ordenamiento bueno.

El modelo ya soporta N rutas por día. Lo que falta es la pantalla de asignación masiva por zona, previa al armado individual de cada ruta. Se construye al tercer vehículo (9.2), no antes.

**Regla de crecimiento de flota:** no se agrega vehículo hasta que el actual sostenga 85% de su capacidad de paradas durante tres semanas consecutivas. Comprar el segundo antes de llenar el primero multiplica el costo fijo sin multiplicar el margen.

**Regla de incorporación de clientes:** cada cliente nuevo se evalúa por si sus destinos caen sobre rutas que ya salen. Si caen, es margen casi puro. Si no, debe pagar la apertura de zona. Tomar clientes por orden de llegada en vez de por geografía sube la facturación y baja el margen, y el efecto es invisible durante meses.

### 10.2 Consolidación en depósito propio

Hoy cada pedido va del depósito del cliente al destino en un tramo. Un operador multi-vehículo termina siempre consolidando: todo entra a un depósito propio, se cruza, y sale ordenado por zona.

**Eso convierte cada pedido en dos tramos y es la migración más cara que espera a este sistema.** No se construye ahora. Se deja previsto el valor `deposito` en el tipo de parada desde la primera versión, aunque no se use. Costo hoy: cero.

---

## 11. Decisiones

### 11.1 Tomadas

| Decisión | Resolución |
|---|---|
| Origen del pedido: ¿nivel pedido o nivel ruta? | A nivel pedido, con colapso en la ruta. `ruta_paradas` con `tipo`, y tabla puente `parada_pedidos` para que N retiros en la misma dirección sean una sola parada. |
| Modelo de usuarios | Tabla de usuarios con cuatro roles: administración, operación, repartidor, consulta de cliente. El actor del log deja de ser texto libre. |
| Formato de importación | Seis columnas obligatorias: referencia externa, destinatario, teléfono, dirección, localidad, bultos. El resto mapeable. |
| Geocodificación y matriz de distancias | Desdobladas. No se usa matriz de distancias: con 8 a 12 paradas, nearest-neighbor con mejora local sobre distancia en línea recta queda a pocos puntos del óptimo. Solo se requiere geocodificador, una vez por dirección nueva, cacheado permanentemente. |

### 11.2 Abiertas — antes de la primera ruta

1. Configuración contractual del repartidor.
2. Fecha de última salida del fundador como acompañante en calle.
3. Límite de responsabilidad por bulto.
4. Plazo de entrega comprometido al cliente.
5. Plan de contingencia por ausencia del repartidor.

Los puntos 1 y 3, más el tratamiento de datos del destinatario, se resuelven en una única consulta legal, agendada antes de la primera ruta y no después del primer conflicto.

---

## 12. Riesgos reconocidos

**Todo depende de la calle.** La información nace de una persona con el teléfono en la mano, apurada, a veces bajo la lluvia. Ninguna sofisticación en la oficina compensa una captura mala en la vereda.

**Densidad antes que volumen.** Más clientes y más vehículos multiplican la economía de la ruta, sea buena o mala. Cinco rutas mal armadas pierden cinco veces.

**Planificar y manejar no son el mismo día.** Implica estructura antes de que el volumen la justifique del todo. Es una decisión tomada a conciencia.

**Sobreconstrucción.** Escribir software cuesta cada vez menos; mantenerlo y depurarlo, no. Cada módulo hecho sobre suposiciones se reescribe cuando aparece la realidad, y cuesta más tirarlo porque ya está escrito. El principio P6 existe para contener esto.

**Presión sobre el corte de carga.** Es la regla que más se va a poner a prueba y la que sostiene el modelo entero.

---

## 13. Lo que este documento no define

Tarifas, costos operativos, esquema de pago al reparto, márgenes objetivo, fechas de entrega por etapa y condiciones contractuales del servicio.

Se definen con la operación en marcha o con asesoramiento específico. Ponerlos hoy sería inventarlos, y un número inventado en un acta es peor que un casillero vacío.

---

## 14. Control de versiones

| Versión | Fecha | Cambios |
|---|---|---|
| 1.0 – 2.0 | 26–27/08/2026 | Ver documentos anteriores, reemplazados por esta versión |
| **3.0** | **27/08/2026** | **Documento consolidado y único.** Incorpora: segundo cliente cerrado y tarifas por cliente (RF-09); principio P7 de capacidad medida en paradas; registro de tiempo de servicio por parada (RF-24); control de desvío de la prueba de entrega (RF-29); previsiones de arquitectura para multi-vehículo y consolidación en depósito (sección 10); reglas de crecimiento de flota e incorporación de clientes por geografía. Valores monetarios excluidos por decisión: se documentan por separado. |
