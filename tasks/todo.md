# Gestión de Repartidores — lista de tareas

Plan completo en [plan.md](plan.md). RF-34, acta changelog 4.6.

## Fase 0 — Gobernanza (bloqueante)

- [x] **0.1** `docs/acta_sistema.md` §5.2 — agregar RF-34 después de RF-17
- [x] **0.2** `docs/acta_sistema.md` §4 — nota junto a `repartidores` ("vista derivada, sin tabla propia")
- [x] **0.3** `docs/acta_sistema.md` §14 — fila de changelog 4.6
- [x] **0.4** `docs/construccion_v1.md` §4 — filas `/repartidores` y `/repartidores/[id]` → RF-34
- [x] **0.5** `docs/construccion_v1.md` §12 — fila de changelog 1.20
- [x] **0.6** `docs/estado_implementacion.md` §3 — párrafo del módulo
- [x] **0.7** `docs/estado_implementacion.md` §4 — encabezado (26) → (28)

> **Checkpoint 0:** los tres documentos citan el mismo RF y la misma distinción con B2/E3.

## Fase 1 — Listado con disponibilidad de hoy

- [x] **1.1** `backend/.../Controllers/RepartidoresController.cs` — nuevo, `GET /api/repartidores`,
      policy `BackOffice` de clase, docstring de límite de alcance, record `RepartidorListado`
      completo (campos `*Rango` en 0 por ahora), 2 queries anti-N+1
- [x] **1.2** Verificar el endpoint con `curl` antes de tocar el frontend
- [x] **1.3** `frontend/lib/dominio/tipos.ts` — tipo espejo `RepartidorListado`
- [x] **1.4** `frontend/components/EstadoBadge.tsx` — `DisponibilidadBadge` (reusa `Badge` interno)
- [x] **1.5** `frontend/app/repartidores/page.tsx` — nuevo, `RequireRole` admin+operacion
- [x] **1.6** `frontend/components/Shell.tsx` — entrada en `NAV` después de `/rutas`

> **Checkpoint 1:** un repartidor sin ruta hoy aparece como "Libre" (hoy `/jornada` lo omite).
> Inactivo gana sobre cualquier estado de ruta.

## Fase 2 — Acumulado del rango

- [x] **2.1** Backend — 3ra query (`GroupBy` sobre `RutaParadas` vía `rp.Ruta`), params
      `desde`/`hasta` con default últimos 7 días
- [x] **2.2** Frontend — dos `<Input type="date">` reusando `hoyLocal()` de `/jornada`
- [x] **2.3** Verificar: mover el rango NO cambia la columna de disponibilidad

> **Checkpoint 2:** funcionalidad núcleo entregada.

## Fase 3 — Detalle por repartidor

- [x] **3.1** Backend — `GET /api/repartidores/{id}`, records `RepartidorDetalle` / `RutaDelRango`,
      4ta query condicional si el rango no incluye hoy
- [x] **3.2** `frontend/lib/dominio/tipos.ts` — tipos espejo del detalle
- [x] **3.3** `frontend/app/repartidores/[id]/page.tsx` — nuevo
- [x] **3.4** Link "Ver detalle" desde cada fila del listado

> **Checkpoint 3 (final):** releer los tres documentos de la Fase 0 y corregirlos si el diseño
> cambió durante la ejecución.

- [x] **3.5** Checkpoint 3 ejecutado (17/09/2026) — los tres documentos releídos contra el código
      final. Correcciones aplicadas, no solo agregados:
      - **Conteo de queries del listado: 3 → 5** en `construccion_v1.md` §4.5, §12/1.20 y
        `estado_implementacion.md` §3.12. Los tres decían 3 porque se escribieron en Fase 0, antes
        de que 2.1 y la verificación agregaran la query propia de `rutas` del rango.
      - Docstring de `RepartidoresController.Listar`: "Cuatro queries planas" → cinco (enumeraba 4
        conceptos pero emitía 5 round-trips).
      - Documentado lo que no estaba: `400` por rango invertido, desempate de dos rutas el mismo
        día (`en_curso` > `planificada` > `Id` mayor), rango en la query string del detalle, y el
        `isolate` de `Mapa.tsx` (ajeno a RF-34, sin entrada de changelog propia).
      - **Desvío anotado, no corregido:** el controller resuelve `hoy` con `DateTime.UtcNow` y no
        con `Dominio/Reloj.HoyLocal()` — entre 21:00 y medianoche AR muestra el día siguiente.
        Preexistente (`JornadaController`, `TarifasController`, `TarifaService`,
        `ClientesController` hacen lo mismo); fuera del alcance de RF-34, queda como decisión
        explícita en `construccion_v1.md` §4.5.
      - Estado de verificación (incluido lo NO verificado: `next build` y render en navegador)
        volcado al changelog 1.20, que no lo tenía.
      - Encabezados de versión sincronizados: acta 4.5 → **4.6**; `construccion_v1.md` seguía
        diciendo "v1.1 · 29/08/2026" con §12 en 1.20 → **v1.20**.
      - Conteos de `estado_implementacion.md` §1 recontados contra el código: controladores 18 →
        **19**, pantallas 26 → **31**, endpoints "~78" → **97** (el previo estaba desactualizado
        desde varias versiones, no por esta).
      - **Corregido de paso, ajeno a 4.6:** §6 decía "Notificaciones (B6): sin integración de
        correo/WhatsApp en el código" — falso desde changelog 4.3 (`EmailService`/Resend,
        `EnlaceWhatsApp`). Reescrito acotando qué existe y qué no.

---

## Estado de verificación (17/09/2026)

**Verificado:**
- `dotnet build` — 0 errores, 0 advertencias.
- Ambos endpoints contra la base real, con `curl`: listado, detalle, `404` para usuario no repartidor,
  `400` para rango invertido, `403` para rol repartidor (policy BackOffice).
- Listado y detalle devuelven los mismos totales para el mismo rango (se corrigió un desvío:
  el listado contaba rutas desde `ruta_paradas` y perdía las rutas sin paradas cargadas).
- `tsc --noEmit` exit 0 y `eslint` exit 0 sobre todo lo nuevo y modificado.

**NO verificado — bloqueado por hardware, no por código:**
- `next build` (Turbopack y webpack) y el render de las dos pantallas en el navegador.
  El disco `E:` se desconectó 10 veces en 30 minutos durante los intentos; los errores eran
  `UNKNOWN: unknown error, open 'package.json'` y `TS6053: File not found` sobre archivos que
  existen (incluido `lib.dom.d.ts` de TypeScript). Reintentar cuando el disco esté sano.
