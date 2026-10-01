using Microsoft.Extensions.Logging;
using TiendaRopa.Contracts.Catalogo;
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
            builder.Services.AddSingleton<ICatalogoService>(_ =>
                new HttpCatalogoService(new HttpClient { BaseAddress = new Uri(ApiBaseUrl) }));

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
