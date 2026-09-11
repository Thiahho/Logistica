using System.Globalization;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;

// El body JSON (System.Text.Json) siempre parsea decimales en invariant culture, pero el model
// binder de [FromForm]/multipart (MisParadasController.Cerrar, H2) usa CultureInfo.CurrentCulture
// por thread — que en un host es-AR/es-* lee "." como separador de miles, no decimal ("34.5905"
// se vuelve 345905 y explota el numeric(10,7) de pruebas_entrega). Fijar invariant culture acá,
// antes de levantar el host, blinda a todo el proceso de esta clase de bug, no solo a un endpoint.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:Postgres");

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.MapEnum<EstadoPedido>("estado_pedido");
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<LogisticaDbContext>(options =>
    options.UseNpgsql(dataSource));

builder.Services.Configure<OpcionesJwt>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<AuthService>();

builder.Services.Configure<OpcionesPrecio>(builder.Configuration.GetSection("Precio"));
builder.Services.Configure<OpcionesDeposito>(builder.Configuration.GetSection("Deposito"));
builder.Services.Configure<OpcionesPruebaEntrega>(builder.Configuration.GetSection("PruebaEntrega"));
builder.Services.AddScoped<PrecioService>();
builder.Services.AddScoped<UbicacionService>();
builder.Services.AddScoped<OrigenRutaService>();
builder.Services.AddScoped<TarifaService>();
builder.Services.AddScoped<AlmacenamientoFotos>();
builder.Services.AddScoped<CuentaCorrienteService>();

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
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 5,
            QueueLimit = 0,
        }));
    options.OnRejected = async (contexto, ct) =>
    {
        contexto.HttpContext.Response.Headers.RetryAfter = "60";
        var problemDetailsService = contexto.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = contexto.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Demasiados intentos",
                Detail = "Demasiados intentos de inicio de sesión. Esperá un minuto y volvé a intentar.",
            },
        });
    };
});

builder.Services.AddControllers();

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<LogisticaDbContext>();
    var deposito = scope.ServiceProvider.GetRequiredService<IOptions<OpcionesDeposito>>().Value;
    await DatosSemilla.SembrarAsync(db, deposito);
}

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
