using TiendaRopa.Contracts.Catalogo;
using TiendaRopa.Shared.Services;

namespace TiendaRopa.Services;

/// <summary>
/// Resuelve las imágenes en MAUI.
///
/// El WebView de Blazor se sirve por https (https://0.0.0.0) y el sistema
/// bloquea como "mixed content" cualquier imagen http de la API. Por eso la
/// app descarga la imagen con su propio HttpClient (fuera del WebView) y la
/// muestra como data URL. Si la API no responde, se cae a la ruta relativa,
/// que sirve la copia del archivo empaquetada en la app.
/// </summary>
public sealed class ResolverImagenesMaui(HttpClient http, AssetsBase assets) : IResolverImagenes
{
    private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);

    public async Task<string?> ResolverAsync(string? urlImagen, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(urlImagen))
        {
            return null;
        }

        if (_cache.TryGetValue(urlImagen, out var guardada))
        {
            return guardada;
        }

        string? resuelta = null;

        try
        {
            var completa = new Uri(new Uri(ConfiguracionApp.ApiBaseUrl), urlImagen).ToString();
            var bytes = await http.GetByteArrayAsync(completa, cancellationToken);

            if (bytes.Length > 0 && bytes.Length <= ProductoImagenes.MaximoBytes)
            {
                var tipo = TipoPorRuta(urlImagen);
                resuelta = $"data:{tipo};base64,{Convert.ToBase64String(bytes)}";
            }
        }
        catch (HttpRequestException)
        {
            // Sin API: se usa la copia empaquetada en la app.
        }
        catch (TaskCanceledException)
        {
            // Tiempo agotado: se usa la copia empaquetada en la app.
        }

        resuelta ??= assets.Resolver(urlImagen);

        _cache[urlImagen] = resuelta;
        return resuelta;
    }

    private static string TipoPorRuta(string urlImagen)
        => urlImagen.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
}
