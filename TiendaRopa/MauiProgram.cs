using Microsoft.Extensions.Logging;
#if MAUI_DEVFLOW
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.DevFlow.Blazor;
#endif
using TiendaRopa.Contracts.Admin;
using TiendaRopa.Contracts.Asistente;
using TiendaRopa.Contracts.Catalogo;
using TiendaRopa.Contracts.Identidad;
using TiendaRopa.Contracts.Pedidos;
using TiendaRopa.Services;
using TiendaRopa.Shared.Services;

namespace TiendaRopa
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

#if MAUI_DEVFLOW
            // Instrumentación MauiDevFlow (sólo en builds con DevFlow):
            // agente de inspección + tools CDP para el BlazorWebView.
            builder.AddMauiDevFlowAgent();
            builder.AddMauiBlazorDevFlowTools();
#endif

            // Cliente HTTP único y de larga vida para la API del catálogo.
            // Un solo HttpClient también para las APIs autenticadas; el JWT se
            // agrega por request desde SesionService (ApiHttp.Preparar).
            var http = new HttpClient { BaseAddress = new Uri(ConfiguracionApp.ApiBaseUrl) };
            builder.Services.AddSingleton(http);

            // En MAUI los activos de productos (/img/…) los empaqueta el host
            // y los sirve el origin local del WebView (base vacía = relativas).
            // Así evitamos que Android bloquee imágenes HTTP de la API como
            // "mixed content" (la app se sirve desde https://0.0.0.1).
            var assets = new AssetsBase(string.Empty);
            builder.Services.AddSingleton(assets);
            builder.Services.AddSingleton<ICatalogoService>(_ => new HttpCatalogoService(http, assets));

            // Estado de sesión y carrito. El storage local va detrás de
            // IPreferenciasLocal (localStorage del WebView): Shared no depende
            // de APIs específicas de MAUI.
            builder.Services.AddScoped<IPreferenciasLocal, PreferenciasLocal>();
            builder.Services.AddScoped<SesionService>();
            builder.Services.AddScoped<CarritoService>();

            // Asistente conversacional (Gemini). La API key vive sólo en el
            // backend: la app llama a /api/asistente/mensaje y nunca la ve.
            builder.Services.AddScoped<ChatContexto>();
            builder.Services.AddScoped<IChatService>(sp =>
                new HttpChatService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SesionService>()));

            // Solicitudes de atención y notificaciones internas de la tienda.
            builder.Services.AddScoped<ISolicitudService>(sp =>
                new HttpSolicitudService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SesionService>()));
            builder.Services.AddScoped<INotificacionService>(sp =>
                new HttpNotificacionService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SesionService>()));

            builder.Services.AddScoped<IAuthService>(sp =>
                new HttpAuthService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SesionService>()));
            builder.Services.AddScoped<IPedidoService>(sp =>
                new HttpPedidoService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SesionService>(), assets));
            builder.Services.AddScoped<IAdminService>(sp =>
                new HttpAdminService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SesionService>(), assets));

            // Selección de imágenes según la plataforma: en Android/iOS el
            // selector nativo (galería) y en Windows el explorador del
            // sistema a través del <InputFile> del componente compartido.
            builder.Services.AddScoped<ISelectorImagenes, SelectorImagenesMaui>();
            builder.Services.AddScoped<IResolverImagenes, ResolverImagenesMaui>();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
