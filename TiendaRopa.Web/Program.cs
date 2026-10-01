using TiendaRopa.Contracts.Catalogo;
using TiendaRopa.Shared.Services;
using TiendaRopa.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Cliente HTTP del catálogo. La UI se ejecuta en el servidor, por lo que
// "localhost" alcanza la API en el mismo equipo (ver Api:BaseUrl en appsettings).
var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5100";
builder.Services.AddSingleton<ICatalogoService>(_ =>
    new HttpCatalogoService(new HttpClient { BaseAddress = new Uri(apiBaseUrl) }));

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
