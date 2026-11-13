namespace TiendaRopa.Contracts.Catalogo;

/// <summary>
/// Posición de la lista final de imágenes de un producto. El orden de la
/// lista enviada es el orden definitivo; la marca <see cref="Principal"/> la
/// asigna el servidor y, si ninguna la lleva, la primera pasa a serlo.
/// </summary>
public sealed class ImagenFinalDto
{
    /// <summary>
    /// Identificador de una imagen ya persistida (&gt; 0). Es la que se conserva.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Índice del archivo nuevo dentro del lote (sólo para imágenes con
    /// <see cref="Id"/> = 0). Permite relacionar el JSON con los archivos
    /// enviados sin depender de su nombre.
    /// </summary>
    public int? IndiceNueva { get; init; }

    /// <summary>True si esta posición es la imagen principal del producto.</summary>
    public bool Principal { get; init; }
}

/// <summary>
/// Archivo nuevo (todavía no persistido) que se envía junto con la lista
/// final de imágenes del producto.
/// </summary>
public sealed class ImagenNuevaDto
{
    /// <summary>Contenido del archivo (máximo <see cref="ProductoImagenes.MaximoBytes"/>).</summary>
    public required byte[] Contenido { get; init; }

    /// <summary>Nombre original del archivo (se conserva sólo como referencia).</summary>
    public required string NombreArchivo { get; init; }

    /// <summary>Tipo MIME validado por el cliente (image/jpeg o image/png).</summary>
    public required string ContentType { get; init; }
}
