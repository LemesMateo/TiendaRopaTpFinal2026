namespace TiendaRopa.Contracts.Catalogo;

/// <summary>
/// Reglas de las imágenes de producto. Vive en los contratos para que la
/// interfaz y el servidor validen lo mismo (límite, tipo y tamaño).
/// </summary>
public static class ProductoImagenes
{
    /// <summary>Máximo de imágenes por producto.</summary>
    public const int MaximoPorProducto = 5;

    /// <summary>Tamaño máximo por archivo (5 MB).</summary>
    public const long MaximoBytes = 5 * 1024 * 1024;

    /// <summary>Prefijo de las rutas de imagen servidas por la API.</summary>
    public const string PrefijoRuta = "/img/productos/";

    /// <summary>Tipos MIME aceptados (JPEG y PNG).</summary>
    public static readonly IReadOnlyList<string> TiposPermitidos = ["image/jpeg", "image/png"];

    /// <summary>True si el tipo MIME está permitido.</summary>
    public static bool EsTipoPermitido(string? contentType)
        => contentType is not null
           && TiposPermitidos.Contains(contentType.Trim().ToLowerInvariant());

    /// <summary>Extensión del archivo según el tipo MIME ("jpg" o "png").</summary>
    public static string? ExtensionPorTipo(string? contentType) => contentType?.Trim().ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => "jpg",
        "image/png" => "png",
        _ => null,
    };

    /// <summary>
    /// True si la cabecera del archivo corresponde a un JPEG o PNG real.
    /// Evita confiar sólo en el Content-Type declarado por el cliente.
    /// </summary>
    public static bool TieneFirmaValida(ReadOnlySpan<byte> cabecera)
    {
        // JPEG: FF D8 FF
        if (cabecera.Length >= 3 && cabecera[0] == 0xFF && cabecera[1] == 0xD8 && cabecera[2] == 0xFF)
        {
            return true;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        return cabecera.Length >= 8
               && cabecera[0] == 0x89 && cabecera[1] == 0x50 && cabecera[2] == 0x4E && cabecera[3] == 0x47
               && cabecera[4] == 0x0D && cabecera[5] == 0x0A && cabecera[6] == 0x1A && cabecera[7] == 0x0A;
    }

    /// <summary>
    /// True si el archivo fue subido por el administrador (el nombre lleva un
    /// GUID). Sirve para borrar de disco sólo los archivos propios y no tocar
    /// los placeholders de desarrollo que se empaquetan con la app.
    /// </summary>
    public static bool EsArchivoSubido(string nombreArchivo)
    {
        var nombre = Path.GetFileName(nombreArchivo);
        var guion = nombre.LastIndexOf('-');
        var punto = nombre.LastIndexOf('.');
        if (guion <= 0 || punto <= guion + 1 || punto >= nombre.Length - 1)
        {
            return false;
        }

        return Guid.TryParseExact(nombre.Substring(guion + 1, punto - guion - 1), "N", out _);
    }
}
