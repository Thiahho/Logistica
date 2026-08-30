# Graph Report - Logistica  (2026-08-30)

## Corpus Check
- 65 files · ~51,143 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1436 nodes · 2470 edges · 135 communities (74 shown, 61 thin omitted)
- Extraction: 99% EXTRACTED · 1% INFERRED · 0% AMBIGUOUS · INFERRED: 32 edges (avg confidence: 0.83)
- Token cost: 133,755 input · 0 output

## Community Hubs (Navigation)
- Pricing Rules & Confirmation
- JWT Auth Config
- CSV Export
- Frontend Dependencies
- Localidad & Tarifa Config
- Rutas CRUD Endpoints
- H2 Delivery Proof Requirements
- Shared Form Handlers
- Clientes CRUD Endpoints
- Auth Service & Login
- Common Controller Types
- Pedido Entity
- Pedido Detail & Route Assembly UI
- Error Format & New-Record Pages
- New Pedido & Usuario Forms
- Dev Launch Settings
- TypeScript Config
- List Pages (Clientes/Pedidos/Rutas/Tarifas)
- Ruta Entity & Config
- Usuarios CRUD Endpoints
- DbContext DbSets
- PruebaEntrega Entity & Config
- Vehiculos CRUD Endpoints
- shadcn UI Config
- ParadaRepartidor View Mapping
- EventoCliente Entity & Config
- PedidoEvento Entity & Config
- Cliente Entity & Config
- Backend Controllers Overview
- RutaParada Entity & Config
- Vehiculo Entity
- Exception Handling Middleware
- Form Submit Handlers
- Prueba de Entrega Photo Endpoint
- Tarifas Endpoints
- Cliente Detail Page
- Cierre de Parada Logic
- ParadaPedido & Pedido Config
- Usuario Entity
- H2 Backend Files
- Jornada del Dia DTOs
- Cierre de Parada Request Fields
- Zona Entity & Config
- AgregarVehiculos Migration
- Pricing Service & Options
- MisParadas Endpoints
- TipoEventoCliente Entity & Config
- EstadoPedido Enum
- ReglasDeBaseDeDatos Migration
- AgregarKmZonas Migration
- Route Optimization (Haversine/2-opt)
- Armar Ruta Handlers
- EF Migrations Overview
- Multi-Vehicle Architecture Decisions
- Entity Configurations Namespace
- Seed Data
- Domain Write Discipline
- Inicial Migration
- Route Planning Requirements
- Current User Claims Helpers
- Backend NuGet Dependencies
- EF Model Snapshot
- EscrituraDominio Service
- Deposito Options
- PruebaEntrega Options
- Ubicacion Config
- Usuario Config
- Migration Builder Types
- Pre-Launch Checklist & Backups
- Pedido State Machine Rules
- Tarifas List Handlers
- Export & H4 Milestone
- Anti-Overengineering Principle
- Session Proxy Middleware
- Frontend Agent Rules Docs
- Cliente Tarifas Handlers
- Danger Zone Handlers
- Capacity-in-Stops Principle
- Deposit Consolidation Provision
- Sync Idempotency Decision
- Capture Time Decision
- One-Handed PWA Design Rule
- Full Route Download Rule
- ESLint Config
- Next.js Config
- PostCSS Config
- Localidad Concept
- Postgres Docker Service
- Acceptance Criterion: Fast Order Entry
- Acceptance Criterion: Timed Route Assembly
- Acceptance Criterion: Close a Route
- Acceptance Criterion: Nonexistent Address
- Import Format Decision
- PWA First-Version Milestone
- Record-Everything Principle
- No Double-Entry Principle
- Fixed Cutoff Time Rule
- Signed Count Rule
- Separate Planner/Driver Rule
- Access-Not-Priority Rule
- Single Urgency Window Rule
- Density-Over-Volume Risk
- Plan-vs-Drive Risk
- Cutoff Pressure Risk
- Personal Data Retention Requirement
- Clientes Table
- EventosCliente Table
- Localidades Table
- PedidoEventos Table
- Pedidos Table
- Tarifas Table
- TiposEventoCliente Table
- Ubicaciones Table
- Zonas Table
- Environment Config Variables
- Photo Compression Decision
- Retry Backoff Decision
- Login Screen
- New Route Screen
- Write-Through-Queue Rule
- Immutable Migrations Rule
- No Client-Side Credentials Rule
- Hosting TBD
- Next.js Stack Choice
- Postgres Stack Choice
- Client-Side Routing Algorithm Choice
- Tailwind/shadcn Stack Choice
- pnpm Workspace Config
- Default Next.js Icon
- Globe Icon Asset
- Next.js Wordmark Asset
- Vercel Logo Asset
- Window Icon Asset
- Frontend README (Boilerplate)

## God Nodes (most connected - your core abstractions)
1. `LogisticaDbContext` - 56 edges
2. `Logistica.Entidades` - 54 edges
3. `useAuth()` - 49 edges
4. `Pedido` - 37 edges
5. `cn()` - 37 edges
6. `Ruta` - 28 edges
7. `leerError()` - 27 edges
8. `Logistica.Datos` - 25 edges
9. `ClientesController` - 23 edges
10. `RutasController` - 23 edges

## Surprising Connections (you probably didn't know these)
- `EscrituraDominio.GuardarComoAsync` --references--> `fn_log_estado_pedido()`  [EXTRACTED]
  docs/construccion_v1.md → docs/schema_v3.sql
- `Decisión: reordenamiento por flechas, sin drag & drop` --semantically_similar_to--> `P6: Se construye por dolor, no por catálogo`  [INFERRED] [semantically similar]
  docs/construccion_v1.md → docs/acta_sistema_v3.md
- `PrecioService.CotizarAsync` --references--> `tarifa_vigente()`  [EXTRACTED]
  docs/construccion_v1.md → docs/schema_v3.sql
- `Fórmula de cálculo de precio` --references--> `trg_congelar_pedido`  [EXTRACTED]
  docs/construccion_v1.md → docs/schema_v3.sql
- `DetalleCliente()` --calls--> `useAuth()`  [EXTRACTED]
  frontend/app/clientes/[id]/page.tsx → frontend/lib/auth/AuthProvider.tsx

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Route planning and capacity enforcement flow** — docs_construccion_v1_pantalla_armar_ruta, frontend_lib_dominio_ruteo, docs_acta_sistema_v3_p7_capacidad_en_paradas, docs_acta_sistema_v3_rf_16 [INFERRED 0.80]
- **Offline capture and sync guarantee group** — docs_construccion_v1_sincronizacion_offline, docs_construccion_v1_dexie_tabla_capturas, docs_acta_sistema_v3_rnf_01, docs_acta_sistema_v3_rnf_02, docs_acta_sistema_v3_rnf_03, docs_acta_sistema_v3_criterio_aceptacion_3 [INFERRED 0.85]
- **JWT auth and role-based access flow** — backend_logistica_auth_tokenservice, backend_logistica_auth_authservice, backend_logistica_auth_currentuser, docs_acta_sistema_v3_rnf_08, docs_construccion_v1_stack_jwt_auth [INFERRED 0.80]

## Communities (135 total, 61 thin omitted)

### Community 0 - "Pricing Rules & Confirmation"
Cohesion: 0.06
Nodes (53): auth, auth.users, PrecioService.CotizarAsync, Acta del Sistema de Gestión Logística v3, Criterio 5: auditar un pedido cualquiera, Criterio 7: intentar modificar precio de pedido confirmado, P1: El precio se congela en el pedido, P3: Precio por zona, ruta por coordenada (+45 more)

### Community 1 - "JWT Auth Config"
Cohesion: 0.04
Nodes (39): OpcionesJwt, AccessMinutos, Audience, Issuer, Key, RefreshDias, Guid, IOptions (+31 more)

### Community 2 - "CSV Export"
Cohesion: 0.07
Nodes (35): ActualizarKmRequest, CancellationToken, DateOnly, HttpGet, IActionResult, Task, ExportarController, CancellationToken (+27 more)

### Community 3 - "Frontend Dependencies"
Cohesion: 0.04
Nodes (46): @base-ui/react, class-variance-authority, clsx, eslint, eslint-config-next, dependencies, @base-ui/react, class-variance-authority (+38 more)

### Community 4 - "Localidad & Tarifa Config"
Cohesion: 0.05
Nodes (37): EntityTypeBuilder, LocalidadConfiguration, EntityTypeBuilder, TarifaConfiguration, Localidad, Cp, Id, Nombre (+29 more)

### Community 5 - "Rutas CRUD Endpoints"
Cohesion: 0.11
Nodes (30): ActualizarRutaRequest, Authorize, CancellationToken, DateOnly, DateTimeOffset, Guid, HttpDelete, HttpGet (+22 more)

### Community 6 - "H2 Delivery Proof Requirements"
Cohesion: 0.05
Nodes (41): Campo desvio_metros (pruebas_entrega), Campo salida_en (ruta_paradas), Campo zonas.km_desde / km_hasta, Criterio 3: prueba de modo avión, Decisión: modelo de usuarios con cuatro roles, RF-09: Precio por cliente y zona, RF-18: Lista de paradas del día con progreso, RF-19: Cambio de estado por parada (+33 more)

### Community 7 - "Shared Form Handlers"
Cohesion: 0.08
Nodes (26): FormularioNuevoCliente(), ListaClientes(), ListaParadas(), ParadaRepartidor, geistMono, geistSans, metadata, FormularioLogin() (+18 more)

### Community 8 - "Clientes CRUD Endpoints"
Cohesion: 0.12
Nodes (28): ActualizarClienteRequest, Authorize, CancellationToken, DateTimeOffset, Dictionary, HttpDelete, HttpGet, HttpPost (+20 more)

### Community 9 - "Auth Service & Login"
Cohesion: 0.11
Nodes (24): AccessTokenResponse, ActionResult, AllowAnonymous, CancellationToken, DateTimeOffset, Task, Usuario, AuthService (+16 more)

### Community 10 - "Common Controller Types"
Cohesion: 0.14
Nodes (26): Authorize, CancellationToken, DateOnly, DateTimeOffset, HttpGet, HttpPost, IActionResult, IOptions (+18 more)

### Community 11 - "Pedido Entity"
Cohesion: 0.06
Nodes (33): DateOnly, DateTimeOffset, Pedido, Bultos, Cliente, ClienteId, CreadoEn, DescuentoRuta (+25 more)

### Community 12 - "Pedido Detail & Route Assembly UI"
Cohesion: 0.09
Nodes (26): DetallePedido(), confirmarTransicion(), ParadaConsolidada, CON_MOTIVO_OBLIGATORIO, ETIQUETA_TRANSICION, motivoObligatorio(), TRANSICIONES, transicionesDisponibles() (+18 more)

### Community 13 - "Error Format & New-Record Pages"
Cohesion: 0.17
Nodes (12): RFC-7807, REPORTES, DatosVehiculo(), ZonaPeligro(), Card(), CardContent(), CardDescription(), CardHeader() (+4 more)

### Community 14 - "New Pedido & Usuario Forms"
Cohesion: 0.12
Nodes (22): DesglosePrecio, FormularioAlta(), hoyISO(), UbicacionResuelta, ROLES, CardAction(), CardFooter(), Checkbox() (+14 more)

### Community 15 - "Dev Launch Settings"
Cohesion: 0.07
Nodes (28): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, launchUrl, applicationUrl (+20 more)

### Community 16 - "TypeScript Config"
Cohesion: 0.07
Nodes (28): compilerOptions, allowJs, esModuleInterop, incremental, isolatedModules, jsx, lib, module (+20 more)

### Community 17 - "List Pages (Clientes/Pedidos/Rutas/Tarifas)"
Cohesion: 0.25
Nodes (11): COLOR_CLASE, CabeceraSesion(), Button(), buttonVariants, Table(), TableBody(), TableCell(), TableHead() (+3 more)

### Community 18 - "Ruta Entity & Config"
Cohesion: 0.08
Nodes (23): EntityTypeBuilder, RutaConfiguration, DateOnly, DateTimeOffset, Guid, Usuario, Ruta, CapacidadParadas (+15 more)

### Community 19 - "Usuarios CRUD Endpoints"
Cohesion: 0.18
Nodes (19): ActivoRequest, ActualizarUsuarioRequest, Authorize, CancellationToken, Guid, HttpGet, HttpPost, HttpPut (+11 more)

### Community 20 - "DbContext DbSets"
Cohesion: 0.08
Nodes (24): Usuario, LogisticaDbContext, Clientes, EventosCliente, Localidades, ParadaPedidos, PedidoEventos, Pedidos (+16 more)

### Community 21 - "PruebaEntrega Entity & Config"
Cohesion: 0.09
Nodes (20): EntityTypeBuilder, PruebaEntregaConfiguration, DateTimeOffset, PruebaEntrega, CapturadaEn, DesvioMetros, DeviceUuid, FotoPath (+12 more)

### Community 22 - "Vehiculos CRUD Endpoints"
Cohesion: 0.22
Nodes (16): ActualizarVehiculoRequest, Authorize, CancellationToken, DateOnly, HttpGet, HttpPost, HttpPut, IActionResult (+8 more)

### Community 23 - "shadcn UI Config"
Cohesion: 0.09
Nodes (21): aliases, components, hooks, lib, ui, utils, iconLibrary, menuAccent (+13 more)

### Community 24 - "ParadaRepartidor View Mapping"
Cohesion: 0.10
Nodes (19): DateTimeOffset, ModelBuilder, ParadaRepartidor, Bultos, CalleNumero, DestinatarioNombre, DestinatarioTelefono, Estado (+11 more)

### Community 25 - "EventoCliente Entity & Config"
Cohesion: 0.11
Nodes (16): EntityTypeBuilder, EventoClienteConfiguration, DateTimeOffset, Guid, EventoCliente, Cliente, ClienteId, Id (+8 more)

### Community 26 - "PedidoEvento Entity & Config"
Cohesion: 0.11
Nodes (16): EntityTypeBuilder, PedidoEventoConfiguration, DateTimeOffset, Guid, PedidoEvento, ActorTexto, ActorTipo, ActorUsuario (+8 more)

### Community 27 - "Cliente Entity & Config"
Cohesion: 0.12
Nodes (15): EntityTypeBuilder, ClienteConfiguration, DateTimeOffset, Cliente, Activo, ColorOper, ColorPago, ColorTrato (+7 more)

### Community 28 - "Backend Controllers Overview"
Cohesion: 0.23
Nodes (4): Logistica.Controllers, Logistica.Datos, Logistica.Auth, Regla: [Authorize] de clase y de acción se combinan con AND

### Community 29 - "RutaParada Entity & Config"
Cohesion: 0.12
Nodes (14): EntityTypeBuilder, RutaParadaConfiguration, DateTimeOffset, RutaParada, Anclada, Estado, Id, LlegadaEn (+6 more)

### Community 30 - "Vehiculo Entity"
Cohesion: 0.12
Nodes (16): DateOnly, DateTimeOffset, Vehiculo, Activo, Anio, CapacidadParadas, CostoKm, CreadoEn (+8 more)

### Community 31 - "Exception Handling Middleware"
Cohesion: 0.16
Nodes (12): CancellationToken, ManejadorExcepciones, Logistica.Web, Detalle, Regla: nada de lógica de negocio duplicada entre trigger y aplicación, Exception, HttpContext, IExceptionHandler (+4 more)

### Community 32 - "Form Submit Handlers"
Cohesion: 0.17
Nodes (15): FormularioExportar(), descargar(), hoyISO(), CierreRuta(), cerrar(), FormularioNuevaRuta(), onSubmit(), hoyISO() (+7 more)

### Community 33 - "Prueba de Entrega Photo Endpoint"
Cohesion: 0.17
Nodes (12): Authorize, CancellationToken, HttpGet, IActionResult, Task, PruebasEntregaController, CancellationToken, IOptions (+4 more)

### Community 34 - "Tarifas Endpoints"
Cohesion: 0.18
Nodes (12): CancellationToken, HttpGet, HttpPut, IActionResult, Task, FijarTarifaRequest, TarifaGeneral, TarifasController (+4 more)

### Community 35 - "Cliente Detail Page"
Cohesion: 0.13
Nodes (10): ClienteDetalle, Color, COLORES, DatosCliente(), DetalleCliente(), DIMENSION_LABEL, EventoResumen, EventosCliente() (+2 more)

### Community 36 - "Cierre de Parada Logic"
Cohesion: 0.15
Nodes (8): Geo, Dictionary, TransicionesPedido, CerrarParadaRequest, Consumes, DbUpdateException, HashSet, RequestSizeLimit

### Community 37 - "ParadaPedido & Pedido Config"
Cohesion: 0.16
Nodes (10): EntityTypeBuilder, ParadaPedidoConfiguration, EntityTypeBuilder, PedidoConfiguration, Pedido, ParadaPedido, Parada, ParadaId (+2 more)

### Community 38 - "Usuario Entity"
Cohesion: 0.14
Nodes (13): DateTimeOffset, Guid, Roles, Usuario, Activo, Cliente, ClienteId, CreadoEn (+5 more)

### Community 39 - "H2 Backend Files"
Cohesion: 0.21
Nodes (5): TransicionesPedido.MotivoObligatorio(nuevo), TransicionesPedido.Permitida(actual, nuevo), Logistica.Servicios, Logistica.Dominio, Logistica.Opciones

### Community 40 - "Jornada del Dia DTOs"
Cohesion: 0.20
Nodes (12): DateOnly, DateTimeOffset, IOptions, List, CierreResultado, JornadaDelDia, MisParadasController, ParadaDelDia (+4 more)

### Community 41 - "Cierre de Parada Request Fields"
Cohesion: 0.17
Nodes (12): CerrarParadaRequest, CapturadaEn, DeviceUuid, Foto, IdentidadVerificada, Lat, LlegadaEn, Lng (+4 more)

### Community 42 - "Zona Entity & Config"
Cohesion: 0.18
Nodes (9): EntityTypeBuilder, ZonaConfiguration, Zona, Activa, Codigo, Id, KmDesde, KmHasta (+1 more)

### Community 43 - "AgregarVehiculos Migration"
Cohesion: 0.18
Nodes (8): DateOnly, DateTimeOffset, MigrationBuilder, DateOnly, DateTimeOffset, Guid, ModelBuilder, AgregarVehiculos

### Community 44 - "Pricing Service & Options"
Cohesion: 0.18
Nodes (9): OpcionesPrecio, FactorDescuentoRuta, FactorUrgencia, CancellationToken, DateOnly, IOptions, Task, DesglosePrecio (+1 more)

### Community 45 - "MisParadas Endpoints"
Cohesion: 0.31
Nodes (7): CancellationToken, HttpGet, HttpPost, IActionResult, Task, PedidoDeParada, RegistrarLlegadaRequest

### Community 46 - "TipoEventoCliente Entity & Config"
Cohesion: 0.22
Nodes (8): EntityTypeBuilder, TipoEventoClienteConfiguration, TipoEventoCliente, Codigo, Descripcion, Dimension, Id, IEntityTypeConfiguration

### Community 47 - "EstadoPedido Enum"
Cohesion: 0.20
Nodes (9): EstadoPedido, Borrador, Cancelado, Confirmado, Devuelto, EnRuta, Entregado, Fallido (+1 more)

### Community 48 - "ReglasDeBaseDeDatos Migration"
Cohesion: 0.22
Nodes (6): MigrationBuilder, DateOnly, DateTimeOffset, Guid, ModelBuilder, ReglasDeBaseDeDatos

### Community 49 - "AgregarKmZonas Migration"
Cohesion: 0.22
Nodes (6): MigrationBuilder, DateOnly, DateTimeOffset, Guid, ModelBuilder, AgregarKmZonas

### Community 50 - "Route Optimization (Haversine/2-opt)"
Cohesion: 0.36
Nodes (8): Decisión: geocodificador cacheado, nearest-neighbor sin matriz de distancias, Stack: Nominatim (OpenStreetMap), server-only, cacheado, haversine(), Punto, mejorar2Opt(), nearestNeighbor(), ParadaParaOrden, sugerirOrden()

### Community 51 - "Armar Ruta Handlers"
Cohesion: 0.29
Nodes (6): ArmarRuta(), guardarAsignacion(), guardarParadas(), onCerrarPlanificacion(), onGuardar(), sugerir()

### Community 53 - "Multi-Vehicle Architecture Decisions"
Cohesion: 0.22
Nodes (9): Decisión: origen del pedido a nivel pedido, colapso en ruta, Previsión de arquitectura: multi-vehículo, Regla de crecimiento de flota (85% capacidad, 3 semanas), Regla de incorporación de clientes por geografía, Tabla parada_pedidos, Tabla ruta_paradas, Tabla rutas, Tabla vehiculos (+1 more)

### Community 54 - "Entity Configurations Namespace"
Cohesion: 0.29
Nodes (4): EntityTypeBuilder, VehiculoConfiguration, Vehiculo, Logistica.Datos.Configuraciones

### Community 55 - "Seed Data"
Cohesion: 0.25
Nodes (7): CancellationToken, Task, DatosSemilla, Localidad, OpcionesDeposito, TipoEventoCliente, Ubicacion

### Community 56 - "Domain Write Discipline"
Cohesion: 0.29
Nodes (8): EscrituraDominio.GuardarComoAsync, P2: Todo cambio de estado se registra por trigger, RNF-04: Trazabilidad automática por trigger, Decisión: reemplazo de Supabase por API .NET propia, Hito H0: proyecto, esquema, auth, Regla: todo evento de dominio usa EscrituraDominio.GuardarComoAsync, Stack: ASP.NET Core 8 + Entity Framework Core, Stack: JWT propio, cuatro roles

### Community 57 - "Inicial Migration"
Cohesion: 0.29
Nodes (6): DateOnly, DateTimeOffset, Guid, MigrationBuilder, Inicial, Migration

### Community 58 - "Route Planning Requirements"
Cohesion: 0.25
Nodes (8): RF-11: Orden de paradas sugerido automáticamente, RF-12: Reordenamiento manual de paradas, RF-13: Anclaje de paradas urgentes, RF-14: Consolidación de retiros en una parada, RF-15: Asignación de vehículo y repartidor, RF-16: Validación de capacidad en paradas, Decisión: reordenamiento por flechas, sin drag & drop, Pantalla: Armar ruta

### Community 59 - "Current User Claims Helpers"
Cohesion: 0.38
Nodes (3): Guid, CurrentUserExtensions, ClaimsPrincipal

### Community 60 - "Backend NuGet Dependencies"
Cohesion: 0.29
Nodes (7): Logistica, net8.0, Microsoft.AspNetCore.Authentication.JwtBearer (8.0.10), Microsoft.EntityFrameworkCore.Design (8.0.10), Npgsql.EntityFrameworkCore.PostgreSQL (8.0.10), Swashbuckle.AspNetCore (6.6.2), Microsoft.NET.Sdk.Web

### Community 61 - "EF Model Snapshot"
Cohesion: 0.29
Nodes (6): DateOnly, DateTimeOffset, Guid, ModelBuilder, LogisticaDbContextModelSnapshot, ModelSnapshot

### Community 62 - "EscrituraDominio Service"
Cohesion: 0.33
Nodes (4): CancellationToken, Guid, Task, EscrituraDominio

### Community 63 - "Deposito Options"
Cohesion: 0.33
Nodes (5): OpcionesDeposito, CalleNumero, Lat, Lng, Localidad

### Community 64 - "PruebaEntrega Options"
Cohesion: 0.33
Nodes (5): OpcionesPruebaEntrega, MotivosFallo, RaizFotos, TamanoMaximoKb, UmbralDesvioMetros

### Community 65 - "Ubicacion Config"
Cohesion: 0.50
Nodes (3): EntityTypeBuilder, UbicacionConfiguration, Ubicacion

### Community 66 - "Usuario Config"
Cohesion: 0.50
Nodes (3): EntityTypeBuilder, UsuarioConfiguration, Usuario

### Community 67 - "Migration Builder Types"
Cohesion: 0.40
Nodes (4): DateOnly, DateTimeOffset, Guid, ModelBuilder

### Community 68 - "Pre-Launch Checklist & Backups"
Cohesion: 0.40
Nodes (5): Decisiones abiertas antes de la primera ruta, Regla: el corte de carga no se cede, RF-08: Cierre automático de carga a hora de corte, RNF-10: Respaldo diario con restauración verificada, Checklist: antes de la primera ruta

### Community 69 - "Pedido State Machine Rules"
Cohesion: 0.40
Nodes (5): Regla: bulto no entregado vuelve y el retorno se registra, Regla: primer reintento sin cargo, segundo genera envío nuevo, RF-17: Cierre de ruta como planificada, Decisión: cerrar-planificacion adelanta confirmado→en_ruta, Máquina de estados del pedido

### Community 70 - "Tarifas List Handlers"
Cohesion: 0.70
Nodes (5): ListaTarifas(), guardar(), valorKmDesde(), valorKmHasta(), valorPrecio()

### Community 71 - "Export & H4 Milestone"
Cohesion: 0.50
Nodes (4): Criterio 8: operar tres rutas reales consecutivas, RF-30: Exportación tabular de pedidos/rutas/resultados, Hito H4: solo lo que rompió la calle, Pantalla: Exportar

### Community 72 - "Anti-Overengineering Principle"
Cohesion: 0.50
Nodes (4): Etapas siguientes habilitadas por hecho, no por fecha, P6: Se construye por dolor, no por catálogo, Riesgo: sobreconstrucción, Restricción: 13 tablas del esquema como techo hasta el tercer cliente

### Community 74 - "Frontend Agent Rules Docs"
Cohesion: 0.67
Nodes (3): frontend AGENTS.md (Next.js agent rules), Next.js breaking-changes agent-rules block, frontend CLAUDE.md

## Knowledge Gaps
- **463 isolated node(s):** `ParadaRepartidor`, `Roles`, `DireccionNominatim`, `LoginRequest`, `Borrador` (+458 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **61 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Logistica.Entidades` connect `EF Migrations Overview` to `JWT Auth Config`, `Localidad & Tarifa Config`, `Pedido Entity`, `Ruta Entity & Config`, `PruebaEntrega Entity & Config`, `EventoCliente Entity & Config`, `PedidoEvento Entity & Config`, `Cliente Entity & Config`, `Backend Controllers Overview`, `RutaParada Entity & Config`, `Vehiculo Entity`, `ParadaPedido & Pedido Config`, `Usuario Entity`, `H2 Backend Files`, `Zona Entity & Config`, `TipoEventoCliente Entity & Config`, `EstadoPedido Enum`, `Entity Configurations Namespace`, `Ubicacion Config`, `Usuario Config`?**
  _High betweenness centrality (0.430) - this node is a cross-community bridge._
- **Why does `LogisticaDbContext` connect `DbContext DbSets` to `JWT Auth Config`, `CSV Export`, `Rutas CRUD Endpoints`, `Clientes CRUD Endpoints`, `Auth Service & Login`, `Common Controller Types`, `Ruta Entity & Config`, `Usuarios CRUD Endpoints`, `Vehiculos CRUD Endpoints`, `ParadaRepartidor View Mapping`, `Prueba de Entrega Photo Endpoint`, `Tarifas Endpoints`, `Jornada del Dia DTOs`, `Zona Entity & Config`, `Pricing Service & Options`, `EF Migrations Overview`, `Entity Configurations Namespace`, `Seed Data`, `EscrituraDominio Service`?**
  _High betweenness centrality (0.111) - this node is a cross-community bridge._
- **Why does `Máquina de estados del pedido` connect `Pedido State Machine Rules` to `Domain Write Discipline`, `H2 Backend Files`?**
  _High betweenness centrality (0.064) - this node is a cross-community bridge._
- **What connects `ParadaRepartidor`, `Roles`, `DireccionNominatim` to the rest of the system?**
  _463 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Pricing Rules & Confirmation` be split into smaller, more focused modules?**
  _Cohesion score 0.06079664570230608 - nodes in this community are weakly interconnected._
- **Should `JWT Auth Config` be split into smaller, more focused modules?**
  _Cohesion score 0.04421768707482993 - nodes in this community are weakly interconnected._
- **Should `CSV Export` be split into smaller, more focused modules?**
  _Cohesion score 0.06845513413506013 - nodes in this community are weakly interconnected._