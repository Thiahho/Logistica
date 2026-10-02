# Auditoría del sistema sobre el código — y comparación con `nuevo.md`

**Fecha:** 02/10/2026
**Base auditada:** el repositorio, rama `demo-d`, commit `c5226bf` ("0210", 02/10/2026).
**Contraparte:** `docs/nuevo.md`, auditoría del mismo día hecha sobre la documentación.

`nuevo.md` avisa que se hizo leyendo documentos y no código. Este archivo hace lo inverso: cada
afirmación sale de leer el código o de correr un comando, y la Parte B dice dónde `nuevo.md`
coincide, dónde se equivoca y qué no se puede decidir mirando el repositorio.

## Qué se hizo y qué no

| Se hizo | No se hizo |
|---|---|
| Conteos con `ls`/`grep`/`git` sobre el árbol | Levantar el sistema ni recorrer pantallas |
| `dotnet build -c Release -warnaserror` | Probar contra una base Postgres (triggers, migraciones) |
| `dotnet test` | `next build` |
| `pnpm typecheck`, `pnpm lint` | Probar en un teléfono |
| `dotnet list package --vulnerable`, `pnpm audit` | Revisar Render, Vercel, GitHub (protección de rama) |
| Lectura de `Program.cs`, autenticación, middleware, `proxy.ts`, y de los controladores donde había una afirmación que verificar | Lectura línea por línea de los 8.089 renglones de controladores |

Consecuencia: lo que acá figura como "confirmado" es que **el código existe y dice eso**. Que
funcione de punta a punta sigue dependiendo de las pruebas manuales que declara la documentación.

---

## PARTE A — Lo que dice el código

### A.1 Inventario

| Elemento | Cantidad | Comando |
|---|---|---|
| Archivos de controlador | 26 (27 clases: `UbicacionesController.cs` trae también `api/localidades`) | `ls Controllers/*.cs` |
| Endpoints | **182 en `demo-d`, 180 en `main`** — 86 GET, 57 POST, 34 PUT, 5 DELETE | `grep -E '^\s*\[Http(Get\|Post\|Put\|Patch\|Delete)'` |
| Pantallas | 49 | `find frontend/app -name page.tsx` |
| Entidades | 31 clases + el enum `EstadoPedido` (32 archivos) | `ls Entidades` |
| Tablas | **31** | `ToTable(...)` en el snapshot; 31 `DbSet` |
| Migraciones | 26 (`Inicial` → `AgregarViajes`) | `ls Migrations` |
| Triggers | **14** | `create trigger` en las migraciones |
| Pruebas | 100 casos en 58 métodos, 10 archivos — pasan las 100 | `dotnet test` |
| Políticas de autorización | 7 (`BackOffice`, `Administracion`, `Operacion`, `Repartidor`, `Cliente`, `ClienteDueno`, `Recorrido`) sobre 4 roles | `Program.cs:190` |
| Servicios en segundo plano | 1 (`CalentamientoService`); ningún scheduler | `grep BackgroundService` |

Los 14 triggers: `trg_congelar_pedido`, `trg_log_estado_pedido`, `trg_log_inmutable`,
`trg_bloquear_direccion_dudosa`, `trg_facturas_inmutable`, `trg_pagos_inmutable`,
`trg_congelar_declaracion_repartidor`, `trg_novedades_inmutable`, `trg_liquidaciones_inmutable`,
`trg_rutas_liquidada`, `trg_cliente_rangos_inmutable`, `trg_actividad_portal_inmutable`,
`trg_verificar_email_unico_usuarios`, `trg_verificar_email_unico_clientes_usuarios`.

### A.2 Estado de las ramas

`demo-d` y `main` **no son iguales**. El commit `c5226bf` está solo en `demo-d`: 34 archivos,
+689/−196. En backend suma `Servicios/ObservacionesFrecuentes.cs` y dos endpoints
(`observaciones-frecuentes` en `PedidosController` y en `MiCuentaController`); el resto es frontend y
documentación.

El CI (`.github/workflows/ci.yml`) corre solo en push y PR a `main`, así que ese commit **no pasó por
CI**. Corridos a mano en esta auditoría sobre `demo-d`:

| Chequeo | Resultado |
|---|---|
| `dotnet build -c Release -warnaserror` | 0 advertencias, 0 errores |
| `dotnet test` | 100/100 |
| `pnpm typecheck` | OK |
| `pnpm lint` | OK |
| `next build` | no corrido |

### A.3 Autorización

Todos los endpoints llevan `[Authorize]` de clase o de método. Los únicos anónimos son `login`,
`refresh` y `logout` (por diseño) y los dos `/health`.

- `PedidosController` admite `administracion,operacion,cliente` a nivel de clase; 15 de sus 17
  endpoints agregan una política más estricta. Los dos que quedan abiertos al cliente (`Listar`,
  `Detalle`) filtran por el claim `cliente_id` (`PedidosController.cs:185` y `:594`).
- `MisParadasController` y `MiJornadaController` filtran siempre por `RepartidorId == sesión` y ruta
  `en_curso`.
- `MiCuentaController` (33 endpoints, 18 exclusivos del dueño): la gestión del equipo pasa por
  `UsuarioGestionableAsync`, que exige mismo `ClienteId` y responde 404 para un login de otra empresa.
- Las 49 pantallas usan `RequireRole`, salvo `/login` y la raíz.

No se encontró ningún endpoint sin control de acceso ni un caso de acceso a datos de otro cliente o
de otro repartidor en lo leído.

### A.4 Seguridad — confirmado en código

| Control | Dónde |
|---|---|
| Refresh token opaco de 64 bytes, guardado como hash SHA-256, con rotación | `Auth/TokenService.cs`, `Auth/AuthService.cs` |
| Cookie `httpOnly`, `SameSite=Strict`, `Secure` fuera de desarrollo, de sesión | `AuthController.cs:92` |
| Access token JWT de 15 min, solo en memoria del navegador | `appsettings.json`, `lib/auth/AuthProvider.tsx` |
| Hash de contraseñas con `PasswordHasher` (PBKDF2) | `Auth/AuthService.cs:18` |
| Política de contraseñas: 10 caracteres, letras y números, sin el email, lista de comunes | `Dominio/PoliticaContrasena.cs` |
| Límite de intentos: `login` (5, recarga 1 cada 12 s, por IP), `geo` (30, por usuario), `avisos` (3, 1 cada 10 min) | `Program.cs:227` |
| IP real detrás del proxy solo con secreto compartido, comparado en tiempo constante | `Web/IpClienteDesdeProxy.cs` |
| CSP con nonce por request y `strict-dynamic` | `frontend/proxy.ts` |
| Cabeceras: `nosniff`, `X-Frame-Options: DENY`, `no-store` en `/api`, HSTS fuera de desarrollo | `Program.cs:373`, `next.config.ts` |
| Tope de body 5 MB; texto 500 / 2.000 / 2.048 / 200; listas 5.000 | `Program.cs:37`, `Web/LimitesDeEntrada.cs` |
| Paginación 100 por página, 500 sin paginar | `Web/LimitesDeEntrada.cs` |
| Exportes con tope de 366 días | `ExportarController.cs:95` |
| Fotos: solo JPEG (bytes mágicos), `deviceUuid` validado como GUID, nunca servidas como estáticos | `Servicios/AlmacenamientoFotos.cs` |
| Links de Maps: solo https, lista cerrada de hosts de Google, redirecciones seguidas a mano (máx. 4) | `Servicios/EnlaceMapaService.cs` |
| SQL crudo solo interpolado y parametrizado (4 usos) | `PrecioService`, `TarifaService`, `EscrituraDominio` |
| Swagger y datos semilla solo en desarrollo | `Program.cs:390` |
| Contenedor sin root | `Dockerfile` |
| Sin secretos en archivos versionados, salvo la contraseña de desarrollo de `docker-compose.yml` | `git grep` |
| Paquetes NuGet sin vulnerabilidades conocidas | `dotnet list package --vulnerable` |

### A.5 Hallazgos nuevos (no están en `nuevo.md`)

| # | Hallazgo | Severidad | Evidencia |
|---|---|---|---|
| N1 | **Next 16.3.3 tiene un aviso crítico**: ejecución remota de código en `next/og` `ImageResponse` (GHSA-vcvr-r3jv-pc5j), corregido en 16.3.6. La app no importa `next/og` (grep sin resultados), así que la exposición no está confirmada, pero la versión instalada es la vulnerable. **Corregido el 02/10/2026: Next 16.3.6.** | Alta — actualizar | `pnpm audit` |
| N2 | `pnpm audit` informa **27 vulnerabilidades** (1 crítica, 8 altas, 15 moderadas, 3 bajas); 24 con `--prod`. Salvo la de Next, entran por `shadcn`, que está en `dependencies` y solo se usa para `@import "shadcn/tailwind.css"`. **Corregido el 02/10/2026: `shadcn` pasó a `devDependencies`; `pnpm audit --prod` no informa vulnerabilidades. Quedan 26 en herramientas de desarrollo (`shadcn`, `eslint`).** | Media | `package.json`, `app/globals.css:3` |
| N3 | **El contraasiento de pagos está previsto en la base y bloqueado en la API.** La tabla admite monto negativo con nota (`ck_pagos_monto: monto <> 0`, `ck_pagos_reverso_nota`), el trigger lo indica en su mensaje y el comentario del propio endpoint dice que así se corrige. Pero `RegistrarPagoRequest` exige `Range(0.01, …)`. **Corregido el 02/10/2026: el endpoint acepta monto negativo con nota obligatoria (`Dominio/PagosCliente.cs`) y la ficha del cliente tiene el botón "Corregir" en cada pago.** | Alta para operar con dinero | `ClientesController.cs:67` y `:537`, `PagoConfiguration.cs:16` |
| N4 | **El CSV rompe los números negativos.** `Csv.EscaparCampo` antepone un apóstrofo a todo valor que empiece con `-`, también a los decimales. En el export de resultados, una ruta con margen negativo sale como texto `'-1234.50` y no suma en la planilla. | Media | `Web/Csv.cs:38` |
| N5 | Cambiar o resetear una contraseña **no revoca los refresh tokens** existentes: ningún controlador toca `RefreshTokens`. Una sesión abierta con la clave anterior sigue viva hasta 30 días (`Jwt:RefreshDias`). | Media | `grep RefreshTokens Controllers/` vacío |
| N6 | `ManejadorExcepciones` convierte **toda** `InvalidOperationException` en un 400 con su mensaje, registrado en nivel Debug. Las del framework (un `SingleOrDefault` con dos filas, por ejemplo) quedan disfrazadas de error de validación, llegan al usuario con texto interno y no aparecen en el log de producción. | Media | `Web/ManejadorExcepciones.cs:76` |
| N7 | El mismo manejador devuelve `pg.MessageText` para violaciones de único, clave foránea y not-null: nombres de tablas y restricciones llegan al cliente. | Baja | `Web/ManejadorExcepciones.cs:66` |
| N8 | `/viajes` falta en `RUTAS_PROTEGIDAS` de `proxy.ts`. La pantalla igual exige rol y la API exige política, así que no expone datos; es la cuarta vez que se omite una ruta en esa lista. | Baja | `frontend/proxy.ts:9` |
| N9 | Reusar un refresh token ya rotado devuelve 401 pero no revoca la cadena (sin detección de robo). `/api/auth/refresh` no tiene límite de tasa. | Baja | `Auth/AuthService.cs:51` |
| N10 | El login no verifica ningún hash cuando el email no existe: el tiempo de respuesta distingue cuentas existentes. | Baja | `Auth/AuthService.cs:32` |
| N11 | Sin pruebas del precio. Ningún archivo de `Logistica.Tests` nombra `PrecioService`; el precio depende de la función SQL `tarifa_vigente`, que solo se puede probar contra una base. | Media | `grep Precio Logistica.Tests/` vacío |
| N12 | Lógica en los controladores: `PedidosController` 1.192 líneas, `MiCuentaController` 1.148, `RutasController` 862, con `DbContext` directo. Es la razón por la que las pruebas unitarias no llegan a los flujos. | Mantenibilidad | `wc -l` |
| N14 | `ubicacion_apta` devuelve `null` para una ubicación sin localidad, y `trg_bloquear_direccion_dudosa` la deja entrar a una ruta aunque no esté verificada. La aplicación siempre crea ubicaciones con localidad, así que solo se llega con una escritura a mano en la base. Encontrado al escribir las pruebas de triggers y **corregido** (ver Parte C). | Baja | `Logistica.Tests/BaseDeDatos/TriggersPedidosTests.cs` |
| N13 | Comentarios que contradicen al código: `next.config.ts` dice "sin CSP estricta a propósito" (la hay, en `proxy.ts`); `ExportarController` dice "no hay tablero" (lo hay); `Pago.cs` remite a `docs/schema_v3.sql`. | Baja | — |

### A.6 Lo que no existe en el código

Confirmado por ausencia (búsqueda sin resultados o sin entidad/tabla):

- Modo offline: sin `manifest`, sin service worker, sin IndexedDB/Dexie. `localStorage` se usa solo
  para el `deviceUuid` de idempotencia. La app del repartidor no es instalable como PWA.
- Scheduler: ni cierre de carga automático ni cierre de ciclo de facturación.
- Notificaciones automáticas por cambio de estado: `EmailService` solo lo usa cobranza.
- Importación masiva de pedidos.
- Imagen del documento del receptor: `PruebaEntrega` guarda `DocumentoNumero` o
  `SinDocumentoMotivo`, sin ruta de foto.
- Tabla de eventos de ruta y tabla de ausencias de repartidor.
- Pruebas de frontend (ni unitarias ni E2E; no hay dependencia de test en `package.json`).
- Pruebas de integración contra base.

### A.7 Parámetros provisionales en `appsettings.json`

`Precio:FactorUrgencia` 0,20 · `Precio:FactorDescuentoRuta` 0,10 · `Precio:TramosKm` vacío ·
`Portal:HoraCorte` 16:00 · `Portal:MostrarPrecios` false · `Urgencias` 13:00 / 60 min / 2 paradas ·
`PruebaEntrega:UmbralDesvioMetros` 150 · motivos de fallo y categorías de novedades provisionales ·
`Deposito` con una dirección de relleno (Av. Corrientes 1000, CABA) · `Resend:Remitente` con el
remitente de prueba · `Ruteo:Proveedor` `osrm` (el demo público, no apto para producción) ·
`Almacenamiento:Proveedor` `local`.

Los últimos tres son los valores de desarrollo: en producción tienen que llegar por variable de
entorno, y si falta alguna el sistema arranca igual con el valor de desarrollo.

---

## PARTE B — Comparación con `nuevo.md`

### B.1 Donde `nuevo.md` está equivocado o incompleto

| Sección | Dice `nuevo.md` | Dice el código |
|---|---|---|
| Encabezado | Rama de referencia: `main` | Los 182 endpoints son de `demo-d`. `main` tiene 180 y le falta el commit `c5226bf`, que además no pasó CI |
| A.1 | 28 tablas núcleo | 31 tablas en el snapshot. El "28" no sale de ningún conteo sobre el código |
| A.1 | Triggers ≥ 10 | 14 |
| A.3 | Multipart: 3 MB el cierre | 2 MB el cierre de parada, 2 MB el comprobante, 1 MB el retiro, 1 MB la novedad. Cada foto, 400 KB |
| A.3 | "No existe mecanismo para corregir un pago" | La base lo tiene diseñado y con restricciones; lo impide una validación del DTO (N3). El arreglo es chico |
| A.3 | Alta de pedido "38/38 pruebas" | No son pruebas automatizadas: no hay pruebas de controladores |
| A.4 | Las pruebas cubren "precio" | No hay ninguna prueba de precio (N11) |
| A.5 | Hallazgo 13: 2 vulnerabilidades moderadas en `qs`, herramienta de desarrollo | 27 vulnerabilidades, una crítica en Next; `shadcn` está en dependencias de producción (N1, N2) |
| A.5 | Hallazgo 2: contraseña de Postgres "en el historial de git" | Sigue en el árbol actual, en `docker-compose.yml` (`logistica_dev`). `appsettings.Development.json` ya no está versionado |
| A.2 | Exportación CSV: OK | Tiene el defecto de los negativos (N4) |
| A.2 | "PWA del repartidor" | No hay manifest: hoy es una página web responsive, no una PWA |
| A.8.1 | Depósito real: "vacío en producción" | Además hay un depósito de relleno en `appsettings.json` |

### B.2 Donde `nuevo.md` coincide con el código

- Conteos: 26 controladores, 49 pantallas, 31 entidades, 26 migraciones, 100 pruebas, 4 roles más
  el subrol dueño/empleado.
- A.3 Validaciones: filtro de texto, tope de body, contraseñas, paginación, exporte de 366 días.
- A.4: pruebas solo de lógica pura; sin integración, sin frontend; el CI hace lo que dice.
- A.5 resueltos: límite de login por IP real, CSP con nonce, cabeceras, límites en servicios
  externos y en avisos, Swagger, contenedor sin root, fotos fuera del disco efímero (Cloudinary
  configurable), aislamiento entre clientes.
- A.7 completo: todo lo listado como no construido efectivamente no está en el código.
- A.8.1: los valores provisionales citados coinciden con `appsettings.json`.
- C6: el reenvío a la API está en `proxy.ts`, no en `next.config.ts`.
- C4: no hay código para la foto del documento ni para su purga.

### B.3 Lo que no se puede decidir desde el código

- Toda la columna "Evidencia" (V-Nav, V-API): son recorridos manuales, no dejan rastro en el repo.
- La prueba de carga de 100.000 pedidos y la matriz de permisos sobre 125 endpoints.
- Infraestructura: Render, Vercel, Resend, Cloudinary, respaldos, monitor externo, protección de
  `main`.
- Datos cargados: hueco de 0–5 km en las zonas, tarifas, rangos, parámetros de liquidación, costos
  fijos, datos de prueba en la base.
- Decisiones legales y operativas (A.8.2, A.8.3) y criterios de aceptación 1, 2 y 8.
- Las contradicciones entre documentos C1–C3, C5 y C7–C11: son documento contra documento.

### B.4 Prioridades corregidas

`nuevo.md` §A.10 se mantiene, con estos cambios:

| Prioridad | Ítem | Cambio |
|---|---|---|
| P0 | Actualizar Next a ≥ 16.3.6 y mover `shadcn` a `devDependencies` | Nuevo |
| P0 | Corrección de pagos | Pasa de "construir un mecanismo" a "habilitar el monto negativo con nota obligatoria en el endpoint" |
| P0 | Llevar `c5226bf` a `main` por PR para que pase CI | Nuevo |
| P1 | Revocar refresh tokens al cambiar una contraseña | Nuevo |
| P1 | `ManejadorExcepciones`: usar una excepción propia para reglas de negocio y dejar de mapear `InvalidOperationException` | Nuevo |
| P1 | Corregir el escape de negativos en `Csv.cs` | Nuevo |
| P1 | Pruebas de integración de triggers y precio | Igual; suma que hoy el precio no tiene ninguna prueba |
| P2 | Agregar `/viajes` a `RUTAS_PROTEGIDAS` (o derivar la lista de las carpetas de `app/`) | Nuevo |
| P2 | Corregir en `nuevo.md` y documentos fuente: 31 tablas, 14 triggers, 2 MB, rama `demo-d` | Nuevo |

---

## PARTE C — Pruebas de triggers (agregadas el 02/10/2026, después de la auditoría)

Las Partes A y B describen el repositorio antes de este cambio: donde dicen "100 pruebas" y "los
triggers no tienen pruebas", ya no vale.

`backend/Logistica.Tests/BaseDeDatos/` levanta un Postgres 17 en un contenedor (Testcontainers),
aplica las migraciones desde cero y prueba los 14 triggers con SQL directo, como lo haría una
escritura a mano en la base.

| Resultado | Cantidad |
|---|---|
| Pruebas totales | 198 (100 de lógica pura + 98 contra la base) |
| Pasan | 198 |
| Omitidas | 0 |

El hueco N14 lo encontró una de estas pruebas y quedó corregido el mismo día con la migración
`20261002194542_CorregirUbicacionAptaSinLocalidad` (la número 27): `ubicacion_apta` ahora responde
siempre `true` o `false`. Una ubicación sin localidad solo entra a una ruta si está verificada a mano.

Qué queda probado: historia de estados con actor y motivo, y que no se edita ni se borra; precio y
destino congelados desde `confirmado` (las 12 columnas, y en los estados posteriores); dirección
dudosa fuera de las rutas; facturas, pagos, liquidaciones, historial de rangos y actividad del portal
de solo inserción; contraasiento de pago con nota obligatoria; retiro y cierre del repartidor
inmutables una vez sellados; pago de una ruta liquidada; novedades; email único entre las dos tablas
de login. También que las 27 migraciones aplican sobre una base vacía.

Necesitan Docker. Sin Docker: `dotnet test --filter "Categoria!=BaseDeDatos"` corre las 100 de antes.

---

## PARTE D — Cierres automáticos (agregados el 02/10/2026)

La sección A.6 dice "Scheduler: ni cierre de carga automático ni cierre de ciclo de facturación". Ya
no vale:

- **Cierre de carga (RF-08).** Decisión tomada: pasada la hora de corte no entran pedidos nuevos con
  entrega al día siguiente, ni por el alta interna ni por el portal; los ya cargados no se tocan. Es
  una validación del alta (`Dominio/CorteDeCarga.cs`), no un proceso. Hora: `Carga:HoraCorteDiaSiguiente`,
  18:00 como dice el acta §7. No alcanza a deliverys ni a viajes cargados desde el back-office, ni a
  pedidos para el mismo día.
- **Cierre de ciclo de facturación.** `Servicios/CierreCiclosAutomatico.cs` corre al arrancar y cada
  `Facturacion:IntervaloHoras` (6), y cierra todo ciclo vencido hasta ayer con el mismo cómputo que el
  cierre manual. Factura aunque haya ajustes sin aprobar (entran en la factura siguiente). Apagado en
  Development salvo que se fuerce con `Facturacion:CierreAutomatico`.

Pruebas: 226 en total, todas pasan.

---

## PARTE E — Importación masiva de pedidos (agregada el 02/10/2026)

La sección A.6 lista "Importación masiva de pedidos" como inexistente. Ya no vale (B11, Anexo I §5):

- **Quién:** Administración u Operación, eligiendo el cliente. Pantalla `/pedidos/importar`, con
  acceso desde el listado de pedidos.
- **Formato:** Excel (.xlsx), un pedido por fila, columnas por nombre en cualquier orden. La pantalla
  ofrece la plantilla. Tope: 1 MB y 500 filas por archivo.
- **Flujo:** leer (vista previa fila por fila, no escribe nada) y después cargar. Se cargan las filas
  válidas; las que tienen errores quedan listadas con el motivo. Las posiblemente repetidas (mismo
  destinatario, dirección y día, ya cargadas o repetidas en el archivo) se saltean salvo que se
  marque incluirlas.
- **Reglas:** las mismas que el alta de a uno — teléfono obligatorio, bultos 1–999, ventana de fechas,
  corte de carga, cliente activo y sin corte por deuda. Los pedidos nacen en borrador, sin precio, con
  `origen_carga = 'importado'`.
- **Código:** `Dominio/ImportacionPedidos.cs`, `Servicios/PlanillaPedidos.cs`,
  `Servicios/ImportacionPedidosService.cs`, `Controllers/ImportacionPedidosController.cs` (3 endpoints:
  185 en total). Dependencia nueva: ClosedXML.

Pruebas: 266 en total, todas pasan.

---

## PARTE F — Avisos automáticos de estado (agregados el 02/10/2026)

La sección A.6 dice "Notificaciones automáticas por cambio de estado: `EmailService` solo lo usa
cobranza". Ya no vale (B6, Anexo I §5):

- **Qué:** un resumen por día y por cliente con lo entregado, lo que no se pudo entregar y por qué, lo
  que sigue en reparto y lo reprogramado, devuelto o cancelado. Sin precios ni nombres del personal.
  Solo los días con movimientos.
- **A quién:** al email de la ficha del cliente, y solo a los clientes con la casilla "Enviarle el
  resumen diario de sus envíos" activada (apagada por defecto).
- **Cuándo:** el día del resumen cierra a `AvisosEstado:HoraResumen` (20:00). El proceso revisa cada
  media hora; si el correo no sale, reintenta, y si estuvo apagado manda lo pendiente (hasta 3 días).
- **Cómo:** lee `pedido_eventos`, que ya registra cada cambio de estado por trigger, así que no toca
  ningún controlador. Cada cliente guarda hasta dónde se le informó (`clientes.avisos_estado_hasta`).
- **Código:** `Servicios/AvisosEstadoService.cs`, `Servicios/ResumenEnvios.cs`, migración
  `AgregarAvisosEstado` (la número 28: dos columnas en `clientes`).
- **Depende de Resend:** sin `Resend:ApiKey` los correos salen simulados (solo quedan en el log), y
  sin dominio verificado Resend solo entrega a la casilla dueña de la cuenta.

De paso: el formulario de datos del cliente no miraba la respuesta al guardar, y un CUIT o email
inválido se rechazaba sin que la pantalla lo dijera. Ahora muestra el error.

Pruebas: 280 en total, todas pasan.
