# Jornada del repartidor — lista de tareas

Plan completo en [plan.md](plan.md). RF-23 (reescrito), RF-35 (nuevo), RF-26 (dos actores),
acta changelog 4.7. **Esta tanda no cierra H2/E5** — la cola offline va en un plan aparte y es lo
último, después del Checkpoint 4.

Lo que carga el repartidor queda como **dato pendiente e inmutable**; administración compara y
aprueba o corrige con motivo escrito. Dos juegos de columnas, un trigger que protege el primero.

## Fase 0 — Gobernanza (bloqueante)

- [x] **0.1** `docs/acta_sistema.md` §5.3 — RF-23 reescrito + párrafo "Sobre RF-23" que declare la
      **inversión** de la redacción anterior ("sin almacenar imagen del documento") y sus controles
- [x] **0.2** `docs/acta_sistema.md` §5.3 — RF-35 nuevo (retiro con conteo firmado), después de RF-25
- [x] **0.3** `docs/acta_sistema.md` §5.4 — nota bajo RF-26: dos actores y **dos actos** (declarar
      pendiente / aprobar o corregir), la declaración es inmutable
- [x] **0.3b** `docs/acta_sistema.md` §7 — regla nueva: **"el número de la calle no se reescribe"**
- [x] **0.4** `docs/acta_sistema.md` §4.1 — columnas nuevas de `rutas` y `pruebas_entrega`, el
      trigger `trg_congelar_declaracion_repartidor`, "sigue en 17"
- [x] **0.5** `docs/acta_sistema.md` §12 — fila de riesgo por la imagen del documento
- [x] **0.6** `docs/acta_sistema.md` §11.2 — la consulta legal pasa de "agendada" a bloqueante
- [x] **0.7** `docs/acta_sistema.md` §14 — fila de changelog 4.7
- [x] **0.8** `docs/construccion_v1.md` §4.2 — **reescribir el encabezado** ("sin construir" es falso);
      filas `/hoy/retiro` → RF-35 y `/hoy/cierre` → RF-26
- [x] **0.9** `docs/construccion_v1.md` §5 — declarar que la máquina de estados **no cambia** (ni
      la del pedido ni la de la ruta: cerrar la ruta *es* aprobar); §3 — nombrar el trigger nuevo
      como caso de la regla 3.3, sin duplicar la validación en C#
- [x] **0.10** `docs/construccion_v1.md` §7 — anotar las tres escrituras que nacen fuera de la cola
- [x] **0.11** `docs/construccion_v1.md` §9 — `PruebaEntrega:RetencionDocumentoDias`
- [x] **0.12** `docs/construccion_v1.md` §10 — retención real, quién ve la imagen, cron del purgado
- [x] **0.13** `docs/construccion_v1.md` §12 — fila de changelog 1.21
- [x] **0.14** `docs/estado_implementacion.md` §3.6 y §3.7 — endpoints nuevos, gate, cierre en dos actores
- [x] **0.15** `docs/estado_implementacion.md` §4 y §6 — **desvío aplicado:** el conteo **queda en
      31**, no pasa a 33, y las columnas nuevas no se listan en §5. Este documento cuenta lo que
      existe; mover el conteo en la fase de gobernanza lo haría afirmar que existen dos pantallas
      que nadie escribió. Las dos aparecen tachadas y marcadas "diseñadas, no construidas", sin
      sumar, y el encabezado gana la regla explícita. Los conteos se mueven en el Checkpoint 4.
      Razonamiento completo en [plan.md](plan.md), Fase 0

> **Checkpoint 0 — hecho (17/09/2026).** Acta **4.7** (RF-23 reescrito con su párrafo de inversión,
> RF-35, nota de RF-26, regla de §7, columnas y trigger en §4.1, riesgo en §12, §11.2 bloqueante,
> fila 4.7). Construcción **1.21** (§4.2 reescrita —el "sin construir" era falso—, §3 con el
> trigger, §5 declarando que la máquina de estados no cambia, §7 con la deuda de los cinco caminos,
> §9 con la retención, §10 con el cron y el respaldo, fila 1.21). `estado_implementacion.md` §3.6,
> §3.7, §4, §6 y encabezado, **sin mover ningún conteo**. Los tres citan los mismos RF, el mismo
> plazo provisional de 30 días y la misma frase sobre H2/E5 sin cerrar.

## Fase 1 — Retiro con conteo firmado (RF-35)

- [ ] **1.1** Migración `AgregarRetiroDeRuta` — 15 columnas en `rutas` (6 de retiro + 6 de cierre
      declarado + `retiro_km_inicial` + `cierre_repartidor_device_uuid` + `cerrada_por`), todas
      nullable, sin índices. **`docs/schema_v3.sql` no se toca** (quedó como snapshot de H0)
- [ ] **1.1b** Misma migración: `trg_congelar_declaracion_repartidor` con `is distinct from`
      (con `<>` y nulls la condición no dispara nunca) y mensaje propio, no `fn_log_inmutable`
- [ ] **1.2** `Entidades/Ruta.cs` + `RutaConfiguration.cs`
- [ ] **1.3** `Servicios/AlmacenamientoFotos.cs` — carpeta y sufijo parametrizables, firma actual
      como default; los tres invariantes intactos (path traversal, KB, magic bytes)
- [ ] **1.4** `Controllers/MiJornadaController.cs` (nuevo) — `POST /api/mi-jornada/retiro`, policy
      `Repartidor` de clase, docstring con el límite B4/E2, 7 reglas del plan. El `KmInicial` del
      request va a **`retiro_km_inicial`**, nunca a `rutas.km_inicial`
- [ ] **1.5** Verificar con `curl` antes de tocar el frontend (400 sin firma, 400 sin observación,
      200, 200 duplicado, 409 otro dispositivo)
- [ ] **1.6** `MisParadasController` — gate 409 en `/llegada` y `/cierre`, **después** del chequeo
      de idempotencia
- [ ] **1.7** `MisParadasController.JornadaDelDia` — `RetiroConfirmadoEn`, `BultosEsperados`,
      `CierreRepartidorEn` + espejos en `lib/dominio/tipos.ts`
- [ ] **1.8** `components/FirmaCanvas.tsx` (nuevo) — **rellenar el canvas de blanco antes de
      dibujar**, o el JPEG sale negro
- [ ] **1.9** `app/hoy/retiro/page.tsx` (nuevo) — 48 px, sin scroll (RNF-06)
- [ ] **1.10** `app/hoy/page.tsx` — tarjeta bloqueante de retiro pendiente; el mapa se sigue viendo

> **Checkpoint 1:** la regla del acta §7 codificada, demostrable con `curl`.

## Fase 2 — Entrega con foto del documento y retención (RF-23)

- [ ] **2.1** Migración `AgregarFotoDocumento` — `documento_numero`, `foto_documento_path`,
      `foto_documento_borrada_en`; `identidad_verificada` se conserva
- [ ] **2.2** `MisParadasController.Cerrar` — `RequestSizeLimit` 2 MB → 3 MB, segunda foto con
      sufijo `-documento`, `identidadVerificada == true` exige la foto
- [ ] **2.3** `Opciones/OpcionesPruebaEntrega.cs` + `appsettings.json` — `RetencionDocumentoDias: 30`,
      marcado PROVISIONAL en el docstring que ya tiene ese párrafo
- [ ] **2.4** `PruebasEntregaController` — `GET /{id}/foto-documento` (policy `Administracion`),
      `POST /purga-documentos`, `GET /documentos-vencidos`; un archivo que no se borró **no** se
      marca como borrado
- [ ] **2.5** Verificar la purga con `RetencionDocumentoDias = 0` y volver a correrla (0 purgadas)
- [ ] **2.6** `app/hoy/parada/[paradaId]/page.tsx` — número + foto del documento al marcar identidad
- [ ] **2.7** `components/PedidoDetalleContenido.tsx` — "Ver imagen" bajo click, solo admin, y la
      fecha de borrado si ya se purgó
- [ ] **2.8** `app/jornada/page.tsx` — aviso de documentos vencidos + botón Purgar (solo admin)

> **Checkpoint 2:** la fase no cierra sin haber corrido la purga de verdad. Sin eso, RNF-09 queda
> en el aire.

## Fase 3 — Siguiente parada

- [ ] **3.1** `CierreResultado` + `SiguienteParadaId`, devuelto **también** en los dos caminos de
      `duplicado: true`
- [ ] **3.2** `app/hoy/parada/[paradaId]/page.tsx` — `router.replace` a la siguiente, o a `/hoy`
- [ ] **3.3** `app/hoy/page.tsx` — sin pendientes, "Cerrar la jornada"
- [ ] **3.4** Verificar reordenando las pendientes desde el back-office mientras el formulario está
      abierto: la siguiente tiene que ser la del orden nuevo

> **Checkpoint 3:** retirar → entregar → siguiente, hasta que no queda ninguna.

## Fase 4 — Declaración de la calle y aprobación de administración (RF-26, dos actores)

- [ ] **4.1** `POST /api/mi-jornada/cierre` — 7 reglas del plan. Escribe **solo** las
      `cierre_repartidor_*`; no toca `Estado`, `km_final`, `combustible_monto`, `peajes_monto`,
      `otros_costos` ni `pago_repartidor`. `KmFinal` se valida contra `retiro_km_inicial`
- [ ] **4.2** `app/hoy/cierre/page.tsx` (nuevo) — km final contra el declarado en el retiro,
      combustible, peajes, notas, resumen. Después, `/hoy` dice **"pendiente de revisión"**
- [ ] **4.3** `RutaDetalle` + 11 campos declarados + `CerradaPor` (aditivo)
- [ ] **4.4** **M1** · `app/rutas/[id]/cierre/page.tsx` — prellenar los 4 campos económicos
      **desde la declaración** en vez de `""`; `otros_costos` y `pago_repartidor` siguen vacíos
- [ ] **4.5** **M2** · `CerrarRutaRequest` + `SinDeclaracionDelRepartidor`; sin declaración y sin
      el flag → `409`
- [ ] **4.6** **M3** · si algún valor difiere del declarado, `NotasCierre` obligatorio → `400`
      nombrando el campo y el delta. Mandar los mismos valores no pide nada
- [ ] **4.7** **M4** · `Cerrar` sella `cerrada_por = User.UsuarioId()` junto a `CerradaEn`
- [ ] **4.8** **M5** · columna "Declarado por el repartidor" al lado de los inputs con el delta y
      "Cargado desde la calle a las HH:MM"; `ResultadoRuta` marca **aprobado tal cual** /
      **corregido**, derivado en lectura, sin columna de estado
- [ ] **4.9** `JornadaController.Resumen` + `/jornada` — retiro y cierre por repartidor, aviso de
      declaraciones pendientes de revisión, discrepancia de bultos marcada
- [ ] **4.10** **Test que sostiene el diseño:** `update rutas set cierre_repartidor_km_final = …`
      a mano en `psql` → excepción del trigger traducida a `409`. Ídem sobre cualquier campo del
      retiro
- [ ] **4.11** Test del bug original: repartidor declara → admin cierra sin tocar nada → los
      valores declarados **siguen en la fila** y el cierre queda marcado aprobado tal cual

> **Checkpoint 4 (final):** releer los tres documentos de la Fase 0 contra el código final y
> **corregirlos**, no solo agregarles cosas. Recontar las pantallas de §4 en vez de confiar en el 33.

---

## Después, y recién después: la cola offline

Plan aparte. Arranca con el Checkpoint 4 cerrado y envuelve los **cinco** caminos de escritura de
una sola vez (llegada, cierre de parada, retiro, cierre de jornada, las dos fotos). Hasta que pase
la prueba de modo avión, **H2/E5 sigue abierto**.

---

## Cierre de la tanda

- [ ] `dotnet build` — 0 errores, 0 advertencias
- [ ] `tsc --noEmit` y `eslint` sobre todo lo nuevo y modificado
- [ ] **`next build`** — quedó sin correr en el módulo anterior por el disco; ahora el repo está en `D:`
- [ ] Jornada completa en un teléfono real: un dedo, sin scroll (RNF-06)
- [ ] Volcar al changelog 1.21 **qué quedó sin verificar**, si algo quedó
