namespace TiendaRopa.Shared.Services;

/// <summary>
/// Base desde la que resolver los assets con ruta relativa (/img/…).
///
/// - Blazor Web: los activos viven en la API → "http://localhost:5100".
/// - MAUI: los mismos activos se empaquetan en wwwroot del host y los sirve
///   el propio origin local del WebView (https://0.0.0.1). Con base vacía la
///   ruta se deja relativa a propósito: en Android el WebView bloquearía una
///   imagen HTTP de la API como "mixed content" (la app se sirve por HTTPS).
/// </summary>
public sealed record AssetsBase(string Base)
{
    /// <summary>Resuelve una ruta de asset; devuelve null/relativa según corresponda.</summary>
    public string? Resolver(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith('/'))
        {
            return url;
        }

        if (string.IsNullOrEmpty(Base))
        {
            // Origen local del WebView (MAUI): la ruta relativa ya es válida.
            return url;
        }

        return new Uri(new Uri(Base), url).ToString();
    }
}
