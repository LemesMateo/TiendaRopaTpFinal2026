namespace TiendaRopa.Shared.Services;

/// <summary>
/// Resuelve la URL de una imagen para poder mostrarla en un &lt;img&gt;.
///
/// En Blazor Web la API es quien sirve los archivos, así que devuelve la URL
/// absoluta. En MAUI el WebView se sirve por https y no puede pedir http a la
/// API (contenido mixto bloqueado), por eso la implementación de MAUI
/// descarga la imagen con el HttpClient de la app y la devuelve como data URL.
/// </summary>
public interface IResolverImagenes
{
    /// <summary>URL usable en el &lt;img&gt;, o null si no hay imagen.</summary>
    Task<string?> ResolverAsync(string? urlImagen, CancellationToken cancellationToken = default);
}
