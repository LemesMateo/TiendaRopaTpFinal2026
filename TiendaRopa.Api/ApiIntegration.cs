using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using TiendaRopa.Api.Auth;
using TiendaRopa.Api.Data;
using TiendaRopa.Api.Entities;
using TiendaRopa.Api.Services;
using TiendaRopa.Contracts.Catalogo;

namespace TiendaRopa.Api;

/// <summary>
/// Registro de la API como módulo reutilizable.
///
/// Existe para que la API pueda hospedarse de dos formas sin duplicar código:
///  1. Como sitio propio: <c>TiendaRopa.Api</c> (proyecto de arranque).
///  2. Fusionada con el frontend en un único website:
///     <c>TiendaRopa.Web</c> llama a estos métodos y expone /api/* y /img/*
///     desde el mismo dominio.
///
/// En ambos casos el pipeline y los servicios son exactamente los mismos.
/// </summary>
public static class ApiIntegration
{
    /// <summary>Registra los servicios de la API (EF Core, JWT, controladores, admin).</summary>
    public static IServiceCollection AddVelvetRoseApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddProblemDetails();

        // La API vive en otro assembly cuando se hospeda fusionada con el frontend:
        // hay que decirle explícitamente dónde están sus controladores.
        services.AddControllers()
            .AddApplicationPart(typeof(ApiIntegration).Assembly);

        // Límite del cuerpo multipart (subida de imágenes de producto).
        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = ProductoImagenes.MaximoBytes + (64 * 1024);
        });

        // Documentación OpenAPI (/openapi/v1.json) + UI Scalar (/scalar/v1)
        services.AddOpenApi();

        services.AddDbContext<VelvetRoseDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("VelvetRose"),
                sql => sql.EnableRetryOnFailure(maxRetryCount: 3)));

        // ---- Autenticación (JWT) ----
        // La clave se crea una única vez: en Development es efímera (no hay secretos
        // en el repo). En producción se configura con Auth__SigningKey.
        var tokenService = new TokenService(configuration, environment);
        services.AddSingleton(tokenService);
        services.AddHttpContextAccessor();
        services.AddScoped<IUsuarioActual, UsuarioActual>();

        // Hash de contraseñas de ASP.NET Core (PBKDF2 + salt) sobre las tablas existentes.
        services.AddScoped(_ => new PasswordHasher<Usuario>());

        services.AddScoped<AuthService>();
        services.AddScoped<PedidoService>();
        services.AddScoped<AdminService>();

        services
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

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                Roles.SoloAdministrador,
                policy => policy.Requirements.Add(new AdministradorRequirement()));
        });

        services.AddSingleton<IAuthorizationHandler, AdministradorHandler>();

        return services;
    }

    /// <summary>
    /// Agrega el pipeline de la API: estáticos (/img/…), seeding, autenticación,
    /// autorización y controladores.
    ///
    /// El seeding se ejecuta en Development o cuando <c>Api:Seed</c> vale true
    /// (para preparar la base de datos de MonsterASP en el primer despliegue).
    /// </summary>
    public static async Task<WebApplication> UseVelvetRoseApiAsync(this WebApplication app)
    {
        var seed = app.Environment.IsDevelopment()
            || app.Configuration.GetValue<bool>("Api:Seed");

        // Placeholders y assets de producto subidos por el panel de administración.
        app.UseStaticFiles();

        if (app.Environment.IsDevelopment())
        {
            // OpenAPI + UI de pruebas (no se publica en Production).
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        if (seed)
        {
            // Migración + datos de desarrollo. Los tres seeders son idempotentes.
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VelvetRoseDbContext>();
            await DevelopmentSeeder.SeedAsync(db);
            await DevelopmentUserSeeder.SeedAsync(db);
            await DevelopmentImageSeeder.SeedAsync(db);
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        return app;
    }
}
