# Acta del Sistema de Gestión Logística
**Versión 4.0 — Documento consolidado · 11/09/2026**
*(antes `acta_sistema_v3.md` — mismo documento, nombre sin número de versión desde esta edición)*

Reemplaza y deja sin efecto: Acta de alcance v1.0, v1.1 y v2.0, y el Acta funcional v1.0.
Documento único de referencia. Toda discusión de alcance se resuelve contra este texto.

---

## 1. Qué es

Sistema de gestión operativa para una empresa de logística tercerizada multi-cliente. Administra el ciclo completo del pedido: carga, tarificación, agrupamiento en ruta, ejecución en calle, prueba de entrega y cierre económico de la jornada.

**No es** un ERP, un sistema de stock ni un e-commerce. **Sí es**, desde esta versión, el sistema de facturación interna y cobranza de la propia Empresa hacia los clientes que le contratan el servicio de logística: cuenta corriente, factura del servicio, vencimiento y cobro. Lo que sigue fuera es la emisión fiscal ante el organismo recaudador — el sistema calcula, registra y reclama; no reemplaza al sistema contable de la Empresa (§2, changelog 4.0).

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
| Clasificación automática de clientes | No hay historial. Cualquier fórmula hoy sería inventada. | Ciclo trimestral (§9.3), primera corrida solo con historial suficiente para no inventar la fórmula |
| Portal de carga para el cliente | Ver sección 9. | Cuando el cliente lo pida explícitamente |

---

## 3. Principios estructurales

Cambiarlos después implica migración de datos, no refactor.

**P1. El precio se congela en el pedido.** Snapshot al confirmar. Nunca se recalcula contra la lista vigente.

**P2. Todo cambio de estado queda registrado automáticamente**, con actor, motivo y hora, por trigger de base de datos. No por decisión de la aplicación.

**P3. El precio depende de la zona; la ruta depende de la coordenada.** El importe se calcula sobre la zona del destino. Las coordenadas existen para ordenar paradas y detectar direcciones dudosas, nunca para tarifar.

**P4. Registrar es gratis, reconstruir es imposible.** Todo evento relevante se guarda desde el día uno aunque hoy no se use.

**P5. Ningún dato de la operación se ingresa dos veces.** Lo que se carga en la calle no se vuelve a tipear.

**P6. Se construye por dolor medido o por ciclo de operación.** Un módulo se justifica de dos maneras y solo de esas dos: su ausencia ya cuesta tiempo medible, o es función básica de un ciclo que la operación repite — diario, semanal, mensual o trimestral (§9.3). Lo que no duele todavía y no cierra un ciclo, no se construye.

**P7. La capacidad se mide en paradas, no en bultos.** El límite del vehículo son las horas del repartidor: 20 a 28 paradas por jornada en conurbano. El volumen de carga casi nunca es la restricción activa.

---

## 4. Modelo de datos

**Nueve tablas núcleo.** Detalle completo de DDL en el documento de especificación técnica.

| Bloque | Tablas |
|---|---|
| Geografía | `zonas`, `localidades`, `ubicaciones` |
| Comercial | `clientes`, `tarifas` |
| Núcleo | `pedidos`, `pedido_eventos` |
| Operación | `rutas`, `ruta_paradas`, `parada_pedidos`, `pruebas_entrega`, `vehiculos` |
| Registro sin maquinaria | `tipos_evento_cliente`, `eventos_cliente` |

**Fuera del modelo inicial:** `listas_precio`, `reglas_precio`, `contratos_dedicados`, `repartidores`, `liquidaciones`, `cliente_scores`, `condiciones_comerciales`.

`cuentas_cobrar` deja de estar fuera del modelo inicial desde changelog 4.0: es función básica del ciclo semanal (§9.3), no catálogo anticipado.

Dos entidades concentran las relaciones: `pedidos` (qué se prometió y a qué precio) y `ruta_paradas` (qué pasó en la calle). Toda tabla que no se conecte a alguna de las dos está fuera de alcance por definición. `vehiculos` entra por esa puerta: cuelga de `rutas`, cabecera de `ruta_paradas`. El texto libre que traía `rutas.vehiculo` no identificaba la unidad de forma confiable ni tenía dónde registrar vencimientos (VTV, seguro) — el motivo del alta es operativo, no una ampliación de alcance comercial.

### 4.1 Campos incorporados en esta versión

| Campo | Tabla | Para qué |
|---|---|---|
| `salida_en` | `ruta_paradas` | Junto a `llegada_en`, da el tiempo real de servicio por parada. Es el dato que revela qué cliente es caro de atender. Sin él, los clientes caros consumen margen en silencio. |
| `desvio_metros` | `pruebas_entrega` | Distancia entre la coordenada de la prueba y la del destino. Detecta entrega en dirección equivocada y sostiene la evidencia ante un reclamo. |
| `capacidad_paradas` | `rutas` | Presupuesto de paradas de la jornada. El sistema avisa cuando la ruta lo excede (P7). |
| `tipo` con valor previsto `deposito` | `ruta_paradas` | Previsión de arquitectura, sección 10.2. No se usa hoy. |
| `origen_ubicacion_id` | `rutas` | Punto de partida de la jornada: un depósito del catálogo o cualquier otra dirección (donde quedó el vehículo el día anterior). `null` = todavía sin elegir; ya no hay depósito implícito, `CerrarPlanificacion` exige un valor concreto antes de pasar a en curso. |
| `nombre_deposito` | `ubicaciones` | No-nulo = esta ubicación es un depósito del catálogo, con este nombre corto. Único entre no-nulos (índice parcial). Varias filas pueden tener el campo cargado a la vez: el catálogo admite más de un depósito. |

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

Ninguna se construye antes por tener tiempo libre: o dispara el hecho de esta tabla, o entra por el ciclo de operación de §9.3. Cada función que existe es una función que hay que mantener.

### 9.3 Etapas por ciclo de operación

Segunda puerta de P6: un módulo se construye si es función básica de un ciclo que la operación repite, aunque el dolor todavía no se haya medido en tiempo perdido.

| Ciclo | Qué cierra | Funciones básicas |
|---|---|---|
| Diario | La jornada | Alta y tarificación, corte de carga, armado y cierre de ruta, ejecución en calle con prueba de entrega |
| Semanal | El cobro y el pago | Facturación al cliente, vencimiento y aviso, pago al repartidor |
| Mensual | El resultado | Costos fijos, rentabilidad, liquidación por período |
| Trimestral | La revisión | Recálculo de rango de cliente, revisión de zonas por costo real de servicio |

Un módulo que entra por esta tabla no espera el disparador de §9.2. Ampliar esta tabla es decisión de negocio registrada en §14, no criterio del día.

---

## 10. Previsiones de arquitectura

Cosas que no se construyen hoy pero cuyo lugar se deja reservado, porque incorporarlas después cuesta migración.

### 10.1 Multi-vehículo

Con un vehículo el problema es en qué orden visitar. **Con tres o más, el problema caro es qué parada va a qué vehículo**, y una asignación mala no la corrige ningún ordenamiento bueno.

El modelo ya soporta N rutas por día, y desde esta versión también el catálogo de flota (`vehiculos`, tabla con patente y ficha propia en vez de texto libre en `rutas`). Lo que falta es la pantalla de asignación masiva por zona, previa al armado individual de cada ruta. Se construye al tercer vehículo (9.2), no antes.

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
| Modelo de usuarios | Personal interno (administración, operación, repartidor) y consulta de cliente son dos tablas separadas, no cuatro roles en una — el login de un cliente no debe convivir con la gestión del personal interno ni compartir su ABM. El actor del log deja de ser texto libre. |
| Formato de importación | Seis columnas obligatorias: referencia externa, destinatario, teléfono, dirección, localidad, bultos. El resto mapeable. |
| Geocodificación y matriz de distancias | Desdobladas. No se usa matriz de distancias: con 8 a 12 paradas, nearest-neighbor con mejora local sobre distancia en línea recta queda a pocos puntos del óptimo. Solo se requiere geocodificador, una vez por dirección nueva, cacheado permanentemente. |
| Entidad `Destinatario` propia | No se crea (changelog 3.5). El destinatario sigue siendo texto en `pedidos` — la reutilización se resuelve agregando el historial existente, no persistiendo una agenda paralela. Cruzaría el techo de tablas por segunda vez sin dolor medible que lo justifique (P6), y el tratamiento de datos del destinatario todavía no tiene dictamen legal (§11.2) — persistirlos en una tabla propia y reutilizable es exactamente lo que ese dictamen tiene que resolver primero. |

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

**Sobreconstrucción.** Escribir software cuesta cada vez menos; mantenerlo y depurarlo, no. Cada módulo hecho sobre suposiciones se reescribe cuando aparece la realidad, y cuesta más tirarlo porque ya está escrito. El principio P6 existe para contener esto, ahora por dos puertas: dolor medido o ciclo cerrado (§9.3). Lo que no entra por ninguna de las dos, no se construye.

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
| **3.1** | **30/08/2026** | Alta de la tabla `vehiculos` (bloque Operación, §4): reemplaza el texto libre `rutas.vehiculo` por una ficha de flota (patente, descripción, marca/modelo/año, km, vencimientos de VTV y seguro, costo/km, capacidad de paradas). Sale de "fuera del modelo inicial" porque cuelga de `rutas`. Nota agregada en §10.1. |
| **3.2** | **30/08/2026** | `zonas.km_desde` / `zonas.km_hasta`: rango de distancia de cada zona, visible y editable junto al precio en la pantalla Tarifas. Sin valores de fábrica, mismo criterio que el precio (§13): un número inventado es peor que un casillero vacío. |
| **3.3** | **30/08/2026** | §11.1 "Modelo de usuarios": el login de consulta de cliente sale de `usuarios` (personal interno) a una tabla propia, `clientes_usuarios`, atada a la empresa. Motivo de negocio: los dos tipos de cuenta no deben compartir tabla ni gestión — el ABM del login de un cliente se hace desde su propia ficha, no desde la pantalla de Usuarios del staff. Sube a 14 tablas núcleo, cruzando el techo de 13 de `construccion_v1.md` §1 con esta línea como justificación. Detalle técnico en `construccion_v1.md` (no acá, regla de corte §0) y en `schema_v3.sql`. |
| **3.4** | **31/08/2026** | Módulo de mapa (planificador y repartidor) y ruteo real por calles (OSRM). No es alta de alcance por catálogo (P6): es capa de lectura sobre datos que ya existen y ya se pagaron — `ubicaciones.lat/lng` (RF-04) y `v_paradas_repartidor` ya traían coordenada sin usarla en ninguna pantalla. Cero tablas nuevas, sigue en 14. Se ejecuta junto con H2 (`construccion_v1.md` §8), no en su lugar: la guía de ruta del repartidor es la puerta de entrada al cierre de parada (RF-19–RF-24) que H2 exige construir de todos modos. Detalle técnico en `construccion_v1.md` §1 y §4.2. |
| **3.5** | **31/08/2026** | Autocompletado de destinatarios y direcciones en el alta de pedido. No es alta de alcance por catálogo (P6): es capa de lectura sobre datos que ya existen y ya se pagaron — `pedidos.destinatario_nombre`/`destinatario_telefono` y la `ubicacion` de destino se cargan en cada alta y nunca se volvían a leer. Cero tablas nuevas, sigue en 14. **Decisión tomada: no se crea una entidad `Destinatario`.** La sugerencia se calcula agrupando el historial de `pedidos` del propio cliente, no persistiendo una agenda paralela — evita cruzar el techo de tablas por segunda vez y no depende del dictamen legal pendiente sobre datos del destinatario (§11.2), que sigue abierto. Detalle técnico en `construccion_v1.md` §4.1. |
| **3.6** | **31/08/2026** | Punto de partida variable de la ruta. Una jornada puede arrancar desde el depósito (default, comportamiento histórico) o desde donde quedó la camioneta el día anterior; el planificador lo elige a mano al armar. **No es rastreo de flota:** no se infiere la posición del vehículo, se registra la decisión del planificador — el rastreo automático sigue fuera de alcance (§2). No es alta de alcance por catálogo (P6): una columna nullable en `rutas` (`origen_ubicacion_id`, §4.1) que apunta a `ubicaciones`, tabla que ya existe y ya se paga. Cero tablas nuevas, sigue en 14. RF-11 ya decía "desde el punto de retiro", no "desde el depósito": esto no amplía el requisito, recién ahora lo implementa como estaba escrito. Efecto colateral saldado: el origen se resolvía en dos lugares con dos fuentes distintas (la fila `ubicaciones.referencia='deposito'` y la sección `Deposito` de `appsettings`), que podían discrepar en silencio; queda una sola (`OrigenRutaService`). Queda pendiente, en fase separada y sin diseño todavía: el retiro directo en la ubicación de un cliente (origen por pedido, no por ruta) — depende de una conversación de precio aún no resuelta. Detalle técnico en `construccion_v1.md` §4.1/§4.2 y en `schema_v3.sql`. |
| **3.7** | **31/08/2026** | La dirección del depósito deja de ser fija: hasta acá solo se fijaba una vez, al sembrar la base desde `appsettings` (`Deposito:CalleNumero/Localidad/Lat/Lng`), sin ninguna pantalla para corregirla. Ahora administración la edita desde `/deposito` (RNF-08: fuera del alcance de operación). **Decisión sobre el historial:** una ruta ya *cerrada* conserva para siempre la dirección que el depósito tenía el día que cerró — cambiar el depósito después no le reescribe el origen. Una ruta todavía planificada o en curso sí ve el cambio al instante, porque su origen (`rutas.origen_ubicacion_id = null`) se resuelve en vivo hasta ese momento; el cierre económico (`RutasController.Cerrar`) es quien materializa el id concreto y la congela. La dirección vieja nunca se edita en el lugar — se mueve la marca "depósito" a una `Ubicacion` distinta (creada o reutilizada por el mismo resolver-o-crear del alta de pedido), así que cualquier ruta o pedido que ya apuntaba a esa fila por id conserva la dirección exacta que tenía. Cero tablas nuevas. |
| **3.8** | **31/08/2026** | El depósito único de 3.7 pasa a ser un **catálogo de depósitos con nombre**, todos seleccionables al armar una ruta — la operación real puede tener más de un punto fijo de partida (varios locales, por ejemplo), no solo uno editable. Reemplaza `/deposito` por `/depositos` (ABM: alta, cambio de nombre, baja). **Decisión explícita del usuario: ya no hay depósito "principal" para rutas.** Antes `origen_ubicacion_id = null` significaba "depósito" (un default implícito); ahora significa "todavía sin elegir", y `CerrarPlanificacion` (RF-17) lo bloquea — el planificador elige siempre a mano entre lo que haya en el catálogo, o tipea otra dirección. La única excepción es el alta de pedido (RF-11 la sigue necesitando sin pantalla de elección propia, fuera de alcance de esta versión): usa el depósito más antiguo del catálogo como default, vía un resolver separado (`PrincipalParaPedidosAsync`) que no comparte código con el de rutas. **Por qué un campo propio y no reusar `Referencia`:** esa columna de `ubicaciones` ya significa la nota de una parada que ve el repartidor (`v_paradas_repartidor`) — mezclar los dos sentidos rompía esa vista para cualquier ubicación marcada como depósito. Se agrega `nombre_deposito` (§4.1): no-nulo y único entre no-nulos marca qué filas son depósito y con qué nombre. Historial igual que en 3.7: renombrar es en el lugar (cosmético), pero "mover" un depósito es desactivar la fila vieja y crear una nueva — ninguna ruta ya cerrada pierde su dirección histórica. Cero tablas nuevas, sigue en 14. Detalle técnico en `construccion_v1.md` §4.1/§4.2 y en `schema_v3.sql`. |
| **3.9** | **31/08/2026** | El catálogo de `localidades` deja de ser estático: hasta acá solo tenía las 5 filas cargadas a mano en el seed, y una dirección real en cualquier otro partido no tenía forma de entrar al sistema. Ahora, al buscar una localidad (alta de pedido, depósito, o punto de partida "otra dirección" de una ruta), el buscador consulta el catálogo propio y, si no alcanza, también OSM (Nominatim) — una localidad real que no está en el catálogo aparece como sugerencia y se da de alta sola al elegirla, **siempre sin zona** (`zona_id = null`). No es alta de alcance por catálogo (P6): es capa de búsqueda sobre una tabla y una columna que ya existían y ya se pagaban (`localidades.zona_id` nullable desde el día uno). **Decisión explícita: la zona nunca se infiere.** Una localidad recién descubierta queda disponible al instante para depósitos y direcciones, pero un pedido ahí sigue bloqueado — `PedidosController.Cotizar`/`Crear` ya rechazaban esto antes de esta versión ("la localidad no tiene zona asignada"), y esa regla no cambia; lo único nuevo es que ahora sí hay una forma de llegar a ese estado sin editar la base a mano. Asignarle zona a una localidad nueva sigue sin tener pantalla propia (fuera de alcance: hoy se hace por SQL, igual que el resto del catálogo antes de esta versión). Cero tablas nuevas. Detalle técnico en `construccion_v1.md` §4.1/§9. |
| **3.10** | **31/08/2026** | Cierra el hueco que dejó 3.9: una localidad descubierta al tipear una dirección quedaba sin zona y sin ninguna pantalla para asignársela — la única forma de destrabarla era editar la base a mano. Ahora `/tarifas` suma "Localidades sin zona": lista las pendientes y, junto a cada una, sugiere una zona por **distancia real** (haversine contra el depósito que usa hoy el alta de pedido, `PrincipalParaPedidosAsync`) comparada contra el rango de km que administración ya carga por zona en esa misma pantalla (`zonas.km_desde/km_hasta`, changelog 3.2) — dato que existía desde antes y no se usaba para nada. **La sugerencia nunca se aplica sola:** precarga el combobox, pero hace falta el click de "Asignar": sigue en pie la decisión de 3.9 de que el precio no se infiere. Si la localidad todavía no tiene ninguna dirección geocodificada (nadie completó el alta que la creó), no hay de dónde sacar distancia y el combobox queda vacío para elegir a mano. Cero tablas nuevas. Detalle técnico en `construccion_v1.md` §4.1/§12. |
| **4.0** | **11/09/2026** | **Cambia un principio estructural (§3) y la definición de qué es el sistema (§1) — no es refactor, por eso el salto de versión mayor, no un 3.12.** P6 pasa de una puerta ("se construye por dolor") a dos: dolor medido o función básica de un ciclo de operación que la Empresa repite — diario, semanal, mensual, trimestral. Nueva §9.3 con la tabla de ciclos y sus funciones básicas; §9.2 corregida para no contradecir la puerta nueva; §12 (riesgo de sobreconstrucción) actualizado a las dos puertas. §1 pasa a declarar que el sistema **sí** es el sistema de facturación interna y cobranza de la Empresa hacia sus clientes (cuenta corriente, factura del servicio, vencimiento, cobro), manteniendo fuera solo la emisión fiscal — corrige la contradicción con el módulo de cuenta corriente ya comprometido en `Anexo_I_Alcance_V2.docx` (E1/B1/D11-D12). `cuentas_cobrar` sale de "fuera del modelo inicial" (§4): entra por el ciclo semanal, no por catálogo. §2, fila "Clasificación automática de clientes": el disparador pasa de "6 meses de operación registrada" al ciclo trimestral de §9.3. Origen de la decisión: `Anexo_I_Alcance_V2.docx`, cuyo cronograma de etapas E0-E5 necesitaba respaldo en este documento para no quedar sin él bajo la cláusula de precedencia del propio Anexo (§"Nota de versionado"). **Pendiente, no resuelto en esta versión:** el techo de tablas de `construccion_v1.md` §1 (14 tablas hasta el tercer cliente) no contempla el módulo de facturación; requiere número nuevo acordado. |
| **3.11** | **31/08/2026** | El precio deja de depender solo de la zona: una entrega en moto y una en camioneta tienen tarifa propia, no una es un factor de la otra. **Decisión explícita del usuario, con una consecuencia deliberada sobre P1:** el tipo de vehículo se *deriva* del vehículo real asignado a la ruta, no lo elige el cliente al cargar el pedido — así que el precio ya no se congela al confirmar el alta, se congela recién cuando la ruta que lo lleva cierra su planificación (`CerrarPlanificacion`, RF-17) y se conoce el vehículo. Esto no inventa un estado nuevo: el pedido pasa a usar de verdad el paso `Borrador → Confirmado` que la máquina de estados (§5 de `construccion_v1.md`) ya documentaba desde el principio ("precio calculado y congelado") pero que hasta ahora se saltaba siempre (`Crear` iba directo a `Confirmado`). Mientras un pedido sigue en Borrador, el alta muestra un **estimado** (camioneta y moto, los dos), nunca un precio comprometido. `vehiculos` gana `tipo` (camioneta|moto, default camioneta — la flota fue siempre así hasta ahora) y `tarifas` gana esa misma dimensión (tarifas existentes migran a camioneta, las de moto nacen vacías — mismo criterio de "casillero vacío antes que número inventado", §13). Riesgo aceptado a sabiendas: si al cerrar la planificación de una ruta falta la tarifa de algún pedido para el tipo de vehículo elegido, la ruta entera rechaza el cierre (nada queda a medio confirmar) — el planificador se entera recién ahí, no al cargar el pedido. Cero tablas nuevas. Detalle técnico en `construccion_v1.md` §5/§6/§12. |
