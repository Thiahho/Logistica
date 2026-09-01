# Graph Report - Logistica  (2026-09-01)

## Corpus Check
- 171 files · ~84,916 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1634 nodes · 3213 edges · 123 communities (81 shown, 42 thin omitted)
- Extraction: 98% EXTRACTED · 2% INFERRED · 0% AMBIGUOUS · INFERRED: 61 edges (avg confidence: 0.81)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- Gestión de ubicaciones (API)
- Layout raíz y componentes de UI reutilizables (frontend)
- Página de cliente individual (frontend)
- Historial de migraciones de base de datos (EF Core)
- Autenticación y gestión de clientes
- Controlador de pedidos (API)
- Actualización de rutas (API)
- Páginas de alta y utilidades (cliente, depósitos, exportar) (frontend)
- Esquema de base de datos
- Entidad Pedido
- Vista de detalle y alta de cliente (frontend)
- Controladores API (auth, clientes, exportar, paradas)
- Gestión de usuarios de cliente y depósitos (frontend)
- Configuración de lanzamiento (.NET)
- Armado de ruta y cálculo de recorrido (frontend)
- Configuración TypeScript
- Mapa interactivo de armado de ruta (frontend)
- Configuraciones EF varias
- Entidad Ruta y su configuración EF
- Alta de pedido y errores RFC-7807 (frontend)
- Gestión de usuarios internos
- Entidad Prueba de entrega
- Controlador de vehículos
- DbContext: definición de DbSets
- Configuración de shadcn/ui
- Endpoints de autenticación
- Controlador de paradas del repartidor
- Entidad Evento de cliente
- Entidad Evento de pedido (historial)
- Servicio de ruteo (OSRM)
- Dependencias de linting y estilos (frontend)
- Entidad Cliente
- Entidad Tarifa
- DbContext: proyección ParadaRepartidor
- Entidad Vehículo
- Entidad RutaParada
- Dependencias UI y mapas (frontend)
- Detalle de parada del repartidor (frontend)
- Entidad Localidad/Zona y su configuración EF
- Máquina de estados del pedido
- Entidad Ubicación
- Controlador y almacenamiento de pruebas de entrega
- Controlador de tarifas (API)
- Entidad ParadaPedido y configuración de Pedido
- Entidad Refresh Token
- Manejo global de excepciones
- Controlador de exportación (CSV)
- Changelog: precio por tipo de vehículo
- Dialog de detalle de pedido (frontend)
- Emisión de tokens JWT
- Changelog: vehículos y depósitos
- Request de cierre de parada (prueba de entrega)
- Datos semilla (seed)
- Entidad ClienteUsuario
- Entidad Usuario
- Changelog: zonas y localidades
- Changelog: autocompletado de direcciones
- Controlador de zonas (API)
- Changelog: mapa y ruteo real (OSRM)
- Login y contexto de autenticación (frontend)
- Entidad TipoEventoCliente
- Jornada del día del repartidor
- Controlador de recorrido
- Metadata del paquete frontend
- Creación de refresh tokens
- Entidad Zona
- Extensión CurrentUser (claims)
- Opciones de configuración JWT
- Controlador de tipos de evento de cliente
- Escritura de dominio (auditoría de escritura)
- Dependencias del proyecto backend
- Changelog: punto de partida y depósito editable
- Página de tarifas (frontend)
- Opciones de prueba de entrega
- Endpoint de perfil propio (Yo)
- Changelog: separación usuarios/clientes
- Proxy de rutas protegidas (frontend)
- Utilidad de cálculo geográfico
- Auditoría: política de contraseñas
- Auditoría: manejo de excepciones
- Auditoría: rate limiting (resuelto)
- Reglas de agentes para el frontend
- Página de tarifas por cliente (frontend)
- Página de zonas (frontend)
- Dependencia clsx
- Algoritmo de ruteo (nearest-neighbor + 2-opt)
- Principio: trazabilidad por trigger
- Principio: registro de eventos del cliente
- Auditoría: frontend sin XSS
- Auditoría: contraseña de Postgres en texto plano
- Configuración ESLint
- Configuración Next.js
- Dependencia Next.js
- Dependencia React
- Dependencia tailwind-merge
- Configuración PostCSS
- Servicio Postgres (docker-compose)
- Previsión: depósito propio
- Principio: precio por zona
- Regla operativa: corte de carga
- Regla operativa: ventana de urgencias
- Requisito: precio por cliente y zona
- Requisito: indicadores por cliente
- Requisito: operación sin conexión
- Requisito: UX de una mano para repartidor
- Título del documento del sistema
- Definición: tabla pedidos
- Definición: tabla ruta_paradas
- Definición: tabla vehiculos
- Hito: proyecto y auth inicial
- Hito: alta de pedido y precio
- Regla: escritura por cola
- Regla: sin credenciales en el bundle
- Regla: migraciones no se editan
- Stack: geocodificación Nominatim
- Configuración pnpm workspace
- Ícono de archivo (asset)
- Ícono de globo (asset)
- Logo Next.js (asset)
- Logo Vercel (asset)
- Ícono de ventana (asset)
- README del frontend (create-next-app)

## God Nodes (most connected - your core abstractions)
1. `cn()` - 75 edges
2. `LogisticaDbContext` - 60 edges
3. `useAuth()` - 60 edges
4. `leerError()` - 54 edges
5. `Logistica.Entidades` - 53 edges
6. `Pedido` - 37 edges
7. `leerJson()` - 37 edges
8. `ClientesController` - 30 edges
9. `Ruta` - 30 edges
10. `Button()` - 28 edges

## Surprising Connections (you probably didn't know these)
- `Hito H4 — Solo lo que rompió la calle, exportar CSV` --semantically_similar_to--> `P6 — Se construye por dolor, no por catálogo`  [INFERRED] [semantically similar]
  docs/construccion_v1.md → docs/acta_sistema_v3.md
- `asignarZona()` --calls--> `leerError()`  [EXTRACTED]
  frontend/app/tarifas/page.tsx → frontend/lib/api/errores.ts
- `onSeleccionar()` --calls--> `leerError()`  [EXTRACTED]
  frontend/components/SelectorLocalidad.tsx → frontend/lib/api/errores.ts
- `Hallazgo Medio 3 — Sin validación de formato en Email/Cuit/Telefono` --semantically_similar_to--> `P5 — Ningún dato se ingresa dos veces`  [INFERRED] [semantically similar]
  docs/auditoria_seguridad.md → docs/acta_sistema_v3.md
- `Fortaleza — Manejo de archivos (AlmacenamientoFotos.cs)` --conceptually_related_to--> `Sincronización offline (Dexie → Storage → pruebas_entrega)`  [INFERRED]
  docs/auditoria_seguridad.md → docs/construccion_v1.md

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Evolución del origen de ruta (variable → editable → catálogo)** — docs_acta_sistema_v3_changelog_36, docs_acta_sistema_v3_changelog_37, docs_acta_sistema_v3_changelog_38, docs_construccion_v1_origenrutaservice [INFERRED 0.85]
- **Excepciones documentadas al techo de tablas por P6** — docs_acta_sistema_v3_p6, docs_acta_sistema_v3_changelog_33, docs_construccion_v1_changelog_12, docs_acta_sistema_v3_decision_entidad_destinatario [INFERRED 0.75]
- **Integridad de la transición Borrador→Confirmado ligada al precio por vehículo** — docs_acta_sistema_v3_changelog_311, docs_construccion_v1_changelog_110, docs_construccion_v1_changelog_115, docs_auditoria_seguridad_borrador_confirmado_fix [INFERRED 0.85]

## Communities (123 total, 42 thin omitted)

### Community 0 - "Gestión de ubicaciones (API)"
Cohesion: 0.05
Nodes (57): AsignarZonaRequest, Authorize, CancellationToken, HttpDelete, HttpGet, HttpPost, HttpPut, IActionResult (+49 more)

### Community 1 - "Layout raíz y componentes de UI reutilizables (frontend)"
Cohesion: 0.06
Nodes (53): geistMono, geistSans, metadata, ComboboxBusquedaProps, OpcionCombobox, ItemNav, NAV, Shell() (+45 more)

### Community 2 - "Página de cliente individual (frontend)"
Cohesion: 0.08
Nodes (34): ClienteDetalle, Color, COLORES, DatosCliente(), DIMENSION_LABEL, EventoResumen, EventosCliente(), TarifaZona (+26 more)

### Community 3 - "Historial de migraciones de base de datos (EF Core)"
Cohesion: 0.05
Nodes (26): DateOnly, DateTimeOffset, Guid, MigrationBuilder, Inicial, MigrationBuilder, ReglasDeBaseDeDatos, DateOnly (+18 more)

### Community 4 - "Autenticación y gestión de clientes"
Cohesion: 0.11
Nodes (33): ActivoRequest, ActualizarClienteRequest, Authorize, CancellationToken, DateTimeOffset, Dictionary, Guid, HttpDelete (+25 more)

### Community 5 - "Controlador de pedidos (API)"
Cohesion: 0.09
Nodes (36): Authorize, CancellationToken, DateOnly, DateTimeOffset, HttpGet, HttpPost, IActionResult, IOptions (+28 more)

### Community 6 - "Actualización de rutas (API)"
Cohesion: 0.10
Nodes (30): ActualizarRutaRequest, Authorize, CancellationToken, DateOnly, DateTimeOffset, Guid, HttpDelete, HttpGet (+22 more)

### Community 7 - "Páginas de alta y utilidades (cliente, depósitos, exportar) (frontend)"
Cohesion: 0.15
Nodes (17): REPORTES, ROLES, RolStaff, DatosVehiculo(), ZonaPeligro(), Card(), CardContent(), CardDescription() (+9 more)

### Community 8 - "Esquema de base de datos"
Cohesion: 0.12
Nodes (34): auth, auth.users, clientes, clientes_usuarios, eventos_cliente, fn_bloquear_direccion_dudosa(), fn_congelar_pedido(), fn_log_estado_pedido() (+26 more)

### Community 9 - "Entidad Pedido"
Cohesion: 0.06
Nodes (33): DateOnly, DateTimeOffset, Pedido, Bultos, Cliente, ClienteId, CreadoEn, DescuentoRuta (+25 more)

### Community 10 - "Vista de detalle y alta de cliente (frontend)"
Cohesion: 0.09
Nodes (18): DetalleCliente(), FormularioNuevoCliente(), ListaClientes(), ESTILO_ESTADO, ETIQUETA_ESTADO, GuiaDeRuta(), urlComoLlegar(), VARIANTE_POR_ESTADO (+10 more)

### Community 11 - "Controladores API (auth, clientes, exportar, paradas)"
Cohesion: 0.18
Nodes (7): Logistica.Servicios, Logistica.Controllers, Logistica.Datos, Logistica.Dominio, Logistica.Opciones, Logistica.Auth, Logistica.Entidades

### Community 12 - "Gestión de usuarios de cliente y depósitos (frontend)"
Cohesion: 0.10
Nodes (27): UsuariosCliente(), crear(), resetearPassword(), CatalogoDepositos(), cargar(), onCrear(), onDesactivar(), onRenombrar() (+19 more)

### Community 13 - "Configuración de lanzamiento (.NET)"
Cohesion: 0.07
Nodes (28): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, launchUrl, applicationUrl (+20 more)

### Community 14 - "Armado de ruta y cálculo de recorrido (frontend)"
Cohesion: 0.09
Nodes (25): ParadaConsolidada, FetchConSesion, trazarRecorrido(), CandidatoRuta, Cliente, Deposito, DesglosePrecio, EventoResumen (+17 more)

### Community 15 - "Configuración TypeScript"
Cohesion: 0.07
Nodes (28): compilerOptions, allowJs, esModuleInterop, incremental, isolatedModules, jsx, lib, module (+20 more)

### Community 16 - "Mapa interactivo de armado de ruta (frontend)"
Cohesion: 0.11
Nodes (19): ArmarRuta(), guardarAsignacion(), guardarParadas(), onCerrarPlanificacion(), onGuardar(), sugerir(), COLOR_VARIANTE, icono() (+11 more)

### Community 17 - "Configuraciones EF varias"
Cohesion: 0.10
Nodes (14): EntityTypeBuilder, ClienteUsuarioConfiguration, EntityTypeBuilder, RefreshTokenConfiguration, RutaConfiguration, EntityTypeBuilder, UbicacionConfiguration, EntityTypeBuilder (+6 more)

### Community 18 - "Entidad Ruta y su configuración EF"
Cohesion: 0.08
Nodes (23): EntityTypeBuilder, DateOnly, DateTimeOffset, Guid, Ruta, CapacidadParadas, CerradaEn, CombustibleMonto (+15 more)

### Community 19 - "Alta de pedido y errores RFC-7807 (frontend)"
Cohesion: 0.10
Nodes (18): RFC-7807, UbicacionResuelta, ComboboxBusqueda(), DireccionResuelta, SelectorDireccion(), SelectorDireccionProps, UbicacionResuelta, LocalidadConocida (+10 more)

### Community 20 - "Gestión de usuarios internos"
Cohesion: 0.19
Nodes (18): ActualizarUsuarioRequest, Authorize, CancellationToken, Guid, HttpGet, HttpPost, HttpPut, IActionResult (+10 more)

### Community 21 - "Entidad Prueba de entrega"
Cohesion: 0.09
Nodes (20): EntityTypeBuilder, PruebaEntregaConfiguration, DateTimeOffset, PruebaEntrega, CapturadaEn, DesvioMetros, DeviceUuid, FotoPath (+12 more)

### Community 22 - "Controlador de vehículos"
Cohesion: 0.22
Nodes (16): ActualizarVehiculoRequest, Authorize, CancellationToken, DateOnly, HttpGet, HttpPost, HttpPut, IActionResult (+8 more)

### Community 23 - "DbContext: definición de DbSets"
Cohesion: 0.09
Nodes (22): Localidad, LogisticaDbContext, Clientes, ClientesUsuarios, EventosCliente, Localidades, ParadaPedidos, PedidoEventos (+14 more)

### Community 24 - "Configuración de shadcn/ui"
Cohesion: 0.09
Nodes (21): aliases, components, hooks, lib, ui, utils, iconLibrary, menuAccent (+13 more)

### Community 25 - "Endpoints de autenticación"
Cohesion: 0.20
Nodes (14): AccessTokenResponse, ActionResult, AllowAnonymous, CancellationToken, DateTimeOffset, HttpPost, Task, AccessTokenResponse (+6 more)

### Community 26 - "Controlador de paradas del repartidor"
Cohesion: 0.19
Nodes (14): CancellationToken, HttpGet, HttpPost, IActionResult, IOptions, Task, CierreResultado, MisParadasController (+6 more)

### Community 27 - "Entidad Evento de cliente"
Cohesion: 0.11
Nodes (16): EntityTypeBuilder, EventoClienteConfiguration, DateTimeOffset, Guid, EventoCliente, Cliente, ClienteId, Id (+8 more)

### Community 28 - "Entidad Evento de pedido (historial)"
Cohesion: 0.11
Nodes (16): EntityTypeBuilder, PedidoEventoConfiguration, DateTimeOffset, Guid, PedidoEvento, ActorTexto, ActorTipo, ActorUsuario (+8 more)

### Community 29 - "Servicio de ruteo (OSRM)"
Cohesion: 0.15
Nodes (16): CancellationToken, HttpClient, IReadOnlyList, List, Task, GeometriaOsrm, PuntoRuta, Recorrido (+8 more)

### Community 30 - "Dependencias de linting y estilos (frontend)"
Cohesion: 0.11
Nodes (19): eslint, eslint-config-next, devDependencies, eslint, eslint-config-next, tailwindcss, @tailwindcss/postcss, @types/leaflet (+11 more)

### Community 31 - "Entidad Cliente"
Cohesion: 0.12
Nodes (15): EntityTypeBuilder, ClienteConfiguration, DateTimeOffset, Cliente, Activo, ColorOper, ColorPago, ColorTrato (+7 more)

### Community 32 - "Entidad Tarifa"
Cohesion: 0.12
Nodes (15): EntityTypeBuilder, TarifaConfiguration, DateOnly, DateTimeOffset, Tarifa, Cliente, ClienteId, CreadaEn (+7 more)

### Community 33 - "DbContext: proyección ParadaRepartidor"
Cohesion: 0.11
Nodes (18): DateTimeOffset, ParadaRepartidor, Bultos, CalleNumero, DestinatarioNombre, DestinatarioTelefono, Estado, Lat (+10 more)

### Community 34 - "Entidad Vehículo"
Cohesion: 0.11
Nodes (17): DateOnly, DateTimeOffset, Vehiculo, Activo, Anio, CapacidadParadas, CostoKm, CreadoEn (+9 more)

### Community 35 - "Entidad RutaParada"
Cohesion: 0.12
Nodes (14): EntityTypeBuilder, RutaParadaConfiguration, DateTimeOffset, RutaParada, Anclada, Estado, Id, LlegadaEn (+6 more)

### Community 36 - "Dependencias UI y mapas (frontend)"
Cohesion: 0.12
Nodes (17): @base-ui/react, class-variance-authority, dependencies, @base-ui/react, class-variance-authority, leaflet, lucide-react, react-dom (+9 more)

### Community 37 - "Detalle de parada del repartidor (frontend)"
Cohesion: 0.21
Nodes (12): ESTILO_ESTADO, ETIQUETA_ESTADO, Modo, obtenerPosicion(), ParadaDetalle(), cerrar(), onLlegue(), Checkbox() (+4 more)

### Community 38 - "Entidad Localidad/Zona y su configuración EF"
Cohesion: 0.14
Nodes (11): EntityTypeBuilder, LocalidadConfiguration, EntityTypeBuilder, ZonaConfiguration, Localidad, Cp, Id, Nombre (+3 more)

### Community 39 - "Máquina de estados del pedido"
Cohesion: 0.14
Nodes (12): Dictionary, TransicionesPedido, EstadoPedido, Borrador, Cancelado, Confirmado, Devuelto, EnRuta (+4 more)

### Community 40 - "Entidad Ubicación"
Cohesion: 0.12
Nodes (15): DateTimeOffset, Ubicacion, CalleNumero, CreadaEn, GeoConfianza, GeoFecha, GeoProveedor, Id (+7 more)

### Community 41 - "Controlador y almacenamiento de pruebas de entrega"
Cohesion: 0.17
Nodes (12): Authorize, CancellationToken, HttpGet, IActionResult, Task, PruebasEntregaController, CancellationToken, IOptions (+4 more)

### Community 42 - "Controlador de tarifas (API)"
Cohesion: 0.18
Nodes (12): CancellationToken, HttpGet, HttpPut, IActionResult, Task, FijarTarifaRequest, TarifaGeneral, TarifasController (+4 more)

### Community 43 - "Entidad ParadaPedido y configuración de Pedido"
Cohesion: 0.15
Nodes (10): EntityTypeBuilder, ParadaPedidoConfiguration, EntityTypeBuilder, PedidoConfiguration, Pedido, ParadaPedido, Parada, ParadaId (+2 more)

### Community 44 - "Entidad Refresh Token"
Cohesion: 0.13
Nodes (14): DateTimeOffset, Guid, RefreshToken, ClienteUsuario, ClienteUsuarioId, CreadoEn, CreadoPorIp, EstaActivo (+6 more)

### Community 45 - "Manejo global de excepciones"
Cohesion: 0.17
Nodes (11): CancellationToken, ManejadorExcepciones, Logistica.Web, Detalle, Exception, HttpContext, IExceptionHandler, IProblemDetailsService (+3 more)

### Community 46 - "Controlador de exportación (CSV)"
Cohesion: 0.32
Nodes (8): CancellationToken, DateOnly, HttpGet, IActionResult, Task, ExportarController, FileContentResult, IEnumerable

### Community 47 - "Changelog: precio por tipo de vehículo"
Cohesion: 0.24
Nodes (14): Changelog 3.11 — Precio por tipo de vehículo, P1 — El precio se congela en el pedido, Corrección — Borrador→Confirmado ya no era transición manual válida, Fortaleza — SQL sin superficie de injection, Cálculo de precio (precio_base + recargo_urgencia - descuento_ruta + peajes), RutasController.CerrarPlanificacion, Changelog 1.10 — Precio por tipo de vehículo, Changelog 1.15 — Fix: Borrador→Confirmado ya no es transición manual (+6 more)

### Community 48 - "Dialog de detalle de pedido (frontend)"
Cohesion: 0.22
Nodes (10): PedidoDetalleContenido(), confirmarTransicion(), PedidoDetalleContenidoProps, CON_MOTIVO_OBLIGATORIO, ETIQUETA_TRANSICION, motivoObligatorio(), TRANSICIONES, transicionesDisponibles() (+2 more)

### Community 49 - "Emisión de tokens JWT"
Cohesion: 0.38
Nodes (6): CancellationToken, DateTimeOffset, Task, AuthService, ResultadoLogin, Usuario

### Community 50 - "Changelog: vehículos y depósitos"
Cohesion: 0.18
Nodes (13): Changelog 3.1 — Alta de la tabla vehiculos, Changelog 3.8 — Catálogo de depósitos con nombre, Previsión de arquitectura — Multi-vehículo, P7 — La capacidad se mide en paradas, no en bultos, RF-11 — Orden de paradas sugerido desde el punto de retiro, RF-12 — Reordenamiento manual siempre disponible, RF-14 — Consolidación de retiros en la misma dirección, RF-16 — Validación de capacidad en paradas (+5 more)

### Community 51 - "Request de cierre de parada (prueba de entrega)"
Cohesion: 0.17
Nodes (12): CerrarParadaRequest, CapturadaEn, DeviceUuid, Foto, IdentidadVerificada, Lat, LlegadaEn, Lng (+4 more)

### Community 52 - "Datos semilla (seed)"
Cohesion: 0.17
Nodes (10): CancellationToken, Task, DatosSemilla, Localidad, OpcionesDeposito, CalleNumero, Lat, Lng (+2 more)

### Community 53 - "Entidad ClienteUsuario"
Cohesion: 0.17
Nodes (11): DateTimeOffset, Guid, ClienteUsuario, Activo, Cliente, ClienteId, CreadoEn, Email (+3 more)

### Community 54 - "Entidad Usuario"
Cohesion: 0.17
Nodes (11): DateTimeOffset, Guid, Roles, Usuario, Activo, CreadoEn, Email, Id (+3 more)

### Community 55 - "Changelog: zonas y localidades"
Cohesion: 0.18
Nodes (12): Changelog 3.10 — Asignación de zona a localidades sin zona, Changelog 3.2 — zonas.km_desde / zonas.km_hasta, Criterio de aceptación 3 — Prueba de modo avión, RF-29 — Cálculo del desvío de la prueba de entrega, RNF-02 — Idempotencia de la sincronización, RNF-03 — La hora válida es la de captura, ambas se almacenan, RNF-07 — Degradación segura: ruta descargada completa antes de salir, Fortaleza — Manejo de archivos (AlmacenamientoFotos.cs) (+4 more)

### Community 56 - "Changelog: autocompletado de direcciones"
Cohesion: 0.18
Nodes (12): Changelog 3.5 — Autocompletado de destinatarios y direcciones, Changelog 3.9 — Descubrimiento de localidades vía Nominatim, Decisión abierta — Consulta legal única antes de la primera ruta, Decisión — No crear entidad Destinatario propia, P5 — Ningún dato se ingresa dos veces, P6 — Se construye por dolor, no por catálogo, Riesgo reconocido — Sobreconstrucción, Hallazgo Medio 3 — Sin validación de formato en Email/Cuit/Telefono (+4 more)

### Community 57 - "Controlador de zonas (API)"
Cohesion: 0.24
Nodes (9): ActualizarKmRequest, Authorize, CancellationToken, HttpGet, HttpPut, IActionResult, Task, ActualizarKmRequest (+1 more)

### Community 58 - "Changelog: mapa y ruteo real (OSRM)"
Cohesion: 0.18
Nodes (11): Changelog 3.4 — Módulo de mapa y ruteo real por calles (OSRM), RF-24 — Registro de hora de llegada y salida por parada, RNF-08 — Control de acceso por rol, Fortaleza — IDOR bien cubierto, Changelog 1.3 — Módulo de mapa (Leaflet + OSM) y RuteoService (OSRM), Desvío consciente del orden de construcción (H3 antes que H2), Hito H2 — PWA completa con cola offline (sin construir), Hito H3 — Armado de ruta con orden sugerido y cierre con margen (+3 more)

### Community 59 - "Login y contexto de autenticación (frontend)"
Cohesion: 0.29
Nodes (8): FormularioLogin(), onSubmit(), Home(), AuthContext, AuthProvider(), EstadoAuth, rutaPorRol(), Usuario

### Community 60 - "Entidad TipoEventoCliente"
Cohesion: 0.22
Nodes (7): EntityTypeBuilder, TipoEventoClienteConfiguration, TipoEventoCliente, Codigo, Descripcion, Dimension, Id

### Community 61 - "Jornada del día del repartidor"
Cohesion: 0.22
Nodes (9): DateOnly, DateTimeOffset, IReadOnlyList, List, JornadaDelDia, ParadaDelDia, RegistrarLlegadaRequest, ParadaDelDia (+1 more)

### Community 62 - "Controlador de recorrido"
Cohesion: 0.22
Nodes (8): CancellationToken, HttpPost, IActionResult, List, Task, RecorridoController, TrazarRequest, TrazarRequest

### Community 63 - "Metadata del paquete frontend"
Cohesion: 0.22
Nodes (8): name, private, scripts, build, dev, lint, start, version

### Community 64 - "Creación de refresh tokens"
Cohesion: 0.43
Nodes (5): Guid, IOptions, TokenService, Entidad, TokenPlano

### Community 65 - "Entidad Zona"
Cohesion: 0.25
Nodes (7): Zona, Activa, Codigo, Id, KmDesde, KmHasta, Nombre

### Community 66 - "Extensión CurrentUser (claims)"
Cohesion: 0.38
Nodes (3): Guid, CurrentUserExtensions, ClaimsPrincipal

### Community 67 - "Opciones de configuración JWT"
Cohesion: 0.29
Nodes (6): OpcionesJwt, AccessMinutos, Audience, Issuer, Key, RefreshDias

### Community 68 - "Controlador de tipos de evento de cliente"
Cohesion: 0.29
Nodes (6): CancellationToken, HttpGet, IActionResult, Task, TiposEventoClienteController, ControllerBase

### Community 69 - "Escritura de dominio (auditoría de escritura)"
Cohesion: 0.43
Nodes (4): CancellationToken, Guid, Task, EscrituraDominio

### Community 70 - "Dependencias del proyecto backend"
Cohesion: 0.29
Nodes (7): Logistica, net8.0, Microsoft.AspNetCore.Authentication.JwtBearer (8.0.10), Microsoft.EntityFrameworkCore.Design (8.0.10), Npgsql.EntityFrameworkCore.PostgreSQL (8.0.10), Swashbuckle.AspNetCore (6.6.2), Microsoft.NET.Sdk.Web

### Community 71 - "Changelog: punto de partida y depósito editable"
Cohesion: 0.33
Nodes (7): Changelog 3.6 — Punto de partida variable de la ruta, Changelog 3.7 — Dirección del depósito editable, Changelog 1.5 — Punto de partida variable de la ruta (rutas.origen_ubicacion_id), Changelog 1.6 — Depósito editable (PUT /api/ubicaciones/deposito), superada por 1.7, OrigenRutaService, Regla 8 — [Authorize] de clase y de acción se combinan con AND, RutasController.Cerrar (cierre económico)

### Community 72 - "Página de tarifas (frontend)"
Cohesion: 0.48
Nodes (7): ListaTarifas(), asignarZona(), guardar(), valorKmDesde(), valorKmHasta(), valorPrecioCamioneta(), valorPrecioMoto()

### Community 73 - "Opciones de prueba de entrega"
Cohesion: 0.33
Nodes (5): OpcionesPruebaEntrega, MotivosFallo, RaizFotos, TamanoMaximoKb, UmbralDesvioMetros

### Community 74 - "Endpoint de perfil propio (Yo)"
Cohesion: 0.50
Nodes (3): Authorize, HttpGet, IActionResult

### Community 75 - "Changelog: separación usuarios/clientes"
Cohesion: 0.83
Nodes (4): Changelog 3.3 — clientes_usuarios separada de usuarios, Decisión — Modelo de usuarios en dos tablas separadas, Tabla clientes_usuarios, Changelog 1.2 — Separación usuarios / clientes_usuarios

### Community 78 - "Auditoría: política de contraseñas"
Cohesion: 0.67
Nodes (3): Fortaleza — JWT/cookies bien configurados, Hallazgo Medio 4 — Política de contraseña mínima débil (8 caracteres), Stack — JWT propio, cuatro roles

### Community 79 - "Auditoría: manejo de excepciones"
Cohesion: 0.67
Nodes (3): Fortaleza — Manejo de excepciones sin fugas, Web/ManejadorExcepciones.cs, Regla 3 — Nada de lógica de negocio duplicada entre trigger y aplicación

### Community 80 - "Auditoría: rate limiting (resuelto)"
Cohesion: 1.00
Nodes (3): Hallazgo Alto 1 — Sin rate limiting en /api/auth/login (RESUELTO), Changelog 1.16 — Rate limiting en /api/auth/login, Program.cs — políticas de autorización, CORS, pipeline

### Community 81 - "Reglas de agentes para el frontend"
Cohesion: 0.67
Nodes (3): frontend AGENTS.md (Next.js agent rules), Next.js breaking-changes agent-rules block, frontend CLAUDE.md

## Knowledge Gaps
- **486 isolated node(s):** `Key`, `Issuer`, `Audience`, `AccessMinutos`, `RefreshDias` (+481 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **42 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `LogisticaDbContext` connect `DbContext: definición de DbSets` to `Gestión de ubicaciones (API)`, `Autenticación y gestión de clientes`, `Controlador de pedidos (API)`, `Actualización de rutas (API)`, `Controladores API (auth, clientes, exportar, paradas)`, `Configuraciones EF varias`, `Entidad Ruta y su configuración EF`, `Gestión de usuarios internos`, `Entidad Prueba de entrega`, `Controlador de vehículos`, `Controlador de paradas del repartidor`, `Entidad Evento de cliente`, `Entidad Evento de pedido (historial)`, `Entidad Cliente`, `Entidad Tarifa`, `Entidad RutaParada`, `Entidad Localidad/Zona y su configuración EF`, `Máquina de estados del pedido`, `Controlador y almacenamiento de pruebas de entrega`, `Controlador de tarifas (API)`, `Entidad ParadaPedido y configuración de Pedido`, `Entidad Refresh Token`, `Controlador de exportación (CSV)`, `Emisión de tokens JWT`, `Datos semilla (seed)`, `Entidad ClienteUsuario`, `Controlador de zonas (API)`, `Entidad TipoEventoCliente`, `Controlador de tipos de evento de cliente`, `Escritura de dominio (auditoría de escritura)`?**
  _High betweenness centrality (0.167) - this node is a cross-community bridge._
- **Why does `Logistica.Entidades` connect `Controladores API (auth, clientes, exportar, paradas)` to `Gestión de ubicaciones (API)`, `Historial de migraciones de base de datos (EF Core)`, `Entidad Pedido`, `Configuraciones EF varias`, `Entidad Ruta y su configuración EF`, `Entidad Prueba de entrega`, `Entidad Evento de cliente`, `Entidad Evento de pedido (historial)`, `Entidad Cliente`, `Entidad Tarifa`, `Entidad Vehículo`, `Entidad RutaParada`, `Entidad Localidad/Zona y su configuración EF`, `Máquina de estados del pedido`, `Entidad Ubicación`, `Entidad ParadaPedido y configuración de Pedido`, `Entidad Refresh Token`, `Entidad ClienteUsuario`, `Entidad Usuario`, `Entidad TipoEventoCliente`, `Entidad Zona`?**
  _High betweenness centrality (0.094) - this node is a cross-community bridge._
- **Why does `MisParadasController` connect `Controlador de paradas del repartidor` to `Gestión de ubicaciones (API)`, `Controlador de tipos de evento de cliente`, `Opciones de prueba de entrega`, `Controlador y almacenamiento de pruebas de entrega`, `Controladores API (auth, clientes, exportar, paradas)`, `Servicio de ruteo (OSRM)`, `Request de cierre de parada (prueba de entrega)`, `DbContext: definición de DbSets`, `Jornada del día del repartidor`?**
  _High betweenness centrality (0.038) - this node is a cross-community bridge._
- **What connects `Key`, `Issuer`, `Audience` to the rest of the system?**
  _486 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Gestión de ubicaciones (API)` be split into smaller, more focused modules?**
  _Cohesion score 0.050980392156862744 - nodes in this community are weakly interconnected._
- **Should `Layout raíz y componentes de UI reutilizables (frontend)` be split into smaller, more focused modules?**
  _Cohesion score 0.05750658472344162 - nodes in this community are weakly interconnected._
- **Should `Página de cliente individual (frontend)` be split into smaller, more focused modules?**
  _Cohesion score 0.08418079096045197 - nodes in this community are weakly interconnected._