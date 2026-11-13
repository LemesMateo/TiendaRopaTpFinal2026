using Microsoft.AspNetCore.DataProtection;
using TiendaRopa.Api;
using TiendaRopa.Contracts.Admin;
using TiendaRopa.Contracts.Asistente;
using TiendaRopa.Contracts.Catalogo;
using TiendaRopa.Contracts.Identidad;
using TiendaRopa.Contracts.Pedidos;
using TiendaRopa.Shared.Services;
using TiendaRopa.Web.Components;
using TiendaRopa.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ---- Hospedaje de la API en el mismo sitio ----
// Registra EF Core, JWT, controladores y servicios de administración. La API y
// el frontend comparten proceso y dominio, así que no hace falta CORS ni una
// URL pública separada. (Alternativa: publicar TiendaRopa.Api como sitio propio.)
builder.Services.AddVelvetRoseApi(builder.Configuration, builder.Environment);

// ---- Data Protection ----
// En shared hosting (IIS sin perfil de usuario ni DPAPI) las claves se pierden
// en cada reinicio y se rompen el antiforgery y las sesiones. La carpeta
// "private" es persistente y el deploy no la toca.
var keysPath = Path.Combine(builder.Environment.ContentRootPath, "..", "private", "keys");
builder.Services.AddDataProtection()
    .SetApplicationName("VelvetRose.Web")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

// Cliente HTTP del catálogo. La UI se ejecuta en el servidor y la API vive en
// este mismo sitio, así que la base por defecto es la raíz ("/").
//
// Si `Api:BaseUrl` es una URL absoluta (por ejemplo la variable de entorno
// Api__BaseUrl en MonsterASP) se usa tal cual; si es relativa se resuelve
// contra la dirección real en la que escucha el servidor. La dirección NO se
// puede conocer en este punto: en IIS (alojamiento InProcess) el puerto recién
// existe después de Build(), y asumir un puerto fijo como "localhost:5101"
// hacía fallar todas las llamadas internas con "connection refused".
var apiBaseUrl = builder.Configuration["Api:BaseUrl"];

// Un único HttpClient de larga vida; las cabeceras de auth se agregan por request.
// El handler de diagnóstico debe terminar en un HttpClientHandler: si se pasa un
// DelegatingHandler sin InnerHandler, TODA llamada falla con
// "The inner handler has not been assigned".
builder.Services.AddSingleton(sp => new HttpClient(
    new RegistroApiHandler(
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("VelvetRose.Api"))
    {
        InnerHandler = new HttpClientHandler(),
    }));

// Las imágenes (/img/…) las sirve este mismo sitio: rutas relativas.
var assets = new AssetsBase(string.Empty);
builder.Services.AddSingleton(assets);
builder.Services.AddSingleton<ICatalogoService>(sp =>
    new HttpCatalogoService(sp.GetRequiredService<HttpClient>(), assets));

// Estado de sesión y carrito: scoped por circuito de Blazor.
// El almacenamiento local va detrás de IPreferenciasLocal (localStorage),
// que funciona tanto en Blazor Server como en MAUI Blazor Hybrid.
builder.Services.AddScoped<IPreferenciasLocal, PreferenciasLocal>();
builder.Services.AddScoped<SesionService>();
builder.Services.AddScoped<CarritoService>();

// Asistente conversacional (Gemini). La API key vive sólo en el backend:
// el frontend nunca la ve, sólo llama a /api/asistente/mensaje.
builder.Services.AddScoped<ChatContexto>();
builder.Services.AddScoped<IChatService>(sp =>
    new HttpChatService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SesionService>()));

// Solicitudes de atención y notificaciones internas de la tienda.
builder.Services.AddScoped<ISolicitudService>(sp =>
    new HttpSolicitudService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SesionService>()));
builder.Services.AddScoped<INotificacionService>(sp =>
    new HttpNotificacionService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SesionService>()));


// Clientes de API autenticados (JWT).
builder.Services.AddScoped<IAuthService>(sp =>
    new HttpAuthService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SesionService>()));
builder.Services.AddScoped<IPedidoService>(sp =>
    new HttpPedidoService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SesionService>(), assets));
builder.Services.AddScoped<IAdminService>(sp =>
    new HttpAdminService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SesionService>(), assets));

// Gestión de imágenes del panel administrativo: en Web la selección la hace
// el <InputFile> del componente compartido (explorador del sistema) y las
// imágenes las resuelve la API.
builder.Services.AddScoped<ISelectorImagenes, SelectorImagenesWeb>();
builder.Services.AddScoped<IResolverImagenes, ResolverImagenesWeb>();

var app = builder.Build();

// La base del cliente interno se fija acá, cuando el servidor ya conoce su
// dirección. Nunca se ejecutó todavía una petición, así que es seguro.
var clienteApi = app.Services.GetRequiredService<HttpClient>();
clienteApi.BaseAddress = ResolverBaseApi(app, apiBaseUrl);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

// ---- Pipeline de la API (mismo sitio) ----
// Aporta /api/* (controladores), /img/* (estáticos), autenticación JWT y el
// seeding inicial de la base de datos.
await app.UseVelvetRoseApiAsync();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(TiendaRopa.Shared._Imports).Assembly);

app.Run();

/// <summary>
/// Resuelve la dirección base del cliente HTTP interno (API en el mismo sitio).
///
/// Orden de prioridad:
///  1. `Api:BaseUrl` absoluta (variable Api__BaseUrl). Es la vía recomendada en
///     IIS/MonsterASP, donde el sitio no expone un puerto propio al proceso.
///  2. La dirección real en la que escucha el servidor (app.Urls o
///     ASPNETCORE_URLS): es lo que usa `dotnet run` y la publicación self-host.
///  3. Último recurso: la raíz del propio host, que falla de inmediato y deja
///     un aviso explícito en el log, en lugar de apuntar a un puerto adivinado.
/// </summary>
static Uri ResolverBaseApi(WebApplication app, string? configurada)
{
    if (!string.IsNullOrWhiteSpace(configurada)
        && Uri.TryCreate(configurada.Trim(), UriKind.Absolute, out var absoluta))
    {
        return absoluta;
    }

    var direccion = app.Urls.FirstOrDefault()
        ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")?.Split(';')[0];

    if (Uri.TryCreate(direccion, UriKind.Absolute, out var interna))
    {
        return interna;
    }

    app.Logger.LogWarning(
        "[VelvetRose] No se pudo determinar la dirección interna de la API. " +
        "Configurá Api__BaseUrl con la URL pública del sitio (por ejemplo " +
        "https://tiendaropa2026.runasp.net/) o el catálogo no podrá cargarse.");

    return new Uri("http://localhost/");
}
