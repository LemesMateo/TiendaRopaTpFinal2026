using TiendaRopa.Contracts.Catalogo;

namespace TiendaRopa.Shared.Services;

/// <summary>
/// Validación de imágenes compartida por la interfaz (Web y MAUI).
/// Aplica las mismas reglas que el servidor: tipo MIME permitido, firma
/// real del archivo y tamaño máximo.
/// </summary>
public static class ImagenesValidador
{
    /// <summary>Normaliza el tipo MIME ("image/jpg" pasa a "image/jpeg").</summary>
    public static string? NormalizarTipo(string? contentType)
    {
        var tipo = contentType?.Trim().ToLowerInvariant();
        if (tipo == "image/jpg")
        {
            tipo = "image/jpeg";
        }

        return ProductoImagenes.EsTipoPermitido(tipo) ? tipo : null;
    }

    /// <summary>
    /// Devuelve el mensaje de error si la imagen no es válida, o null si lo es.
    /// </summary>
    public static string? Validar(
        string? contentType, string? nombreArchivo, ReadOnlySpan<byte> contenido, long tamano)
    {
        if (tamano <= 0)
        {
            return $"\"{nombreArchivo}\" está vacío.";
        }

        if (tamano > ProductoImagenes.MaximoBytes)
        {
            return $"\"{nombreArchivo}\" supera el máximo de {ProductoImagenes.MaximoBytes / (1024 * 1024)} MB.";
        }

        if (NormalizarTipo(contentType) is null)
        {
            return $"\"{nombreArchivo}\" no es una imagen JPEG o PNG.";
        }

        if (!ProductoImagenes.TieneFirmaValida(contenido))
        {
            return $"\"{nombreArchivo}\" no es una imagen JPEG o PNG válida.";
        }

        return null;
    }
}
