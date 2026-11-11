namespace TiendaRopa.Shared.Services;

/// <summary>
/// Imagen elegida por el usuario. El contenido ya viene acotado por el
/// límite de tamaño y validado (tipo y firma) antes de armar esta clase.
/// </summary>
public sealed class ImagenSeleccionada
{
    /// <summary>Nombre original del archivo (sólo informativo).</summary>
    public required string NombreArchivo { get; init; }

    /// <summary>Tipo MIME detectado (image/jpeg o image/png).</summary>
    public required string ContentType { get; init; }

    /// <summary>Tamaño del archivo en bytes.</summary>
    public required long Tamano { get; init; }

    /// <summary>Bytes del archivo.</summary>
    public required byte[] Contenido { get; init; }

    /// <summary>Vista previa en línea (data URL) para mostrarla al instante.</summary>
    public string UrlDatos => $"data:{ContentType};base64,{Convert.ToBase64String(Contenido)}";
}

/// <summary>Resultado de abrir el selector de imágenes.</summary>
/// <param name="Imagenes">Imágenes válidas elegidas.</param>
/// <param name="Error">Mensaje para el usuario, si algún archivo fue rechazado.</param>
public sealed record ResultadoSelectorImagenes(IReadOnlyList<ImagenSeleccionada> Imagenes, string? Error)
{
    /// <summary>El usuario canceló el selector (no es un error).</summary>
    public bool Cancelado => Imagenes.Count == 0 && Error is null;
}

/// <summary>
/// Selección de imágenes con el mecanismo de cada plataforma:
/// - MAUI en Android/iOS/MacCatalyst: selector nativo (galería del sistema).
/// - MAUI en Windows y Blazor Web: explorador de archivos del sistema
///   (&lt;InputFile&gt;), que en el WebView abre el diálogo del sistema.
///
/// La interfaz compartida decide según <see cref="UsaSelectorNativo"/>.
/// </summary>
public interface ISelectorImagenes
{
    /// <summary>True si la plataforma abre su selector nativo de imágenes.</summary>
    bool UsaSelectorNativo { get; }

    /// <summary>Abre el selector nativo y devuelve las imágenes válidas.</summary>
    Task<ResultadoSelectorImagenes> SeleccionarAsync(CancellationToken cancellationToken = default);
}
