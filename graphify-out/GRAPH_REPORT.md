# Graph Report - Logistica  (2026-08-28)

## Corpus Check
- Corpus is ~26,405 words - fits in a single context window. You may not need a graph.

## Summary
- 842 nodes · 1236 edges · 47 communities (37 shown, 10 thin omitted)
- Extraction: 98% EXTRACTED · 2% INFERRED · 0% AMBIGUOUS · INFERRED: 23 edges (avg confidence: 0.81)
- Token cost: 274,269 input · 0 output

## Community Hubs (Navigation)
- Frontend Pages & Layout
- Pedido Event & State Config
- Frontend Dependencies
- System Charter & Principles
- Backend Controllers & DbContext
- Auth Service & Tokens
- Pedido Entity Model
- Seed Data & Paradas
- Database Schema & Triggers
- Pedidos Controller Endpoints
- Backend Launch Settings
- Frontend TypeScript Config
- Parada/Ruta EF Config
- Localidad & Ubicacion Config
- Ruta Entity Config
- PruebaEntrega Config
- shadcn UI Components Config
- DbContext DbSets
- EventoCliente Config
- Cliente Entity Config
- RefreshToken Config
- Ubicaciones Controller
- Misc EF Configurations
- Tarifa Entity Config
- Usuario Entity
- Geocodificacion Service
- TipoEventoCliente Config
- MisParadas & Localidades Controllers
- Jwt Options
- Backend Project Dependencies
- CurrentUser Claims Helpers
- Clientes Controller
- Zonas Controller
- EscrituraDominio Write Helper
- Pedido EF Config
- Zona EF Config
- Frontend Auth Proxy Middleware
- ESLint Config
- Next.js Config
- PostCSS Config
- pnpm Workspace Config
- Generic Next.js Icon Asset
- Generic Next.js Icon Asset
- Generic Next.js Logo Asset
- Generic Next.js Logo Asset
- Generic Next.js Icon Asset

## God Nodes (most connected - your core abstractions)
1. `LogisticaDbContext` - 46 edges
2. `Logistica.Entidades` - 42 edges
3. `Pedido` - 37 edges
4. `cn()` - 36 edges
5. `Acta del Sistema de Gestión Logística v3.0` - 27 edges
6. `Ruta` - 25 edges
7. `PruebaEntrega` - 23 edges
8. `Documento de construcción — Sistema de gestión logística v1.0` - 23 edges
9. `ParadaRepartidor` - 21 edges
10. `EventoCliente` - 21 edges

## Surprising Connections (you probably didn't know these)
- `Postgres Service (docker-compose)` --semantically_similar_to--> `Stack tecnológico (Next.js, Supabase, Tailwind, Dexie, Vercel)`  [INFERRED] [semantically similar]
  docker-compose.yml → docs/construccion_v1.md
- `frontend README.md (create-next-app)` --semantically_similar_to--> `Stack tecnológico (Next.js, Supabase, Tailwind, Dexie, Vercel)`  [INFERRED] [semantically similar]
  frontend/README.md → docs/construccion_v1.md
- `frontend AGENTS.md (Next.js agent rules)` --conceptually_related_to--> `Stack tecnológico (Next.js, Supabase, Tailwind, Dexie, Vercel)`  [INFERRED]
  frontend/AGENTS.md → docs/construccion_v1.md
- `AuthService` --references--> `LogisticaDbContext`  [EXTRACTED]
  backend/Logistica/Auth/AuthService.cs → backend/Logistica/Datos/LogisticaDbContext.cs
- `TokenService` --references--> `OpcionesJwt`  [EXTRACTED]
  backend/Logistica/Auth/TokenService.cs → backend/Logistica/Auth/OpcionesJwt.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Principios estructurales P1–P7** — docs_acta_sistema_v3_p1_precio_congelado, docs_acta_sistema_v3_p2_registro_automatico_estado, docs_acta_sistema_v3_p3_precio_zona_ruta_coordenada, docs_acta_sistema_v3_p4_registrar_gratis_reconstruir_imposible, docs_acta_sistema_v3_p5_dato_unico, docs_acta_sistema_v3_p6_construir_por_dolor, docs_acta_sistema_v3_p7_capacidad_en_paradas [EXTRACTED 1.00]
- **Orden de construcción H0–H4** — docs_construccion_v1_h0_setup, docs_construccion_v1_h1_alta_pedido, docs_construccion_v1_h2_pwa_offline, docs_construccion_v1_h3_armado_ruta, docs_construccion_v1_h4_exportar [EXTRACTED 1.00]
- **Requisitos no funcionales de offline implementados por la cola de sincronización** — docs_construccion_v1_cola_offline, docs_acta_sistema_v3_rnf01_offline, docs_acta_sistema_v3_rnf02_idempotencia, docs_acta_sistema_v3_rnf03_hora_captura, docs_acta_sistema_v3_rnf07_degradacion_segura [INFERRED 0.85]

## Communities (47 total, 10 thin omitted)

### Community 0 - "Frontend Pages & Layout"
Cohesion: 0.06
Nodes (58): ListaParadas(), ParadaRepartidor, geistMono, geistSans, metadata, LoginPage(), onSubmit(), ListaEnvios() (+50 more)

### Community 1 - "Pedido Event & State Config"
Cohesion: 0.04
Nodes (40): EntityTypeBuilder, PedidoEventoConfiguration, ModelBuilder, EstadoPedido, Borrador, Cancelado, Confirmado, Devuelto (+32 more)

### Community 2 - "Frontend Dependencies"
Cohesion: 0.04
Nodes (46): @base-ui/react, class-variance-authority, clsx, eslint, eslint-config-next, dependencies, @base-ui/react, class-variance-authority (+38 more)

### Community 3 - "System Charter & Principles"
Cohesion: 0.06
Nodes (47): Postgres Service (docker-compose), Previsión de arquitectura: consolidación en depósito propio, Regla: el corte de carga no se cede, Acta del Sistema de Gestión Logística v3.0, RF-32 — Tres indicadores independientes por cliente, Previsión de arquitectura: multi-vehículo, P1 — El precio se congela en el pedido, P2 — Todo cambio de estado se registra automáticamente (+39 more)

### Community 4 - "Backend Controllers & DbContext"
Cohesion: 0.08
Nodes (20): Zona, Activa, Codigo, Id, Nombre, DateOnly, DateTimeOffset, Guid (+12 more)

### Community 5 - "Auth Service & Tokens"
Cohesion: 0.10
Nodes (27): AccessTokenResponse, ActionResult, AllowAnonymous, CancellationToken, DateTimeOffset, Task, AuthService, ResultadoLogin (+19 more)

### Community 6 - "Pedido Entity Model"
Cohesion: 0.06
Nodes (33): DateOnly, DateTimeOffset, Pedido, Bultos, Cliente, ClienteId, CreadoEn, DescuentoRuta (+25 more)

### Community 7 - "Seed Data & Paradas"
Cohesion: 0.07
Nodes (28): CancellationToken, Task, DatosSemilla, DateTimeOffset, ParadaRepartidor, Bultos, CalleNumero, DestinatarioNombre (+20 more)

### Community 8 - "Database Schema & Triggers"
Cohesion: 0.15
Nodes (28): auth, auth.users, clientes, eventos_cliente, fn_bloquear_direccion_dudosa(), fn_congelar_pedido(), fn_log_estado_pedido(), fn_log_inmutable() (+20 more)

### Community 9 - "Pedidos Controller Endpoints"
Cohesion: 0.11
Nodes (23): Authorize, CancellationToken, DateOnly, HttpGet, HttpPost, IActionResult, Task, CotizarRequest (+15 more)

### Community 10 - "Backend Launch Settings"
Cohesion: 0.07
Nodes (28): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, launchUrl, applicationUrl (+20 more)

### Community 11 - "Frontend TypeScript Config"
Cohesion: 0.07
Nodes (28): compilerOptions, allowJs, esModuleInterop, incremental, isolatedModules, jsx, lib, module (+20 more)

### Community 12 - "Parada/Ruta EF Config"
Cohesion: 0.08
Nodes (21): EntityTypeBuilder, ParadaPedidoConfiguration, EntityTypeBuilder, RutaParadaConfiguration, ParadaPedido, Parada, ParadaId, Pedido (+13 more)

### Community 13 - "Localidad & Ubicacion Config"
Cohesion: 0.08
Nodes (22): EntityTypeBuilder, LocalidadConfiguration, Localidad, Cp, Id, Nombre, Partido, ZonaId (+14 more)

### Community 14 - "Ruta Entity Config"
Cohesion: 0.08
Nodes (22): EntityTypeBuilder, RutaConfiguration, DateOnly, DateTimeOffset, Guid, Ruta, CapacidadParadas, CerradaEn (+14 more)

### Community 15 - "PruebaEntrega Config"
Cohesion: 0.09
Nodes (20): EntityTypeBuilder, PruebaEntregaConfiguration, DateTimeOffset, PruebaEntrega, CapturadaEn, DesvioMetros, DeviceUuid, FotoPath (+12 more)

### Community 16 - "shadcn UI Components Config"
Cohesion: 0.09
Nodes (21): aliases, components, hooks, lib, ui, utils, iconLibrary, menuAccent (+13 more)

### Community 17 - "DbContext DbSets"
Cohesion: 0.10
Nodes (20): Localidad, LogisticaDbContext, Clientes, EventosCliente, Localidades, ParadaPedidos, PedidoEventos, Pedidos (+12 more)

### Community 18 - "EventoCliente Config"
Cohesion: 0.11
Nodes (16): EntityTypeBuilder, EventoClienteConfiguration, DateTimeOffset, Guid, EventoCliente, Cliente, ClienteId, Id (+8 more)

### Community 19 - "Cliente Entity Config"
Cohesion: 0.12
Nodes (15): EntityTypeBuilder, ClienteConfiguration, DateTimeOffset, Cliente, Activo, ColorOper, ColorPago, ColorTrato (+7 more)

### Community 20 - "RefreshToken Config"
Cohesion: 0.12
Nodes (14): EntityTypeBuilder, RefreshTokenConfiguration, DateTimeOffset, Guid, RefreshToken, CreadoEn, CreadoPorIp, EstaActivo (+6 more)

### Community 21 - "Ubicaciones Controller"
Cohesion: 0.16
Nodes (13): CancellationToken, HttpGet, HttpPost, IActionResult, Task, ResolverRequest, UbicacionesController, UbicacionResuelta (+5 more)

### Community 22 - "Misc EF Configurations"
Cohesion: 0.16
Nodes (8): TarifaConfiguration, EntityTypeBuilder, UbicacionConfiguration, EntityTypeBuilder, UsuarioConfiguration, Ubicacion, Logistica.Datos.Configuraciones, IEntityTypeConfiguration

### Community 23 - "Tarifa Entity Config"
Cohesion: 0.13
Nodes (13): EntityTypeBuilder, DateOnly, DateTimeOffset, Tarifa, Cliente, ClienteId, CreadaEn, Id (+5 more)

### Community 24 - "Usuario Entity"
Cohesion: 0.14
Nodes (13): DateTimeOffset, Guid, Roles, Usuario, Activo, Cliente, ClienteId, CreadoEn (+5 more)

### Community 25 - "Geocodificacion Service"
Cohesion: 0.18
Nodes (10): CancellationToken, Task, DireccionNominatim, GeocodificacionService, ResultadoGeocodificacion, ResultadoNominatim, DireccionNominatim, HttpClient (+2 more)

### Community 26 - "TipoEventoCliente Config"
Cohesion: 0.22
Nodes (7): EntityTypeBuilder, TipoEventoClienteConfiguration, TipoEventoCliente, Codigo, Descripcion, Dimension, Id

### Community 27 - "MisParadas & Localidades Controllers"
Cohesion: 0.25
Nodes (7): CancellationToken, HttpGet, IActionResult, Task, MisParadasController, LocalidadesController, ControllerBase

### Community 28 - "Jwt Options"
Cohesion: 0.29
Nodes (6): OpcionesJwt, AccessMinutos, Audience, Issuer, Key, RefreshDias

### Community 29 - "Backend Project Dependencies"
Cohesion: 0.29
Nodes (7): Logistica, net8.0, Microsoft.AspNetCore.Authentication.JwtBearer (8.0.10), Microsoft.EntityFrameworkCore.Design (8.0.10), Npgsql.EntityFrameworkCore.PostgreSQL (8.0.10), Swashbuckle.AspNetCore (6.6.2), Microsoft.NET.Sdk.Web

### Community 30 - "CurrentUser Claims Helpers"
Cohesion: 0.47
Nodes (3): Guid, CurrentUserExtensions, ClaimsPrincipal

### Community 31 - "Clientes Controller"
Cohesion: 0.33
Nodes (5): CancellationToken, HttpGet, IActionResult, Task, ClientesController

### Community 32 - "Zonas Controller"
Cohesion: 0.33
Nodes (5): CancellationToken, HttpGet, IActionResult, Task, ZonasController

### Community 33 - "EscrituraDominio Write Helper"
Cohesion: 0.33
Nodes (4): CancellationToken, Guid, Task, EscrituraDominio

### Community 34 - "Pedido EF Config"
Cohesion: 0.67
Nodes (3): EntityTypeBuilder, PedidoConfiguration, Pedido

### Community 35 - "Zona EF Config"
Cohesion: 0.67
Nodes (3): EntityTypeBuilder, ZonaConfiguration, Zona

## Knowledge Gaps
- **340 isolated node(s):** `Key`, `Issuer`, `Audience`, `AccessMinutos`, `RefreshDias` (+335 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **10 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `LogisticaDbContext` connect `DbContext DbSets` to `Pedido Event & State Config`, `Backend Controllers & DbContext`, `Auth Service & Tokens`, `Seed Data & Paradas`, `Pedidos Controller Endpoints`, `Parada/Ruta EF Config`, `Ruta Entity Config`, `PruebaEntrega Config`, `EventoCliente Config`, `Cliente Entity Config`, `RefreshToken Config`, `Ubicaciones Controller`, `Misc EF Configurations`, `Tarifa Entity Config`, `TipoEventoCliente Config`, `MisParadas & Localidades Controllers`, `Clientes Controller`, `Zonas Controller`, `EscrituraDominio Write Helper`, `Pedido EF Config`, `Zona EF Config`?**
  _High betweenness centrality (0.195) - this node is a cross-community bridge._
- **Why does `Logistica.Entidades` connect `Backend Controllers & DbContext` to `Pedido Event & State Config`, `Auth Service & Tokens`, `Pedido Entity Model`, `Parada/Ruta EF Config`, `Localidad & Ubicacion Config`, `Ruta Entity Config`, `PruebaEntrega Config`, `EventoCliente Config`, `Cliente Entity Config`, `RefreshToken Config`, `Misc EF Configurations`, `Tarifa Entity Config`, `Usuario Entity`, `TipoEventoCliente Config`?**
  _High betweenness centrality (0.076) - this node is a cross-community bridge._
- **Why does `Pedido` connect `Pedido Entity Model` to `Pedido Event & State Config`, `Cliente Entity Config`, `Zona EF Config`, `Misc EF Configurations`?**
  _High betweenness centrality (0.055) - this node is a cross-community bridge._
- **What connects `Key`, `Issuer`, `Audience` to the rest of the system?**
  _340 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Frontend Pages & Layout` be split into smaller, more focused modules?**
  _Cohesion score 0.05966386554621849 - nodes in this community are weakly interconnected._
- **Should `Pedido Event & State Config` be split into smaller, more focused modules?**
  _Cohesion score 0.0425531914893617 - nodes in this community are weakly interconnected._
- **Should `Frontend Dependencies` be split into smaller, more focused modules?**
  _Cohesion score 0.0425531914893617 - nodes in this community are weakly interconnected._