using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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

        // Diagnóstico de arranque (sin credenciales): confirma a qué servidor y
        // base apunta el sitio publicado. En MonsterASP la causa más frecuente de
        // un 500 en /api/* es una cadena ausente, apuntando a otra base, o un
        // usuario SQL sin permisos sobre una base restaurada desde un .bak.
        // El mensaje queda en el log del sitio.
        RegistrarEstadoBaseDatos(app);

        // Diagnóstico del asistente: distingue "falta la clave" (503
        // sin_configuracion, verificable en el log) de un fallo real de Gemini.
        RegistrarEstadoAsistente(app);

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
            // Si la base no está disponible, el sitio igual arranca: se registra el
            // error y /api/* responde 500 con una traza. Antes, un fallo de seeding
            // tumbaba toda la aplicación y el sitio no cargaba nada.
            try
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<VelvetRoseDbContext>();
                await DevelopmentSeeder.SeedAsync(db);
                await DevelopmentUserSeeder.SeedAsync(db);
                await DevelopmentImageSeeder.SeedAsync(db);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(
                    ex,
                    "[VelvetRose] Falló el seeding inicial: {Pista}",
                    PistaErrorSql(ex));
            }
        }

        app.UseAuthentication();
        app.UseAuthorization();

        // Errores no controlados en /api/*: se responde JSON (ProblemDetails) en
        // lugar de la página HTML de Blazor. Un 500 en el catálogo dejaba de ser
        // diagnosticable porque el cuerpo era la UI de error del frontend.
        app.UseWhen(
            contexto => contexto.Request.Path.StartsWithSegments("/api"),
            rama => rama.Use(async (contexto, siguiente) =>
            {
                try
                {
                    await siguiente(contexto);
                }
                catch (Exception ex)
                {
                    var traza = contexto.TraceIdentifier;

                    // Se registra la excepción completa (sin cabeceras ni
                    // credenciales) para poder leerla en el log del sitio.
                    app.Logger.LogError(
                        ex,
                        "[VelvetRose] Error en {Metodo} {Ruta} (traza {Traza}).",
                        contexto.Request.Method,
                        contexto.Request.Path.Value,
                        traza);

                    if (contexto.Response.HasStarted)
                    {
                        throw;
                    }

                    contexto.Response.Clear();
                    contexto.Response.StatusCode = StatusCodes.Status500InternalServerError;

                    await contexto.Response.WriteAsJsonAsync(
                        new ProblemDetails
                        {
                            Title = "Error interno del servidor",
                            Detail = "La operación no pudo completarse. " +
                                     "Consultá el log del sitio con la traza indicada.",
                            Status = StatusCodes.Status500InternalServerError,
                            Extensions = { ["traceId"] = traza },
                        });
                }
            }));

        app.MapControllers();

        return app;
    }

    /// <summary>
    /// Registra (sin usuario, contraseña ni la cadena completa) a qué base apunta
    /// el proceso y si realmente puede consultarla.
    ///
    /// Si la cadena falta, si el nombre de la base no coincide con la restaurada
    /// o si el login SQL no tiene permisos sobre ella, el error queda tipado en el
    /// log: es la explicación directa de un 500 en todos los endpoints de datos.
    /// </summary>
    private static void RegistrarEstadoBaseDatos(WebApplication app)
    {
        var cadena = app.Configuration.GetConnectionString("VelvetRose");

        if (string.IsNullOrWhiteSpace(cadena))
        {
            app.Logger.LogError(
                "[VelvetRose] Falta la cadena de conexión: configurá ConnectionStrings__VelvetRose " +
                "con la cadena LOCAL del panel. Sin ella, /api/* responde 500.");
            return;
        }

        string servidor;
        string baseDatos;

        try
        {
            var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(cadena);
            servidor = cs.DataSource;
            baseDatos = cs.InitialCatalog;

            app.Logger.LogInformation(
                "[VelvetRose] Base configurada: servidor {Servidor}, base {Base}, autenticación {Auth}.",
                servidor,
                baseDatos,
                string.IsNullOrWhiteSpace(cs.UserID) ? "integrada" : "SQL");
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "[VelvetRose] ConnectionStrings__VelvetRose no es una cadena válida.");
            return;
        }

        // La comprobación real se hace en segundo plano: si la base no responde,
        // el sitio igual arranca y atiende; sólo queda el error en el log. Antes
        // un fallo de conexión retrasaba (o impedía) el arranque en shared hosting.
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<VelvetRoseDbContext>();

                // Se abre la conexión de forma explícita: CanConnectAsync devuelve
                // false sin motivo y lo que hace falta es justamente el motivo.
                await db.Database.OpenConnectionAsync();
                await db.Database.CloseConnectionAsync();

                // Si conecta, se confirma que el esquema existe (caso típico:
                // .bak restaurado en la base correcta).
                await db.Categorias.AsNoTracking().AnyAsync();
                app.Logger.LogInformation("[VelvetRose] Base accesible y esquema verificado.");
            }
            catch (Exception ex)
            {
                // El SqlException incluye el número de error (4060 base inexistente,
                // 18456 login fallido, 53/40 servidor inalcanzable, 916 permisos).
                // Nunca incluye la contraseña.
                app.Logger.LogError(
                    ex,
                    "[VelvetRose] Error al consultar {Base} en {Servidor}: {Pista}",
                    baseDatos,
                    servidor,
                    PistaErrorSql(ex));
            }
        });
    }

    /// <summary>
    /// Registra si el asistente tiene API key y con qué modelo va a responder.
    ///
    /// El mensaje "El asistente todavía no está configurado" se produce cuando
    /// <c>Asistente:Gemini:ApiKey</c> no llegó a la configuración del proceso.
    /// En MonsterASP eso ocurre si la variable se cargó en el sitio equivocado,
    /// con otro nombre (guiones en vez de dobles guiones bajos) o sin reiniciar
    /// la aplicación. Este log permite distinguirlo sin exponer la clave.
    /// </summary>
    private static void RegistrarEstadoAsistente(WebApplication app)
    {
        var opciones = app.Services.GetService<AsistenteOpciones>();

        if (opciones is null)
        {
            app.Logger.LogWarning("[VelvetRose] Asistente: no hay AsistenteOpciones registradas.");
            return;
        }

        var modelo = string.IsNullOrWhiteSpace(opciones.Gemini.Modelo)
            ? "(por defecto)"
            : opciones.Gemini.Modelo;

        if (!opciones.Habilitado)
        {
            app.Logger.LogError(
                "[VelvetRose] Asistente DESHABILITADO: no llegó Asistente__Gemini__ApiKey. " +
                "El chat responderá 'El asistente todavía no está configurado'. Cargá la variable " +
                "en el MISMO sitio (no en la base de datos) y reiniciá la aplicación.");
            return;
        }

        app.Logger.LogInformation(
            "[VelvetRose] Asistente habilitado con modelo {Modelo} (clave presente, {Largo} caracteres).",
            modelo,
            opciones.Gemini.ApiKey!.Trim().Length);
    }

    /// <summary>
    /// Traduce el número de error de SQL Server a una acción concreta, para que
    /// el log del sitio publicado diga qué configurar sin exponer credenciales.
    /// </summary>
    private static string PistaErrorSql(Exception ex)
    {
        // EF Core envuelve el fallo en RetryLimitExceededException / InvalidOperationException
        // cuando la conexión tiene EnableRetryOnFailure. Hay que bajar hasta el SqlException.
        var sql = BuscarSqlException(ex);

        if (sql is null)
        {
            return ex.Message;
        }

        return sql.Number switch
        {
            4060 => "La base indicada no existe o el login no puede abrirla. Si restauraste " +
                    "un .bak, verificá que el nombre final sea el de la cadena (RESTORE ... WITH MOVE) " +
                    "y usá la cadena LOCAL del panel, no la remota (*.databaseasp.net).",
            18456 => "Login SQL rechazado: revisá usuario y contraseña desplegados en " +
                     "ConnectionStrings__VelvetRose (la del panel, sin caracteres de más).",
            916 or 262 or 229 => "El login existe pero no tiene permisos sobre la base restaurada. " +
                                 "Asignale el rol db_owner desde el panel o con ALTER AUTHORIZATION.",
            53 or 64 or 26 or 40 or -1 => "No se pudo alcanzar el servidor SQL. En MonsterASP el sitio " +
                                          "debe usar la cadena LOCAL del panel (Server=localhost o la que " +
                                          "indica el propio panel para el sitio); la remota " +
                                          "(*.databaseasp.net) depende de 'Remote access', usa otro puerto " +
                                          "y suele estar bloqueada desde el propio hosting.",
            208 => "Falta una tabla: el esquema de la base no corresponde a esta versión de la " +
                   "aplicación. Restaurá el .bak correcto o aplicá las migraciones pendientes.",
            _ => $"Error SQL {sql.Number}.",
        };
    }

    /// <summary>Busca el SqlException en la cadena de excepciones internas.</summary>
    private static Microsoft.Data.SqlClient.SqlException? BuscarSqlException(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex is Microsoft.Data.SqlClient.SqlException sql)
            {
                return sql;
            }

            ex = ex.InnerException;
        }

        return null;
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
