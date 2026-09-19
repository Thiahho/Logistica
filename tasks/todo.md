# Ciclo completo del repartidor — lista de tareas

Plan en [plan.md](plan.md). Absorbe el plan de la jornada del repartidor (acta 4.7) y suma las
novedades de la calle (acta 4.8, RF-36/RF-37). **No cierra H2/E5**: la cola offline es el plan
siguiente y último.

Estado al **18/09/2026**. `[x]` = escrito **y** verificado como se indica. Las pantallas se verificaron
con `tsc`, `eslint` y compilación bajo `next dev`, **no en un navegador real** (no había herramienta
de navegador): RNF-06 y el trazo del canvas de firma están sin probar en un teléfono.

## Fase 0 — Gobernanza de 4.7 (hecha el 17/09/2026)

- [x] Acta 4.7, construcción 1.21, `estado_implementacion.md` (sin adelantar conteos)

## F1 — Retiro firmado (RF-35)

- [x] Migración `AgregarRetiroDeRuta` + `trg_congelar_declaracion_repartidor` (commit `c60d1c6`)
- [x] `MiJornadaController.Retiro` (commit `c60d1c6`)
- [x] Gate `409` en `/llegada` y `/cierre` — verificado con `curl` (`/cierre`: después de la idempotencia)
- [x] `FirmaCanvas` (fondo blanco antes de dibujar), `/hoy/retiro`, tarjeta bloqueante en `/hoy`
- [x] `curl`: sin firma 400 · discrepancia sin observación 400 · completo 200 · repetido `duplicado` · otro uuid 409

## F2 — Siguiente parada

- [x] `CierreResultado.SiguienteParadaId` (también en `duplicado`), `router.replace`, "Cerrar la jornada"
- [x] `curl`: entrega devuelve la siguiente por orden; el reintento devuelve la misma; la última devuelve `null`

## F3 — Cierre de jornada en dos actores (RF-26)

- [x] `POST /api/mi-jornada/cierre` — `curl`: pendientes 409 · km final < inicial 400 · ok 200 · repetido `duplicado` · otro uuid 409
- [x] `/hoy/cierre`; `/hoy` "jornada cerrada / pendiente de revisión"
- [x] Administración: `RutaDetalle` con la declaración, `SinDeclaracionDelRepartidor` (M2), `NotasCierre` obligatorio si difiere (M3), `cerrada_por` (M4), prellenado y columna "Declarado" con delta (M1/M5), `ResultadoRuta.Aprobacion`
- [x] `curl`: sin declaración 409 · corregir sin notas 400 con el delta · aprobar tal cual 200 `tal_cual`
- [x] **Trigger:** `update` a mano sobre `cierre_repartidor_km_final` y `retiro_bultos_contados` → excepción
- [x] `JornadaController.Resumen` + `/jornada`: retiro, declaraciones pendientes, discrepancia de bultos

## F4 — Novedades, backend (acta 4.8)

- [x] Migración `AgregarNovedades`: tabla, checks, `trg_novedades_inmutable`, `ruta_paradas.estado` admite `cancelada`, vista con `pedido_estado`
- [x] `MiJornadaController`: `POST /novedades` (idempotente, whitelist, 404 por ajeno) y `POST /novedades/{id}/visto`
- [x] `NovedadesController`: listar, por ruta, foto, resolver (aceptar aplica el cambio; 409 si el dato cambió)
- [x] `PUT /api/pedidos/{id}/contacto`; cancelar en ruta deja aviso y pasa la parada a `cancelada`
- [x] `MisParadasController.Cerrar` saltea pedidos cancelados
- [x] `POST /api/rutas/{id}/interrumpir` — y la ruta interrumpida se cierra después (repartidor y administración)
- [x] `curl`/SQL: duplicado, categoría inválida, campo no editable, pedido de otra ruta, foto, doble resolución, trigger de novedades

## F5 — PWA del repartidor

- [x] `/hoy` con `useSondeo`, avisos con acuse, "Reportar un problema"
- [x] `/hoy/problema`, "Corregir un dato" en la parada, pedido cancelado tachado, parada `cancelada`
- [ ] **Sin verificar en navegador real / teléfono** (un dedo, sin scroll, canvas de firma)

## F6 — Back-office responde

- [x] `PanelNovedades` en `/rutas/[id]` (aceptar/rechazar/resolver, foto, "Interrumpir ruta")
- [x] Contador en `/jornada` y en la navegación; contacto y novedades en el detalle de pedido
- [ ] **Sin verificar en navegador real**

## F7 — Foto del documento con retención (RF-23 reescrito) — **NO HECHA**

Diferida a propósito: la consulta legal de acta §11.2 la bloquea y no afecta a F1–F6.
- [ ] Migración `AgregarFotoDocumento`, segunda foto en `Cerrar` (3 MB), `OpcionesPruebaEntrega.RetencionDocumentoDias`
- [ ] Purga idempotente + contador de vencidas, `foto-documento` solo `Administracion`
- [ ] UI en la parada, en el detalle de pedido y aviso en `/jornada`

## F8 — Gobernanza y cierre

- [x] Acta 4.8 (RF-36, RF-37, 18 tablas, changelog), construcción 1.22, `estado_implementacion.md` recontado (34 pantallas, 21 entidades, 107 endpoints, 14 migraciones)
- [x] `dotnet build` 0 errores · `tsc --noEmit` limpio · `eslint app components lib` limpio
- [x] `next build` (pasa)
- [ ] Jornada completa en un teléfono real

## Después: la cola offline

Plan aparte. Envuelve **siete** caminos de escritura de una vez: llegada, cierre de parada, retiro,
cierre de jornada, novedad, acuse y (con F7) la segunda foto. Hasta que pase la prueba de modo avión,
**H2/E5 sigue abierto**.

## Datos de prueba que quedaron en la base de desarrollo

8 pedidos marcados `TEST-CICLO` (ids 45–52, estados terminales). No se pudieron borrar: `pedido_eventos`
es de solo inserción. Las rutas 25 y 26, sus paradas, novedades, pruebas de entrega e ítems de factura
de prueba sí se borraron.
