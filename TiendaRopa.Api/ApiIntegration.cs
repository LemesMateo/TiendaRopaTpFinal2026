using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using TiendaRopa.Api.Asistente;
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
        services.AddScoped<SolicitudService>();
        services.AddScoped<NotificacionService>();

        // ---- Asistente conversacional (Gemini + Function Calling) ----
        // La API key se resuelve desde la sección "Asistente" y, si no está,
        // desde los secretos de usuario de TiendaRopa.Api. NUNCA viaja al
        // frontend (ni a MAUI, ni a la Web, ni a Shared).
        var opcionesAsistente = ResolverOpcionesAsistente(configuration, environment);

        services.AddSingleton(opcionesAsistente);
        services.AddScoped<AsistenteGemini>();
        services.AddScoped<IGeneradorChat>(sp => sp.GetRequiredService<AsistenteGemini>());
        services.AddScoped<HerramientasAsistente>();
        services.AddScoped<AsistenteServicio>();

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

            // Personal de tienda: Administrador o Vendedor.
            options.AddPolicy(
                Roles.PersonalTienda,
                policy => policy.Requirements.Add(new PersonalTiendaRequirement()));

            // Una política por permiso (pedidos.ver, solicitudes.responder, …).
            foreach (var (nombre, permiso) in Politicas.PorPermiso)
            {
                options.AddPolicy(
                    nombre,
                    policy => policy.Requirements.Add(new PermisoRequirement { Permiso = permiso }));
            }
        });

        services.AddSingleton<IAuthorizationHandler, AdministradorHandler>();
        services.AddSingleton<IAuthorizationHandler, PersonalTiendaHandler>();
        services.AddSingleton<IAuthorizationHandler, PermisoHandler>();

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

    /// <summary>
    /// Resuelve la configuración del asistente.
    ///
    /// La API key puede venir de:
    ///  1. "Asistente:Gemini:ApiKey" en la configuración del host (en
    ///     producción: variable de entorno Asistente__Gemini__ApiKey).
    ///  2. Los secretos de usuario de TiendaRopa.Api (dotnet user-secrets),
    ///     que es el mecanismo recomendado en desarrollo.
    ///
    /// En ambos casos la clave queda en el servidor: nunca se envía al
    /// cliente (MAUI, Web ni Shared).
    /// </summary>
    private static AsistenteOpciones ResolverOpcionesAsistente(
        IConfiguration configuration, IHostEnvironment environment)
    {
        var opciones = configuration
            .GetSection(AsistenteOpciones.Seccion)
            .Get<AsistenteOpciones>() ?? new AsistenteOpciones();

        if (string.IsNullOrWhiteSpace(opciones.Gemini.ApiKey))
        {
            // Secretos de usuario: se leen del perfil de Windows, fuera del repo.
            var secretos = new ConfigurationBuilder()
                .AddUserSecrets(typeof(ApiIntegration).Assembly, optional: true)
                .AddEnvironmentVariables()
                .Build();

            var clave = secretos[AsistenteOpciones.ClaveApiKey];
            if (!string.IsNullOrWhiteSpace(clave))
            {
                opciones.Gemini.ApiKey = clave;
            }
        }

        if (!opciones.Habilitado)
        {
            // Aviso visible en el log: el chat responderá con un error claro.
            Console.WriteLine(
                "[VelvetRose] Asistente sin API key. Configurala con: " +
                "dotnet user-secrets set \"Asistente:Gemini:ApiKey\" \"<clave>\" --project TiendaRopa.Api");
        }

        return opciones;
    }
}
