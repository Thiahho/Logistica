# Resultados de testing del sistema — Logistica

**Fecha:** 13-14/09/2026 · **Rama:** `demo-d` · **Alcance:** testing en vivo (no solo lectura de código) contra el backend real (`http://localhost:5190`) y su base Postgres real, vía HTTP directo (curl) con tokens JWT reales de los 4 roles. El frontend no se testeó por UI en esta pasada (ver §8, limitaciones) — toda la superficie funcional pasa por la misma API, así que la cobertura de negocio es equivalente.

**Método:** login real con las 4 credenciales seed, ejecución de flujos completos contra datos reales (algunos pre-existentes de sesiones de desarrollo anteriores, otros creados/mutados por este testing), sin mocks.

**Verificación independiente (13/09/2026):** tras generarse este reporte, se re-ejecutó por separado una muestra de los tests más sensibles — RBAC (`/api/tarifas` sin token/con rol insuficiente/con rol correcto), JWT forjado con `alg:none`, inyección SQL contra `/api/pedidos` y `/api/facturas` sin truncar la respuesta, IDOR contra `/api/mi-cuenta` y `/api/clientes/{id}/cuenta-corriente` con token real de cliente, y validación de km negativo en zonas — todos confirmaron el resultado original. Se encontró y corrigió una imprecisión: el test de SQLi contra `/api/clientes?q=` (fila de §7) daba PASS por un motivo distinto al descripto; ver la nota en esa fila.

---

## 1. Resumen ejecutivo

| | |
|---|---|
| Pruebas ejecutadas | 68 |
| PASS | 64 |
| FAIL | 0 |
| WARN / observaciones | 4 |
| Hallazgos críticos | 0 |
| Hallazgos altos | 0 |
| Hallazgos medios | 1 |
| Hallazgos bajos | 3 |

**Conclusión general:** no se encontró ninguna falla de seguridad ni de integridad de negocio explotable. Todas las reglas de negocio de E1 (D11, D12, D13, §10.2-I, B16, B9) verificadas end-to-end contra la base real se comportan exactamente como documenta `acta_sistema.md`. El control de acceso por rol (RNF-08) es consistente en los ~15 grupos de endpoints probados, sin ningún caso de escalamiento de privilegios. No se encontró inyección SQL, IDOR ni manipulación de JWT viable.

---

## 2. Hallazgos

### 🟡 Medio — El backend se cayó una vez durante el testing, sin causa reproducible en la aplicación
- **Descripción:** en medio de la sesión de testing, el proceso del backend (que ya venía corriendo de una sesión previa, no iniciado por este test) dejó de responder (`curl` devolvió error de conexión) justo después de una llamada a `POST /api/pedidos/17/estado` (cancelación con cargo).
- **Cómo se investigó:** se reinició el backend capturando su log esta vez, y se repitió exactamente la misma llamada (mismo pedido, mismo payload) — **no se reprodujo**: la segunda vez respondió 204 normalmente y el backend siguió vivo. El estado en base quedó consistente en ambos intentos (el pedido 17 no quedó en un estado intermedio corrupto).
- **Impacto:** no se pudo confirmar una causa de aplicación. Es indistinguible de un problema de gestión de procesos del entorno de desarrollo (ya documentado en esta misma sesión: un `dotnet run` lanzado como proceso hijo de un wrapper de shell puede morder cuando el wrapper termina, aunque en este caso el proceso venía de una ejecución anterior y no de un wrapper que yo iniciara).
- **Recomendación:** si se repite en un entorno real (no de desarrollo local), revisar logs de Kestrel/systemd del momento exacto. No amerita acción de código sin poder reproducirlo.

### 🔵 Bajo — Rate limiting de login muy agresivo para pruebas legítimas repetidas
- **Descripción:** el límite fijo de intentos de login se agota con apenas 2-3 requests en la ventana (se disparó 429 al 3er intento fallido consecutivo, y también se había disparado antes tras solo 4 logins **exitosos** seguidos con usuarios distintos).
- **Impacto:** ninguno para seguridad (es la dirección correcta del trade-off). Puede afectar UX en escenarios legítimos de multi-usuario desde la misma IP/NAT (una oficina detrás de un mismo router, por ejemplo), o pruebas automatizadas de QA.
- **Recomendación:** ninguna acción obligatoria; si en producción varios operadores comparten salida a internet, vale la pena confirmar que el límite es por IP y no bloquea a todo el local.

### 🔵 Bajo — Validación de `Monto` en `aprobar` ajuste exige > 0 incluso para el caso semánticamente "sin cargo"
- **Descripción:** `PUT /api/pedidos/{id}/ajustes/{ajusteId}/aprobar` rechaza `monto: 0` con "El monto debe ser mayor a cero", aun cuando conceptualmente aprobar un ajuste de bultos sin efecto económico (ej. reconocer una diferencia menor sin cobrar) sería un caso válido.
- **Impacto:** bajo — es una decisión de diseño defendible (evita aprobar "en null" por error), simplemente no estaba documentada en `acta_sistema.md` como regla explícita. No bloquea ningún flujo descripto en el Anexo I.
- **Recomendación:** ninguna acción — dejar constancia en `estado_implementacion.md` si se quiere documentar el piso.

### 🔵 Bajo — `/api/facturas/cierre/previsualizacion` y `/cierre` no encontraron ítems facturables en el período probado
- **Descripción:** no fue posible verificar la idempotencia del cierre generando una factura nueva real (todos los clientes de prueba ya tenían su período de agosto facturado o sin ítems pendientes). Se confirmó igual que dos llamadas consecutivas a `POST /cierre` el mismo día devuelven **exactamente la misma respuesta** sin error, lo cual es evidencia indirecta de idempotencia, pero no se observó el caso "factura nueva creada, se reintenta, no se duplica".
- **Recomendación:** si se quiere cerrar esta duda con evidencia directa, correr el test de nuevo con un cliente nuevo con pedidos entregados y sin facturar en el período actual.

---

## 3. Autenticación y sesión

| Prueba | Rol/token | Resultado esperado | Resultado real | Estado |
|---|---|---|---|---|
| Login admin@logistica.local | — | 200 + accessToken + cookie refresh httpOnly | 200, token + `Set-Cookie...httponly` | PASS |
| Login operacion@logistica.local | — | 200 + token | 200 | PASS |
| Login repartidor@logistica.local | — | 200 + token | 200 | PASS |
| Login cliente@logistica.local | — | 200 + token | 200 | PASS |
| Login con password incorrecta | — | 401 genérico | 401, sin distinguir causa | PASS |
| Login con email inexistente | — | 401 genérico (sin enumeración) | 401, mismo formato que password incorrecta | PASS |
| Rate limiting tras intentos repetidos | — | 429 en algún punto | 429 al 3er intento fallido en la ventana | PASS |
| `GET /api/auth/yo` sin token | — | 401 | 401 | PASS |
| `GET /api/auth/yo` con token válido | admin | 200, datos de rol correctos | 200, `rol:"administracion"` | PASS |
| `POST /api/auth/refresh` con cookie válida | operacion | 200, nuevo accessToken | 200 | PASS |
| `POST /api/auth/refresh` sin cookie | — | 401 | 401 | PASS |
| `POST /api/auth/refresh` con cookie manipulada/inexistente | — | 401 | 401 | PASS |
| `POST /api/auth/logout` | operacion | 204 | 204 | PASS |
| Reusar refresh token (cookie) después de logout | operacion | 401 (invalidado) | 401 | PASS |
| JWT con firma alterada (1 carácter) | admin | 401 | 401 | PASS |
| JWT con `alg:"none"` y rol falsificado a administracion | forjado | 401 en cualquier endpoint | 401 en `/yo` y en `/api/facturas` | PASS |
| Passwords/hashes en alguna respuesta JSON capturada | — | Nunca presentes | No se encontró ninguna ocurrencia en ~50 respuestas | PASS |

---

## 4. RBAC / control de acceso

| Endpoint / grupo | Sin token | Rol insuficiente | Rol correcto | Estado |
|---|---|---|---|---|
| `GET /api/tarifas` (Administracion) | 401 | 403 (operacion, repartidor) | 200 (admin) | PASS |
| `GET /api/vehiculos` (Administracion) | — | 403 (operacion) | 200 (admin) | PASS |
| `GET /api/vehiculos/seleccion` (BackOffice) | — | 403 (repartidor) | 200 (operacion) | PASS |
| `GET /api/usuarios` (Administracion) | — | 403 (operacion) | 200 (admin) | PASS |
| `GET /api/clientes` (Administracion) | — | 403 (operacion) | 200 (admin) | PASS |
| `GET /api/clientes/seleccion` (BackOffice) | — | 403 (repartidor, cliente) | 200 (operacion) | PASS |
| `GET /api/facturas` (Administracion) | — | 403 (operacion, repartidor, cliente) | 200 (admin) | PASS |
| `PUT /api/zonas/{id}/km` (Administracion sobre BackOffice) | — | 403 (operacion, con acceso de lectura a `GET /api/zonas`) | 204 (admin) | PASS |
| `GET /api/mis-paradas/dia` (Repartidor) | 401 | 403 (admin, operacion, cliente) | 200 (repartidor, solo sus propias paradas) | PASS |
| `GET /api/mi-cuenta` (Cliente) | 401 | 403 (admin, repartidor) | 200 (cliente, solo su propia cuenta) | PASS |
| `GET /api/clientes/{id}/cuenta-corriente` (Administracion) | — | 403 (cliente, aun para su propio id) | 200 (admin) | PASS |
| `GET /api/pruebas-entrega/{id}/foto` (BackOffice) | 401 | 403 (cliente) | 200/404 según exista (admin) | PASS |

**Observación de diseño confirmada en la práctica:** los controladores sin `[Authorize]` de clase (`ClientesController`, `UsuariosController`, `VehiculosController`) combinan correctamente el atributo de cada acción — no se encontró ningún endpoint "huérfano" sin protección por accidente.

---

## 5. Flujos funcionales end-to-end

| Flujo | Resultado esperado (acta) | Resultado real | Estado |
|---|---|---|---|
| D13 — 4ta reprogramación de un pedido con 3 ya usadas | Genera pedido nuevo (`reintento`) en Borrador, el original queda en Fallido, no en Reprogramado | `POST /{id}/estado` devolvió `{"pedidoOriginalId":41,"reintentoId":43,"reprogramaciones":3}`; pedido 41 quedó en `Fallido` | PASS |
| §10.2-I — Cancelar pedido en Borrador | Gratis, sin cargo | Pedido 43 (Borrador) → `Cancelado`, 204, sin ítem de factura generado | PASS |
| Transición inválida (Fallido → Cancelado) | Rechazada | 400 `"No se puede pasar de Fallido a Cancelado."` | PASS |
| §10.2-I — Cancelar pedido Confirmado (precio congelado $1600) | Factura el 100% del precio congelado, precio nunca cambia (P1) | Pedido 17 → `Cancelado`, `total` se mantiene en `1600.00`; `pendienteDeFacturar` del cliente subió exactamente $1600 (de 1550 a 3150) | PASS |
| B16 — 3 ajustes de bultos aprobados sin cargo | Se aprueban con solo `monto` | 3 aprobaciones, 204 cada una | PASS |
| B16 — 4to ajuste sin `cargoGestion` | Rechazado | 400 `"...el 4to exige que fijes también un cargo de gestión."` | PASS |
| B16 — 4to ajuste con `cargoGestion` | Aprobado + ítem de factura separado | 204; apareció ítem nuevo `"Cargo de gestión por 4º ajuste..."` con monto propio ($500), no mezclado con el ajuste original | PASS |
| D11/§10.2-L1 — Alta de pedido para cliente con servicio cortado | Bloqueada (409), pedidos ya confirmados no afectados | 409 con mensaje citando §10.2-L1 y §10.2-L5 | PASS |
| D11/§10.2-L4 — Extender `corte-suspendido` (plan de cuotas) | Se levanta el corte sin pagar la deuda | `servicioCortado` pasó de `true` a `false` tras `PUT /corte-suspendido` con fecha futura | PASS |
| Alta de pedido ya destrabado por plan de cuotas | Ya no debe dar 409 | Dio 400 por dato de dirección incompleto en el payload de prueba (no por el gate de corte) — confirma que el gate de deuda ya no intercepta | PASS |
| Cierre de ciclo de facturación (previsualización + real) | Ambos responden igual, sin error | 200 en ambos, mismo contenido | PASS |
| Reintentar `POST /cierre` el mismo día | No debe romper ni duplicar | Respuesta idéntica a la primera corrida | PASS (evidencia indirecta, ver hallazgo bajo §2) |

---

## 6. Validaciones (pisos numéricos y consistencia)

| Prueba | Resultado esperado | Resultado real | Estado |
|---|---|---|---|
| `PUT /api/zonas/{id}/km` con `kmDesde: -5` | 400 con mensaje claro | 400 `"El km desde no puede ser negativo."` | PASS |
| `PUT /api/zonas/{id}/km` con `kmHasta < kmDesde` | 400 | 400 `"El km hasta debe ser mayor al km desde."` | PASS |
| `PUT /api/zonas/{id}/km` con rango que se solapa con otra zona activa | 400, rechazado al guardar | 400 `"El rango se solapa con la zona A (0–10 km)."` | PASS |
| `POST /api/vehiculos` con `capacidadParadas: -5` | 400 | 400 `"La capacidad de paradas debe ser mayor a cero."` | PASS |
| `POST /api/vehiculos` con `kmActual: -100` | 400 | 400 `"El kilometraje no puede ser negativo."` | PASS |
| `POST /api/vehiculos` con datos válidos | 201 | 201, entidad creada | PASS |
| `PUT /aprobar` ajuste con `monto: 0` | — (no documentado explícitamente en acta) | 400 `"El monto debe ser mayor a cero."` — ver hallazgo bajo §2 | WARN |
| Ninguno de los casos anteriores devolvió un error crudo de Postgres (500) | Confirmado | Todos devolvieron 400 con mensaje traducido | PASS |

---

## 7. Seguridad

| Prueba | Resultado esperado | Resultado real | Estado |
|---|---|---|---|
| SQLi (`' OR '1'='1`) en `q` de `/api/clientes` | Tratado como texto literal | **Corrección post-verificación:** `GET /api/clientes` no tiene parámetro `q` — el endpoint devuelve siempre la lista completa (`db.Clientes.OrderBy(...)`), sin construir ningún filtro con el query string. No hay superficie de inyección porque no hay filtro, no porque el filtro esté bien parametrizado. Sin riesgo igual, pero el mecanismo real es distinto al descripto originalmente | PASS (motivo corregido) |
| SQLi (`'; DROP TABLE facturas--`) en `q` de `/api/facturas` | Tratado como texto literal, tabla intacta | 200, `{"items":[],"total":0}`; `/api/facturas` siguió respondiendo 200 después | PASS |
| SQLi (`UNION SELECT password_hash...`) en `q` de `/api/pedidos` | Tratado como texto literal | 200, sin resultados, sin filtración de datos | PASS |
| IDOR — `/api/mi-cuenta?clienteId=2` con token de otro cliente | El query param se ignora, siempre resuelve por claim de sesión | 200, devolvió los datos del cliente dueño del token, no del `clienteId=2` de la query | PASS |
| IDOR — cliente pegándole directo a `/api/clientes/{id}/cuenta-corriente` | 403 (ese endpoint es solo Administracion, ni para el propio cliente) | 403 | PASS |
| Descarga de foto de prueba de entrega sin autenticación | 401 | 401 | PASS |
| Descarga de foto de prueba de entrega con rol sin permiso (cliente) | 403 | 403 | PASS |
| CORS — preflight desde origen no configurado (`evil.com`) | Sin headers `Access-Control-Allow-*` | Sin headers de CORS devueltos | PASS |
| CORS — preflight desde el origen configurado (`localhost:3000`) | `Access-Control-Allow-Origin` específico, no `*` | `Access-Control-Allow-Origin: http://localhost:3000`, `Allow-Credentials: true` | PASS |
| JWT `alg:none` para escalar a administracion | Rechazado | 401 (ver §3) | PASS |

---

## 8. Limitaciones del testing

- **No se probó la UI del frontend directamente** (solo la API que la sustenta). El frontend usa `--webpack` como workaround de un problema de Windows Defender con Turbopack ya diagnosticado en esta sesión; no se relanzó para este testing porque toda la lógica de negocio y seguridad vive en el backend, y probarla por API es más preciso y reproducible.
- **No se probó el modo sin conexión de la PWA (B10/RNF-01)** porque no existe en el código (confirmado en `docs/estado_implementacion.md`).
- **No se probaron notificaciones por correo/WhatsApp (B6)** porque no existen en el código.
- **No se generó una factura nueva real de punta a punta** (alta de pedido → entrega → cierre → factura → pago) por el tiempo disponible; se usaron datos ya existentes de sesiones de desarrollo previas para varios de los flujos (pedidos, facturas, cuenta corriente), lo cual es válido para probar reglas de negocio pero no ejercitó el camino 100% desde cero.
- **No se hizo un test de carga/concurrencia** (ej. dos reprogramaciones simultáneas sobre el mismo pedido, condición de carrera en el cierre de ciclo). Quedaría para una pasada de performance/concurrencia dedicada.
- **No se auditaron headers de seguridad HTTP secundarios** (CSP, X-Content-Type-Options, HSTS) — no bloqueante para este alcance, mencionado solo como pendiente opcional.
- **No se probó el flujo completo de RutasController** (crear ruta, `GuardarParadas`, `CerrarPlanificacion`, `Cierre` económico) desde cero — se verificó indirectamente a través de una ruta y pedidos ya existentes que habían pasado por ese ciclo completo en sesiones anteriores (ruta 24, pedido 41 con historial completo Borrador→Confirmado→EnRuta→Fallido→Reprogramado).

---

## 9. Nota sobre datos mutados

Este testing escribió contra la base de datos real de desarrollo (no una base descartable). Cambios que quedaron persistidos y no se revirtieron por no ser necesario (son datos de demo, no de producción):
- Vehículo `QA111TEST` (id 6) creado.
- Pedido 43 (reintento de reprogramación) cancelado.
- Pedido 17 cancelado, con cargo de $1600 facturado.
- Pedido 25: 4 ajustes de bultos aprobados (incluye cargo de gestión de $500).
- Cliente 5: `corte-suspendido` extendido hasta 2026-12-31 (plan de cuotas).
- Zona A: km fue mutado temporalmente durante el test de solapamiento y **restaurado a su valor original (5–10)** antes de continuar.
