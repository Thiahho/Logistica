using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Threading.RateLimiting;
using Logistica.Auth;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Opciones;
using Logistica.Servicios;
using Logistica.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

// El body JSON (System.Text.Json) siempre parsea decimales en invariant culture, pero el model
// binder de [FromForm]/multipart (MisParadasController.Cerrar, H2) usa CultureInfo.CurrentCulture
// por thread — que en un host es-AR/es-* lee "." como separador de miles, no decimal ("34.5905"
// se vuelve 345905 y explota el numeric(10,7) de pruebas_entrega). Fijar invariant culture acá,
// antes de levantar el host, blinda a todo el proceso de esta clase de bug, no solo a un endpoint.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// Sin cabecera "Server: Kestrel" y con un tope global al body. Los endpoints multipart (fotos, firmas)
// ya tienen su [RequestSizeLimit] propio, más chico; esto cubre a todo lo demás (antes el default era
// 30 MB para cualquier JSON).
builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;
    o.Limits.MaxRequestBodySize = 5 * 1024 * 1024;
});

// Compresión de respuestas: los listados y exportes son JSON/CSV muy repetitivo (un export de pedidos
// de 14 MB baja ~10 veces; la cuenta corriente de un cliente, de 371 KB a unos 30). Nivel rápido: el
// costo de CPU es mínimo y en el teléfono se nota en la carga.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["text/csv", "application/problem+json"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

builder.Services.AddDatosLogistica(builder.Configuration);

// /health para el readiness probe del hosting gestionado — no existía ninguno. Solo chequea
// que el DbContext puede conectar (AddDbContextCheck ejecuta un "select 1" equivalente), no
// reglas de negocio.
builder.Services.AddHealthChecks().AddDbContextCheck<LogisticaDbContext>();

builder.Services.Configure<OpcionesJwt>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<AuthService>();

builder.Services.Configure<OpcionesPrecio>(builder.Configuration.GetSection("Precio"));
builder.Services.Configure<OpcionesDeposito>(builder.Configuration.GetSection("Deposito"));
builder.Services.Configure<OpcionesPruebaEntrega>(builder.Configuration.GetSection("PruebaEntrega"));
builder.Services.Configure<OpcionesDistancia>(builder.Configuration.GetSection("Distancia"));
builder.Services.Configure<OpcionesPortal>(builder.Configuration.GetSection("Portal"));
builder.Services.AddScoped<PrecioService>();
builder.Services.AddScoped<DistanciaService>();
builder.Services.AddScoped<UbicacionService>();
builder.Services.AddScoped<OrigenRutaService>();
builder.Services.AddScoped<ZonaLocalidadService>();
builder.Services.AddScoped<DireccionDesdeMapaService>();
builder.Services.AddScoped<TarifaService>();
builder.Services.AddScoped<AlmacenamientoFotos>();
builder.Services.AddScoped<CuentaCorrienteService>();
builder.Services.AddScoped<JornadaService>();
builder.Services.AddHostedService<CalentamientoService>();

// RuteoService cachea recorridos en memoria (acta changelog 3.4) — sin tabla nueva.
builder.Services.AddMemoryCache();

// construccion_v1.md §3 regla 3: si el trigger lo impide, la app muestra el error, no lo
// previene por su cuenta. ManejadorExcepciones traduce las excepciones de reglas de negocio de
// la base (triggers, checks) a ProblemDetails en vez de un 500 con stack trace.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ManejadorExcepciones>();

// Nominatim exige un User-Agent identificable y limita a ~1 req/s (construccion_v1.md §1).
builder.Services.AddHttpClient<GeocodificacionService>(client =>
{
    client.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
    client.DefaultRequestHeaders.Add("User-Agent", "Logistica/1.0 (contacto@logistica.local)");
});

// Links de Google Maps (fijar el punto exacto de una dirección). Sin auto-redirect: los links
// cortos se siguen a mano y solo hacia hosts de Google (Servicios/EnlaceMapaService.cs).
builder.Services.AddHttpClient<EnlaceMapaService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
    client.DefaultRequestHeaders.Add("User-Agent", "Logistica/1.0 (contacto@logistica.local)");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// "Ruteo:BaseUrl" es PROVISIONAL: en desarrollo apunta al demo público de OSRM
// (router.project-osrm.org), cuya política de uso no admite producción — ahí exige un
// contenedor propio (construccion_v1.md §1, acta changelog 3.4).
var ruteoBaseUrl = builder.Configuration["Ruteo:BaseUrl"]
    ?? throw new InvalidOperationException("Falta Ruteo:BaseUrl");
builder.Services.AddHttpClient<RuteoService>(client =>
{
    client.BaseAddress = new Uri(ruteoBaseUrl);
    client.DefaultRequestHeaders.Add("User-Agent", "Logistica/1.0 (contacto@logistica.local)");
});

// Panel de cobranza: BaseUrl tiene default sensato en OpcionesResend, no hace falta un throw acá
// como en Ruteo. La ApiKey (nullable a propósito) SÍ se resuelve en runtime dentro de
// EmailService vía IOptions — nunca acá, para no exigirla en el arranque del host (modo
// simulado sin ella, ver Servicios/EmailService.cs).
builder.Services.Configure<OpcionesResend>(builder.Configuration.GetSection("Resend"));
var resendBaseUrl = builder.Configuration["Resend:BaseUrl"] ?? "https://api.resend.com/";
builder.Services.AddHttpClient<EmailService>(client =>
{
    client.BaseAddress = new Uri(resendBaseUrl);
    client.DefaultRequestHeaders.Add("User-Agent", "Logistica/1.0 (contacto@logistica.local)");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<AvisosCobranzaService>();

var jwt = builder.Configuration.GetSection("Jwt").Get<OpcionesJwt>()
    ?? throw new InvalidOperationException("Falta la sección Jwt");
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "Falta Jwt:Key. En desarrollo: dotnet user-secrets set \"Jwt:Key\" \"<valor>\". En producción: variable de entorno.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Sin esto, el handler remapea "sub" a ClaimTypes.NameIdentifier y rompe
        // CurrentUserExtensions.UsuarioId(), que busca JwtRegisteredClaimNames.Sub tal cual.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("BackOffice", p => p.RequireRole(Roles.Administracion, Roles.Operacion))
    .AddPolicy("Administracion", p => p.RequireRole(Roles.Administracion))
    .AddPolicy("Operacion", p => p.RequireRole(Roles.Operacion))
    .AddPolicy("Repartidor", p => p.RequireRole(Roles.Repartidor))
    .AddPolicy("Cliente", p => p.RequireRole(Roles.Cliente))
    // Ninguna de las policies de arriba cubre "back-office O repartidor": el mapa lo consultan
    // tanto el planificador (armar ruta) como el repartidor (guía del día).
    .AddPolicy("Recorrido", p => p.RequireRole(Roles.Administracion, Roles.Operacion, Roles.Repartidor));

var frontendOrigin = builder.Configuration["Frontend:Origin"]
    ?? throw new InvalidOperationException("Falta Frontend:Origin");

builder.Services.AddCors(options =>
    options.AddPolicy("Frontend", p => p
        .WithOrigins(frontendOrigin)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

// Login sin límite de intentos era fuerza bruta viable contra /api/auth/login (auditoría de
// seguridad). Por IP, no por email: frenar por email dejaría a cualquiera bloquear la cuenta de
// otro con solo mandar intentos fallidos a su nombre (un DoS de negación de servicio disfrazado
// de "protección"). 5 intentos por minuto alcanza para un typo real y frena un ataque automatizado.
//
// Token bucket, no fixed window: fixed window resetea la ventana entera de golpe, así que un
// atacante puede mandar 5 intentos a los 0:59 y otros 5 a los 1:01 — 10 intentos reales en 2
// segundos. Token bucket recarga gradualmente (1 token cada 12s hasta el tope de 5), sin ese
// doble-burst en el borde de la ventana.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext => RateLimitPartition.GetTokenBucketLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
        factory: _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 5,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromSeconds(12),
            AutoReplenishment = true,
            QueueLimit = 0,
        }));
    // Endpoints que le pegan a un servicio externo (Nominatim, OSRM) o dan de alta catálogo (localidades):
    // un cliente del portal podía dispararlos sin límite (Nominatim admite ~1 req/s y bloquea la IP del
    // servidor si se abusa). Por usuario autenticado; sin sesión, por IP. 30 de ráfaga, 10 cada 10 s.
    options.AddPolicy("geo", httpContext => RateLimitPartition.GetTokenBucketLimiter(
        partitionKey: httpContext.User.FindFirst("sub")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
        factory: _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 30,
            TokensPerPeriod = 10,
            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
            AutoReplenishment = true,
            QueueLimit = 0,
        }));
    options.OnRejected = async (contexto, ct) =>
    {
        var esLogin = contexto.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == "login";
        // 12, no 60: con token bucket el próximo token llega a los 12s (ReplenishmentPeriod), no
        // hay que esperar la ventana entera como con fixed window.
        contexto.HttpContext.Response.Headers.RetryAfter = esLogin ? "12" : "10";
        var problemDetailsService = contexto.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = contexto.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = esLogin ? "Demasiados intentos" : "Demasiadas solicitudes",
                Detail = esLogin
                    ? "Demasiados intentos de inicio de sesión. Esperá unos segundos y volvé a intentar."
                    : "Demasiadas solicitudes seguidas. Esperá unos segundos y volvé a intentar.",
            },
        });
    };
});

// FiltroLimitesDeTexto: ningún campo de texto de un body puede ser gigante (ver Web/LimitesDeEntrada.cs).
builder.Services.AddControllers(o => o.Filters.Add<FiltroLimitesDeTexto>());

// Auditoría §7 (pisos numéricos): sin esto, un [Range] fallido sale como ValidationProblemDetails
// SIN `detail` — lib/api/errores.ts:leerError cae a `problema.title`, "One or more validation
// errors occurred." en inglés y sin decir qué campo. Con esto, el mensaje en español del
// ErrorMessage de cada atributo llega a la pantalla igual que un error de trigger
// (ManejadorExcepciones), sin abrir una segunda convención de error.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var detalle = string.Join(" ", context.ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m)));

        return new BadRequestObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Solicitud inválida",
            Detail = string.IsNullOrWhiteSpace(detalle) ? "Uno o más campos son inválidos." : detalle,
        });
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Los DTO viven anidados en cada controller y varios se repiten con el mismo nombre (p. ej.
    // CrearLocalidadRequest en LocalidadesController y en MiCuentaController): con el id por defecto
    // (solo el nombre) Swagger no podía generar el documento y /swagger devolvía 500.
    c.CustomSchemaIds(t => (t.FullName ?? t.Name).Replace("Logistica.", "").Replace("+", "."));
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Access token emitido por /api/auth/login",
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        },
    });
});

var app = builder.Build();

// Primero de todo el pipeline: tiene que envolver cualquier middleware/controller downstream.
app.UseExceptionHandler();

// Cabeceras de seguridad en toda respuesta. La API nunca se embebe en un frame ni necesita que el
// navegador adivine tipos de contenido; y sus respuestas (datos de clientes, DNI, facturas) no se
// cachean en disco de un equipo compartido ("no-store" salvo que el endpoint fije su propio caché,
// como las fotos).
app.Use(async (contexto, siguiente) =>
{
    contexto.Response.OnStarting(() =>
    {
        var h = contexto.Response.Headers;
        h.XContentTypeOptions = "nosniff";
        h.XFrameOptions = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        if (contexto.Request.Path.StartsWithSegments("/api") && !h.ContainsKey("Cache-Control"))
            h.CacheControl = "no-store";
        return Task.CompletedTask;
    });
    await siguiente();
});

app.UseResponseCompression();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<LogisticaDbContext>();
    var deposito = scope.ServiceProvider.GetRequiredService<IOptions<OpcionesDeposito>>().Value;
    await DatosSemilla.SembrarAsync(db, deposito);
}

if (!app.Environment.IsDevelopment()) app.UseHsts();

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseAuthentication();
// Después de autenticar: la política "geo" particiona por usuario (claim "sub"), no por IP.
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
