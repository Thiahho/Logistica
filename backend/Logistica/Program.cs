using System.Text;
using Logistica.Auth;
using Logistica.Datos;
using Logistica.Entidades;
using Logistica.Opciones;
using Logistica.Servicios;
using Logistica.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;

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
builder.Services.AddScoped<PrecioService>();
builder.Services.AddScoped<UbicacionService>();
builder.Services.AddScoped<TarifaService>();

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
    .AddPolicy("Cliente", p => p.RequireRole(Roles.Cliente));

var frontendOrigin = builder.Configuration["Frontend:Origin"]
    ?? throw new InvalidOperationException("Falta Frontend:Origin");

builder.Services.AddCors(options =>
    options.AddPolicy("Frontend", p => p
        .WithOrigins(frontendOrigin)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

builder.Services.AddControllers();
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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
