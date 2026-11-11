namespace TiendaRopa.Shared.Models;

/// <summary>
/// Imagen del producto en edición. Puede venir de la base (ya guardada) o ser
/// nueva (subida al servidor al guardar el producto).
/// </summary>
public sealed class ImagenProducto
{
    /// <summary>Identificador en la base; 0 si la imagen todavía no se subió.</summary>
    public int Id { get; init; }

    /// <summary>Ruta en la API (/img/productos/…) o data URL para la vista previa.</summary>
    public required string Url { get; init; }

    /// <summary>True si la imagen todavía no está en el servidor.</summary>
    public bool EsNueva { get; init; }

    /// <summary>True si es la imagen principal del producto.</summary>
    public bool Principal { get; init; }

    /// <summary>Bytes de la imagen nueva (sólo si <see cref="EsNueva"/>).</summary>
    public byte[]? Contenido { get; init; }

    /// <summary>Tipo MIME de la imagen nueva.</summary>
    public string? ContentType { get; init; }

    /// <summary>Nombre original del archivo.</summary>
    public string? NombreArchivo { get; init; }
}
