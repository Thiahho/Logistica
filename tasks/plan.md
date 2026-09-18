# Módulo Jornada del repartidor — retiro, entrega con documento, siguiente parada, cierre

**17/09/2026 · rama `demo-d`.** El plan del módulo anterior (RF-34, disponibilidad y carga de
repartidores) quedó completo y está en git en `290d034`, junto a su `todo.md`.

## Contexto

`docs/construccion_v1.md` §4.2 dice "PWA — repartidor (sin construir, H2/F3)". **Eso ya no es
cierto y el título del §4.2 miente:** la lectura del código muestra tres piezas construidas y
funcionando contra la API real.

| Pieza | Estado real, verificado por lectura de código |
|---|---|
| `/hoy` — mapa Leaflet, recorrido OSRM, progreso, próxima parada, "Cómo llegar" | **construido** (`app/hoy/page.tsx`, 263 líneas) |
| `/hoy/parada/[paradaId]` — dirección, llamar, bultos, observaciones, "Llegué" (RF-24) | **construido** |
| Cierre de parada — entregado (foto + receptor + posición + desvío) / fallido (motivo de lista cerrada) | **construido** (`MisParadasController.Cerrar`, idempotente por `device_uuid`, multipart atómico) |
| `GET /api/mis-paradas/dia` — bundle único de la jornada (RNF-07) | **construido** (`Servicios/JornadaService.cs`) |
| Cola offline: `lib/offline/`, Dexie, service worker, `manifest.json`, indicador de pendientes | **no existe** — ninguna de las cuatro cosas, ni la dependencia en `package.json` |
| Retiro 07:30 con conteo de bultos contra lista y firma | **no existe** — regla del acta §7, sin una línea de código |
| Foto del documento del receptor | **no existe, y hoy está prohibida por escrito** (RF-23) |
| "Siguiente pedido" después de entregar | `router.push("/hoy")` — hay que volver a elegir la parada a mano |
| Cierre de jornada del repartidor | **no existe** — RF-26 es hoy pantalla de solo-admin y los datos de calle llegan en papel |

Este módulo cierra los cuatro huecos de abajo de esa tabla. **No cierra el primero**, y por lo
tanto **no cierra H2/E5**: la prueba de salida de ese hito es la de modo avión (§7), y sin cola
offline no se puede correr. Ver "Lo que queda afuera".

## Decisiones tomadas por el usuario en esta sesión

| Pregunta | Resuelto |
|---|---|
| Foto de DNI vs. RF-23 | **Foto de documento con retención definida** — cambio de alcance, no tarea de UI |
| Alcance del "diario" del repartidor | **Las tres cosas**: retiro firmado + entrega con siguiente parada + cierre de jornada propio |
| Qué pasa con los datos que carga el repartidor | **Quedan como dato pendiente.** Administración los compara con los suyos y **aprueba o corrige** — no los sobrescribe. Revierte la recomendación original de este plan (un solo juego de columnas) |
| Cola offline | **Plan aparte y último** — se arranca después del Checkpoint 4. Esta tanda escribe directo contra la API |

## Restricciones de gobernanza (no negociables)

1. **Techo de tablas: 17, alcanzado en E1.** Cero tablas nuevas. Todo lo que este módulo persiste
   va como **columnas aditivas** en `rutas` y `pruebas_entrega`. Sigue en 20 entidades / 17 tablas.
2. **Regla 3.4 — el repartidor consulta `v_paradas_repartidor`, nunca `pedidos`.** Esa vista no
   tiene importes y no se toca. El cierre de jornada del repartidor **no devuelve ni muestra un
   solo precio, margen ni `pago_repartidor`** (RNF-08). Lo que sí carga son *gastos de calle* —
   km del odómetro, combustible y peajes que pagó él — que no son precios y que ya conoce.
   `otros_costos` y `pago_repartidor` quedan exclusivamente del lado admin (B4/E2).
3. **Regla 3.5 queda incumplida a propósito.** Dice: *"toda escritura desde la PWA pasa por la
   cola, incluso con señal. Un solo camino de escritura, probado siempre."* Esta tanda agrega
   **tres escrituras nuevas** (`retiro`, `cierre`, la segunda foto del cierre de parada) que
   escriben directo, igual que las dos que ya existen. Costo asumido y explícito: cuando se
   construya la cola hay que migrar **cinco** caminos, no dos. Es la consecuencia de la decisión
   de dejar la cola para un plan aparte, no un olvido.
4. **RF-23 se invierte, no se extiende.** Hoy dice *"verificación de identidad registrada **sin
   almacenar imagen del documento**"* y el código lo cumple con un booleano. Guardar la imagen es
   cambio de alcance: exige reescribir el RF en el acta con changelog 4.7, definir plazo de
   retención y construir el purgado. Fase 0 es bloqueante por esto, no por prolijidad.
5. **Migraciones aplicadas no se editan** (regla 3.6). Dos migraciones nuevas, una por fase.
6. **Sin scheduler en el proyecto** (confirmado en `Dominio/CiclosFacturacion.cs`: no hay
   Hangfire/Quartz/IHostedService). El purgado por retención sigue el precedente de
   `/api/facturas/cierre`: endpoint disparado desde afuera, idempotente, que se recupera solo si
   nadie lo corre un día.

## El riesgo que se asume por escrito

La imagen de un documento de identidad de una persona que, en palabras de RNF-09, *"no es cliente
y no consintió nada"*. El tratamiento de datos del destinatario es uno de los tres puntos de la
consulta legal única de acta §11.2, todavía sin hacer. La decisión está tomada; lo que el plan
puede hacer es construirla con los controles puestos desde el día uno, no después:

- **Retención con plazo configurable y purgado idempotente** que borra el archivo y deja constancia
  de la fecha de borrado en la fila (`foto_documento_borrada_en`). Una foto sin fecha de borrado
  programado es una foto para siempre.
- **`documento_numero` sobrevive a la purga, la imagen no.** Es lo que hace que la retención sea
  viable: sin él, purgar deja `identidad_verificada = true` y nada más — exactamente el estado de
  hoy, con lo cual la foto no habría servido para nada en el conflicto de dentro de seis meses.
- **La imagen es solo-admin** (`Administracion`), más estricta que la foto de la entrega
  (`BackOffice`), con `Cache-Control: private, no-store` y detrás de un click explícito: nunca se
  precarga en un `<img>` al abrir el detalle del pedido.
- **Fila nueva en acta §12 (riesgos reconocidos)** y dependencia dura anotada en §11.2: la consulta
  legal pasa de "agendada" a "bloquea la primera ruta con fotos de documento".
- **`RetencionDocumentoDias` arranca en 30, provisional** — mismo estatus declarado que
  `MotivosFallo` y `UmbralDesvioMetros`. Es decisión de negocio, va al checklist de §10.

## Decisiones de diseño

| Decisión | Resuelto | Por qué |
|---|---|---|
| Estado del retiro | Columnas en `rutas`, **sin estado nuevo** en la máquina de la ruta | `Estado == "en_curso"` se compara en `MisParadasController` (3 veces), `JornadaService`, `RutasController` y `RepartidoresController`. Un cuarto estado toca todo eso para expresar algo que es un atributo de la ruta, no una fase |
| Firma | `<canvas>` a mano → JPEG, mismo `AlmacenamientoFotos` con carpeta `retiros/` | El frontend no tiene una sola dependencia de interacción compleja (precedente: flechas ↑/↓ en vez de drag & drop, §4.1). Una firma es un path sobre un canvas, no justifica una librería |
| Conteo distinto al esperado | **Permitido, con observación obligatoria. Nunca bloqueo** | El acta §7 exige *conteo firmado*, no *conteo coincidente*. Bloquear la salida por un bulto de diferencia cuesta la jornada entera; lo que la regla protege es que la discrepancia quede escrita y firmada antes de salir |
| Gate del retiro | `409` en `/llegada` y `/cierre` si la ruta no tiene retiro confirmado | Acta §7: *"ninguna ruta sale sin conteo firmado"*. Las reglas operativas están *"codificadas en el software, no libradas al criterio del día"* |
| Segunda foto | Columna `foto_documento_path`, archivo `{deviceUuid}-documento.jpg` en la misma carpeta | Aditivo puro: las fotos ya guardadas siguen en `pruebas/{paradaId}/{deviceUuid}.jpg` y ninguna migración de archivos hace falta |
| Cuándo es obligatoria la foto del documento | Solo si `identidadVerificada` viene en `true` | Mantiene la verificación opcional como hoy (no todo receptor la necesita) y vuelve imposible el estado "verificada sin evidencia" |
| Siguiente parada | `CierreResultado.SiguienteParadaId`, resuelto **en el servidor** | El back-office puede reordenar paradas pendientes en vivo (`PUT /api/rutas/{id}/paradas/orden`). El bundle que el cliente tiene en memoria puede estar viejo justo en el momento del cierre |
| Cierre de jornada | **Dos juegos de columnas.** El repartidor escribe `retiro_km_inicial` y `cierre_repartidor_*`: eso es una **declaración pendiente**, no el cierre. `km_inicial`/`km_final`/`combustible_monto`/`peajes_monto` los sigue escribiendo solo `RutasController.Cerrar` | **Decisión del usuario, 17/09/2026, que revierte la recomendación original de este plan** (un solo juego de columnas, el admin corrigiendo encima). Con dos juegos, lo que dijo la calle queda intacto para siempre al lado de lo que cerró administración, y el desvío entre ambos es evidencia, no un dato perdido. El costo es 6 columnas más y una comparación en la pantalla de cierre; lo que compra es que el admin **aprueba o corrige**, y nunca puede tapar el número original |
| Aprobación | Implícita en `RutasController.Cerrar` + `cerrada_por`: cerrar la ruta **es** aprobar | Un estado `aprobado/rechazado` propio sería una segunda máquina de estados para una decisión que ya tiene un acto que la expresa. No hay "rechazar": hay cerrar con el número del repartidor (aprobar) o cerrar con otro y decir por qué (corregir) |
| Corrección silenciosa | **Prohibida.** Si algún valor del cierre difiere del declarado, `NotasCierre` pasa a ser obligatorio → `400` | Mismo criterio que `TransicionesPedido.MotivoObligatorio`: la transición que contradice lo que informó otro actor no se hace sin motivo escrito |
| Inmutabilidad de lo declarado | **Trigger** `trg_congelar_declaracion_repartidor` sobre `rutas` | Regla 3.3: si el trigger lo impide, la app muestra el error y no reimplementa la validación. Precedente directo: `trg_congelar_pedido` con el precio congelado, y `trg_facturas_inmutable`. Una declaración de calle que el back-office puede editar no es evidencia de nada |
| Divergencia declarado vs. cerrado | **Derivada en lectura**, nunca persistida | Mismo criterio que el saldo de facturas (`v_facturas_saldo`) y la disponibilidad de RF-34: si los dos números están en la fila, el "¿coincide?" es una comparación, no una columna |
| Endpoints del retiro y del cierre | **`MiJornadaController` nuevo**, policy `Repartidor` de clase | `MisParadasController` escribe por parada contra `v_paradas_repartidor` y su docstring declara que nunca toca `pedidos`; estos dos escriben `rutas`. Recurso distinto, controller distinto — un controller por recurso (§2) |
| Lectura del retiro/cierre | Campos aditivos en el bundle que ya existe (`GET /api/mis-paradas/dia`) | RNF-07 pide un solo request antes de salir. Un `GET /api/mi-jornada` paralelo sería un segundo round-trip para tres campos |

---

## Fase 0 — Gobernanza (bloqueante)

Puramente documental, sin build. Se hace **antes** del código, como en el módulo anterior.

**`docs/acta_sistema.md`** → versión **4.7**

- §5.3, **RF-23 reescrito** (el número no se toca — los RF no se renumeran nunca):
  *"Verificación de identidad del receptor registrada con número de documento e imagen del
  documento, retenida por un plazo definido y purgada al vencer: el número sobrevive a la purga,
  la imagen no."* Debajo, un párrafo **"Sobre RF-23"** que diga que esto **revierte** la redacción
  anterior (*"sin almacenar imagen del documento"*), quién lo decidió y qué controles lo
  acompañan. Una inversión de requisito que no se anota como inversión se lee, en tres meses, como
  si el requisito original nunca hubiera existido.
- §5.3, **RF-35 nuevo** (al final de la sección, después de RF-25): *"Retiro registrado con conteo
  de bultos contra la lista de la ruta y firma del repartidor, antes de la primera parada."*
  Materializa la regla de §7 que hasta hoy no tenía RF propio.
- §5.4, **nota bajo RF-26**: el cierre pasa a ser de **dos actores y dos actos** — el repartidor
  **declara** km, combustible y peajes desde la calle, y esa declaración queda **pendiente e
  inmutable**; administración la **compara y la aprueba o la corrige con motivo escrito** al
  cerrar la economía (otros costos, pago al repartidor, margen). No es un RF nuevo: es el mismo
  requisito con el actor que le faltaba y con la separación entre informar y aprobar.
- §7 (reglas operativas que el sistema impone), regla nueva: **"El número de la calle no se
  reescribe."** Lo que el repartidor declaró queda; corregirlo exige un motivo escrito y deja
  los dos números visibles. Es la regla que el trigger codifica.
- §4.1 (campos incorporados en esta versión): las columnas nuevas de `rutas` y `pruebas_entrega`,
  el trigger `trg_congelar_declaracion_repartidor`, **explicitando "sigue en 17 tablas"**.
- §12 (riesgos reconocidos): fila del riesgo de la imagen del documento.
- §11.2: el punto de la consulta legal pasa a estar **bloqueando**, no solo agendado.
- §14: fila de changelog **4.7**, mismo estilo de párrafo largo que 4.5/4.6. Debe decir qué
  resuelve (RF-23 invertido, RF-35, RF-26 en dos actores), que no crea tablas, y que **no cierra
  H2/E5**.

**`docs/construccion_v1.md`** → versión **1.21**

- **§4.2: reescribir el encabezado.** Hoy dice "PWA — repartidor (sin construir, H2/F3)" y es
  falso desde antes de este módulo. Pasa a declarar qué está construido, qué falta (cola offline)
  y que el hito sigue abierto por eso. Filas nuevas: `/hoy/retiro` → RF-35, `/hoy/cierre` → RF-26.
- §5 (máquina de estados): **sin cambios**, y decirlo. El retiro no agrega estado de ruta y la
  aprobación del cierre tampoco: cerrar la ruta es el acto de aprobar.
- §3 (reglas no negociables): el trigger nuevo es un caso de la regla 3.3 y va nombrado donde se
  enumeran los triggers; la validación de "no editar la declaración" **no** se duplica en C#.
- §7 (sincronización offline): nota de que las tres escrituras nuevas nacen **fuera** de la cola y
  hay que migrarlas — la deuda queda contada donde vive el diseño de la cola, no solo acá.
- §9 (configuración): `PruebaEntrega:RetencionDocumentoDias`.
- §10 (antes de la primera ruta): tres ítems nuevos — plazo de retención real, quién puede ver la
  imagen, y **el cron del purgado configurado y probado**.
- §12: fila de changelog **1.21** con el detalle técnico (endpoints, columnas, round-trips,
  archivos).

**`docs/estado_implementacion.md`**

- §3.6 (ejecución en calle) reescrito con los endpoints nuevos y el gate del retiro.
- §3.x nuevo o ampliación de §3.7: el cierre de dos actores.
- §4: encabezado **"Pantallas del frontend (31)" → (33)**.
- §5: las columnas nuevas; **conteos de entidades y tablas sin cambios** (20 / 17).
- §6: la cola offline sigue listada como no construida, ahora con el detalle de que las pantallas
  sí están.

**Desvío aplicado en la ejecución de la Fase 0 (17/09/2026), contra lo que esta misma sección
pedía.** Los dos ítems de conteo de `estado_implementacion.md` **no** se movieron: las pantallas
siguen en **31**, no en 33, y las columnas nuevas no se listaron en §5. El módulo anterior sí
había movido su conteo en la Fase 0 (26 → 28, tarea 0.7), y copiar ese precedente acá habría hecho
que el único documento que responde *"¿qué hay escrito hoy?"* afirmara que existen dos pantallas
que nadie escribió todavía. En su lugar: la nota de §4 explica que el conteo pasa a 33 cuando el
código exista, las dos pantallas aparecen tachadas y marcadas "diseñadas, no construidas" sin
sumar, y el encabezado del documento gana una **regla explícita** — acá se cuenta lo que existe; lo
acordado y no escrito se nombra como tal y no suma a ningún conteo. El acta y
`construccion_v1.md` sí se escribieron por adelantado, que es su función (alcance y diseño), y
ambos lo declaran en su propia fila de changelog. Los conteos de `estado_implementacion.md` se
mueven en el Checkpoint 4, recontados contra el código.

> **Checkpoint 0 — hecho (17/09/2026).** Los tres documentos citan los mismos RF (23 reescrito, 35
> nuevo, 26 anotado), el mismo plazo de retención provisional de 30 días y la misma frase sobre
> H2/E5 sin cerrar. Acta **4.7**, construcción **1.21**, `estado_implementacion` con sus conteos
> deliberadamente sin mover.

---

## Fase 1 — Retiro con conteo firmado (RF-35)

### Tarea 1.1 · Migración `AgregarRetiroDeRuta`

Columnas en `rutas`, todas nullable (las rutas existentes quedan sin retiro, que es la verdad):

| Columna | Tipo | Nota |
|---|---|---|
| `retiro_confirmado_en` | `timestamptz null` | hora de captura del dispositivo (RNF-03) |
| `retiro_bultos_esperados` | `int null` | congelado al confirmar: es la lista contra la que se contó |
| `retiro_bultos_contados` | `int null` | lo que el repartidor contó de verdad |
| `retiro_observaciones` | `text null` | obligatorio solo si los dos números difieren |
| `retiro_firma_path` | `text null` | ruta relativa, mismo criterio que `pruebas_entrega.foto_path` |
| `retiro_device_uuid` | `text null` | idempotencia (RNF-02), mismo rol que en `pruebas_entrega` |
| `retiro_km_inicial` | `int null` | **declarado por el repartidor.** No es `km_inicial`: ese lo sigue escribiendo solo el cierre de admin |
| `cierre_repartidor_en` | `timestamptz null` | fase 4, entra en esta migración para no hacer dos |
| `cierre_repartidor_km_final` | `int null` | fase 4, declarado |
| `cierre_repartidor_combustible` | `numeric(12,2) null` | fase 4, declarado |
| `cierre_repartidor_peajes` | `numeric(12,2) null` | fase 4, declarado |
| `cierre_repartidor_notas` | `text null` | fase 4, declarado |
| `cierre_repartidor_device_uuid` | `text null` | idempotencia del cierre (RNF-02) |
| `cerrada_por` | `uuid null` FK `usuarios` | quién aprobó el cierre. Sin esto, "aprobar o no" no tiene actor registrado |

**Las cuatro columnas económicas que ya existen (`km_inicial`, `km_final`, `combustible_monto`,
`peajes_monto`) no las toca el repartidor.** Son el cierre de administración; las `retiro_*` y
`cierre_repartidor_*` son la declaración de la calle. Los dos números conviven en la fila.

Sin índices nuevos: siempre se llega a estas columnas por `rutas.id` o por la ruta `en_curso` del
repartidor, dos caminos ya indexados.

**`docs/schema_v3.sql` no se toca** (verificado: no contiene `facturas` ni sus triggers — quedó
como el snapshot de H0 y desde E1 los triggers viven solo en las migraciones). No es un olvido de
este plan.

**En la misma migración: `trg_congelar_declaracion_repartidor`.** Va acá y no en la fase 4 porque
la declaración del retiro se sella en la fase 1 y tiene que estar protegida desde el momento en
que existe. Mismo patrón que `fn_congelar_pedido` (comparar `old`/`new` y levantar excepción) y
mismo criterio de mensaje propio que `fn_facturas_inmutable` (no reusar `fn_log_inmutable`, que
tiene "pedido_eventos" hardcodeado en el `raise`):

```sql
create or replace function fn_congelar_declaracion_repartidor()
returns trigger language plpgsql as $$
begin
  if old.retiro_confirmado_en is not null and (
       new.retiro_confirmado_en    is distinct from old.retiro_confirmado_en
    or new.retiro_bultos_esperados is distinct from old.retiro_bultos_esperados
    or new.retiro_bultos_contados  is distinct from old.retiro_bultos_contados
    or new.retiro_observaciones    is distinct from old.retiro_observaciones
    or new.retiro_firma_path       is distinct from old.retiro_firma_path
    or new.retiro_km_inicial       is distinct from old.retiro_km_inicial) then
    raise exception 'Ruta %: el retiro firmado por el repartidor no se edita. El cierre de administración corrige sus propias columnas, no la declaración de la calle.', old.id;
  end if;
  if old.cierre_repartidor_en is not null and (
       new.cierre_repartidor_en           is distinct from old.cierre_repartidor_en
    or new.cierre_repartidor_km_final     is distinct from old.cierre_repartidor_km_final
    or new.cierre_repartidor_combustible  is distinct from old.cierre_repartidor_combustible
    or new.cierre_repartidor_peajes       is distinct from old.cierre_repartidor_peajes
    or new.cierre_repartidor_notas        is distinct from old.cierre_repartidor_notas) then
    raise exception 'Ruta %: el cierre de jornada del repartidor no se edita. Para cambiar el número, administración cierra con otro valor y NotasCierre obligatorio.', old.id;
  end if;
  return new;
end $$;

create trigger trg_congelar_declaracion_repartidor
  before update on rutas
  for each row execute function fn_congelar_declaracion_repartidor();
```

`is distinct from` y no `<>`: con nulls de por medio, `<>` devuelve `null` y la condición no
dispara nunca — el bug clásico de este tipo de trigger. `ManejadorExcepciones` ya traduce la
excepción de Postgres a `409`/`400` con el mensaje del trigger (regla 3.3): **nada de esto se
revalida en C#**.

### Tarea 1.2 · `AlmacenamientoFotos` — carpeta parametrizable

Hoy la ruta está hardcodeada: `pruebas/{paradaId}/{deviceUuid}.jpg`. Agregar un parámetro de
carpeta y un sufijo opcional de nombre, **conservando la firma actual como default** para no tocar
la llamada que ya existe en `MisParadasController`. Los tres invariantes se mantienen intactos:
`Guid.TryParse` del `deviceUuid` contra path traversal, tope de KB, y magic bytes JPEG.

*Detalle que muerde:* `canvas.toBlob("image/jpeg")` sobre un canvas con fondo transparente sale
**negro**, no blanco — JPEG no tiene canal alfa. El canvas de la firma se rellena de blanco antes
de dibujar. Sin eso, el chequeo de magic bytes pasa y la firma es un rectángulo negro.

### Tarea 1.3 · `MiJornadaController` — `POST /api/mi-jornada/retiro`

Controller nuevo, `[Authorize(Policy = "Repartidor")]` **de clase** (todas sus acciones comparten
policy, así que se declara una vez — mismo criterio que `JornadaController`, regla 3.8).

Docstring que declare el límite, siguiendo el precedente de `MisParadasController` y
`RepartidoresController`:

```
/// Escrituras del repartidor sobre SU ruta (RF-35 y la mitad de calle de RF-26, acta changelog
/// 4.7). Hermano de MisParadasController, que escribe por parada contra v_paradas_repartidor:
/// este toca `rutas`, y solo la ruta en_curso del repartidor autenticado.
///
/// NO es liquidación (B4/E2): no escribe ni devuelve rutas.pago_repartidor ni otros_costos, y
/// ninguna respuesta de este controller contiene un precio, un margen ni un importe que el
/// repartidor no haya tipeado él mismo (RNF-08, regla 3.4).
```

`multipart/form-data` (viaja la firma). Request: `DeviceUuid`, `CapturadaEn`, `BultosContados`,
`KmInicial`, `Observaciones?`, `Firma`. El `KmInicial` del request va a **`retiro_km_inicial`**, no
a `rutas.km_inicial`: es lo que el repartidor leyó del odómetro, y queda pendiente de aprobación
igual que el resto de la declaración. Reglas:

1. Resuelve la ruta `en_curso` del repartidor autenticado, igual que `MisParadasController.Dia`
   (`FirstOrDefault` sobre `RepartidorId == yo && Estado == "en_curso"`). Sin ruta → `404`.
2. **Idempotente**: si `retiro_confirmado_en` ya tiene valor y el `device_uuid` coincide, `200`
   con el retiro existente y `duplicado: true` — mismo contrato que `CerrarParadaRequest`. Con
   otro `device_uuid`, `409`: el retiro ya lo firmó otro dispositivo.
3. `bultos_esperados` lo calcula **el servidor** sumando `pedidos.bultos` de las paradas de la
   ruta, y lo congela en la fila. Nunca llega del cliente: es la lista contra la que se firma.
4. `bultos_contados != bultos_esperados` y `Observaciones` vacío → `400`.
5. `Firma` ausente → `400`. Es lo único que el acta §7 exige sin excepción.
6. Escribe con `SaveChangesAsync` plano, **no** `GuardarComoAsync`: no toca `pedidos`, así que el
   GUC `app.usuario_id` no tiene consumidor — mismo razonamiento ya escrito en
   `MisParadasController.RegistrarLlegada` y `RutasController.Cerrar` (regla 3.7).
7. La firma se escribe a disco **antes** de la transacción, por el mismo motivo documentado en
   `Cerrar`: un archivo huérfano es inofensivo, una fila apuntando a un archivo inexistente no.

### Tarea 1.4 · Gate del retiro

En `MisParadasController.RegistrarLlegada` y `Cerrar`: si `parada.Ruta.RetiroConfirmadoEn is null`
→ `409` *"La ruta no tiene el retiro confirmado (acta §7): ninguna ruta sale sin conteo firmado."*

El chequeo va **después** del chequeo de idempotencia de `Cerrar`, no antes: un reintento de algo
que ya se cerró tiene que seguir respondiendo `duplicado: true`, incluso si alguien limpió el
retiro a mano en la base.

*Consecuencia en desarrollo:* la ruta `en_curso` del seed (`DatosSemilla.cs`) no tiene retiro, así
que lo primero que pide `/hoy` es confirmarlo. Es el comportamiento correcto, no un bug del seed.

### Tarea 1.5 · Bundle: tres campos aditivos

`MisParadasController.JornadaDelDia` suma `RetiroConfirmadoEn`, `BultosEsperados` y
`CierreRepartidorEn`. `JornadaService.JornadaRuta` **no se toca**: el back-office los recibe por
`RutaDetalle`, que ya lee `rutas` directo. Espejo en `lib/dominio/tipos.ts`.

### Tarea 1.6 · `/hoy/retiro` + tarjeta bloqueante en `/hoy`

- `frontend/components/FirmaCanvas.tsx` (nuevo) — canvas con pointer events, botón "Borrar",
  `toBlob` a JPEG con fondo blanco. Alto fijo, sin scroll (RNF-06).
- `frontend/app/hoy/retiro/page.tsx` (nuevo) — `RequireRole roles={["repartidor"]}`. Muestra
  bultos esperados en grande, un input numérico para lo contado, `textarea` de observaciones que
  **aparece solo si los números difieren**, km inicial, la firma y un botón. Botones de 48 px,
  contraste alto (RNF-06).
- `frontend/app/hoy/page.tsx` — si `retiroConfirmadoEn === null`, la tarjeta de "Próxima parada" se
  reemplaza por una tarjeta de retiro pendiente con un solo botón, y las tarjetas de parada quedan
  no navegables. El mapa se sigue viendo: saber a dónde vas antes de cargar la camioneta es útil.
- `frontend/proxy.ts` — `/hoy` ya está en `RUTAS_PROTEGIDAS` con `:path*`, **nada que agregar**
  (verificado, no asumido).

**Aceptación**
1. Repartidor con ruta `en_curso` sin retiro: `/hoy` no deja abrir ninguna parada y ofrece retirar.
2. Conteo igual al esperado → sin observaciones, firma y listo.
3. Conteo distinto sin observación → `400` con el mensaje del servidor visible en pantalla.
4. Sin firma → `400`.
5. Reintentar el mismo retiro desde el mismo dispositivo → `200 duplicado: true`, no un segundo.
6. `curl` a `/llegada` de una ruta sin retiro → `409` con el texto del acta §7.
7. Después del retiro, `/hoy` vuelve a comportarse como hoy.

> **Checkpoint 1:** la regla del acta §7 está codificada, no documentada. Demostrable con `curl`
> antes de mirar el navegador.

---

## Fase 2 — Entrega con foto del documento y retención (RF-23 reescrito)

### Tarea 2.1 · Migración `AgregarFotoDocumento`

En `pruebas_entrega`: `documento_numero text null`, `foto_documento_path text null`,
`foto_documento_borrada_en timestamptz null`. `identidad_verificada` **se conserva** — sigue siendo
el booleano que responde "¿se verificó?", ahora con evidencia al lado.

### Tarea 2.2 · Backend del cierre de parada

`MisParadasController.CerrarParadaRequest` suma `DocumentoNumero?` y `FotoDocumento?`.

- `[RequestSizeLimit(2 * 1024 * 1024)]` → **3 MB**. `TamanoMaximoKb` sigue en 400 **por foto**: dos
  fotos de 400 KB más los campos no entran cómodas en 2 MB.
- Validación nueva, una sola: `identidadVerificada == true` en una entrega exige `FotoDocumento`.
  Si viene `false`, todo el bloque del documento se ignora — y `DocumentoNumero` no se persiste,
  para no guardar un dato de identidad que nadie declaró haber verificado.
- La segunda foto se guarda con sufijo `-documento`, misma carpeta y mismo `deviceUuid`: el
  reintento pisa el mismo archivo, idempotente sin lógica extra, igual que la primera.
- **Orden de escritura:** las dos fotos a disco antes de la transacción, por el motivo ya
  documentado. Si la segunda falla, la primera queda huérfana y el reintento la pisa.

### Tarea 2.3 · Retención y purgado

- `OpcionesPruebaEntrega.RetencionDocumentoDias = 30` (provisional, documentado como tal en el
  propio docstring de la clase, que ya tiene el párrafo "PROVISIONALES" donde sumarse).
- `POST /api/pruebas-entrega/purga-documentos`, policy `Administracion`. Por cada fila con
  `foto_documento_path is not null` y `capturada_en < hoy - N`: borra el archivo, pone el path en
  `null` y sella `foto_documento_borrada_en`. Devuelve `{ purgadas, fallidas }`. **Idempotente y
  recuperable**: correrlo tarde purga todo lo vencido, correrlo dos veces el mismo día no hace
  nada la segunda — el mismo criterio que `CiclosFacturacion` documenta para el cierre de ciclo.
  Un archivo que no se pudo borrar **no** se marca como borrado: cuenta en `fallidas` y se reintenta
  en la corrida siguiente. Mentir en `foto_documento_borrada_en` es peor que no borrar.
- `GET /api/pruebas-entrega/documentos-vencidos` → `{ cantidad }`, policy `BackOffice`.
- **Mecanismo real: cron externo** con `curl`, línea nueva en el checklist de §10. El endpoint es
  el mecanismo; el contador de abajo es el detector de que el cron no está corriendo. Uno sin el
  otro es el modo de falla que `CiclosFacturacion` describe: *"un endpoint que solo supiera '¿cierra
  hoy?' perdería el período entero si nadie lo corre ese día"*.
- `GET /api/pruebas-entrega/{id}/foto-documento`, policy **`Administracion`** (más estricta que la
  foto de entrega, que es `BackOffice`). `Cache-Control: private, no-store`, ya el patrón del
  controller. `404` después de la purga — y el frontend lo distingue de un error mostrando la fecha
  de borrado, que sigue en la fila.

### Tarea 2.4 · Frontend

- `app/hoy/parada/[paradaId]/page.tsx` — dentro del modo `entregado`, cuando "Identidad verificada"
  queda marcada aparecen el input de número y el `<Input type="file" capture="environment">` del
  documento, con su preview. Reusa `comprimirFoto` tal cual: los mismos 1280 px / calidad 0.7
  dejan un DNI perfectamente legible y por debajo de 400 KB. El `useMemo` + `revokeObjectURL` del
  preview se replica para la segunda foto — el patrón ya está escrito, no se inventa otro.
- `components/PedidoDetalleContenido.tsx` — fila "Documento del receptor" con el número y un botón
  **"Ver imagen"** que recién ahí baja el blob con `fetchConSesion`. Visible solo para
  `administracion`. Si la imagen fue purgada, muestra la fecha de borrado en vez de un error.
- `frontend/app/jornada/page.tsx` — aviso de una línea *"N imágenes de documento superaron la
  retención"* con botón "Purgar", solo para `administracion`. Va acá porque es la única pantalla
  que el back-office abre todos los días; **es la parte más débil de este diseño** y se mueve sin
  costo si aparece un lugar mejor.

**Aceptación**
1. Entrega con identidad verificada y sin foto del documento → `400`, y la UI no deja enviar.
2. Entrega sin identidad verificada → se comporta exactamente como hoy; nada del documento se
   persiste, ni el número.
3. Las dos fotos quedan en `pruebas/{paradaId}/` con y sin sufijo `-documento`.
4. Rol `operacion` contra `/foto-documento` → `403`. Rol `administracion` → la imagen.
5. Con `RetencionDocumentoDias = 0`, la purga borra el archivo, deja el número, sella la fecha y
   el detalle del pedido muestra "imagen purgada el …". Correrla de nuevo devuelve `purgadas: 0`.
6. El aviso de `/jornada` aparece con vencidas y desaparece después de purgar.

> **Checkpoint 2:** la pieza que cruza el alcance queda entregada **con su purgado funcionando**,
> no con el purgado prometido. Si la fase se cierra sin correr la purga, RNF-09 quedó en el aire.

---

## Fase 3 — Siguiente parada

### Tarea 3.1 · `SiguienteParadaId` en el cierre

`CierreResultado` suma `long? SiguienteParadaId`: la parada `pendiente` de menor `Orden` de la
misma ruta, después de aplicar el cierre. Una query, en la misma transacción lógica. **Los dos
caminos de salida la devuelven** — el feliz y los dos de `duplicado: true`. Un reintento después de
recuperar señal tiene que llevar al repartidor al mismo lugar que el intento original.

### Tarea 3.2 · Navegación

`app/hoy/parada/[paradaId]/page.tsx` — `router.push` a `/hoy/parada/{siguiente}` si vino, a `/hoy`
si no. Usa `router.replace`, no `push`: el botón atrás del teléfono no debe volver a una parada ya
cerrada, que solo puede mostrar "ya quedó completada".

`app/hoy/page.tsx` — cuando no queda ninguna pendiente y la jornada no está cerrada, la tarjeta de
"Próxima parada" se reemplaza por "Cerrar la jornada" → `/hoy/cierre`.

**Aceptación**
1. Entregar la parada 3 de 8 cae directo en la 4, sin pasar por `/hoy`.
2. La última entrega cae en `/hoy` con el botón de cerrar jornada.
3. Si el back-office reordena las pendientes mientras el repartidor está en el formulario, la
   siguiente es la del **orden nuevo** (por eso la resuelve el servidor).
4. Atrás desde la parada nueva no vuelve a la cerrada.

> **Checkpoint 3:** el ciclo de calle queda cerrado — retirar, entregar, siguiente, hasta que no
> queda ninguna.

---

## Fase 4 — Declaración de la calle y aprobación de administración (RF-26, dos actores)

El repartidor **declara**; administración **compara, aprueba o corrige**. Son dos actos separados,
sobre dos juegos de columnas, y el primero es inmutable una vez firmado.

### Tarea 4.1 · `POST /api/mi-jornada/cierre` — la declaración

JSON, no multipart. Request: `DeviceUuid`, `CapturadaEn`, `KmFinal`, `CombustibleMonto`,
`PeajesMonto`, `Notas?`. Reglas:

1. Ruta `en_curso` del repartidor, con retiro confirmado (sin retiro no hay `retiro_km_inicial`
   contra el cual validar) → si no, `409`.
2. Ninguna parada en `pendiente` → si queda alguna, `409` con cuántas faltan. La jornada se cierra
   cuando la calle terminó, no cuando el repartidor se quiere ir.
3. `KmFinal < retiro_km_inicial` → `400`. Se compara contra lo que **él mismo** declaró al retirar,
   no contra `rutas.km_inicial`, que en este momento todavía es `null`.
4. Escribe **solo** `cierre_repartidor_km_final`, `cierre_repartidor_combustible`,
   `cierre_repartidor_peajes`, `cierre_repartidor_notas`, `cierre_repartidor_device_uuid` y sella
   `cierre_repartidor_en`. **No toca** `Estado`, ni `km_final`, ni `combustible_monto`, ni
   `peajes_monto`, ni `otros_costos`, ni `pago_repartidor`. La ruta sigue `en_curso`.
5. Idempotente: ya sellada con el mismo `device_uuid` → `200` con el mismo cuerpo y
   `duplicado: true`. Con otro `device_uuid` → `409`, igual que el retiro.
6. La respuesta son **conteos** (entregadas / fallidas / total) más lo que el repartidor tipeó.
   Cero importes de la empresa.
7. Un segundo intento de editar la declaración lo rechaza el **trigger**, no el controller. El
   controller solo tiene el camino de idempotencia; si algo se cuela, `ManejadorExcepciones`
   traduce el `raise` del trigger (regla 3.3).

### Tarea 4.2 · `/hoy/cierre`

Km final (con `retiro_km_inicial` a la vista y los km del día calculados en vivo), combustible,
peajes, notas, y el resumen de la jornada. Un botón. Después, `/hoy` en estado "jornada cerrada":
sin acciones, con el resumen y la leyenda *"pendiente de revisión de administración"* — el
repartidor tiene que saber que lo que cargó todavía no es el cierre.

### Tarea 4.3 · Medidas en `RutasController.Cerrar` — el bug, y qué hacer con la declaración

**El bug, concreto:** `app/rutas/[id]/cierre/page.tsx:32-38` arranca los siete inputs en
`useState("")` y `Cerrar` (líneas 525-531) asigna sin condición `ruta.KmInicial = req.KmInicial`,
`KmFinal`, `CombustibleMonto`, `PeajesMonto`, `OtrosCostos`, `PagoRepartidor`, `NotasCierre`. Hoy
eso no rompe nada porque nadie escribe esas columnas antes. Con la fase 4 encima, el admin que
abre el formulario y cierra **pisa las cuatro columnas económicas con lo que haya en pantalla**.

El diseño de dos juegos de columnas ya neutraliza el daño grave: la declaración de la calle vive
en `retiro_*` / `cierre_repartidor_*` y el trigger la protege, así que `Cerrar` **no puede**
borrarla ni editarla, pase lo que pase en el formulario. Sobre eso, cinco medidas:

**M1 · Prellenado desde la declaración, no desde vacío.** `app/rutas/[id]/cierre/page.tsx` arranca
`kmInicial` en `retiroKmInicial`, `kmFinal` en `cierreRepartidorKmFinal`, `combustible` y `peajes`
en los declarados. `otros_costos` y `pago_repartidor` siguen arrancando vacíos: no los declara
nadie más. Si no hay declaración, el formulario arranca vacío como hoy.

**M2 · Cerrar sin declaración exige decirlo.** Si `cierre_repartidor_en is null` → `409`
*"La ruta no tiene el cierre de jornada del repartidor"*, **salvo** que el request traiga
`SinDeclaracionDelRepartidor: true`. El admin puede cerrar igual (teléfono muerto, repartidor que
se olvidó), pero tiene que declarar que está cerrando con datos que nadie verificó en la calle.
Un 409 sin escape haría inoperable la ruta el día que el celular no arranque; un cierre silencioso
convierte la fase 4 en decorado.

**M3 · Corregir sí, corregir en silencio no.** Si alguno de los cuatro valores del request difiere
del declarado, `NotasCierre` pasa a ser **obligatorio** → `400` con el detalle de qué campo
difiere y en cuánto. Es la misma forma que ya tiene `TransicionesPedido.MotivoObligatorio`: el
acto que contradice lo que informó otro actor no se hace sin motivo escrito. Aprobar (mandar los
mismos números) no pide nada.

**M4 · `cerrada_por`.** `Cerrar` sella `cerrada_por = User.UsuarioId()` junto a `CerradaEn`.
Cerrar la ruta **es** el acto de aprobación, y sin actor registrado "aprobar o no" no queda en
ninguna parte. `rutas` no tiene log de eventos propio y crear uno cruzaría el techo de tablas
(gap ya aceptado por escrito en §4.3 para reasignar/reordenar), así que esta es la única huella:
quién cerró, cuándo, con qué números, y al lado los del repartidor, intactos.

**M5 · La divergencia se ve, no se guarda.** `RutaDetalle` suma los campos declarados; la pantalla
de cierre los muestra en una columna "Declarado por el repartidor" al lado de los inputs, con el
delta cuando difieren, y la leyenda *"Cargado desde la calle a las HH:MM"*. `ResultadoRuta` marca
si el cierre fue **aprobado tal cual** o **corregido** comparando los dos juegos — derivado en
lectura, mismo criterio que `v_facturas_saldo` y la disponibilidad de RF-34. Cero columnas de
estado de aprobación.

`RutaDetalle` suma: `RetiroConfirmadoEn`, `RetiroBultosEsperados`, `RetiroBultosContados`,
`RetiroObservaciones`, `RetiroKmInicial`, `CierreRepartidorEn`, `CierreRepartidorKmFinal`,
`CierreRepartidorCombustible`, `CierreRepartidorPeajes`, `CierreRepartidorNotas`, `CerradaPor`.
Todo aditivo.

### Tarea 4.4 · El back-office ve la regla cumplida

`JornadaController.Resumen` suma, por ruta, `RetiroConfirmadoEn` y `CierreRepartidorEn`; `/jornada`
los muestra en el panel por repartidor, más un aviso de **rutas con declaración pendiente de
revisión** (`cierre_repartidor_en is not null and estado = 'en_curso'`). Es la diferencia entre
*"la regla está codificada"* y *"desde el escritorio se ve que se cumplió"*, que es lo que el acta
§7 realmente quiere. Son columnas de `rutas`, que esa query ya trae: ningún `GroupBy` nuevo.

*Discrepancia de bultos visible:* si `contados != esperados`, `/jornada` y `/rutas/[id]` lo marcan.
Un bulto faltante que solo se ve entrando al detalle de la ruta no se ve.

**Aceptación**
1. Cerrar la jornada con una parada pendiente → `409` diciendo cuántas faltan.
2. `km_final` declarado menor que `retiro_km_inicial` → `400`.
3. Cerrada la jornada, `/hoy` no ofrece más acciones y avisa que está pendiente de revisión.
4. `UPDATE rutas set cierre_repartidor_km_final = ...` a mano en la base → **excepción del
   trigger**, traducida a `409` con el mensaje del `raise`. Lo mismo sobre cualquier campo del
   retiro. **Este es el test que sostiene todo el diseño.**
5. El admin abre `/rutas/{id}/cierre` y encuentra los cuatro campos **ya cargados con lo declarado**,
   con el "Cargado desde la calle a las HH:MM" al lado.
6. Cerrar mandando los mismos cuatro valores → `200`, sin exigir notas, y el resultado queda
   marcado **aprobado tal cual**. Los valores declarados siguen en la fila después del cierre.
7. Cerrar cambiando el combustible sin `NotasCierre` → `400` nombrando el campo y el delta. Con
   notas → `200`, marcado **corregido**.
8. Cerrar una ruta sin declaración del repartidor → `409`; con `SinDeclaracionDelRepartidor: true`
   → `200`.
9. `cerrada_por` queda con el id del admin que cerró.
10. Ninguna respuesta de `/api/mi-jornada/*` contiene un importe que el repartidor no haya tipeado.

> **Checkpoint 4 (final):** releer los tres documentos de la Fase 0 **contra el código final** y
> corregirlos donde el diseño cambió durante la ejecución — corregir, no solo agregar, como en el
> Checkpoint 3 del módulo anterior. Recontar las pantallas de `estado_implementacion.md` §4 en vez
> de confiar en el 33 de la Fase 0.

---

## Archivos

**Nuevos**
- `backend/Logistica/Controllers/MiJornadaController.cs`
- `backend/Logistica/Migrations/*_AgregarRetiroDeRuta.cs`
- `backend/Logistica/Migrations/*_AgregarFotoDocumento.cs`
- `frontend/components/FirmaCanvas.tsx`
- `frontend/app/hoy/retiro/page.tsx`
- `frontend/app/hoy/cierre/page.tsx`

**Modificados — backend**
- `Entidades/Ruta.cs` (8 columnas), `Entidades/PruebaEntrega.cs` (3 columnas)
- `Datos/Configuraciones/{Ruta,PruebaEntrega}Configuration.cs`
- `Servicios/AlmacenamientoFotos.cs` (carpeta y sufijo parametrizables)
- `Controllers/MisParadasController.cs` (gate del retiro, segunda foto, `SiguienteParadaId`, tres
  campos en el bundle)
- `Controllers/PruebasEntregaController.cs` (`/foto-documento`, purga, vencidos)
- `Controllers/RutasController.cs` — `RutaDetalle` + 11 campos declarados, `CerrarRutaRequest` +
  `SinDeclaracionDelRepartidor`, las cinco medidas M1-M5 de la tarea 4.3, `cerrada_por`
- `Controllers/JornadaController.cs` (retiro y cierre por ruta)
- `Opciones/OpcionesPruebaEntrega.cs`, `appsettings.json`

**Modificados — frontend**
- `lib/dominio/tipos.ts` (espejos), `app/hoy/page.tsx`, `app/hoy/parada/[paradaId]/page.tsx`
- `components/PedidoDetalleContenido.tsx`, `app/jornada/page.tsx`, `app/rutas/[id]/cierre/page.tsx`

**Modificados — docs**
- `docs/acta_sistema.md` (4.7), `docs/construccion_v1.md` (1.21), `docs/estado_implementacion.md`

**Sin cambios:** `Dominio/TransicionesPedido.cs`, la máquina de estados del pedido, la máquina de
estados de la ruta, la vista `v_paradas_repartidor`, `Servicios/JornadaService.cs`,
`docs/schema_v3.sql`. **Cero tablas nuevas; un trigger nuevo** (`trg_congelar_declaracion_repartidor`,
en la migración de la fase 1).

## Verificación end-to-end

No hay test runner en el repo — la verificación es manual, corriendo la app. Mismo procedimiento
que el módulo anterior:

1. `cd backend/Logistica && dotnet run --launch-profile http` → `:5190`.
2. `cd frontend && pnpm run dev -p 3000` — **forzar el 3000**: `Frontend:Origin` está fijo ahí y
   desde 3001 el login falla por CORS.
3. Backend con `curl` **antes** del navegador, en este orden: retiro sin firma (`400`) → llegada sin
   retiro (`409`) → retiro completo (`200`) → retiro repetido (`200 duplicado`) → cierre de parada
   con identidad verificada sin foto (`400`) → con las dos fotos (`200`, `siguienteParadaId`) →
   cierre de jornada con paradas pendientes (`409`) → cierre de jornada completo (`200`) →
   **`psql` con un `update` a mano sobre `cierre_repartidor_km_final` (excepción del trigger)** →
   `Cerrar` sin declaración (`409`) y con el flag (`200`) → `Cerrar` cambiando el combustible sin
   notas (`400`) y con notas (`200`) → `Cerrar` con los mismos valores (`200`, aprobado tal cual)
   → purga con retención 0.
4. En el navegador: repartidor del seed (`DatosSemilla.cs` trae **uno**, con una ruta `en_curso`),
   jornada completa de punta a punta en un teléfono real o en el modo dispositivo del navegador —
   RNF-06 se verifica con un dedo y sin scroll, no leyendo el CSS.
5. `dotnet build` (0 errores, 0 advertencias), `tsc --noEmit`, `eslint`, y **`next build`**, que
   quedó sin correr en el módulo anterior por el disco.

> El plan anterior advertía que el disco `E:` se desconectaba del bus SATA y hacía fallar
> `next dev` con exit `3221225478`. **El repo ahora está en `D:`** — si `next build` vuelve a
> fallar con `UNKNOWN: unknown error` o `TS6053` sobre archivos que existen, es el mismo síntoma en
> otro disco y no el código.

## Lo que queda afuera, explícito

- **La cola offline va última: es el plan siguiente, no una fase de este.** Dexie, service worker,
  `manifest.json`, compresión en cola, indicador de pendientes, reintento con backoff y la prueba
  de modo avión. Se arranca **después del Checkpoint 4**, con la superficie de escritura ya
  cerrada, y envuelve los **cinco** caminos de una sola vez: llegada, cierre de parada, retiro,
  cierre de jornada y las dos fotos. **Con esto afuera, H2/E5 sigue abierto** y el dato de la
  calle sigue dependiendo de que haya señal. Es el 60% del riesgo técnico según §7 y el criterio
  de aceptación 3 del acta.
- `manifest.json` e íconos de instalación. Van con la cola: sin service worker, instalar la PWA
  promete algo que no cumple. (`app/layout.tsx` sigue con el `metadata` de `create-next-app`,
  título "Create Next App" — se arregla ahí.)
- **Liquidación al repartidor (B4/E2)**: `pago_repartidor` y `otros_costos` siguen solo del lado
  admin. Ningún importe de la empresa cruza a la PWA.
- **Reprogramar desde la calle**: `fallido → reprogramado` sigue siendo back-office. El repartidor
  informa el motivo; la decisión de reprogramar tiene consecuencia económica (3 gratis, después
  pedido nuevo) y no se toma en la vereda.
- **Urgencias insertadas en una ruta en curso** (acta §7, ventana de las 13:00): sigue sin código,
  como ya estaba verificado en el changelog 4.5.
- **Tablero de indicadores (B2/E3)** y cualquier serie histórica de la jornada del repartidor.
