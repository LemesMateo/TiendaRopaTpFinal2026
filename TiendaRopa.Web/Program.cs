using Microsoft.AspNetCore.DataProtection;
using TiendaRopa.Api;
using TiendaRopa.Contracts.Admin;
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
// este mismo sitio, por lo que la base es la raíz ("/"). En desarrollo también
// funciona, porque el propio host expone /api y /img.
var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "/";
if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiUri))
{
    // Base relativa: resolvemos contra la primera URL en la que escucha el host.
    var hostUrl = builder.Configuration["urls"]
        ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
        ?? "http://localhost:5101";
    apiUri = new Uri(hostUrl.Split(';')[0]);
}

// Un único HttpClient de larga vida; las cabeceras de auth se agregan por request.
var http = new HttpClient { BaseAddress = apiUri };
builder.Services.AddSingleton(http);

// Las imágenes (/img/…) las sirve este mismo sitio: rutas relativas.
var assets = new AssetsBase(string.Empty);
builder.Services.AddSingleton(assets);
builder.Services.AddSingleton<ICatalogoService>(_ => new HttpCatalogoService(http, assets));

// Estado de sesión y carrito: scoped por circuito de Blazor.
// El almacenamiento local va detrás de IPreferenciasLocal (localStorage),
// que funciona tanto en Blazor Server como en MAUI Blazor Hybrid.
builder.Services.AddScoped<IPreferenciasLocal, PreferenciasLocal>();
builder.Services.AddScoped<SesionService>();
builder.Services.AddScoped<CarritoService>();

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
