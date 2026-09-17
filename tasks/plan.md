# Módulo Gestión de Repartidores

## Contexto

Hoy no existe ningún lugar donde ver **quién está libre** para tomar una ruta. La asignación
(`RF-15`) ya está construida — `Ruta.RepartidorId` + `PUT /api/rutas/{id}/repartidor` — pero se
decide a ciegas: `/jornada` solo muestra repartidores **que ya tienen ruta asignada**
(`JornadaController.cs`, `.Where(r => r.RepartidorId is not null)`), así que el que está libre
literalmente no aparece en ninguna pantalla. Y `/usuarios` los lista como filas de personal, sin
ninguna noción de carga.

Este módulo cierra ese hueco: un panel donde se ve, por repartidor, su **disponibilidad de hoy** y
su **carga acumulada** en un rango de fechas, para decidir asignación y reasignación sin planilla
aparte.

### Restricciones de gobernanza (no negociables)

Tres reglas del acta condicionan el diseño y explican por qué el módulo es más chico de lo que el
nombre sugiere:

1. **`docs/acta_sistema.md` §4 lista `repartidores` como tabla "fuera del modelo inicial".** El
   repartidor es `usuarios.rol='repartidor'`, no una entidad propia. Se respeta: **cero tablas
   nuevas**, sigue en 17 tablas / 20 entidades.
2. **El acumulado histórico roza B2/E3** (tablero de indicadores, fuera de alcance). Hay que
   declarar el límite por escrito, igual que ya hacen `JornadaController` y `/cobranza` en su
   propio docstring.
3. **Liquidación/pago al repartidor es B4/E2**, bloqueada por la definición abierta §10.2-C y
   disparada por "el segundo repartidor". Queda **fuera**: este módulo no toca
   `rutas.pago_repartidor` ni muestra un solo importe.

### Decisiones tomadas

| Decisión | Resuelto |
|---|---|
| Disponibilidad | **Derivada**, cero tablas nuevas |
| Carga de trabajo | **Rango histórico** (semana/mes) |
| Ubicación | **Sección nueva `/repartidores`** |
| Gobernanza | **Acta primero**, después el código |

**Consecuencia de elegir disponibilidad derivada:** no hay ausencias declaradas (franco,
vacaciones, licencia). Los cuatro estados posibles salen todos de datos que ya existen:

| Estado | Se deriva de |
|---|---|
| `inactivo` | `usuarios.activo = false` (corta antes de mirar rutas) |
| `en_ruta` | ruta de hoy con `estado = 'en_curso'` |
| `asignado` | ruta de hoy con `estado = 'planificada'` |
| `libre` | activo, sin ruta hoy |

**La disponibilidad es siempre de HOY**, no del rango elegido — es un estado del presente. El
selector de fechas gobierna únicamente el acumulado. Esto hay que respetarlo aunque el rango no
incluya hoy.

---

## Fase 0 — Gobernanza (bloqueante)

### Tarea 0 · Actualizar los tres documentos antes de escribir código

**`docs/acta_sistema.md`**
- §5.2 Planificación, después de `RF-17`: **`RF-34`** — *"Visibilidad de disponibilidad (en ruta /
  asignado / libre / inactivo) y carga de trabajo acumulada de cada repartidor en un rango de
  fechas, para decidir asignación y reasignación sin planilla aparte."*
- §14 Control de versiones: fila **4.6** (la última es 4.5 del 16/09/2026). Mismo estilo de párrafo
  largo que 4.3/4.5. Debe decir explícitamente: qué resuelve (RF-34), que deriva de
  `usuarios`+`rutas`+`ruta_paradas` sin tabla propia (§4 sigue igual, `repartidores` sigue fuera
  del modelo inicial, **"Sigue en 17"**), y la distinción con B2/E3 y B4/E2.
- §4: **sin cambios estructurales.** Opcional, una frase junto a `repartidores` en la lista de
  "fuera del modelo inicial": *"expuesto como vista derivada por `RepartidoresController`, sin
  tabla propia"* — para que quien lea §4 no crea que el gap sigue abierto.

**`docs/construccion_v1.md`**
- §4 (tabla pantallas → requisito): filas `/repartidores` y `/repartidores/[id]` → RF-34.
- §12 Control de versiones: fila **1.20** (la última es 1.19), con detalle técnico — records,
  queries, round-trips, archivos tocados.

**`docs/estado_implementacion.md`**
- §3 Módulos implementados: párrafo nuevo, con el mismo formato que el de "Monitor de jornada".
- §4 encabezado: **"Pantallas del frontend (26)" → (28)**.
- §1/§5: sin cambios (20 entidades, 17 tablas núcleo).

**Aceptación:** los tres documentos usan el mismo número de RF y citan la misma distinción con
B2/E3. **Verificación:** lectura cruzada — es puramente documental, no hay build.

---

## Fase 1 — Slice vertical 1: listado con disponibilidad de hoy

### Tarea 1 · `GET /api/repartidores` + pantalla `/repartidores`

**Backend — `backend/Logistica/Controllers/RepartidoresController.cs` (nuevo)**

Controller propio, **no** extender `UsuariosController`: ese es ABM de identidad sobre `Usuario`
con policy `Administracion`; esto es consulta operativa de solo lectura que cruza tres tablas, con
policy `BackOffice`. El precedente correcto es `JornadaController`, que tampoco tiene tabla propia.

A diferencia de `UsuariosController`, acá **sí va `[Authorize(Policy = "BackOffice")]` a nivel de
clase**: aquel lo omite porque mezcla policies por acción (`Seleccion` necesita BackOffice mientras
el resto exige Administracion, y ASP.NET combina clase+acción con AND). Acá todas las acciones
comparten policy, así que se declara una vez — igual que `JornadaController`.

El docstring debe declarar el límite de alcance, siguiendo literalmente el precedente de
`JornadaController` ("NO es el tablero de indicadores… Cero tablas nuevas") y del changelog 4.3:

```
/// Disponibilidad y carga por repartidor (RF-34, acta changelog 4.6) — deriva de
/// usuarios/rutas/ruta_paradas, cero tabla propia: el acta §4 mantiene `repartidores` fuera del
/// modelo inicial.
///
/// NO es el tablero de indicadores (B2/E3, Anexo I §5, fuera de alcance): no calcula ninguna de
/// sus 10 métricas (entregas/día, km, tiempo, margen, NPS, ocupación de flota), no compara
/// períodos ni exporta series — el acumulado es una sumatoria de paradas para decidir asignación
/// hoy, recalculada en cada request, sin persistir. Mismo encuadre que /jornada y /cobranza.
///
/// Tampoco es liquidación (B4/E2, disparador "segundo repartidor"): no toca
/// rutas.pago_repartidor ni muestra importes.
```

Records anidados en el controller (patrón del repo), `Disponibilidad` como `string` snake_case
igual que `Ruta.Estado`:

```csharp
public record RepartidorListado(
    Guid Id, string Nombre, bool Activo, string Disponibilidad,
    long? RutaHoyId, string? RutaHoyEstado, string? VehiculoHoyPatente,
    int ParadasHoy, int CompletadasHoy, int FallidasHoy, int PendientesHoy,
    int RutasRango, int ParadasRango, int CompletadasRango, int FallidasRango);
```

Definir el record **completo** ya en esta tarea, con los campos `*Rango` en `0`, para no tocar el
tipo TS dos veces. Se llenan en la Tarea 2.

Sin paginación ni `ListaPaginada<T>`: la cantidad de repartidores es la plantilla de personal, no
crece con el volumen de operación — mismo criterio que `UsuariosController.Listar`.

**Queries — 2 round-trips en esta tarea**, replicando el patrón anti-N+1 de
`JornadaController.Resumen` (dos queries + composición en memoria):

1. `db.Usuarios.Where(u => u.Rol == Roles.Repartidor).OrderBy(u => u.Nombre)` — **incluye
   inactivos**, que deben seguir apareciendo con badge "Inactivo".
2. `db.Rutas.Where(r => r.Fecha == hoy && r.RepartidorId != null).Include(r => r.Vehiculo)` +
   `db.RutaParadas` agrupadas por `RutaId` (idéntico al `GroupBy` que ya existe en
   `JornadaController`).

La diferencia clave con `/jornada`: **el punto de partida es la lista de usuarios, no la de
rutas**. Por eso el repartidor sin ruta no desaparece. Composición final en memoria con un
diccionario, como ya hace `Resumen`.

*Caso borde:* si un repartidor tuviera dos rutas hoy (`RutasController.Actualizar` no lo impide
para `planificada`; solo `ReasignarRepartidor` valida duplicados de `en_curso`), tomar
`en_curso > planificada` y a igualdad el `Id` mayor. Dejarlo comentado, igual que el comentario
que ya existe en `ReasignarRepartidor`.

*Índice:* **no agregar** `rutas(repartidor_id, fecha)` en esta pasada. `rutas` ya tiene índice en
`fecha` y `ruta_paradas.ruta_id` es FK indexada; con el volumen que el propio `JornadaController`
asume ("la cantidad de rutas por día es de un dígito") no hay plan que sufra. P6.

**Frontend**
- `frontend/lib/dominio/tipos.ts` — agregar `RepartidorListado` con JSDoc *"Espejo de
  RepartidoresController.RepartidorListado"*, como el resto del archivo.
- `frontend/components/EstadoBadge.tsx` — agregar ahí mismo `DisponibilidadBadge`, con sus mapas
  `ESTILO_DISPONIBILIDAD` / `ETIQUETA_DISPONIBILIDAD`. **Reusa el `Badge` interno y el mapa
  `PADDING` que ya existen en ese archivo** (líneas 29-37), sin extraerlos a un archivo nuevo: es
  un badge de estado, pertenece al mismo módulo, y así el refactor es cero.
- `frontend/app/repartidores/page.tsx` (nuevo) — `"use client"`, envuelto en
  `<RequireRole roles={["administracion", "operacion"]}>`, `<CabeceraSesion titulo="Repartidores" />`,
  fetch con el patrón estándar del repo
  (`fetchConSesion(...).then(r => leerJson<RepartidorListado[]>(r)).then(...).catch(...)` dentro de
  `useEffect`). Tabla con `components/ui/table`: Nombre · `DisponibilidadBadge` · ruta de hoy con
  `EstadoRutaBadge` · `ProgresoParadas` si tiene ruta. Fila de `TarjetaMetrica` arriba con el conteo
  por estado (En ruta / Asignados / Libres / Inactivos).
- `frontend/components/Shell.tsx` — entrada en `NAV`, después de `/rutas`:
  `{ href: "/repartidores", label: "Repartidores", roles: ["administracion", "operacion"] }`.

**Aceptación**
1. "Repartidores" aparece en el sidebar solo para administración y operación.
2. Un repartidor **sin ruta hoy aparece como "Libre"** — es la diferencia concreta con `/jornada`,
   que hoy lo omite.
3. `activo = false` → "Inactivo", sin importar si tiene ruta asignada.
4. Ruta `en_curso` hoy → "En ruta" con barra de progreso; `planificada` → "Asignado".

**Verificación:** levantar backend (`dotnet run --launch-profile http`) y frontend
(`pnpm run dev`), entrar como `admin@logistica.local` / `Logistica123!`, ir a `/repartidores`.
Cambiar el estado de una ruta desde `/rutas` y confirmar que el badge cambia al recargar.
Desactivar el repartidor desde `/usuarios` y confirmar que pasa a "Inactivo".

> **Checkpoint 1** — listado navegable y correcto, demostrable de punta a punta antes de tocar el
> rango histórico.

---

## Fase 2 — Slice vertical 2: acumulado del rango

### Tarea 2 · `?desde=&hasta=` en el listado

**Backend** — agregar la tercera query al mismo endpoint, agrupando `RutaParadas` por repartidor a
través de la navegación `rp.Ruta` (ya existe en `Entidades/RutaParada.cs`):

```csharp
db.RutaParadas.AsNoTracking()
  .Where(rp => rp.Ruta.RepartidorId != null && rp.Ruta.Fecha >= desde && rp.Ruta.Fecha <= hasta)
  .GroupBy(rp => rp.Ruta.RepartidorId)
  .Select(g => new {
      RepartidorId = g.Key!.Value,
      RutasDistintas = g.Select(p => p.RutaId).Distinct().Count(),
      Total = g.Count(),
      Completadas = g.Count(p => p.Estado == "completada"),
      Fallidas = g.Count(p => p.Estado == "fallida"),
  })
  .ToDictionaryAsync(x => x.RepartidorId, ct);
```

`desde`/`hasta` son `DateOnly?` opcionales; default **últimos 7 días** (`hoy.AddDays(-6)..hoy`),
mismo criterio que `/jornada` defaulteando a hoy. Total: 3 round-trips, constantes.

**Frontend** — dos `<Input type="date">` con `<Label>`, copiando el patrón de
`app/jornada/page.tsx` (incluida su función `hoyLocal()`, que corrige el offset de zona horaria —
reusarla, no reinventarla). `[desde, hasta]` como dependencias del `useEffect`. Columnas nuevas en
la tabla y actualización de `RepartidorListado` en `tipos.ts`.

**Aceptación:** cambiar el rango recalcula sin recargar la página; un rango sin rutas muestra `0`,
nunca `null` ni error; **la columna de disponibilidad no cambia al mover el rango** (sigue siendo
el estado de hoy).

**Verificación:** elegir un rango con rutas cerradas conocidas y contrastar a mano contra `/rutas`
filtrado por ese repartidor.

> **Checkpoint 2** — funcionalidad núcleo entregada.

---

## Fase 3 — Slice vertical 3: detalle por repartidor

### Tarea 3 · `GET /api/repartidores/{id}` + `/repartidores/[id]`

**Backend** — records `RepartidorDetalle` y `RutaDelRango(long RutaId, DateOnly Fecha, string
Estado, string? VehiculoPatente, int Paradas, Completadas, Fallidas, Pendientes)`. Tres queries:
usuario (404 si no existe **o no es repartidor**), sus rutas del rango con `.Include(Vehiculo)`
`.OrderByDescending(Fecha)`, y el `GroupBy` de paradas por `RutaId`.

*Detalle importante:* si el rango elegido **no incluye hoy**, la disponibilidad necesita igual el
estado de hoy — una cuarta query condicional acotada a `Fecha == hoy`, solo en ese caso. Dejarlo
comentado.

**Frontend** — `frontend/app/repartidores/[id]/page.tsx` con `useParams<{id:string}>()`, cabecera
con nombre/email/`DisponibilidadBadge`, `TarjetaMetrica`s del rango, y lista de `RutaDelRango` con
`EstadoRutaBadge` + `ProgresoParadas` + link a `/rutas/{rutaId}` vía
`Button render={<Link/>} nativeButton={false}`. Link "Ver detalle" desde cada fila del listado.

**Aceptación:** los totales del detalle coinciden con la fila del listado para el mismo rango; cada
ruta linkea a `/rutas/{id}`; un repartidor inactivo es visitable (no 404) y muestra su historial.

> **Checkpoint 3 (final)** — releer los tres documentos de la Fase 0 y corregirlos si el diseño
> cambió durante la ejecución. El doc se corrige antes de cerrar la tarea, no después.

---

## Archivos

**Nuevos**
- `backend/Logistica/Controllers/RepartidoresController.cs`
- `frontend/app/repartidores/page.tsx`
- `frontend/app/repartidores/[id]/page.tsx`

**Modificados**
- `frontend/lib/dominio/tipos.ts` — tres tipos espejo
- `frontend/components/EstadoBadge.tsx` — `DisponibilidadBadge` (reusa el `Badge` interno)
- `frontend/components/Shell.tsx` — entrada en `NAV`
- `docs/acta_sistema.md`, `docs/construccion_v1.md`, `docs/estado_implementacion.md`

**Sin cambios:** ninguna migración, ninguna entidad, ningún índice. Cero tablas nuevas.

## Verificación end-to-end

No hay tests ni test runner en el repo — la verificación es manual, corriendo la app:

1. `cd backend/Logistica && dotnet run --launch-profile http` → escucha en `:5190`.
2. `cd frontend && pnpm run dev` → **forzar el puerto 3000** (`-p 3000`): `Frontend:Origin` en
   `appsettings.json` está fijo ahí y desde 3001 el login falla por CORS.
3. Login `admin@logistica.local` / `Logistica123!`.
4. Los datos semilla traen **un solo repartidor** con una ruta de ayer en estado `en_curso`. Para
   probar los cuatro estados de disponibilidad hace falta crear 2-3 repartidores más desde
   `/usuarios/nuevo` y asignarles rutas en distintos estados desde `/rutas`.
5. Probar el backend directo antes del frontend:
   `curl "http://localhost:5190/api/repartidores?desde=2026-09-01&hasta=2026-09-17" -H "Authorization: Bearer <token>"`.

> ⚠️ El disco `E:` se sigue desconectando del bus SATA (ver diagnóstico de esta sesión). Si
> `next dev` muere con exit `3221225478` a mitad de la implementación, es hardware, no el código.
