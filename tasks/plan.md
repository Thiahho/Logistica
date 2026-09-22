# Ciclo completo del repartidor — de la ruta del día al cierre, con incidencias y cambios de pedido

**18/09/2026 · rama `demo-d`.** Reemplaza el plan anterior "Jornada del repartidor" (acta 4.7), que
queda absorbido. Estado de cada tarea en [todo.md](todo.md).

**Nota posterior (21/09/2026):** `components/Shell.tsx`, `/hoy/*` y el resto de la PWA que este plan
construyó fueron restyleados sin cambio de contrato en `construccion_v1.md` changelog 1.23 (identidad
"BF Transportes" — `docs/Paleta.png`, `Logo.png`, `MobileVistaOperador.png`). No es parte de este plan;
se anota acá porque el pendiente de F5/F6/F8 ("sin verificar en un teléfono real") cae sobre esas mismas
pantallas — verificar contra la UI actual, no contra la de esta fecha.

## Contexto

`/hoy` cubría solo la mitad del día. Faltaban las dos puntas —retiro firmado al salir, cierre de
jornada al volver— y todo lo que pasa en el medio cuando algo sale distinto de lo planeado: una
avería, un bulto dañado, un teléfono equivocado, un pedido que operación cancela con el repartidor
ya en la calle. Objetivo: que un pedido y una ruta recorran su ciclo entero sin que el repartidor
salga del teléfono ni el back-office tenga que adivinar qué pasó.

```
login → /hoy → RETIRO firmado → parada N: llegué → entregué (foto) | no pude (motivo)
      → siguiente parada automática → … → CIERRE de jornada (km, combustible, peajes)
      ↕ en cualquier momento: "Reportar un problema" / "Corregir un dato"
      ↕ back-office cambia o cancela un pedido → aviso en /hoy, "Entendido"
```

## Decisiones (del usuario, 18/09/2026)

| Tema | Resuelto |
|---|---|
| Relación con el plan 4.7 | Absorberlo: un solo plan de punta a punta |
| Problemas a cubrir | Entrega fallida con motivo (ya existía), incidencia de ruta/vehículo, problema con la carga. **Fuera:** aviso de demora al cliente (B6) |
| Modificación de un pedido | Ambas direcciones: operación cambia y el repartidor lo ve; el repartidor propone y operación aprueba |
| Almacenamiento | Una tabla nueva, `novedades` (**18 tablas**), justificada en acta 4.8 |

## Decisiones de diseño (tomadas por quien implementó; revisables)

1. **La dirección de destino no se edita en vivo.** `fn_congelar_pedido` protege destino y precio desde
   Borrador (P1). Dirección incorrecta = entrega fallida. Lo editable es lo que el trigger no toca:
   teléfono, nombre del destinatario, observaciones.
2. **Un problema de bultos no toca `pedidos.bultos`:** deja evidencia y operación abre el ajuste B16 a mano.
3. **Cancelar un pedido en ruta** deja aviso al repartidor y, si la parada queda sin pedidos activos,
   pasa a `cancelada`. `Cerrar` saltea los cancelados en vez de dar `409` sobre toda la parada.
4. **Ruta que no sigue:** `POST /api/rutas/{id}/interrumpir` — lo pendiente pasa a fallido con motivo y
   entra al circuito D13. Para que otro repartidor la continúe, la herramienta ya existente es reasignar.
5. **El repartidor propone, no modifica:** un cambio propuesto se aplica recién cuando operación lo acepta,
   y falla con `409` si el dato cambió mientras tanto.
6. **La cola offline sigue siendo el plan siguiente y último.** Las escrituras nuevas llevan `device_uuid`
   idempotente; la deuda de la cola pasa de 5 a 7 caminos.

## Fases

Mapeo al plan 4.7 que se absorbió: F1 = su Fase 1, F2 = su Fase 3, F3 = su Fase 4, F7 = su Fase 2.

- **F1 Retiro firmado (RF-35).** Gate `409` en `/llegada` y `/cierre`; `FirmaCanvas`; `/hoy/retiro`.
- **F2 Siguiente parada.** `SiguienteParadaId` resuelto por el servidor, también en los `duplicado`.
- **F3 Cierre en dos actores (RF-26).** `POST /api/mi-jornada/cierre`; `/hoy/cierre`; en administración:
  prellenado desde lo declarado, `NotasCierre` obligatorio si difiere, `SinDeclaracionDelRepartidor`,
  `cerrada_por`, `Aprobacion` derivada en lectura; monitor en `/jornada`.
- **F4 Novedades, backend.** Migración `AgregarNovedades` (tabla, checks, `trg_novedades_inmutable`,
  `cancelada`, `pedido_estado` en la vista); endpoints del repartidor y `NovedadesController`;
  `PUT /pedidos/{id}/contacto`; sincronización de cancelaciones; `interrumpir`.
- **F5 PWA.** `/hoy` con `useSondeo` y avisos con acuse; `/hoy/problema`; "Corregir un dato"; pedido
  cancelado tachado.
- **F6 Back-office.** `PanelNovedades` en `/rutas/[id]`, contador en `/jornada` y navegación, contacto y
  novedades en el detalle de pedido.
- **F7 Foto del documento con retención (RF-23 reescrito).** **No construida**, bloqueada por la consulta
  legal de acta §11.2. Migración `AgregarFotoDocumento`, segunda foto (3 MB), purga idempotente con
  contador de vencidas, imagen solo para `Administracion`, `RetencionDocumentoDias` (provisional, 30).
- **F8 Gobernanza.** Acta 4.8, construcción 1.22, `estado_implementacion.md` recontado contra el código.

## Verificación

No hay test runner en el repo: `curl` y SQL contra la API real, luego compilación.

1. Backend `cd backend/Logistica && dotnet run --launch-profile http` (:5190); frontend en :3000
   (`Frontend:Origin` fijo). Repartidor `repartidor@logistica.local` / `Logistica123!`. Matar los
   procesos al terminar.
2. `curl`/SQL por fase (códigos esperados en [todo.md](todo.md)).
3. **Escenario integral:** retirar con un bulto de diferencia → entregar → operación cancela un pedido de
   una parada consolidada → problema de carga con foto → corrección de teléfono aceptada → entrega
   fallida → avería → interrumpir → cierre de jornada → cierre de administración. Al final ningún pedido
   en `EnRuta` y la ruta `cerrada`.
4. `dotnet build`, `tsc --noEmit`, `eslint`, `next build`.
5. **RNF-06 en un teléfono real** — pendiente: un dedo, botones ≥ 48 px, sin scroll, trazo de la firma.

## Fuera de alcance

Cola offline / `manifest.json` / service worker · aviso de demora al cliente (B6) · editar dirección o
precio de un pedido confirmado (P1) · urgencias insertadas en ruta en curso (ventana de las 13:00) ·
liquidación al repartidor (B4/E2) · reprogramar desde la calle · tablero de indicadores (B2).
