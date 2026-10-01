using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using TiendaRopa.Api.Auth;
using TiendaRopa.Api.Data;
using TiendaRopa.Api.Entities;
using TiendaRopa.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Servicios ----
builder.Services.AddProblemDetails();
builder.Services.AddControllers();

// Documentación OpenAPI (/openapi/v1.json) + UI Scalar (/scalar/v1)
builder.Services.AddOpenApi();

builder.Services.AddDbContext<VelvetRoseDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("VelvetRose"),
        sql => sql.EnableRetryOnFailure(maxRetryCount: 3)));

// ---- Autenticación (JWT) ----
// La clave se crea una única vez: en Development es efímera (no hay secretos en el repo).
var tokenService = new TokenService(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(tokenService);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUsuarioActual, UsuarioActual>();

// Hash de contraseñas de ASP.NET Core (PBKDF2 + salt) sobre las tablas existentes.
builder.Services.AddScoped(_ => new PasswordHasher<Usuario>());

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PedidoService>();
builder.Services.AddScoped<AdminService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "nombre",
            RoleClaimType = TokenService.RolClaim,
            IssuerSigningKey = new SymmetricSecurityKey(tokenService.Clave),
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        Roles.SoloAdministrador,
        policy => policy.Requirements.Add(new AdministradorRequirement()));
});

builder.Services.AddSingleton<IAuthorizationHandler, AdministradorHandler>();

var app = builder.Build();

// ---- Pipeline ----
// Estáticos de la API: placeholders locales de productos (wwwroot/img) y
// futuros assets. En producción irían a un CDN/hosting (ver docs/backend.md).
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    // OpenAPI + UI de pruebas
    app.MapOpenApi();
    app.MapScalarApiReference();

    // Migración y datos de desarrollo (sólo Development).
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<VelvetRoseDbContext>();
    await DevelopmentSeeder.SeedAsync(db);
    await DevelopmentUserSeeder.SeedAsync(db);
    await DevelopmentImageSeeder.SeedAsync(db);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
