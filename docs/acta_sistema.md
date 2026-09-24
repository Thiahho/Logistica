# Acta del Sistema de Gestión Logística
**Versión 4.25 — Documento consolidado · 24/09/2026**
*(antes `acta_sistema_v3.md` — mismo documento, nombre sin número de versión desde esta edición. Este
encabezado había quedado clavado en 4.7 mientras §14 ya tenía la fila 4.8 — mismo patrón de atraso que
`construccion_v1.md` señala sobre sí mismo; la versión vigente es siempre la última fila de §14.)*

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
| Portal de carga para el cliente | Construido — changelog 4.9 (22/09/2026), a pedido explícito del cliente. Ver ahí. | Hecho |

---

## 3. Principios estructurales

Cambiarlos después implica migración de datos, no refactor.

**P1. El precio se congela en el pedido.** Snapshot al confirmar. Nunca se recalcula contra la lista vigente.

**P2. Todo cambio de estado queda registrado automáticamente**, con actor, motivo y hora, por trigger de base de datos. No por decisión de la aplicación.

**P3. El precio depende de la zona; la ruta depende de la coordenada.** El importe se calcula sobre la zona del destino. Las coordenadas existen para ordenar paradas y detectar direcciones dudosas, nunca para tarifar — **excepto el recargo por kilómetro de deliverys/urgencias (Anexo I §10.2-N, changelog 4.5): ahí la distancia real entra a la fórmula como un término aditivo sobre el precio de zona ya resuelto, nunca en su reemplazo.** La excepción es puntual y declarada, no reabre la regla general: un pedido programado normal sigue tarifando solo por zona.

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
| Comercial | `clientes`, `tarifas`, `clientes_destinatarios` |
| Núcleo | `pedidos`, `pedido_eventos` |
| Operación | `rutas`, `ruta_paradas`, `parada_pedidos`, `pruebas_entrega`, `vehiculos`, `novedades` |
| Registro sin maquinaria | `tipos_evento_cliente`, `eventos_cliente` |
| Cuenta corriente (E1) | `facturas`, `factura_items`, `pagos` |

**Fuera del modelo inicial:** `listas_precio`, `reglas_precio`, `contratos_dedicados`, `repartidores`, `condiciones_comerciales`.

**Acordadas para E2 (changelog 4.21), sin construir todavía:** `parametros_liquidacion` y `liquidaciones` (B4, ciclo semanal/mensual), `rangos` y `cliente_rangos` (B3, ciclo trimestral — `cliente_rangos` es lo que antes figuraba como `cliente_scores`), `costos_fijos` y `objetivos_rentabilidad` (B7, ciclo mensual). Llevan el conteo de 19 a 25 cuando se construyan. Diseño en `diseño_e2_rangos_liquidacion.md`.

`repartidores` sigue fuera del modelo inicial después de RF-34 (changelog 4.6) y no está pendiente de alta: la disponibilidad y la carga de trabajo se derivan en lectura de `usuarios` + `rutas` + `ruta_paradas`, sin estado propio que persistir. Un repartidor sigue siendo `usuarios.rol='repartidor'`. La tabla recién tendría razón de existir si hiciera falta guardar algo que hoy no existe en ningún lado — ausencias declaradas (franco, vacaciones, licencia), condiciones contractuales (§11.2) o liquidaciones (B4/E2) — y ninguna de las tres está en alcance.

`cuentas_cobrar` dejó de estar fuera del modelo inicial desde changelog 4.0 (función básica del ciclo semanal, §9.3, no catálogo anticipado) y se materializó en E1 (changelog 4.2) — no como una tabla con ese nombre, sino como `facturas`/`factura_items`/`pagos` juntas: dos libros de solo inserción, con el saldo derivado en lectura (`v_facturas_saldo`, `construccion_v1.md` §6.1), no persistido.

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
| `precio_manual`, `precio_manual_por`, `precio_manual_en` | `pedidos` | B9 del Anexo I (§4, "+40 km → Cotización"): precio fijado a mano cuando la zona no tiene tarifa cargada en ningún tipo de vehículo. Sustituye solo el origen de `precio_base` — la fórmula del §6 de `construccion_v1.md` no cambia. Solo editable en Borrador; el trigger que congela el precio lo protege igual que al resto una vez confirmado (P1). Quién y cuándo, porque es un criterio subjetivo con efecto sobre el precio. |
| `ciclo_facturacion` | `clientes` | E1 (Anexo I §10.2-A/D2): quincenal o mensual, elegido por cliente. Se copia a cada `factura` al emitirla — cambiarlo después no reescribe el historial. |
| `corte_suspendido_hasta`, `corte_suspendido_motivo`, `corte_suspendido_por`, `corte_suspendido_en` | `clientes` | E1 (§10.2-L4, plan de cuotas): mientras la fecha no pasó, el corte de servicio por deuda vencida no bloquea altas nuevas. Sin cronograma de cuotas propio — con dos clientes cerrados, llevarlo es papel (P6); admin extiende la fecha cada vez que entra una cuota. |
| `retiro_confirmado_en`, `retiro_bultos_esperados`, `retiro_bultos_contados`, `retiro_observaciones`, `retiro_firma_path`, `retiro_km_inicial`, `retiro_device_uuid` | `rutas` | RF-35 (changelog 4.7): el retiro de las 07:30 con conteo contra lista y firma, que §7 exigía desde la v3.0 sin tener dónde registrarse. `bultos_esperados` lo congela el sistema sumando los bultos de la ruta — es la lista contra la que se firmó, y tiene que sobrevivir a cualquier cambio posterior. `bultos_contados` distinto del esperado no bloquea la salida, pero exige observación: lo que la regla protege es que la discrepancia quede escrita **antes** de salir, no que los números coincidan. |
| `cierre_repartidor_en`, `cierre_repartidor_km_final`, `cierre_repartidor_combustible`, `cierre_repartidor_peajes`, `cierre_repartidor_notas`, `cierre_repartidor_device_uuid` | `rutas` | RF-26 en dos actores (changelog 4.7): lo que el repartidor **declara** desde la calle. No son las columnas económicas del cierre (`km_inicial`, `km_final`, `combustible_monto`, `peajes_monto`), que sigue escribiendo solo administración: son un segundo juego, deliberado, para que el número de la calle y el número del cierre convivan en la fila y el desvío entre ambos sea evidencia en vez de un dato perdido. |
| `cerrada_por` | `rutas` | Quién cerró económicamente la ruta, que es el mismo acto que aprobar la declaración del repartidor (changelog 4.7). `rutas` no tiene log de eventos propio y crearlo cruzaría el techo de tablas: esta columna es la única huella del actor de la aprobación. |
| `documento_numero`, `foto_documento_path`, `foto_documento_borrada_en` | `pruebas_entrega` | RF-23 reescrito (changelog 4.7). `foto_documento_borrada_en` no es metadata de mantenimiento: es la constancia de que la imagen existió y se purgó al vencer la retención. El número sobrevive a la purga; la imagen no. |
| `bultos_declarados_cliente`, `recepcion_confirmada_en`, `recepcion_confirmada_por` | `pedidos` | RF-39 / B13 (changelog 4.9). Snapshot de lo que el cliente cargó desde el portal (RF-38), completado una sola vez al alta y nunca vuelto a exponer en ningún endpoint de edición — inmutable de hecho, sin necesitar un trigger como `retiro_bultos_contados`, porque eso lo firma un dispositivo fuera del control del backend y esto no. Las otras dos sellan quién y cuándo concilió la recepción contra ese snapshot. |

**Sigue en 17 tablas.** Ninguna de estas columnas abre una tabla nueva. El único agregado
estructural es un trigger, `trg_congelar_declaracion_repartidor`, que impide editar el retiro
firmado y el cierre declarado una vez sellados — mismo mecanismo y mismo motivo que
`trg_congelar_pedido` con el precio: un dato que quien lo recibe puede reescribir no es evidencia
de nada.

**Desde el changelog 4.8 son 18 tablas: cruza el techo de 17 con una tabla, `novedades`.** Es la
única entidad nueva de esta versión y el cruce es una decisión explícita del usuario (18/09/2026),
no un descuido: lo que el repartidor informa desde la calle —una avería, un bulto dañado, una
corrección de un dato— son **varias filas por ruta, con foto, con historial y con una respuesta de
otra persona**, y ninguna columna aditiva de `rutas` o `pruebas_entrega` lo expresa (una columna
admite un solo reporte por ruta y ninguna respuesta). Cumple la puerta 2 de P6 (§3): es función
básica del ciclo diario, no un catálogo anticipado. Es **una** tabla para cinco clases
(`incidencia_ruta`, `problema_carga`, `cambio_propuesto`, `cambio_operacion`, `cancelacion`)
porque comparten ciclo — alguien informa, alguien ve, alguien responde — y una tabla por clase
habría cruzado el techo cinco veces. Mismo principio que la declaración del retiro: lo informado no
se edita después (`trg_novedades_inmutable`); solo se completan estado, resolución y `visto_en`.
Columnas aditivas de la misma versión: `ruta_paradas.estado` admite `cancelada` (todos sus pedidos
los canceló operación con la ruta en curso) y `v_paradas_repartidor` suma `pedido_estado` —un
estado, no un importe: RNF-08 y la regla 3.4 siguen intactas.

**Desde el changelog 4.10 son 19 tablas: cruza el techo de 18 con `clientes_destinatarios`**
(RF-40, "mis clientes"). A diferencia de las cruces anteriores, esta no es una función básica de un
ciclo de operación (P6, §3) — es la reversión explícita de la decisión 3.5 (§11.1), a pedido del
cliente, con el riesgo del dictamen legal pendiente (§11.2) asumido a sabiendas. Sin trigger de
inmutabilidad: a diferencia de `pedidos`/`rutas`, esta tabla es ABM libre del propio cliente sobre
su propia libreta — nada la referencia por FK desde `pedidos` (el alta de un envío copia los
valores del contacto elegido, no guarda su id).

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
- **RF-38** Alta de pedido por el propio cliente desde un portal web propio, con corte horario diario y el precio cotizado en el momento de la carga (según el tipo de vehículo que el cliente elige) como precio final, vinculante, no estimado.
- **RF-39** Conciliación en depósito de lo recibido contra lo que el cliente declaró al cargar desde el portal (RF-38): corrección directa mientras el pedido sigue sin confirmar, con nota obligatoria si el conteo difiere.
- **RF-40** Registro propio del cliente de sus destinatarios habituales ("mis clientes") desde el portal, reutilizable al cargar un envío nuevo sin volver a tipear ni geocodificar.

**Sobre RF-38 y RF-39 (changelog 4.9):** los números rompen la secuencia por el mismo motivo que
RF-34 a RF-37 — los RF no se renumeran nunca. Van en §5.1 porque son actos de carga y
tarificación, no de calle. **RF-38 no es solo "otro canal de alta":** el precio que resuelve es
vinculante desde el momento de la carga, a diferencia de todo pedido interno, que sigue estimando
camioneta y moto hasta que la ruta que lo lleva cierra su planificación (changelog 3.11) — ver
Anexo I, definición D. **RF-39 no reabre P1:** mientras el pedido conciliado sigue en Borrador no
hay ningún precio ni destino congelado que proteger; una vez que el pedido sale de Borrador, la vía
para una discrepancia de bultos vuelve a ser el ajuste ya existente (B16), no esto.

**Sobre RF-40 (changelog 4.10) — revierte la decisión 3.5 (§11.1) a propósito, no en silencio.**
3.5 evitó una entidad `Destinatario` propia precisamente para no adelantarse al dictamen legal
pendiente sobre tratamiento de datos del destinatario (§11.2). RF-40 es distinto en un punto que
importa: es el propio cliente quien registra a mano a SUS destinatarios en SU portal, no la Empresa
derivando datos de un tercero sin pedírselo — pero sigue siendo una tabla nueva y persistente con
nombre/teléfono/dirección de una persona que no es el cliente, así que **el dictamen pendiente de
§11.2 pasa a cubrir también esta tabla**, no solo la foto de documento de RF-23. Decisión tomada a
sabiendas por el cliente (22/09/2026), riesgo explícito, no una lectura de quien construyó.

### 5.2 Planificación

- **RF-10** Listado de pedidos confirmados del día agrupados por zona.
- **RF-11** Orden de paradas sugerido automáticamente, minimizando recorrido total desde el punto de retiro.
- **RF-12** Reordenamiento manual siempre disponible sobre la sugerencia.
- **RF-13** Anclaje de paradas urgentes en su posición, ordenando el resto alrededor.
- **RF-14** Consolidación en una sola parada de todos los retiros en la misma dirección.
- **RF-15** Asignación de vehículo y repartidor.
- **RF-16** Validación de capacidad en paradas y alerta al excederla (P7).
- **RF-17** Cierre de la ruta como planificada, quedando disponible para el dispositivo del repartidor.
- **RF-34** Visibilidad de la disponibilidad de cada repartidor (en ruta / asignado / libre / inactivo) y de su carga de trabajo acumulada en un rango de fechas, para decidir asignación y reasignación sin planilla aparte.

**Sobre RF-34:** el número rompe la secuencia de esta sección a propósito. Los RF no se renumeran nunca — hay comentarios de código y dos documentos que los citan por número, y correr la numeración para ganar prolijidad rompería esas referencias en silencio. Se agrega acá, y no al final del documento, porque el requisito es de planificación: existe para decidir a quién asignar, junto a RF-15.

### 5.3 Ejecución en calle

- **RF-18** Lista de paradas del día en el orden planificado, con progreso visible.
- **RF-19** Cambio de estado por parada, en el momento y en el lugar.
- **RF-20** Prueba de entrega con foto, nombre del receptor, posición y hora de captura.
- **RF-21** Motivo de entrega fallida desde lista cerrada de opciones.
- **RF-22** Contacto telefónico directo con el destinatario desde la parada.
- **RF-23** Verificación de identidad del receptor registrada con número de documento e imagen del documento, retenida por un plazo definido y purgada al vencer: el número sobrevive a la purga, la imagen no.
- **RF-24** Registro de hora de llegada y de salida por parada.
- **RF-25** Operación completa sin conexión (ver RNF-01).
- **RF-35** Retiro registrado con conteo de bultos contra la lista de la ruta y firma del repartidor, antes de la primera parada.
- **RF-36** Novedades del repartidor sobre su ruta en curso: incidencia de ruta o vehículo, problema con la carga y corrección propuesta de un dato de contacto, cada una con categoría de lista cerrada, nota, foto opcional y respuesta de operación.
- **RF-37** Aviso al repartidor de todo cambio o cancelación que operación haga sobre un pedido de su ruta en curso, con acuse de recibo.

**Sobre RF-23 — esto revierte la redacción anterior, no la amplía.** Hasta la versión 4.6 este
requisito decía, textual: *"Verificación de identidad registrada **sin almacenar imagen del
documento**"*, y el sistema lo cumplía con un booleano (`pruebas_entrega.identidad_verificada`).
La inversión es decisión explícita del usuario, 17/09/2026, tomada después de que se le señalara
que contradecía este RF y RNF-09. Se anota como inversión y no se reescribe en silencio porque un
requisito dado vuelta sin dejar rastro se lee, seis meses después, como si el original nunca
hubiera existido — y el original era una decisión, no un descuido.

Lo que la decisión trae consigo, no como mejora futura sino como parte del mismo cambio: plazo de
retención configurado y purgado que borra el archivo dejando constancia de la fecha
(`foto_documento_borrada_en`, §4.1); el número de documento persistido aparte, porque es lo único
que sobrevive a la purga y sin él la foto no sirve para el conflicto de dentro de seis meses;
acceso a la imagen restringido a administración, más estricto que la foto de la entrega; y la
consulta legal de §11.2 pasa de agendada a **bloqueante** — ver ahí. El riesgo asumido está en §12.

**Sobre RF-35:** el número rompe la secuencia igual que RF-34, y por el mismo motivo — los RF no
se renumeran nunca. Va en §5.3 y no en §5.2 porque es un acto de la calle, no de planificación.
El requisito no es nuevo como regla: §7 lo exige desde la v3.0 (*"ninguna ruta sale sin conteo
firmado"*). Lo que era nuevo hasta el changelog 4.7 es que existiera un lugar donde registrarlo:
la regla estaba escrita en el acta y en ningún lado del sistema.

**Sobre RF-36 y RF-37 (changelog 4.8):** los números rompen la secuencia por el mismo motivo que
RF-34 y RF-35 y van en §5.3 porque son actos de la calle. Tres límites que son parte del requisito:
(1) **el repartidor propone, no modifica.** Un cambio propuesto no toca el pedido hasta que
operación lo acepta, con su nombre como actor, y solo se puede proponer lo que P1 no congela:
teléfono, nombre del destinatario y observaciones. (2) **La dirección de destino y el precio no se
editan en vivo**: `fn_congelar_pedido` los protege desde que el pedido sale de Borrador. Una
dirección incorrecta sigue siendo una entrega fallida y sigue el circuito de siempre (reprogramar,
tres gratis, el cuarto intento genera un pedido nuevo); cambiar eso exigiría reabrir P1 y no está en
este alcance. (3) **Un problema de bultos no edita `pedidos.bultos`**: deja la evidencia (nota y
foto) y operación abre el ajuste de bultos que ya existe (B16), a mano. RF-37 cubre el otro sentido:
operación corrige un contacto o cancela un pedido —que factura el 100 % si ya estaba confirmado,
§10.2-I— y el repartidor no puede seguir yendo a un domicilio donde ya no hay nada para entregar ni
llamando a un teléfono viejo.

### 5.4 Cierre y trazabilidad

- **RF-26** Cierre de ruta con kilómetros, combustible, peajes, entregas efectivas, fallidas y reprogramadas.

**Sobre RF-26 — dos actores y dos actos, desde el changelog 4.7.** El requisito no cambia de
contenido: cambia el actor que le faltaba. El repartidor **declara** desde la calle los km del
odómetro, el combustible y los peajes que pagó, y esa declaración queda **pendiente e inmutable**.
Administración la **compara con sus propios números y la aprueba o la corrige con motivo escrito**
al cerrar la economía de la ruta, que es lo único que agrega lo que el repartidor no ve (otros
costos, pago al repartidor, margen — RNF-08). Cerrar la ruta *es* el acto de aprobar: no hay un
estado "aprobado" aparte. Hasta esta versión el dato de la calle llegaba en papel y administración
lo tipeaba, con lo cual no había ninguna declaración contra la que comparar.
- **RF-27** Resultado económico de cada ruta disponible el mismo día.
- **RF-28** Registro inmutable de toda transición de estado.
- **RF-29** Cálculo del desvío entre la prueba de entrega y el destino, con marcado sobre umbral.
- **RF-30** Exportación tabular de pedidos, rutas y resultados por rango de fechas.
- **RF-41** Pago al repartidor calculado al cerrar la ruta: entregas por un valor según el tipo de vehículo, más un bono si la ruta cumple un mínimo de entregas exitosas; los fallos que no dependen del repartidor no cuentan en su contra. Comprobante de liquidación por repartidor y período, inmutable una vez emitido. *(Changelog 4.21; construido en 4.23.)*
- **RF-43** Costos fijos mensuales y resultado del mes contra una estructura económica objetivo configurable. *(Changelog 4.21; construido en 4.23.)*
- **RF-44** Tablero de indicadores de la operación por período, con corte por cliente, zona, vehículo y repartidor: entregas por día, km y tiempo por entrega, facturación por cliente y por rango, cancelaciones, costo por entrega y por ruta, margen por ruta y por día, ocupación de flota y NPS (sin datos hasta que exista una encuesta). *(Changelog 4.24.)*

### 5.5 Registro de comportamiento

- **RF-31** Registro desde el día uno de todo evento relevante del cliente: pagos, rechazos, cambios sobre ruta armada, direcciones erróneas, reclamos.
- **RF-32** Tres indicadores independientes por cliente (pago, trato, operación), asignados manualmente hasta que haya volumen suficiente.
- **RF-33** Indicador de uso interno, nunca visible para el cliente.

**Sobre RF-32:** un indicador único obliga a promediar dimensiones que llevan a decisiones opuestas. El cliente que más factura y peor paga es simultáneamente el más valioso y el más riesgoso, y el promedio destruye esa información. Default para cliente nuevo: pago en rojo, trato y operación en amarillo. Un cliente sin historial no es neutro, es desconocido, y el costo del error es asimétrico.

- **RF-42** Rango de cliente (Sin rango, Bronce, Plata, Oro, Empresa) recalculado cada trimestre con umbrales configurables, con un ajuste manual de un rango como máximo, justificado y con vencimiento, e historial de cada cambio. El rango da un descuento sobre la tarifa general. *(Changelog 4.21; construido en 4.23.)*

**Sobre RF-42 y RF-32:** los tres indicadores de RF-32 no se reemplazan. El rango es una clasificación comercial con efecto en el precio; los indicadores siguen siendo la lectura interna de riesgo, sin efecto automático. RF-33 alcanza al rango: el cliente puede ver su rango y su descuento, nunca los números con que se calculó.

**Sobre RF-33 (brecha cerrada en changelog 4.10, no abierta por él):** el mismo criterio de "nunca visible para el cliente" tenía un hueco sin código desde antes de esta versión — `GET /api/pedidos/{id}` (alcanzable por el rol cliente sobre su propio pedido desde changelog 4.1/4.2) devolvía el nombre real del usuario interno que tocó ese pedido en su historial, y quién fijó un precio manual. Nadie lo había notado porque ninguna pantalla de cliente llamaba a ese endpoint hasta que el portal ganó una vista de detalle (RF-38 ya lo permitía, faltaba la pantalla). Corregido ahí mismo, no es una regla nueva.

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

- **El corte de carga no se cede.** Sin conjunto cerrado no hay planificación; sin planificación no hay densidad; sin densidad la operación es mensajería cara. Es la primera regla que un cliente va a pedir excepcionar. **El delivery/urgencia ad-hoc (D14, changelog 4.5) no es una excepción a esta regla: es otro producto**, fuera de la ruta planificada de la jornada, con su propio recargo — no compite por lugar en el conjunto cerrado ni lo diluye.
- **Las urgencias entran en una sola ventana**, solo si desplazan menos de tres paradas, siempre con recargo. Una urgencia fuera de ventana no cuesta el recargo no cobrado: cuesta las entregas que llegaron tarde. **Sigue sin código propio** (verificado en changelog 4.5): esta regla es sobre insertar una urgencia dentro de una ruta ya en curso, un caso distinto del delivery ad-hoc de D14.
- **Ninguna ruta sale sin conteo firmado.** El primer conflicto serio no será por un retraso sino por un bulto faltante, y sin firma contra lista esa discusión se pierde siempre. **Codificada desde el changelog 4.7** (RF-35): sin retiro confirmado, el sistema no acepta registrar llegada ni cerrar ninguna parada. Un conteo distinto al esperado no bloquea la salida — bloquearla por un bulto de diferencia cuesta la jornada entera — pero exige observación escrita antes de firmar.
- **El número de la calle no se reescribe.** Lo que el repartidor declaró al retirar y al cerrar su jornada queda como lo declaró, para siempre. Administración puede cerrar con otro número, pero entonces tiene que decir por qué, y los dos quedan a la vista uno al lado del otro. Sin esta regla el circuito es teatro: quien recibe el dato lo corrige en silencio y el dato de la calle no existió nunca. Lo impone un trigger, no la aplicación (§4.1).
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
| Entidad `Destinatario` propia | No se crea (changelog 3.5), **revertido parcialmente en changelog 4.10 (22/09/2026)** — ver esa fila. El alta interna sigue igual: la reutilización se resuelve agregando el historial existente, no persistiendo una agenda paralela. |

### 11.2 Abiertas — antes de la primera ruta

1. Configuración contractual del repartidor.
2. Fecha de última salida del fundador como acompañante en calle.
3. Límite de responsabilidad por bulto.
4. Plazo de entrega comprometido al cliente.
5. Plan de contingencia por ausencia del repartidor.

Los puntos 1 y 3, más el tratamiento de datos del destinatario, se resuelven en una única consulta legal, agendada antes de la primera ruta y no después del primer conflicto.

**Desde el changelog 4.7 esa consulta pasa de agendada a bloqueante en un punto concreto:** el
tratamiento de datos del destinatario ya no es una pregunta abierta sobre datos que el sistema
guarda de todos modos (nombre, teléfono, dirección), es la condición para operar RF-23 reescrito,
que guarda **la imagen del documento de identidad** de una persona que no es cliente y no
consintió nada (RNF-09). El plazo de retención con el que el sistema arranca (30 días) es un
valor provisional puesto para poder construir el purgado, no una decisión tomada: el plazo real,
y si la captura es admisible, salen de esa consulta. **Ninguna ruta real debería capturar imágenes
de documento antes de tenerla hecha.**

**Desde el changelog 4.10, la misma consulta pasa a cubrir también `clientes_destinatarios`
(RF-40, "mis clientes"):** es una tabla nueva y persistente de nombre/teléfono/dirección de un
destinatario, cargada esta vez a pedido explícito del cliente (no derivada por la Empresa), pero el
dato en sí es el mismo tipo de dato que 3.5 (§11.1) evitó persistir hasta tener este dictamen.
Riesgo asumido a sabiendas por el cliente, no una decisión tomada por quien construyó.

---

## 12. Riesgos reconocidos

**Todo depende de la calle.** La información nace de una persona con el teléfono en la mano, apurada, a veces bajo la lluvia. Ninguna sofisticación en la oficina compensa una captura mala en la vereda.

**Densidad antes que volumen.** Más clientes y más vehículos multiplican la economía de la ruta, sea buena o mala. Cinco rutas mal armadas pierden cinco veces.

**Planificar y manejar no son el mismo día.** Implica estructura antes de que el volumen la justifique del todo. Es una decisión tomada a conciencia.

**Sobreconstrucción.** Escribir software cuesta cada vez menos; mantenerlo y depurarlo, no. Cada módulo hecho sobre suposiciones se reescribe cuando aparece la realidad, y cuesta más tirarlo porque ya está escrito. El principio P6 existe para contener esto, ahora por dos puertas: dolor medido o ciclo cerrado (§9.3). Lo que no entra por ninguna de las dos, no se construye.

**Presión sobre el corte de carga.** Es la regla que más se va a poner a prueba y la que sostiene el modelo entero.

**Imágenes de documento de identidad de terceros (changelog 4.7).** Es el riesgo más nuevo y el único de esta lista que el propio sistema crea en vez de administrar. RF-23 pasó a guardar la foto del documento del receptor: una persona que no contrató nada, no es cliente y no consintió (RNF-09). Lo que lo contiene es operativo y frágil — retención con plazo, purgado que hay que correr, acceso restringido a administración — y depende de que el purgado efectivamente se ejecute: un endpoint que nadie dispara deja las imágenes para siempre y convierte la retención en una declaración de intenciones. La consulta legal de §11.2 pasa a bloquear este punto. Si el dictamen dice que la captura no corresponde, lo que se revierte es el requisito, no la implementación: las columnas quedan, se dejan de llenar y se purga lo capturado.

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
| **4.1** | **11/09/2026** | **E0 completa** (`Anexo_I_Alcance_V2.docx` §5: nomenclatura, hallazgos de auditoría, B9) — nota: esta fila queda después de la 4.0 en la tabla por orden de escritura, no cronológico; mismo caso ya señalado para la 3.11 más arriba. Cierra B9 ("Zona D `+40 km → Cotización`", Anexo I §4): antes, sin tarifa cargada, el sistema **rechazaba la cotización** en vez de derivarla a un flujo manual — ahora el pedido nace igual en Borrador, marcado `requiere_cotizacion`, y administración le fija un precio manual (§4.1, campos `precio_manual`) antes de que la ruta que lo lleva pueda cerrar su planificación. No es alta de alcance por catálogo (P6): sustituye solo el origen de un término ya existente en la fórmula del §6 de `construccion_v1.md`, no agrega ninguno. **Techo de tablas de `construccion_v1.md` §1 resuelto: sube a 17** (`facturas`, `factura_items`, `pagos` reservadas para E1 — entran por el ciclo semanal de §9.3, no se crean todavía). Unificación de nomenclatura Auto/camioneta: el sistema sigue guardando `camioneta` (cero migración de datos), la pantalla dice "Auto" — la infografía y el sistema nombraban lo mismo con dos palabras distintas. Hallazgos de auditoría corregidos: pisos numéricos en los formularios que aceptaban negativos sin aviso (kilómetros, montos de cierre de ruta, bultos, capacidad de paradas — antes entraban en silencio o rebotaban como error de Postgres sin traducir); coherencia de `zonas.km_desde/km_hasta` (changelog 3.2) — un rango que se solapa con otra zona activa se rechaza al guardar, un hueco entre zonas no bloquea pero se avisa en `/tarifas`. Detalle técnico en `construccion_v1.md` §1/§4.1/§6/§8.1, changelog 1.17. |
| **4.2** | **11/09/2026** | **E1 completa** (Anexo I §5, B1: cuenta corriente y facturación) — primera etapa contractual construida entera, no solo ajustes de cierre. Estaba bloqueada por seis definiciones del Anexo §10.2 (A residual, B, I, L, M, N); todas resueltas por decisión explícita de la Empresa, **excepto §10.2-L(2)**, que sigue **sin resolver** — requiere dictamen legal sobre qué pasa con mercadería de terceros ya retirada al momento del corte de servicio (R12 del Anexo). Mientras no haya dictamen, el sistema no retiene nada: el corte solo alcanza altas nuevas (§10.2-L1), así que la mercadería ya retirada se entrega igual porque su pedido ya está confirmado — el punto queda inerte, no resuelto. Decisiones incorporadas: dos ciclos de facturación por cliente, quincenal (vencimiento a 7 días) o mensual (a 10), en fechas fijas de calendario (D2); pago parcial admitido, imputación FIFO a la factura más antigua sin que el cliente elija, sobrante como saldo a favor (D12); corte de servicio automático por deuda vencida, sin días de gracia, se levanta solo al pagar el total (D11); reprogramaciones gratis suben de una a tres por pedido, la 4ta genera un pedido nuevo facturado aparte (D13); cancelar un pedido es gratis en Borrador y cuesta el 100% del precio ya congelado una vez confirmado (§10.2-I); ajuste de bultos al retiro físico con aprobación de un admin, tope de 3 por pedido sin cargo extra, el 4to exige un cargo de gestión fijado a mano (B16, sumado a esta etapa por decisión de la Empresa). Techo de tablas de `construccion_v1.md` §1, reservado en 17 desde changelog 4.1, alcanzado: `facturas`/`factura_items`/`pagos` (§4 de esta acta). Detalle técnico completo en `construccion_v1.md` §6.1, changelog 1.18. |
| **4.3** | **14/09/2026** | Panel de riesgo de cuenta corriente ("Cobranza", solo Administración) y aviso de vencimiento por correo/WhatsApp — implementa la función "vencimiento y aviso" que §9.3 ya declaraba básica del ciclo semanal desde el changelog 4.0, apoyada en B1 (cuenta corriente), cerrado en E1. **No es el tablero de indicadores** (B2/E3, Anexo I §5, actualizado en la misma fecha): no calcula ninguna de las 10 métricas operativas que ese ítem prevé (entregas/día, km, tiempo, margen, NPS, ocupación de flota, etc.) — es una vista de riesgo derivada de datos que ya existían (`v_facturas_saldo`, el corte de servicio de changelog 4.2), con una acción de aviso, nada más. **"Cliente crítico" se define con datos duros, no con los tres indicadores de RF-32/33**: deuda vencida mayor a cero, o una factura que vence dentro de los 15 días siguientes — ambos ya calculados por el sistema desde E1. Los tres indicadores manuales (pago/trato/operación) no cambian: siguen sin regla de cálculo automático (motor de rango, B3, sigue fuera de alcance) y en esta vista son solo una columna informativa, sin efecto sobre qué cliente aparece como crítico. Canal de aviso: correo (proveedor Resend) y un link de WhatsApp con el mensaje precargado para envío manual desde el teléfono de quien administra — **sin integración de API de WhatsApp** (Anexo I, D9/R7, actualizados en la misma fecha: evita el costo recurrente por conversación, no presupuestado, en vez de solo diferirlo). El envío siempre lo dispara una persona desde el panel, nunca un proceso automático — el sistema sigue sin scheduler, mismo criterio que el cierre de ciclo de facturación (changelog 4.2). Cada aviso enviado queda registrado en `eventos_cliente` bajo un tipo nuevo, `aviso_cobranza` (dimensión "pago") — no reusa `impago` porque es una acción del sistema, no una conducta del cliente, y debe quedar afuera del futuro motor de rango (B3) cuando se construya. Cero tablas nuevas: una fila de catálogo (`tipos_evento_cliente`) y reutilización de `eventos_cliente`/`facturas`/`pagos` ya existentes. Sigue en 17. |
| **4.4** | **15/09/2026** | **Sin cambio de código — entrada puramente documental.** Cierra la brecha entre la auditoría del 03/09 y este changelog: los cuatro hallazgos de pisos numéricos y validación cruzada que esa auditoría señalaba ya estaban corregidos desde changelog 4.1 / `construccion_v1.md` 1.17 (11/09), pero nunca quedaron marcados acá con trazabilidad explícita hallazgo-por-hallazgo. Verificado contra el código actual, no solo contra el texto de 1.17: `RutasController.Cerrar` rechaza `KmFinal < KmInicial` (`RutasController.cs:416` — chequeo cruzado manual, no expresable como `[Range]` por involucrar dos campos); `PedidosController.CrearPedidoRequest` tiene `[Range(0, double.MaxValue)]` en `Peajes` y en `ValorDeclarado`; `Trim()` ya aplicado en los campos de texto libre de negocio que la auditoría señalaba (`DestinatarioNombre`, `DestinatarioTelefono`, `Patente`). El piso de `PesoKg` (parte del mismo hallazgo original de la auditoría) también existe en código (`[Range(0, double.MaxValue)]`) pero queda fuera de este seguimiento por decisión del usuario. |
| **4.5** | **16/09/2026** | **Módulo de deliverys/urgencias punto a punto (D14) y recargo por kilómetro (Anexo I §10.2-N).** Resuelve §10.2-N por la lectura (ii) que el propio Anexo describía: "precio proporcional al kilometraje recorrido", **declarada ahí mismo como cambio de alcance según Anexo I §6** ("agregar un término a la fórmula de precio") — a diferencia del resto de este changelog, esto no es config, es la fórmula misma de `construccion_v1.md` §6 con un término nuevo: `recargo_km = precio_base × factor_km(km)`, entre `precio_base` y `recargo_urgencia`. La escala de tramos de km (`Precio:TramosKm`, % por tramo) es parámetro de configuración, vacía por defecto — sin valores de fábrica, mismo criterio que el resto de los parámetros comerciales (§13): con la escala vacía, `recargo_km = 0` siempre y la fórmula se comporta exactamente igual que antes de esta versión para todo pedido programado. **La decisión comercial de qué escala cargar sigue sin tomarse** — el Anexo I abrió la pregunta, este changelog cierra cuál de las dos lecturas implementa el sistema si la Empresa la activa, no el número. De dónde sale el kilómetro es también configuración (`Distancia:Fuente`): distancia real por calles (OSRM, con fallback automático a línea recta si el proveedor no responde) o un valor cargado a mano por pedido — nunca bloquea una cotización. P3 (§3) gana la excepción puntual correspondiente. **Delivery (D14, "solo entrega, sin retiro programado"):** no es una tabla nueva — sigue en 17 (techo de `construccion_v1.md` §1) — es un `pedido` con `tipo='delivery'` nuevo en el discriminador existente, con origen arbitrario (no el depósito) y el tipo de vehículo elegido por el operador en el alta misma, así que el precio se cotiza y congela ahí, sin esperar a que una ruta cierre su planificación (a diferencia de un pedido programado desde changelog 3.11). Zona sin tarifa cargada (B9) exige un precio manual en la misma alta — no hay una segunda pasada futura como con un pedido en Borrador. Pantalla `/deliverys` (alta y listado), reusa el detalle de pedido existente. **Pendiente, no tocado en este changelog:** `Precio:FactorUrgencia` sigue en el valor provisional 0,20 — el Anexo I (gap B14, §10.2-H) preveía 0,40–0,60 para el recargo de urgencia, decisión comercial que sigue sin cerrarse. Detalle técnico en `construccion_v1.md` §6/§12, changelog 1.19; DDL en `schema_v3.sql`. |
| **4.6** | **17/09/2026** | **Panel de disponibilidad y carga de repartidores (RF-34), pantalla `/repartidores`.** Cierra un hueco operativo, no comercial: la asignación de repartidor existe desde RF-15 y la reasignación sobre ruta en curso desde changelog 4.4, pero **quién está libre no aparecía en ninguna pantalla** — `/jornada` parte de las rutas del día y filtra las que tienen repartidor, así que el repartidor sin ruta es exactamente el que el monitor no muestra, que es el que se necesita ver para asignar. Este panel invierte el punto de partida: arranca de `usuarios` con rol repartidor y le cuelga la ruta de hoy si la tiene. **Cero tablas nuevas: sigue en 17.** La disponibilidad no se declara, se deriva en lectura, con cuatro estados y una precedencia fija: `inactivo` (`usuarios.activo=false`, gana sobre cualquier ruta asignada), `en_ruta` (ruta de hoy `en_curso`), `asignado` (ruta de hoy `planificada`), `libre` (activo, sin ruta hoy). **No hay ausencias declaradas** — franco, vacaciones y licencia no se registran en ningún lado y siguen sin registrarse: guardarlas es lo único que justificaría la tabla `repartidores`, que §4 mantiene fuera del modelo inicial (nota agregada ahí en esta misma versión). El costo de esa decisión es explícito: un repartidor de vacaciones figura "libre" y quien planifica tiene que saberlo por fuera del sistema, igual que hoy. **No es el tablero de indicadores** (B2/E3, Anexo I §5, sigue fuera de alcance): el acumulado por rango de fechas es una sumatoria de paradas de `ruta_paradas` recalculada en cada request, sin persistir, sin comparar períodos entre sí, sin serie temporal y sin ninguna de las 10 métricas que ese ítem prevé (entregas/día, km, tiempo, margen, NPS, ocupación de flota). Existe para responder "¿a quién le cargo la ruta de mañana?", no para medir rendimiento. Mismo encuadre declarado que `/jornada` (changelog 4.4) y `/cobranza` (changelog 4.3). **Tampoco es liquidación al repartidor** (B4/E2, Anexo I; disparador "segundo repartidor" según §9.2, y bloqueada por la definición abierta §10.2-C): no toca `rutas.pago_repartidor`, no calcula ni muestra un solo importe. Detalle técnico en `construccion_v1.md` §4.5/§12, changelog 1.20. |
| **4.7** | **17/09/2026** | **Jornada del repartidor: retiro firmado (RF-35), verificación de identidad con imagen (RF-23 reescrito) y cierre de ruta en dos actores (RF-26).** Tres cosas que el acta exigía desde la v3.0 y que el sistema no tenía dónde registrar, más una que **invierte** una decisión anterior. **RF-35** codifica la regla de §7 (*"ninguna ruta sale sin conteo firmado"*): el retiro de las 07:30 con conteo de bultos contra la lista congelada por el sistema y firma del repartidor, sin el cual no se puede registrar llegada ni cerrar parada. Un conteo distinto al esperado no bloquea la salida pero exige observación escrita — lo que la regla protege es que la discrepancia quede firmada antes de salir, no que los números coincidan. **RF-23 se invierte**: hasta esta versión decía "sin almacenar imagen del documento" y el sistema lo cumplía con un booleano; ahora guarda número de documento e imagen, con plazo de retención, purgado que deja constancia de la fecha de borrado, y acceso a la imagen restringido a administración (más estricto que la foto de la entrega, que es back-office). Decisión explícita del usuario tomada después de que se le señalara la contradicción con este RF y con RNF-09; anotada como inversión en §5.3, con su riesgo en §12 y la consulta legal de §11.2 pasando de agendada a **bloqueante** — el plazo de 30 días con el que arranca es provisional, puesto para poder construir el purgado, no una decisión tomada. **RF-26 pasa a dos actores y dos actos:** el repartidor declara km, combustible y peajes desde la calle sobre un juego de columnas propio, esa declaración queda inmutable, y administración la compara y la **aprueba o la corrige con motivo escrito** al cerrar la economía. Decisión explícita del usuario, que revierte el diseño original de un solo juego de columnas: el costo son seis columnas más, lo que compra es que el número de la calle y el del cierre convivan en la fila y que nadie pueda tapar el primero. Regla nueva en §7 (*"el número de la calle no se reescribe"*), impuesta por trigger y no por la aplicación, mismo mecanismo que el congelamiento del precio. **Cero tablas nuevas: sigue en 17**, con columnas en `rutas` y `pruebas_entrega` (§4.1) y un trigger nuevo. **Lo que este changelog NO cierra: H2/E5.** La PWA del repartidor sigue sin cola offline — sin Dexie, sin service worker, sin manifest — así que la prueba de modo avión (criterio de aceptación 3, `construccion_v1.md` §7) no se puede correr y el hito sigue abierto. Las escrituras que esta versión agrega nacen **fuera** de la cola, contra la regla 3.5 de `construccion_v1.md`: son tres más para migrar cuando la cola se construya, decisión tomada a sabiendas y no un olvido. **Esta fila se escribió en la fase de gobernanza, antes del código**, mismo procedimiento que 4.6: el alcance se acuerda acá primero y el detalle de implementación de `construccion_v1.md` 1.21 se relee y corrige contra el código al cerrar la tanda. Detalle técnico en `construccion_v1.md` §4.2/§7/§12, changelog 1.21. |
| **4.8** | **18/09/2026** | **Novedades de la calle (RF-36, RF-37) y ciclo diario completo del repartidor.** Cierra el ciclo de la jornada: retiro firmado (RF-35), paradas con siguiente parada automática, cierre de jornada en dos actores (RF-26) —todo del changelog 4.7, ahora construido y verificado contra la API— más lo que 4.7 no tenía: qué hace el repartidor cuando algo sale distinto de lo planeado. **Sube a 18 tablas** (cruza el techo de 17 con `novedades`, §4.1, decisión explícita del usuario): incidencia de ruta o vehículo, problema con la carga y corrección propuesta de un dato de contacto (origen repartidor), más aviso de cambio y de cancelación (origen operación), en una sola tabla con historial y respuesta. **Límites que son parte de la decisión:** el repartidor propone y operación aplica (P1 no se toca: destino y precio siguen congelados desde Borrador); una dirección incorrecta sigue siendo entrega fallida, no edición; un problema de bultos no modifica `pedidos.bultos` sino que deja evidencia para el ajuste B16. Operación cancelar un pedido de una ruta en curso ya no deja al repartidor con una parada muerta: le llega el aviso y, si la parada se queda sin pedidos activos, pasa a `cancelada`. Nueva herramienta de back-office, `POST /api/rutas/{id}/interrumpir` (una ruta que no sigue y no se reasigna: lo pendiente se declara fallido con motivo y entra al circuito de reprogramación D13). **No cierra H2/E5:** la cola offline sigue siendo el plan siguiente y ahora envuelve siete caminos de escritura, no cinco. **Diferido, no olvidado:** la imagen del documento con retención (RF-23 reescrito en 4.7) sigue sin construirse, bloqueada por la consulta legal de §11.2. Categorías de novedad (`PruebaEntrega:CategoriasIncidencia/CategoriasCarga`) provisionales, mismo estatus que `MotivosFallo`. Detalle técnico en `construccion_v1.md` 1.22. |
| **4.9** | **22/09/2026** | **Portal de carga del cliente (B5, RF-38) y recepción/conciliación (B13, RF-39) — Anexo I §5, E4 adelantada a pedido del cliente respecto del orden original (E2/E3 quedan para después).** El cliente carga su propio pedido desde `/mis-envios/nuevo`, autenticado con el mismo login de `clientes_usuarios` (changelog 3.3) que ya usaba el portal de solo lectura — el `ClienteId` se resuelve siempre del claim de sesión, nunca del body ni de la URL, mismo criterio que el resto de `MiCuentaController`. **Precio vinculante (Anexo I, definición D, resuelta 22/09/2026):** el precio que el portal cotiza en el momento de la carga, con el tipo de vehículo que el cliente elige, es el precio que se cobra — no un estimado. Decisión explícita del cliente: con facturación por ciclo (cuenta corriente, no contra entrega), un precio estimado que cambia después lo descubre semanas más tarde, en la factura, sin nadie en el medio para absorber esa fricción a diferencia de la carga interna. Mecanismo: reusa `precio_manual`/`precio_manual_por`/`precio_manual_en` (B9, changelog 4.1) — cero columnas nuevas para esto. **Corrección de un supuesto de esquema, no de la decisión:** `precio_manual_por` tenía FK a `usuarios` (personal interno); el cliente lo fija con su propio id de `clientes_usuarios`, tabla separada a propósito desde 3.3 y ausente de `usuarios`, así que la FK se relaja (la columna sigue siendo un `uuid`, ahora sin FK, resuelta a mano contra las dos tablas al mostrar en pantalla quién fijó el precio). Por el mismo motivo, el alta desde el portal no puede quedar registrada con el id del cliente como actor en `pedido_eventos` (misma FK, ahora sobre el log automático de `fn_log_estado_pedido`) — queda como actor `sistema`; el "quién" real de la carga sigue completo en `precio_manual_por`, que es el campo pensado para eso. Riesgo asumido, explícito, no del que construyó: si el planificador termina asignando un vehículo más caro que el elegido en el portal, la diferencia la absorbe la Empresa. **Corte horario (B13 §1):** 16:00, dos horas antes del corte general de las 18:00 (§7), configurable (`Portal:HoraCorte`, valor provisional, mismo estatus que `Precio:FactorUrgencia`) — se valida la hora del servidor al confirmar, no solo al mostrar el formulario, y gatilla solo sobre un pedido cuya fecha de entrega es hoy o antes: cargar para un día futuro no espera al corte. **Recepción (B13) no agrega un estado nuevo al pedido**, a diferencia de lo que preveía el Anexo I: con el corte del portal a las 16:00, la recepción siempre cae dentro de la ventana en que el pedido sigue en Borrador, así que es una edición dentro de Borrador, no una transición de estado — no hace falta tocar la máquina de estados. `bultos_declarados_cliente` (§4.1) es el snapshot contra el que se concilia; B16 (ajuste de bultos) no se toca, sigue exactamente para discrepancias descubiertas después de que el pedido se congela. Pantalla `/recepcion`: cola de trabajo con confirmación en lote para lo que coincide y corrección con nota obligatoria fila por fila para lo que no. **Cero tablas nuevas: sigue en 18.** Detalle técnico en `construccion_v1.md`, changelog 1.24. |
| **4.10** | **22/09/2026** | **Portal del cliente: detalle de envío, envío en curso y "mis clientes" (RF-40).** Tres pedidos del cliente sobre `/mis-envios`. **Detalle y estado en curso no cambian ninguna decisión** — el backend (`GET /api/pedidos/{id}`, filtro `?estado=EnRuta`) ya estaba alcanzable por el rol cliente sobre sus propios pedidos desde que el portal se resolvió por claim de sesión (E1); solo faltaba la pantalla. **Brecha real encontrada al construir la pantalla de detalle, no introducida por ella:** ese mismo endpoint devolvía el nombre real de un usuario interno que tocó el pedido (historial) o fijó un precio manual — nadie lo notaba porque ninguna pantalla de cliente lo llamaba todavía. Cerrado ahí mismo con el mismo criterio de RF-33 ("nunca visible para el cliente"): un caller cliente ve "Empresa" en vez del nombre real de un actor interno; ve su propio nombre sin problema si el actor fue su propia cuenta de portal. **"Mis clientes" (RF-40) es la pieza que sí revierte una decisión — 3.5 (§11.1)** — a pedido explícito del cliente, con el riesgo del dictamen legal pendiente de §11.2 asumido a sabiendas y anotado ahí y en RF-40, no en silencio. Tabla nueva `clientes_destinatarios` (**sube a 19**): el cliente registra a mano nombre/teléfono/dirección de sus destinatarios habituales desde `/mis-envios/contactos`, reutilizable al cargar un envío nuevo sin volver a tipear ni geocodificar. Sin trigger de inmutabilidad (ABM libre del propio cliente sobre su propia libreta) y sin FK desde `pedidos` (el alta copia los valores del contacto elegido, no guarda su id). Detalle técnico en `construccion_v1.md`, changelog 1.25. |
| **4.11** | **23/09/2026** | **Zona automática de las localidades por distancia al depósito, y precio sugerido al elegir la localidad — revierte a propósito el criterio "la zona nunca se infiere sola" (changelog 3.9/3.10), a pedido del cliente.** Cada localidad guarda su centro (geocodificado en el servidor, nunca aportado por el cliente del portal) y la distancia al depósito principal (0 km = el propio depósito, medida con ruta y, si no responde, en línea recta). Al darse de alta —desde el alta interna o desde el portal— recibe sola la zona activa cuyo rango de km `[km_desde, km_hasta)` la cubre; los rangos siguen siendo datos comerciales que administración carga en `/tarifas` (sin valores de fábrica). Sin depósito, sin coordenadas o fuera de todo rango la localidad queda sin zona y aparece en pendientes, sin bloquear el alta. **Una zona fijada a mano (`zona_manual`) no se pisa nunca**: las localidades que ya tenían zona quedaron como manuales en la migración; "Volver a automática" las devuelve al cálculo. Cambiar un rango reasigna las automáticas con el km ya guardado; cambiar de depósito requiere "Recalcular distancias y zonas" en `/tarifas` (manual, Nominatim admite ~1 req/s). **Precio sugerido:** al elegir la localidad, el portal muestra la tarifa de la zona (con la lista propia del cliente si la tiene) para camioneta y moto, sin recargos; el precio vinculante sigue fijándose al crear el envío (si se configuran tramos de km en `OpcionesPrecio.TramosKm`, el total puede superar al sugerido). Tabla `localidades` suma `lat`, `lng`, `distancia_km_deposito`, `distancia_fuente`, `zona_manual` (no cambia el conteo de tablas). |
| **4.12** | **23/09/2026** | **Portal del cliente: "Mi plan", detalle en popup, línea de tiempo de estados y ubicación con mapa — sin cambios de decisión de negocio.** `/mis-envios` pasa a llamarse "Mi plan": arriba los envíos de hoy con su línea de tiempo (Cargado → Confirmado → En camino → Entregado, con la hora de cada paso; se actualiza sola cada 30 s), luego la cuenta compacta y el listado completo. Tocar un envío abre el detalle en un popup (hoja inferior en el teléfono, modal en escritorio) en vez de navegar; `/mis-envios/[id]` sigue existiendo para links directos. El detalle muestra la línea de tiempo vertical (estados fuera del camino —fallido, reprogramado, devuelto, cancelado— como último paso con su motivo) y la ubicación: mapa con el punto de entrega y botón "Abrir en Google Maps" (por coordenadas si son confiables; si no, por dirección). El rol cliente suma barra de navegación inferior (Mi plan, Cargar envío, Mis clientes) y sidebar desde `md`. Backend: `GET /api/mi-cuenta/plan-del-dia` (cliente por claim, sin nombres de actor: RF-33) y `PedidoDetalle` suma `destinoLat`/`destinoLng` (la dirección de entrega del propio cliente). |
| **4.13** | **23/09/2026** | **Ubicación exacta del envío con link de Google Maps — no cambia ninguna decisión de negocio.** Al cargar la dirección de entrega (portal del cliente, libreta "Mis clientes" y alta interna de pedidos) se puede pegar, de forma opcional, el link de «Compartir» de Google Maps (o un "lat, lng"). El servidor toma el punto exacto y lo guarda en la ubicación con `geo_proveedor = google_maps` y `geo_confianza = alta`, sin pasar por el geocoder; la calle y número siguen siendo obligatorios (es lo que ve el repartidor). Formatos: link largo (`!3d/!4d` del pin, `@`, `q`, `ll`, `query`, `destination`) y corto (`maps.app.goo.gl`, que se sigue por redirect solo hacia hosts de Google, con tope de saltos y timeout, para evitar SSRF). Cualquier URL que no sea https de Google se rechaza. **Nunca bloquea el alta:** si el link no sirve, queda fuera de la Argentina o a más de 50 km del centro de la localidad, se ubica por la dirección como siempre y la respuesta trae el motivo (`urlMapaError`). Una ubicación `Verificada` por administración no se mueve; una no verificada sí adopta el punto del link (corrige direcciones dudosas). Sin cambios de esquema. |
| **4.14** | **23/09/2026** | **El link de Google Maps completa la dirección y la localidad, y el cliente las confirma o corrige — completa el changelog 4.13.** Al pegar el link en el portal (nuevo envío y "Mis clientes") el servidor lee el punto, lo convierte a calle, número y localidad (geocodificación inversa de Nominatim) y busca esa localidad en el catálogo; si no está, la da de alta con zona automática. La Ciudad de Buenos Aires, que OSM llama "Buenos Aires", se resuelve a "CABA" para no duplicarla. **Nada se guarda ni se resuelve hasta que la persona confirma:** los campos se completan, aparece un aviso "Verificá la dirección de tu link" con la calle y la localidad y el botón "Confirmar dirección" (deshabilitado sin calle o localidad); editar la calle o cambiar la localidad cuenta como verificar. Si no se pudo leer la calle, se pide escribirla; si falta la altura, se avisa. Un link ilegible no bloquea: se sigue con la dirección tipeada. Endpoints `POST /api/ubicaciones/desde-mapa` y su espejo `POST /api/mi-cuenta/ubicaciones/desde-mapa` (solo leen). La alta interna de `/pedidos/nuevo` sigue tomando solo el punto del link, sin autocompletar. |
| **4.15** | **23/09/2026** | **Bultos visibles y sumados, armado rápido de ruta y preparación del día siguiente — sin cambios de decisión de negocio.** El administrador ahora ve los bultos que declara el cliente: columna en `/pedidos` y en `/rutas` (total de la ruta), y en el armado de ruta totales por zona, por parada y de lo seleccionado. Armado rápido: "Seleccionar toda la zona" y "Seleccionar todos" (solo direcciones aptas; las dudosas siguen sin poder rutearse). `/rutas` suma la tarjeta "Preparar la ruta del día siguiente": para una fecha (mañana por defecto, editable) muestra cuántos pedidos y bultos siguen sin ruta —se actualiza sola cada 30 s— y "Preparar ruta" abre la ruta planificada de esa fecha o la crea, y entra al armado con todo lo apto preseleccionado (`?todos=1`, solo si la ruta todavía no tiene paradas). Backend: `PedidoResumen.Bultos` y `RutaResumen.CantidadBultos`. **Seed de desarrollo:** la ruta de demostración ya no se recrea en una base que ya tuvo usuarios (antes, borrar las rutas hacía que el arranque intentara sembrarla de nuevo y fallaba con más de un repartidor). Sin cambios de esquema. |
| **4.16** | **23/09/2026** | **Detalle de ruta adaptado al teléfono y ruta en Google Maps — sin cambios de decisión de negocio.** `/rutas/[id]` se reordena para mobile: botón principal de Google Maps, acciones en grilla de botones de 44 px (ya no se salen de la pantalla), resumen con paradas / pedidos / **bultos**, la lista de paradas antes que el mapa (lado a lado desde `md`, con el mapa fijo; plegable con listas largas), filas de parada de tres niveles con horas HH:mm, "Cómo llegar" y "Llamar" por parada, resultado económico en una columna y diálogos con pie apilable y flechas táctiles. **Ruta en Google Maps:** el botón abre Maps con el punto de salida y las paradas en el orden de la ruta (`origin` + `waypoints` + `destination`); usa coordenadas y, si una parada no las tiene, su dirección como texto, así que ninguna se pierde. Maps limita los puntos intermedios, por eso una ruta de más de 10 paradas se parte en tramos encadenados (el siguiente arranca donde terminó el anterior). Con la ruta en curso solo incluye las paradas pendientes y parte de la ubicación actual; cerrada, no aparece. "Copiar link" permite mandárselo al repartidor. Sin cambios de backend ni de esquema. |
| **4.17** | **23/09/2026** | **Flujo del repartidor: empezar ruta, toda la ruta en Google Maps, terminar la ruta y DNI en cada entrega.** "Empezar ruta" es el retiro firmado de siempre (RF-35: contar bultos, km inicial y firma), ahora presentado como el arranque de la ruta, con la hora de salida visible una vez hecho; sin él el servidor sigue rechazando llegada y cierre. `/hoy` suma el botón de Google Maps con el punto de salida y todas las paradas (mismo generador que el back-office: tramos de hasta 10 paradas, con la ruta en marcha solo las pendientes y desde la ubicación actual), visible también antes de empezar. Un bloque "Terminé la ruta" indica cuántas paradas faltan y se habilita al resolver la última (lleva al cierre de jornada existente); al cerrar cada parada, la pantalla siguiente confirma "Parada N cerrada · quedan M". **DNI (RF-23, versión reducida):** cada entrega exige el número de DNI del receptor (6 a 9 dígitos, se aceptan puntos) **o** el motivo escrito de por qué no lo dio, además de la foto del pedido y el nombre; `identidad_verificada` deja de ser un tilde del cliente y se deriva en el servidor (`true` si hay DNI válido). Columnas nuevas `pruebas_entrega.documento_numero` y `sin_documento_motivo`. **Sin imagen del documento**: la foto del DNI de un tercero sigue pendiente del dictamen legal y de la retención con purga que el acta exige para ella. El DNI lo ve solo administración (`GET /api/pedidos/{id}/prueba-entrega`, que ahora lo incluye, y una tarjeta "Prueba de entrega" en el detalle del pedido); operación y cliente reciben 403. |
| **4.18** | **23/09/2026** | **Auditoría de validación, seguridad y carga — endurecimiento y mejoras de velocidad, sin cambios de decisión de negocio.** Se probó con 100.000 pedidos, 195.000 eventos, 40.000 paradas y 60.000 ítems de factura en una base aparte, y con 100 usuarios simultáneos. **Seguridad:** JWT manipulado (alg none, firma alterada, rol editado), CORS, inyección SQL en filtros, aislamiento entre clientes (sin fuga de pedidos, contactos ni cuenta; un `clienteId` inyectado se ignora) y permisos por rol sobre los 125 endpoints (sin sesión todo da 401) pasaron. Se corrigió: faltaban cabeceras de seguridad (`nosniff`, `X-Frame-Options`, `Referrer-Policy`, `no-store` en `/api`, HSTS en producción; lo mismo en el frontend, sin CSP estricta) y se oculta la cabecera `Server`. **Validación:** la carga de pedidos aceptaba bultos de 2.147.483.647, nombre vacío, textos de 100.000 caracteres y fecha de entrega en el año 9999; ahora hay límites (bultos 1–999, peso, valor, nombre y teléfono obligatorios), ventana de fechas (portal: hoy a 90 días; interno: 30 días atrás a 365 adelante) y un filtro global que rechaza cualquier campo de texto desmedido; tope global de 5 MB por body. **Límite de tasa** (30 de ráfaga, 10 cada 10 s, por usuario) en los endpoints que consultan servicios externos o dan de alta localidades. **Rendimiento:** la paginación no tenía techo (`tamanioPagina=1000000` devolvía 27 MB en 1,2 s y con varios en paralelo bajaba a 3 respuestas/s): tope de 100 por página y 500 sin paginar; la búsqueda de pedidos por nombre pasó de ~200 ms (29 req/s con 100 usuarios, p95 4,4 s) a ~20 ms con un índice de trigramas (migración `IndiceBusquedaPedidos`, requiere la extensión `pg_trgm`); los exportes CSV se acotan a 366 días y el de resultados deja de usar una lista IN de miles de ids; compresión Brotli/Gzip de respuestas; calentamiento en segundo plano al arrancar. Swagger (`/swagger`) devolvía 500 por nombres de DTO repetidos y ya funciona. |
| **4.19** | **24/09/2026** | **Preparación del despliegue (backend en Render, frontend en Vercel) — sin cambios de decisión de negocio, sin cambios de alcance.** El código queda consolidado en la rama `main` (merge de `demo-d` del 23/09/2026) y se prepara para publicarse: imagen Docker del backend y reenvío de `/api/*` desde el dominio del frontend al backend, para que la sesión funcione con un solo dominio visible para el usuario. Tres consecuencias que sí tocan reglas de este documento y quedan anotadas antes de operar con clientes reales: **(1) RNF-10 y la trazabilidad de la entrega (RF-19–RF-24, RF-35):** las fotos de entrega y las firmas del retiro se guardan hoy en el disco del servidor; en el hosting elegido ese disco se borra en cada actualización, así que sin un almacenamiento persistente se perdería la evidencia de entrega — condición previa a la primera ruta real, no una mejora. **(2) Protección del inicio de sesión:** el límite de intentos contra la fuerza bruta (auditoría del 31/08) deja de distinguir usuarios detrás del reenvío y, tal como está, permite bloquear el acceso de todos; se corrige antes de abrir el sistema. **(3) Datos de producción:** la base de producción arranca vacía — sin los usuarios de prueba de desarrollo, que solo se siembran en desarrollo — y con una contraseña propia; como solo administración da de alta usuarios (RNF-08), el primer administrador se crea por un procedimiento de arranque único, fuera de la aplicación. Detalle técnico en `construccion_v1.md` §1/§9/§10, changelog 1.33, y en `auditoria_seguridad.md` (revisión del 24/09/2026, hallazgos 14 a 17). |
| **4.20** | **24/09/2026** | **Infraestructura de producción — sin cambios de decisión de negocio.** Resuelve dos de las tres condiciones que dejó la 4.19. **(1) Evidencia de entrega:** las fotos de entrega, las firmas del retiro y las fotos de novedades pasan a guardarse, en producción, en un servicio de almacenamiento externo (Cloudinary), como archivos privados que solo el sistema puede leer; quién puede verlos sigue decidido por el sistema, igual que antes (la foto de entrega, back-office; RNF-09). **Nota para la consulta legal de §11.2:** esas fotos contienen datos personales del destinatario y el proveedor los procesa fuera del país; no bloquea, pero entra en la misma consulta. **(2) Protección del inicio de sesión:** el límite de intentos vuelve a contar por usuario real detrás del reenvío, y no se puede esquivar falsificando la dirección de origen. Además, el recorrido por calles en producción pasa a un proveedor que admite uso comercial (OpenRouteService, plan gratuito con tope diario); si no responde, el mapa y el cálculo de distancia caen a línea recta, como hasta ahora. Queda pendiente de la 4.19 la creación del primer administrador en producción. Detalle técnico en `construccion_v1.md`, changelog 1.34. |
| **4.21** | **24/09/2026** | **E2 — fase de gobernanza: se cierran las definiciones C, F y J del Anexo I §10.2 y se acuerda el alcance de B4, B3 y B7. Esta fila se escribe antes del código**, mismo procedimiento que 4.6 y 4.7. Entra por los ciclos de §9.3 (semanal: pago al repartidor; mensual: costos fijos, rentabilidad, liquidación por período; trimestral: recálculo de rango), no por el disparador de §9.2. **Decisiones de la Empresa:** **(C)** el mínimo de entregas exitosas para el bono y la lista de motivos de fallo imputables al repartidor son configurables desde el sistema — el porcentaje arranca vacío, no con un número inventado (§13); **(J)** el rango da un **descuento porcentual** sobre la tarifa de la zona, un término nuevo en la fórmula de precio — la tabla de tarifas no se multiplica por rango; **(F)** el buen trato se mide por **ajuste manual de administración de un rango como máximo, con justificación escrita y vencimiento** — un criterio subjetivo que mueve el precio deja rastro; **(D4)** el recálculo es **solo trimestral**, y se **rechaza la contrapropuesta de §10.3** (bajada inmediata por mora): la Empresa acepta a sabiendas que un cliente pueda acumular deuda un trimestre conservando su descuento, mientras el corte por deuda de D11 sigue actuando igual; **(B7)** la estructura económica objetivo (60-65% / 10-15% / 20-30%) se define después: los tramos son configurables y el sistema no trae ninguno. Requisitos nuevos **RF-41** (liquidación al repartidor), **RF-42** (rango de cliente) y **RF-43** (costos fijos y rentabilidad). **Techo de tablas: de 19 a 25** cuando se construya — `parametros_liquidacion` y `liquidaciones` (la liquidación necesita un comprobante inmutable por período; hasta ahora el pago era un número suelto en cada ruta), `rangos` (umbrales y efectos que la Empresa configura, Anexo §6) y `cliente_rangos` (historial: sin él no hay forma de explicarle a un cliente por qué cambió su precio), `costos_fijos` y `objetivos_rentabilidad` (no existían en ningún lado). **Puntos que quedaron con un valor por defecto, confirmados tal cual por la Empresa el mismo 24/09/2026:** el pago es por parada, no por pedido; el descuento por rango aplica solo sobre la tarifa general, no sobre la lista propia de un cliente; el límite de crédito por rango solo avisa, no bloquea (el bloqueo sigue siendo D11); el cliente ve su rango y su descuento; y **la "prioridad de asignación" que el Anexo le asigna al rango choca con §7** ("al buen cliente se lo premia con acceso, nunca con prioridad… nunca reordenando la ruta a su favor") — se limita al orden en que aparecen los pedidos pendientes al armar (quién entra primero si no hay lugar para todos), sin tocar nunca el orden de las paradas, que sigue siendo geográfico. §7 no cambia: premiar con acceso a la ruta no es reordenarla. Diseño completo en `diseño_e2_rangos_liquidacion.md`. |
| **4.22** | **24/09/2026** | **Corrección: el precio vinculante del portal (changelog 4.9) no se respetaba en todos los casos. Sin decisión nueva: se cumple la que ya estaba tomada.** El portal le muestra al cliente un precio que es el que se cobra. Pero al armar la ruta, el sistema volvía a calcularlo tomando ese precio final como punto de partida y le sumaba otra vez los recargos. Un envío urgente cotizado en $1.200 terminaba facturándose en $1.440. Se detectó al construir el descuento por rango (RF-42), que habría tenido el mismo problema. Ahora el precio del portal no se recalcula al armar la ruta. No se encontraron envíos afectados en la base de desarrollo, porque no había envíos urgentes del portal ni recargo por km configurado. **Antes de operar con clientes reales conviene revisar si algún pedido del portal ya facturado sufrió el recargo duplicado.** Detalle técnico en `construccion_v1.md` §6 y changelog 1.36, junto con la construcción de B3 (rangos). |
| **4.23** | **24/09/2026** | **E2 completa (Anexo I §5: "Rangos y liquidación", B3, B4 y B7) — sin decisiones nuevas: se construyó lo acordado en la 4.21.** Liquidación al repartidor (RF-41), rangos de cliente (RF-42) y costos fijos con rentabilidad mensual contra la estructura objetivo (RF-43). Techo de tablas alcanzado: 25. **Lo que la Empresa tiene que cargar para que E2 produzca números**, todo desde el sistema (Anexo I §6, configuración): los valores de pago por entrega, bono y mínimo de éxito por tipo de vehículo; los umbrales, descuentos, límites y prioridad de cada rango; los costos fijos de cada mes; y la estructura objetivo, cuando se defina qué es cada tramo del 60-65% / 10-15% / 20-30%. Hasta entonces, cada pieza se comporta como antes: el pago se tipea a mano, nadie sube de rango y el mes se muestra sin comparar contra un objetivo. Detalle técnico en `construccion_v1.md`, changelogs 1.35 a 1.37. |
| **4.24** | **24/09/2026** | **E3 completa: tablero de indicadores (Anexo I §5, B2; RF-44).** Sin decisiones de negocio nuevas y sin tablas nuevas: es una lectura sobre datos que ya existen, calculada al consultar. Las definiciones de cada indicador quedan escritas en `diseño_b2_tablero.md`. **Dos criterios explícitos:** (1) el costo de una ruta **no se reparte** entre sus clientes ni entre sus zonas; con un filtro de cliente o de zona, los indicadores de ruta (km, tiempo, costo, margen, ocupación) no se muestran, en vez de inventar un reparto; (2) la facturación por rango usa el rango **de hoy** de cada cliente, porque no se guarda el rango que tenía en cada fecha, y la pantalla lo aclara. El NPS figura sin datos: el Anexo I §7 prevé el indicador pero no la encuesta. Queda aclarado que el tablero **no es criterio de aceptación** (§8): los ocho criterios siguen siendo los mismos. Detalle técnico en `construccion_v1.md`, changelog 1.38. |
| **4.25** | **24/09/2026** | **Reglas de seguridad que ven los usuarios — sin cambios de alcance.** Las contraseñas nuevas tienen que tener al menos 10 caracteres, con letras y números, sin contener el email y sin ser una contraseña común. Las que ya existen siguen valiendo hasta que se cambien. El email, el CUIT y el teléfono de un cliente se validan al cargarlos o editarlos, y el CUIT tiene que ser uno real (se controla el dígito verificador). El envío masivo de avisos de cobranza admite tres envíos seguidos y después uno cada diez minutos. Detalle en `auditoria_seguridad.md` (hallazgos 3, 4, 6 y 9) y `construccion_v1.md`, changelog 1.40. |
