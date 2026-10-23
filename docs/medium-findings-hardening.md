# medium-findings-hardening

## Objective

Resolve the eight medium-severity findings of the review: surviving sessions, Next.js advisories,
price rounding, route close with pending stops, billing run isolation, adjustments without state
control, delivery capture without timeouts, and slow planning close.

## Problem and why

Verified against the code on 2026-10-12 (branch `demo-d`, base `c870b11`) by a read-only explorer;
M3 spot-checked by the parent (`PrecioService.cs:58-67`, no rounding).

- **M1 (partially confirmed)** `AuthService` only revokes one refresh token (`RevocarAsync`,
  `AuthService.cs:90-98`). Password resets in `UsuariosController.cs:111-122`,
  `ClientesController.cs:486-498` and `MiCuentaController.cs:720-732` leave the sessions alive.
  Login and refresh check `ClienteUsuario.Activo`, never `Cliente.Activo`. A role change already
  applies at the next refresh; the old role survives only in the 15 min access token.
- **M2 (version confirmed, advisories not checked)** `frontend/package.json` pins `next` and
  `eslint-config-next` at `16.3.6`; pnpm 11.1.2, `frontend/pnpm-lock.yaml`.
- **M3 (confirmed)** `PrecioService.CotizarAsync` sums raw decimal products; the columns are
  `numeric(12,2)`, so Postgres rounds each component and the total independently.
- **M4 (confirmed)** `RutasController.Cerrar` (`:767-839`) never looks at the stops. Orders on
  pending stops stay `EnRuta` on a closed route and `Interrumpir` needs `en_curso`, so they cannot
  be rescued. The doc comment at `:243-247` says interrupting is how a broken route is closed.
- **M5 (confirmed)** `CuentaCorrienteService.CerrarCiclosAsync` (`:169-259`) has no per-client
  catch. `CierreCiclosAutomatico` retries every tick, so one failing client blocks the rest forever.
- **M6 (confirmed)** `PedidosController.SolicitarAjuste` (`:1118-1138`) and `AprobarAjuste`
  (`:1145-1184`) never check the order state; the billing run takes any approved item.
- **M7 (confirmed)** `frontend/app/hoy/parada/[paradaId]/page.tsx` keeps photo and form in memory;
  `fetchConSesion` (`frontend/lib/auth/AuthProvider.tsx:148-158`) has no timeout. No service
  worker, manifest or IndexedDB exists.
- **M8 (confirmed)** `RutasController.PrepararCongelamientoAsync` (`:587-605`) runs one OSRM call
  and four queries per Borrador order, sequentially.

## Scope

Backend fixes with tests for M1, M3, M4, M5, M6, M8. Frontend changes for M2 and M7.
M7 is limited to request timeouts and a local draft of the capture (photo included): the full
offline queue was moved to future work on 24/09 (`docs/nuevo.md:123`).

## Constraints

- State changes go through `GuardarComoAsync`, never plain `SaveChangesAsync`.
- A confirmed order keeps its frozen price (P1).
- `ux_factura_items_pedido` stays: an order is invoiced once.
- No new migration unless the fix requires it; if one is added, report it explicitly.
- Controller constructors are called positionally by test helpers; a signature change must update them.
- The frontend has no test runner: frontend tasks are checked with lint, typecheck and build.
- `frontend/AGENTS.md`: read `node_modules/next/dist/docs/` before touching Next-specific code.

## Tasks

- [x] **M3** Round each price component to 2 decimals in `PrecioService`, total = sum of the
  rounded components. Route: delegated.
- [x] **M4** `RutasController.Cerrar` answers 409 when the route has pending stops, pointing to
  interrupt; the close screen shows that message. Route: delegated.
- [x] **M5** Billing run isolates each client: a failing client is reported in `Omitido`, logged,
  and the run continues with a clean change tracker. Route: delegated.
- [x] **M8** Planning close batches the per-order queries and resolves distances in bounded
  parallel, with identical resulting prices. Route: delegated.
- [x] **M2** Upgrade `next` and `eslint-config-next` to the patched release after confirming the
  advisories with `pnpm audit`. Route: delegated.
- [x] **M7** Timeout on `fetchConSesion` and a local draft of the delivery capture that survives a
  reload. Route: delegated.
- [x] **M1** Revoke every refresh token of a user on password reset, role change and deactivation.
  Company deactivation (user decision, 2026-10-12: "se corta el servicio que se le proporciona
  hasta que pague"): a deactivated company keeps portal login and read-only access to its
  invoices and debt so it can pay, and every portal action that requests or changes service is
  rejected. Read-only is the parent's reading of that answer, reported to the user. Route: delegated.
- [x] **M6** Reject adjustments on `Borrador` and `Cancelado` orders, in request and in approval
  (user decision, 2026-10-12: a cancelled order "deja de tener valor"). An adjustment left
  pending on an order cancelled afterwards cannot be approved. Route: delegated.

Route evidence: every task prepares its write with reading across controllers, services and tests
(preparation trigger); the batch touches 2+ non-trivial files (writer trigger).

## Acceptance criteria

- Each backend fix has a test that fails before it and passes after it, RED observed.
- `dotnet test backend/Logistica.Tests` passes with Docker running, or every failure is reported.
- Frontend tasks: `pnpm lint` and `pnpm build` pass.

## Delivery

Strategy: `ask-on-risk`. Forecast: about 850 authored changed lines (M1 120, M2 10, M3 60, M4 80,
M5 120, M6 80, M7 200, M8 180), over the 400 budget. Chain strategy (`stacked-to-main` or
`feature-branch-chain`) still has to be asked before any pull request; one commit per task keeps
both options open. Nothing is pushed.

Review: receipt-driven development is on (global). Base for the first assessment: `c870b11`.

## Progress

2026-10-12. M3, M4, M5 done by one delegated writer; RED and GREEN observed for each.

| Task | Commit | Tests | Observed |
|------|--------|-------|----------|
| M3 | `faae23e` | `PrecioServiceTests` (3), `PrecioConDescuentoDeRangoTests` (1) | RED 4 failed, GREEN 4 passed |
| M4 | `932de1f` | `CierreDeRutaTests` (3) | RED 2 failed of 3, GREEN 3 passed |
| M5 | `44da169` | `CierreDeCiclosTests` (+1 theory, 2 cases) | RED 2 failed, GREEN 9 passed |

Verification on `44da169`: build 0 warnings; `dotnet test backend/Logistica.Tests` 309 passed,
0 failed (writer run and parent spot check). Frontend not edited in M4: the close screen already
shows the 409 message. Running count: 425 changed lines (8 files) since `c870b11`.

Decisions accepted from the writer:
- M3 rounds base price and tolls too; factors apply to the rounded base. Rule: half away from zero.
- M4 check sits after the state checks, before km validation and the `SinDeclaracionDelRepartidor` path.
- M5 adds an optional `ILogger` constructor parameter to `CuentaCorrienteService` (positional
  callers unchanged) and `CuentaCorrienteService.OmitidoPorError`; after a failure the client's
  later closes in the same run are skipped; cancellation propagates (no test covers it).

Follow-ups noticed, not fixed:
- `CerrarCiclosAsync`: a transient retry of the execution strategy re-runs the delegate with the
  previous attempt's `Factura` still tracked; the cleanup only runs after the strategy gives up.
- `frontend/lib/dominio/tipos.ts:1188` (`omitido`): the invoices screen was not checked for how it
  shows a client skipped by error versus one without items.

Review (RDD on): assessment for `c870b11..44da169` is medium, `review_due` true
(`slice_budget_reached`), untracked tooling files excluded. Consent granted by the user, lineage
`review-436e802643a1179e`, lens `review-reliability`: approved and acknowledged (authority
burned). Reviewed boundary is now `44da169`.

Advisory findings of that review (non-blocking), taken as task **M5b** in the next batch because
they correct the M5 fix itself:
- `CuentaCorrienteService.cs:217-229`: the per-client catch swallows every exception type, so a
  systemic failure (database unreachable, timeout) looks like a list of skipped clients, also in
  the manual single-client close and the preview. Isolate data-level failures only.
- `PrecioConDescuentoDeRangoTests.cs:22-30`: the test rewrites the shared `bronce` seed row and
  inserts a tariff on the shared zone without restoring them.
- `CierreDeCiclosTests.cs:189-211`: the failure trigger is dropped without try/finally.

- [x] **M5b** Apply the three corrections above. Route: delegated. Commit `5eb9341`; diff read by
  the parent on resume (2026-10-12): catch narrowed to `DbUpdateException` with SQLSTATE class
  22/23/P0 (`EsFallaDeLosDatos`), `bronce` seed restored in a finally, trigger dropped in a finally,
  plus tests for a non-data failure and for cancellation. RED was never reported by the stopped
  writer; only the full suite (312 passed) was observed.

### Session stop, 2026-10-12

The user asked to stop because the session was ending. The writer for M5b, M1 and M6 was stopped
mid-work and never returned its report.

State of the repository at the stop:

- **M5b**: committed as `5eb9341` (`CuentaCorrienteService.cs`, `CierreCiclosAutomatico.cs`,
  `CierreDeCiclosTests.cs`, `PrecioConDescuentoDeRangoTests.cs`; 160 insertions, 39 deletions).
  Not checked off: there is no writer report, so RED/GREEN for it was not reported and the parent
  did not read the diff. Only the full suite was observed.
- **M1**: started, nothing committed. Uncommitted work in progress in the working tree, test
  helpers only: `backend/Logistica.Tests/BaseDeDatos/BaseDePrueba.cs` and
  `backend/Logistica.Tests/BaseDeDatos/ControladoresDePrueba.cs` (80 insertions, 2 deletions).
  No production code and no tests for M1 exist yet. The changes were left in place, not reverted.
- **M6, M8, M2, M7**: not started.

Observed by the parent on that tree (`5eb9341` plus the uncommitted helpers), Docker running:
`dotnet test backend/Logistica.Tests` 312 passed, 0 failed.

Review: `44da169..5eb9341` has not been assessed. Reviewed boundary is still `44da169`.

To resume:

1. Read the diff of `5eb9341` and confirm it does what M5b asks (catch narrowed to data-level
   failures, shared seed restored, trigger drop in a finally); then check M5b off.
2. Read the two uncommitted helper files and decide whether to keep them as the base for M1.
3. Writer for M1 and M6 with the task text already in this document (user decisions included).
4. `gentle-ai review assess` with `--base-ref 44da169 --committed-only` plus
   `--untracked-scope=exclude --expected-untracked-inventory=<sha from review status>`.
5. Then M8; then M2 and M7.

### Resume, 2026-10-12

- M5b checked off after reading the diff of `5eb9341`.
- The two uncommitted helper files are kept as the base for M1.
- One writer launched for M1 and M6 (one commit each); report pending.
- M2 advisories confirmed by the parent: `pnpm audit --prod` in `frontend` reports 8
  vulnerabilities (3 high, 4 moderate, 1 low); the `next` ones tail-listed are vulnerable
  `>=16.0.0 <16.3.8`, patched `>=16.3.8` (for example GHSA-f87g-xv8r-7p7x, GHSA-mcj8-r9mp-w47p,
  GHSA-39w2-rjm5-chcv). Whether all 8 are on `next` was not read (output tailed).

- This document was moved by the user from `odd/tasks/` to `docs/medium-findings-hardening.md`
  (untracked); that is its locator now.
- The writer first returned `blocked`: Docker Desktop was not running. The parent started it and
  resumed the same writer.

| Task | Commit | Tests | Observed |
|------|--------|-------|----------|
| M1 | `46e67cb` | `RevocacionDeSesionesTests` (9), `EmpresaInactivaEnPortalTests` (32) | RED 8 of 9 and 13 of 32 failed, GREEN 41 passed |
| M6 | `6dcd72f` | `AjustesSegunEstadoDelPedidoTests` (6) | RED 4 of 6 failed, GREEN 6 passed |

The new tests that passed before the implementation are negative controls or pin behavior that
must not change (active company not cut, login and refresh of a deactivated company, rejecting a
pending adjustment on a cancelled order).

Verification on `6dcd72f`: build 0 warnings; `dotnet test backend/Logistica.Tests` 359 passed,
0 failed (writer run and parent spot check). Running count since `44da169`: 854 insertions,
50 deletions, 15 files.

Decisions accepted from the writer:
- M1 revoke-all lives in `AuthService.RevocarSesionesAsync`; it marks the tokens and the caller's
  own save persists them with the change. Called on password reset, role change and deactivation
  in `UsuariosController`, `ClientesController` and `MiCuentaController` (each takes `AuthService`
  as a new last constructor parameter). No portal-login role-change endpoint exists.
- M1 company deactivation is the class-level filter `[CortaServicioAEmpresaInactiva]` on
  `MiCuentaController`: every non-GET action answers 409 unless marked
  `[PermitidoConEmpresaInactiva]`; it reads `Cliente.Activo` from the database on each write.
  Allowed writes: `POST pagos-informados`, `PUT usuarios/{id}/activo`, `PUT usuarios/{id}/password`.
  Rejected: orders and trips (create, edit, cancel, preview), `localidades`, `ubicaciones`,
  `destinatarios` (create, edit, delete), `POST usuarios`. Login and refresh untouched.
- M6 `SolicitarAjuste` and `AprobarAjuste` answer 409 on `Borrador` and `Cancelado`
  (`MotivoSinAjustes`); `RechazarAjuste` stays open so a stale pending adjustment can be cleared.

Open product question asked to the user, unanswered: may a deactivated company cancel its pending
orders and trips? Rejected today (conservative). Other writer readings not yet shown for
decision: employee deactivation and password reset allowed, employee creation rejected, address
book rejected, `localidades/buscar` and `precio-sugerido` open as reads.

Follow-ups noticed, not fixed:
- A refresh rotating between the token load and the save leaves one new token alive after a
  revoke-all.
- The 15 min access token keeps working after a reset, role change or deactivation.
- Frontend: nothing tells the portal UI that the company is inactive; it should hide or disable
  the rejected actions and show the 409 message.

Review: assessment for `44da169..6dcd72f` is high (`hot_path`: auth in `AuthService.cs`),
`review_due` true (`high_risk`), 15 paths, 904 lines. Consent granted by the user, lineage
`review-8b6923ad4b7f0acf`, four lenses: approved and acknowledged (authority burned). Reviewed
boundary is now `6dcd72f`.

Advisory findings of that review (non-blocking, 4 warnings and 9 suggestions; not accepted as
tasks, pending the user's decision):
- WARNING, two lenses, `CuentaCorrienteService.cs:220`: the M5b catch only isolates
  `DbUpdateException` with SQLSTATE 22/23/P0; a deterministic per-client failure of another kind
  (in-memory exception, `PostgresException` from a read, deferred constraint at commit) aborts the
  whole run again, which is the original M5 problem for those cases.
- WARNING, `AuthService.cs:114-119` (three lenses): revoke-all reads then marks; a refresh that
  rotates in between leaves a live token after a password reset.
- WARNING, `CuentaCorrienteService.cs:139`: the doc sentence added in M5b leaves "Sin eso"
  pointing at the wrong antecedent.
- SUGGESTION, `PedidosController.cs:1167-1169`: `AprobarAjuste` reads the order state by the route
  id, not by the adjustment's own `PedidoId`.
- SUGGESTION, `CorteDeServicioPorEmpresaInactiva.cs:33-34`: the filter lets a non-GET through
  when the session has no `cliente_id` claim; failing closed would be safer.
- Other suggestions: comment on `RevocarSesionesAsync` not saving, pattern precedence readability
  at `CuentaCorrienteService.cs:317`, a test comment, cancellation test timing, the hand-built
  filter pipeline in `ControladoresDePrueba.cs:63-65`.

**M8** done by a delegated writer, commit `66084a4` (5 files, 699 insertions, 23 deletions; about
237 production lines): `RutasController.PrepararCongelamientoAsync`,
`PrecioService.PrepararLoteAsync` / `LoteDePrecios.Cotizar` (shares the static `Desglosar` formula
with `CotizarAsync`), `DistanciaService.ResolverLoteAsync` with `MaxConsultasALaVez = 4`, and
`CierreDePlanificacionTests` (7 tests).
- RED 2 failed of 7 (query count grows with orders: 10 SELECTs with 2 orders, 26 with 6; max
  concurrency 1), GREEN 7 passed (6 SELECTs per close regardless of count; max concurrency 4).
  The other 5 pin behavior that must not change; the writer mutation-checked the equivalence test.
- Verification on `66084a4`: build 0 warnings; `dotnet test backend/Logistica.Tests` 366 passed,
  0 failed (writer run and parent spot check).
- Behavior kept: the close is all-or-nothing with the same 400 messages; a distance failure falls
  back to straight line; cancellation propagates.
- Differences reported by the writer: after a failing order, lookups for later orders have
  already been issued; an `InvalidOperationException` thrown by EF during the batched tariff read
  now surfaces as 500 instead of `400 Pedido {id}: ...`.
- Open: the limit of 4 is a judgment call, not measured against the production provider; the
  order of `pedidos` comes from a query without `ORDER BY` (pre-existing); `InsertarUrgencia` has
  no test of its own.

Review: assessment for `6dcd72f..66084a4` is medium, `review_due` true (`slice_budget_reached`),
5 paths, 722 lines. Consent granted by the user, lineage `review-d76462d951a622d6`, lens
`review-reliability`: approved and acknowledged (authority burned). Reviewed boundary is now
`66084a4`.

Advisory findings of the M8 review (non-blocking; not accepted as tasks, pending the user):
- WARNING, `CierreDePlanificacionTests.cs:338-359`: the 9-calls assertion does not prove the
  in-flight dedupe; the twin request has usually finished and been cached before the repeat starts.
- WARNING, `CierreDePlanificacionTests.cs:410-421`: the no-zone test uses a single order, so the
  new `TakeWhile` prefix and the ordering of errors across orders are unproved.
- WARNING, `DistanciaService.cs:59`: four simultaneous provider calls raise the chance of a
  rate-limit, and a failed lookup silently freezes the straight-line km as the billed price.
- SUGGESTION, `BaseDePrueba.cs:72-73`: the `CrearContexto` overload with interceptors builds its
  options from scratch instead of extending `Opciones`.
- SUGGESTION, `RutasController.cs:598-605`: on rejection the close now pays for every distance
  and tariff read before reporting the first failing order.

**M2** done by a delegated writer, commit `269a3c9` (`package.json` +2/-2, `pnpm-lock.yaml`
+50/-50): `next` and `eslint-config-next` 16.3.6 to 16.3.8, exact pins kept (16.3.7 is still
vulnerable; 16.4.0 not taken). `pnpm audit --prod` before: 8 advisories, six on `next`. After: 2
remain, both high and transitive, NOT upgraded and pending the user's decision:
`source-map-js` (`.>next>postcss>source-map-js`, patched `>=1.2.2`, GHSA-68fv-2mgg-jv7q) and
`sharp` (`.>next>sharp`, patched `>=0.35.5`, GHSA-wq5f-xc86-pv6w). Lint, typecheck and build exit 0
after the upgrade (writer); `pnpm lint` exit 0 re-run by the parent on `1f123f7`.

**M7** by the same writer, commit `1f123f7` (398 insertions, 10 deletions): stop page,
`AuthProvider.tsx`, new `lib/auth/tiempos.ts`, new `lib/borradores/capturaDeParada.ts`.
- `fetchConSesion` takes `limiteMs`; default 30 s, 120 s when the body is `FormData` or `Blob`.
  A timeout rejects with `ErrorDeTiempoAgotado`; a caller abort keeps its `AbortError`. The timer
  covers until response headers; each attempt after a token refresh starts its own timer.
- Draft in IndexedDB (`logistica-borradores`), key `${usuario.id}:${paradaId}`, max age 24 h,
  original photo stored, fields saved 300 ms after the last keystroke, restored only for a
  `pendiente` stop, cleared on success, "Descartar lo cargado" button. Storage errors are
  swallowed: the page works as before without the draft.
- No test runner, so no RED. Checked with lint, typecheck (`pnpm typecheck`), build (41 pages) and
  a throwaway Node smoke script against the two modules with a fake IndexedDB and fake `fetch`.
- NOT verified: nothing was run in a browser or on a phone (real IndexedDB, restore UI, slow
  network), nor the 401 refresh retry path end to end.
- Follow-up done, commit `b7d2956` (7 files, 26 insertions, 5 deletions): the 30 s default also
  cut long non-idempotent operations on other screens, so the same writer gave
  `LIMITE_OPERACION_LARGA_MS` (120 s, new constant in `tiempos.ts`) to `exportar`,
  `liquidaciones/[id]`, `facturas` (preview and run), `rutas/[id]/armar` (`cerrar-planificacion`),
  `tarifas` (`recalcular`) and `pedidos/importar` (per batch). Lint, typecheck and build exit 0
  (writer); `pnpm typecheck` exit 0 re-run by the parent on `b7d2956`. The two saves that precede
  `cerrar-planificacion` in `armar/page.tsx` (`:396`, `:411`) stay on 30 s. None of these
  operations was timed against a real backend.
- Review: assessment for `66084a4..b7d2956` is high (`hot_path`: auth in `AuthProvider.tsx`),
  `review_due` true, 12 paths, 543 lines. Consent granted by the user, lineage
  `review-5bb286026d5478c7`, four lenses: approved and acknowledged (authority burned). Reviewed
  boundary is now `b7d2956`.

Advisory findings of the frontend review (non-blocking, 10 warnings and 9 suggestions; not
accepted as tasks, pending the user):
- WARNING, three lenses, `frontend/app/facturas/page.tsx:376`: the 120 s client abort sits on
  state-changing POSTs with no visible idempotency key (billing close, import batch, tariff
  recalculation, planning close); on timeout the user is told to retry while the server may still
  finish, so a retry can repeat the operation.
- WARNING, `AuthProvider.tsx:152`: every caller without `limiteMs` now aborts at 30 s, including
  mutating POSTs on untouched pages; a cold start or slow endpoint over 30 s now fails.
- WARNING, `hoy/parada/[paradaId]/page.tsx:262`: a photo upload slower than 120 s is aborted and
  every retry restarts from zero, the exact slow-uplink case M7 targets.
- WARNING, `capturaDeParada.ts:20-22`: the draft keeps recipient name, DNI and the uncompressed
  photo unencrypted in IndexedDB, and the 24 h purge only runs when a stop page is opened again.
  RESOLVED on 2026-10-13 (`odd/tasks/cifrado-borrador-entrega.md`): both stores are encrypted with
  AES-GCM (fresh IV per write; store, record key and date bound as additional data; file name and
  MIME type inside the ciphertext). The key is not on the phone: `GET /api/auth/clave-borradores`
  derives it per user from `Jwt:Key` with HKDF-SHA256 (`9e66635`) and `AuthProvider` holds it in
  memory only, non-extractable. No plaintext fallback. The IndexedDB version bump destroys the
  drafts of the old schema; logout and a change of user wipe every draft; the purge also runs when
  the app starts. Not exercised in a browser or on a phone.
- WARNING, `tiempos.ts:32-41`: any `TypeError` is shown as "no connection"; any `FormData`
  (file or not) gets the upload limit.
- WARNING, `tiempos.ts` and `capturaDeParada.ts`: no automated tests (the frontend has no runner).
- Suggestions: body read has no limit; duplicated timeout sentence; debounce literal 300; the
  limit applies per stage (worst case about three times `limiteMs`); draft restore can overwrite
  what the courier typed meanwhile; the fields' 24 h expiry slides on every open while the
  photo's does not; the `next` bump shares a slice with the feature (it is its own commit).

### State at the end of this session, 2026-10-12

All eight findings plus M5b are implemented and committed on `demo-d`; every commit after
`c870b11` is covered by an approved and acknowledged native review. HEAD `b7d2956`. Since
`c870b11`: 33 files, 2394 insertions, 118 deletions. Nothing is pushed. Working tree clean except
the untracked `.atl/`, `.codegraph/` and this document.

Last observed checks: `dotnet test backend/Logistica.Tests` 366 passed, 0 failed on `66084a4`
(no backend change since); frontend lint, typecheck and build exit 0 on `b7d2956` (writer), with
lint and typecheck re-run by the parent. Nothing was exercised in a browser or on a phone.

Waiting on the user:
- M1: may a deactivated company cancel its pending orders and trips (rejected today)?
- Which advisory findings of the three reviews become tasks.
- The two remaining transitive advisories (`sharp`, `source-map-js`).
- Chain strategy before any pull request.
- Open: whether 30 s survives a Render cold start; report GETs (`tablero`, `rentabilidad`,
  `repartidores`) stay on the default; a timed-out write may still finish on the server; no
  "Reintentar" on a failed stop load.

Still open, not part of the tasks above:

- Chain strategy for the pull requests (`stacked-to-main` or `feature-branch-chain`); the branch
  is over the 400-line budget.
- T5 mechanism of `billing-routing-concurrency-fixes` (Render pre-deploy with an EF bundle vs. a
  GitHub Actions job).
- Double count of route revenue: `ExportarController.cs:74`, `TableroController.cs:146`.
- The two follow-ups listed above (`CerrarCiclosAsync` transient retry, `omitido` on the invoices screen).
- Migration `20261012201258_ConcurrenciaOptimistaDePedidos` has to be applied in each environment.
- Nothing is pushed; no pull request exists for any commit after `ac7bc5b`.
