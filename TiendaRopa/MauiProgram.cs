using Microsoft.Extensions.Logging;
using TiendaRopa.Contracts.Admin;
using TiendaRopa.Contracts.Catalogo;
using TiendaRopa.Contracts.Identidad;
using TiendaRopa.Contracts.Pedidos;
using TiendaRopa.Shared.Services;

namespace TiendaRopa
{
    public static class MauiProgram
    {
        // URL base de la API Velvet Rose por plataforma:
        //  - Windows/macOS: la API corre en esta misma máquina -> localhost.
        //  - Android (emulador): 10.0.2.2 apunta al host de la máquina.
        //    En un dispositivo físico hay que usar la IP en LAN de la PC
        //    (ajustar acá; verificar que la API escuche en esa red).
        // Nota: en Android se habilitó usesCleartextTraffic sólo en desarrollo.
#if ANDROID
        private const string ApiBaseUrl = "http://10.0.2.2:5100";
#else
        private const string ApiBaseUrl = "http://localhost:5100";
#endif

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

            // Cliente HTTP único y de larga vida para la API del catálogo.
            // Un solo HttpClient también para las APIs autenticadas; el JWT se
            // agrega por request desde SesionService (ApiHttp.Preparar).
            var http = new HttpClient { BaseAddress = new Uri(ApiBaseUrl) };
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

            builder.Services.AddScoped<IAuthService>(sp =>
                new HttpAuthService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SesionService>()));
            builder.Services.AddScoped<IPedidoService>(sp =>
                new HttpPedidoService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SesionService>(), assets));
            builder.Services.AddScoped<IAdminService>(sp =>
                new HttpAdminService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<SesionService>(), assets));

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
