using TiendaRopa.Contracts.Admin;
using TiendaRopa.Contracts.Catalogo;
using TiendaRopa.Contracts.Identidad;
using TiendaRopa.Contracts.Pedidos;
using TiendaRopa.Shared.Services;
using TiendaRopa.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Cliente HTTP del catálogo. La UI se ejecuta en el servidor, por lo que
// "localhost" alcanza la API en el mismo equipo (ver Api:BaseUrl en appsettings).
var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5100";

// Un único HttpClient de larga vida; las cabeceras de auth se agregan por request.
var http = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
builder.Services.AddSingleton(http);

// En Web los activos de productos (/img/…) los sirve la API.
var assets = new AssetsBase(apiBaseUrl);
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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(TiendaRopa.Shared._Imports).Assembly);

app.Run();
