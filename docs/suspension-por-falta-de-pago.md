# suspension-por-falta-de-pago

## Objective

When a company is deactivated for non-payment, its service is really cut: its orders and trips
leave their routes and stop being pending for delivery, stay held, and the portal shows them as
"Suspendido por falta de pago". Reactivating the company lets the operation resume them.

## Problem and why

Mapped by a read-only explorer on 2026-10-12 (branch `demo-d`, HEAD `b7d2956`).

- `Cliente.Activo` is written only at `ClientesController.cs:335` and `:353`, with plain
  `SaveChangesAsync` and no side effects, in either direction.
- Deactivation only blocks new orders. No routing pool, planning close, urgency or courier screen
  looks at the company; they filter by order state only.
- `EstadoPedido` (`Entidades/EstadoPedido.cs:3-13`) has no held state. It is a Postgres enum type
  `estado_pedido`; no migration since `Inicial` has altered it. Transitions live in
  `Dominio/TransicionesPedido.cs:32-39`.
- `Devuelto` is reachable only from `Fallido` and creates a priced `tipo='retorno'` order.
- The only path that detaches an order from a route is `RutasController.GuardarParadas`
  (`planificada` only). Cancelling on a route `en_curso` cancels the stop and sends a `Novedad` to
  the driver (`PedidosController.cs:988-1020`), and bills 100%.
- Stop close and the courier PWA special-case only `Cancelado`: any other non-`EnRuta` order on a
  stop makes the close answer 409 (`MisParadasController.cs:276`, `:331-332`).
- There is no per-order custody, so the system cannot decide "held" versus "returned" by itself.
- The portal has no field for the company's active flag; its banner says confirmed orders "siguen
  su curso" (`frontend/app/mis-envios/page.tsx:89-93`), the opposite of this decision.
- Naming: `CorteSuspendidoHasta` means the debt cut is suspended (service allowed) and
  `ServicioCortado` is debt-computed. Neither is this feature. The state is `Retenido`.

## User decisions (2026-10-12)

- "No debe poder dar de baja nada": a deactivated company cannot cancel orders or trips (already so).
- "A los pedidos o viajes pendientes debe indicar que suspendido por falta de pago".
- "Los pedidos quedan retenidos o en su caso devueltos. Pero ya no deben permanecer en la ruta o
  pendientes para entregar."
- The six-point design below was presented and answered with "avanza".

## Design (approved)

1. New state `Retenido`; the portal labels it "Suspendido por falta de pago".
2. On deactivation, every order of the company in `Borrador`, `Confirmado`, `EnRuta` or
   `Reprogramado` becomes `Retenido` and leaves its route. On a route `en_curso` the driver gets
   the notice, as with a cancellation. Deliveries (`tipo='delivery'`) get the same treatment.
3. No charge: holding bills nothing and does not consume a free reprogramming.
4. `Devuelto` is decided by the operation: back-office may mark a `Retenido` order as returned.
5. After reactivation the held orders can be routed again with their frozen price and a new date
   set by the operation.
6. Pending trips: their orders are held and the proposed route is removed.

`Entregado`, `Fallido`, `Devuelto` and `Cancelado` orders are not touched.

Parent's reading of point 5, to report to the user: resuming is an explicit back-office action
per order, allowed only while the company is active. An order that had a frozen price resumes
through `Reprogramado` with a new date; one that was never confirmed returns to `Borrador`.
Nothing resumes automatically on reactivation. A bulk "resume all" is not in scope.

## Scope

Backend: state, migration, transitions, hold on deactivation, resume and return, tests.
Frontend: labels in every exhaustive state map, portal notice and banner, back-office actions,
courier PWA treating `Retenido` like `Cancelado`.
Out of scope: holding on debt-computed `ServicioCortado`; motive or audit on deactivation; bulk
resume; per-order custody.

## Constraints

- A migration IS required (new value in `estado_pedido`); report it explicitly. It must be applied
  in each environment, like `20261012201258_ConcurrenciaOptimistaDePedidos`.
- State changes go through `GuardarComoAsync` where the surrounding code does so; every state
  change must be logged by `fn_log_estado_pedido` as today.
- A confirmed order keeps its frozen price through hold and resume (`fn_congelar_pedido`).
- `ux_factura_items_pedido` stays; holding mints no `factura_items`.
- Controller constructors are called positionally by test helpers.
- The frontend has no test runner: lint, typecheck (`pnpm typecheck`) and build.
- `frontend/AGENTS.md`: read `node_modules/next/dist/docs/` before Next-specific code.

## Tasks

- [x] **S1** (`f19a80f`) State `Retenido` (enum, migration, transitions, SQL that enumerates states) and hold
  on deactivation: orders leave their routes, stops and driver notice on `en_curso`, trips lose
  their proposed route, no charge. Route: delegated.
- [x] **S2** (`db238e6`) Resume and return from `Retenido`: resume only while the company is active, frozen
  price kept, no free reprogramming consumed; back-office return. Route: delegated.
- [x] **S3** (`564b8b8`) Frontend: state labels everywhere, portal "Suspendido por falta de pago" and banner,
  company active flag in the portal account response, back-office resume and return actions,
  courier PWA. Route: delegated.

Audit fixes, accepted by the user on 2026-10-13 (the four medium findings A1 to A4):

- [x] **S4** (`d7f3faf`, A1) One-off hold for companies already inactive before the release:
  `POST /api/clientes/inactivas/retener-pendientes`, administration policy, no UI button,
  idempotent, one transaction per company. RED on assertions against a stub, then GREEN.
  Route: delegated.
- [x] **S5** (`41becac`, A4) Expected parcels at pickup exclude `Cancelado` and `Retenido`, in
  both `MisParadasController.Dia` and `MiJornadaController.Retiro` (the writer stopped because
  the second file was outside its surface). RED: `Retiro` answered 400 for a correct count; GREEN
  after the fix. Route: inline (two one-line filters and one test).
- [x] **S6** (`1122283`, A3) Back-office route detail marks held and cancelled orders and leaves
  them out of the counts and of the phone choice. Lint, typecheck, build exit 0; not exercised in
  a browser. Route: delegated.
- [x] **S7** (`1d2f36c`, comments `cb7f751`; A2 and A7) User decision (2026-10-13): the old trip route must NOT come back on resume
  ("ya es vieja, desorganizaría el manejo de los pedidos"); no route is rebuilt and no button is
  added. Verified: resumed trip orders are routed like any other (`RutasController.cs:430` has no
  trip filter). Fix the read side only: a trip without its own route derives its visible state
  from its orders (all held: `retenido`; some on a route `en_curso`: `en_curso`; some on a
  `planificada` route: `planificado`; all resolved: `cerrado`; otherwise `sin_ruta`). Accepted
  trade-off: such a trip has no route order of its own. Also covers A7 and the open point that
  trip lists cannot tell a held trip. Route: delegated.

Second round, asked by the user on 2026-10-13 ("corrijamos esos comportamientos, y si es
necesario sigamos con lo del documento"). Parent's defaults where the user gave no detail, to
report: lists show a held count instead of changing the trip state; in the portal a held order or
trip reads "Pendiente de reanudación" once the company is active again and "Suspendido por falta
de pago" only while it is inactive.

- [x] **S8** (`6051c40`) Trips: held count in trip lists for a partly resumed trip; portal wording of held
  orders and trips after reactivation; cancelling a route-less trip must not leave its orders on
  ordinary `planificada` routes (prove with a test first). Route: delegated.
- [x] **S9** (`9ceca93`) Resume: date must be today or later when resuming (A5); "Reanudar"/"Reprogramar"
  disabled while the company is inactive, with the flag in the order detail (A9); the portal
  timeline does not print the internal hold motive (A8). Route: delegated.
- [x] **S10** (`0106f18`) Portal owner falls back to `cuenta.empresaActiva` when `servicio` fails (A6);
  dashboard denominator excludes held orders (A10); `docs/schema_v3.sql` lists nine states (A12).
  Route: delegated.
- [x] **S11** (`9671893`; originally committed as `5abd135`, recreated with an identical tree after
  an accidental reset on 2026-10-13, so `5abd135` is no longer on the branch) Bulk hold hardening and test gaps from the last review: re-check `Activo` inside
  each company's transaction; the answer reports what was held even when a later company fails;
  non-vacuous back-office list assertion; test for the `Dia` parcel filter; test pinning the
  `Administracion` policy. Route: delegated.

Not taken: the other races of A11 (plausible, no cheap guard), unit tests for frontend helpers
(no test runner).

Route evidence: each task reads across controllers, services, migrations and tests before
writing (preparation trigger) and touches 2+ non-trivial files (writer trigger).

## Acceptance criteria

- Each backend behavior has a test that fails before it and passes after it, RED observed.
- `dotnet test backend/Logistica.Tests` passes with Docker running, or every failure is reported.
  Baseline: 366 passed on `b7d2956`.
- After deactivation no order of the company is `Borrador`, `Confirmado`, `EnRuta` or
  `Reprogramado`, none is attached to a live stop, and planning close, stop close and route close
  of the affected routes still work for the other companies' orders.
- Frontend: `pnpm lint`, `pnpm typecheck`, `pnpm build` pass.

## Delivery

Strategy: `ask-on-risk`. Forecast: about 800 authored changed lines (S1 350, S2 200, S3 250),
over the 400 budget. The branch `demo-d` is already over budget and its chain strategy
(`stacked-to-main` or `feature-branch-chain`) is still to be asked before any pull request.
Nothing is pushed.

Review: receipt-driven development is on (global). Reviewed boundary: `b7d2956`.

## Progress

2026-10-12. Document created; S1 and S2 writer launched.

2026-10-13 (resume). Document now lives at `docs/suspension-por-falta-de-pago.md` (untracked).
S1 is committed as `f19a80f` and S2 as `db238e6`; both boxes stay unchecked until the full
backend suite is observed in this session (run in progress). Migration added:
`20261013025219_AgregarEstadoRetenido`, to be applied in each environment as a deploy step.
Running count from `b7d2956`: 1279 insertions and 22 deletions excluding the generated
migration designer, over the 400 budget.

Verification of S1 and S2: `dotnet test backend/Logistica.Tests` on `db238e6` gave 425 passed,
0 failed, 0 skipped (baseline 366 on `b7d2956`). RED per behavior was the writer's to observe and
was not re-observed in this session.

Review of `b7d2956..db238e6` (committed only, untracked excluded): assessed `medium`,
`review_due` by `slice_budget_reached`; lineage `review-8371b8a64bccc3ea`; consent granted by the
user. START refused in preflight with `lens_context_budget_exceeded` (22 files, 4451 lines, of
which 3150 are the generated migration designer): no review authority was created. Outcome:
unavailable for this range; reviewing it needs smaller candidates (decision pending with the
user). Reviewed boundary stays `b7d2956`.

User decision (2026-10-13): review S2 now; review S1 at the end, when it can be done.

Review of S2 alone, `f19a80f..db238e6` (10 files, 519 lines, medium, one lens
`review-reliability`), lineage `review-74cbf18932397e0c`: consent granted, approved and
acknowledged (authority burned). Four non-blocking suggestions, kept as follow-ups, not accepted
into scope:

- `PedidosController.cs:720-722`: the detail derives `ReanudaEn` from the in-memory event list
  while `CambiarEstado` uses the event with the highest id; no test retains the same order twice.
- `PedidosController.cs:880-881`: the fallback for a `Retenido` with no retention event has no
  test.
- `PedidosController.cs:889-890`: resuming to `Borrador` accepts or keeps a past delivery date
  without validation.
- `RetencionPedidosService.cs:172`: `SoltarParadasPendientesAsync` only covers routes
  `en_curso`; a pending stop on a route not yet started is unproved.

S1 review pending: `b7d2956..f19a80f` needs a candidate under the reviewer budget (the generated
designer is 3150 lines) and a worktree at `f19a80f`, or another way to select that range.

S3 committed as `564b8b8` (14 files, 224 insertions, 38 deletions). Writer's verification of
record: `dotnet test backend/Logistica.Tests` 429 passed, 0 failed; `pnpm lint`, `pnpm typecheck`
and `pnpm build` exit 0. RED was compile-time (the members did not exist), GREEN on the focused
filter. Parent spot check: `pnpm typecheck` exit 0. The UI was not exercised in a browser.
Review assessment of `db238e6..564b8b8`: medium, `under_budget` (262 lines), not due; it stays
pending in the slice. Reviewed boundary: `db238e6` for S2 only; S1 still unreviewed.

S1 review attempted at the end, as the user asked: temporary detached worktree at `f19a80f`,
range `b7d2956..f19a80f` (16 files, 3934 lines, medium), lineage `review-973d31fafd6efe58`,
consent granted. START refused again with `lens_context_budget_exceeded`; no authority created;
worktree removed. S1 cannot be reviewed natively as one commit: the generated migration designer
alone is 3150 lines. Outcome: unavailable. Needs the user's decision.

User decision (2026-10-13): no history rewrite for the S1 review; cover the open stop case with a
test instead. Added
`El_retenido_de_una_ruta_en_planificacion_no_queda_en_ninguna_parada_al_reanudarlo_o_devolverlo`
(two cases) in `ReanudacionDeRetenidosTests.cs`: it passed on first run (22 passed on the
filter), so it is a characterization test with no RED. It closes the follow-up on
`RetencionPedidosService.cs:172`: on a `planificada` route the hold already removes the row
(`SacarDelArmadoAsync`), so nothing is left to release on resume. S1 stays without native review.

## Audit (2026-10-13, read-only, range `b7d2956..7bf3c52`)

Two read-only reviewers (backend S1+S2+test; frontend S3 + `MiCuentaController`). Full backend
suite on `7bf3c52`: 431 passed, 0 failed. No critical or high finding. Nothing fixed yet; the
findings are not accepted into scope until the user decides. Parent spot-checked A1, A2, A3, A4
and A7 in code.

Medium:

- **A1** (introduced) Companies already inactive at deploy are not held: `GuardarBajaAsync` has a
  single caller (`ClientesController.cs:366`), the migration does no backfill, and routing and
  delivery filter by order state only. Needs a one-off post-migration step.
- **A2** (introduced) A held trip never gets a route again: the hold nulls `viaje.RutaId`
  (`RetencionPedidosService.cs:86-88`) and the only non-null writer is trip creation
  (`ViajeService.cs:256`). After resume the trip stays `sin_ruta`.
- **A3** (gap, pre-existing for `Cancelado`) Back-office route detail lists and counts held
  orders as live (`frontend/app/rutas/[id]/page.tsx:163-164`, `:406-430`).
- **A4** (pre-existing for `Cancelado`) Expected parcels at pickup sum held and cancelled orders
  (`MisParadasController.cs:156-159`).

Low:

- **A5** Resuming to `Borrador` keeps or accepts a past date (`PedidosController.cs:889-890`); no
  endpoint edits the date afterwards; the date input has no minimum.
- **A6** Portal: no suspended notice when `/api/mi-cuenta/servicio` fails or loads
  (`mis-envios/page.tsx:245-250`); the owner could fall back to `cuenta.empresaActiva`.
- **A7** `DetalleViaje.variante` tests `!enRuta` before `Retenido`, so a held trip without route
  shows doubtful-address markers (`DetalleViaje.tsx:19-23`).
- **A8** The portal timeline prints the event motive "Retenido: la empresa fue dada de baja..."
  (`LineaTiempoEstados.tsx:144`), leaking the back-office word.
- **A9** "Reanudar" / "Reprogramar" are offered while the company is inactive; the backend
  answers 409 (no active flag in the order detail).
- **A10** Dashboard denominator counts an order held from `Borrador` (`TableroController.cs:55`).
- **A11** Races not covered by the `xmin` token (plausible): `GuardarParadas` racing a hold; two
  companies sharing a pending stop deactivated together; order creation racing a deactivation.
- **A12** `docs/schema_v3.sql:166-168` still lists eight states. Migration `Down` is empty on
  purpose (Postgres cannot drop an enum value).

Earlier S2 suggestions: `ReanudaEn` divergence refuted as a bug (event time order equals id
order across transactions); the no-event fallback is correct but reachable only for hand-loaded
rows and untested; the past date is A5.

Untested behaviours named by the audit: order retained twice, no-event fallback, hold racing
closes, rollback of `Activo` when the hold fails, re-rake on an inactive company, hold and resume
of a `tipo='delivery'` order, a trip after resume, a held order on a stop closed as failed or
interrupted.

Audit fixes S4 to S6 verified on `41becac`: `dotnet test backend/Logistica.Tests` 436 passed, 0
failed (parent run). Not yet assessed for native review.

Deploy steps to add (no runbook exists; the migration step is the checklist line at
`docs/construccion_v1.md:626`, not edited): after applying `20261013025219_AgregarEstadoRetenido`
and deploying the backend, once per environment an administration user calls
`POST /api/clientes/inactivas/retener-pendientes` and keeps the answer; a second call must answer
zero companies and zero orders.

Known limits of S4: a company reactivated between the listing and its turn would still be held
(narrow, not guarded); an inactive company whose trip has a proposed `planificada` route loses
that route even with no pending order and does not appear in the answer's detail.

S7 (A2) options, from the writer's read-only analysis; user decision pending:

1. Explicit back-office action "proponer ruta" on the trip, reusing the route building of
   `ViajeService.CrearAsync` (about 200-250 lines; `ViajeService.cs`, `ViajesController.cs`,
   `frontend/app/viajes/[id]`, types, tests). Recommended.
2. Re-propose the route automatically on resume in `PedidosController.CambiarEstado` (about
   150-200 lines); orders resume one by one with their own dates, so the route date is ambiguous.
3. Read side only: derive the trip state from the routes that carry its orders (about 80-120
   lines); breaks the one-company trip route assumption (`RutasController.cs:448`).

S7 result (`1d2f36c`): `ViajeService.EstadoSinRutaPropia` with precedence `en_curso` (order
`EnRuta` on a route `en_curso`), `planificado` (pending order on a `planificada` route),
`sin_ruta` (any pending order), `retenido` (no pending order, some held), `cerrado`; a trip with
no orders stays `sin_ruta` (writer's assumption). New trip state `retenido` in the frontend
(portal "Suspendido por falta de pago", back-office "Retenido"); the portal inference of `564b8b8`
removed; stop markers follow the order state when the trip has no own route. Lists run 3 queries.
RED on assertions against a stub (13 of 20 failed), then GREEN. `cb7f751` corrects two comments
that still said the route is rebuilt on resume. Verification on `cb7f751`: backend suite 456
passed, 0 failed (parent run on `1d2f36c`, build clean after the comment commit); `pnpm lint`,
`typecheck`, `build` exit 0 (writer). UI not exercised in a browser.

Open after S7, for the user:

- A partly resumed trip reads "Por planificar" in the lists while some orders are still held.
- A fully held trip keeps reading suspended after reactivation until an order is resumed.
- Not introduced here, read but not tested: cancelling a route-less trip whose `Borrador` orders
  sit on an ordinary `planificada` route appears to leave their `parada_pedidos` rows
  (`ViajeService.CancelarAsync`).

Review of `db238e6..cb7f751` (S3 to S7, 25 files, 1062 lines): medium, `review_due` by
`slice_budget_reached`; lineage `review-a78f263e06d76ef0`; consent granted; lens
`review-reliability`; approved and acknowledged. Reviewed boundary: `cb7f751` (S1 `f19a80f`
remains the only commit without native review). Non-blocking findings, kept as follow-ups, not
accepted into scope:

- Warning: `EstadoDeViajeSinRutaTests.cs:132-133`, the back-office list read falls back to the
  portal value when the trip is not in the first 200 rows, so that assertion can be vacuous.
- Warning: `RetencionPedidosService.cs:94-101`, if a company fails mid-pass the caller never
  learns which companies were already held; that failure path has no test.
- Warning: `mis-envios/page.tsx:92`, an owner of an inactive company with overdue debt sees
  neither notice when the `servicio` request fails (same root as A6).
- Suggestions: re-check `Activo` inside each company's transaction in the bulk pass; no test pins
  the `Administracion` policy on the bulk endpoint; the `MisParadasController.Dia` side of the
  parcel filter is untested; no unit tests for the frontend transition and label helpers.

## Second round (2026-10-13, S8 to S11)

Verification on `9671893` (observed on `5abd135`, the same tree before the commit was recreated as
`9671893`): `dotnet test backend/Logistica.Tests` 470 passed, 0 failed (writer and
parent run); `pnpm typecheck` exit 0 (writer and parent); `pnpm lint` and `pnpm build` exit 0
(writer). RED observed on assertions for every new backend behavior; five tests passed on first
run (characterization or refactor). UI not exercised in a browser.

- S8: `ViajeResumen.Retenidos` (lists stay at 3 queries). Portal says "Pendiente de reanudación"
  (amber) while the company is active and "Suspendido por falta de pago" (red) while inactive or
  unknown. The cancel bug was real: `ViajeService.CancelarAsync` left the `parada_pedidos` rows of
  a route-less trip on an ordinary `planificada` route; it now calls
  `RetencionPedidosService.SacarDeRutasEnPlanificacionAsync`. `ViajeService` gained a fourth
  constructor parameter.
- S9: resume rejects a past date (400); resume to `Borrador` without a date is rejected when the
  current date is past. `PedidoDetalle.EmpresaActiva`; every transition into a pending state is
  disabled in the back-office while the company is inactive. The portal timeline drops the hold
  motive. Resume tests are now clock-relative (`Reloj.HoyLocal()`).
- S10: owner fallback to `cuenta.empresaActiva`; the dashboard total excludes held orders never
  confirmed (`Retenido` with no total), per `docs/diseño_b2_tablero.md:29`; `schema_v3.sql`.
- S11: the bulk pass locks and re-checks each company (`select … for update`); on a failure it
  answers 500 `ProblemDetails` with extensions `retenidas` (held before the failure) and `fallo`
  (company where it stopped); parcel sum shared in `MiJornadaController.BultosPorRetirarAsync`;
  non-vacuous list assertion; policy pinned by reflection.

Review of `cb7f751..9671893` (assessed as `cb7f751..5abd135`, before `5abd135` was recreated as
`9671893`; 22 files, 959 lines, medium, due by budget): the user declined
this candidate; no review record. Under ordinary policy the medium tier is covered by the
writer's verification and the parent's spot checks above. Commits without native review: S1
`f19a80f` (reviewer budget) and S8 to S11 (declined).

Deploy step, updated: if `POST /api/clientes/inactivas/retener-pendientes` answers 500, its body
lists what was already held and the company that failed; fix the cause and call it again.

Open for the user after the second round: colours and copy chosen by the writer (amber/red,
"Pendiente de reanudación", the order note, the back-office explanation); cancelling a whole
route-less trip removes its orders from a planning route while the portal rejects cancelling a
single `Borrador` order already in planning (asymmetry); `PedidoDetalleCliente` and the portal
trip detail still depend on `/servicio` alone; every failure of the bulk pass answers 500.

Beyond the literal ask in S3: new `GET /api/mi-cuenta/servicio` (company active flag for any
portal login), because the account endpoint is owner-only and employees also see held orders.

Open after S3, for the user:

- Trip lists cannot tell a held trip: `ViajeResumen` carries no per-order state. The portal list
  infers it (company inactive and trip `sin_ruta`); after reactivation a trip with held orders
  shows "Por planificar"; the back-office `/viajes` list shows nothing. A fix needs a held count
  from `backend/Logistica/Servicios/ViajeService.cs`.
- Whether employees should see the suspended banner (they do now).
- Wording and colour to confirm: portal banner, courier "Retenido por operación: no entregar.",
  back-office chip "N paradas retenidas", red held badge.
