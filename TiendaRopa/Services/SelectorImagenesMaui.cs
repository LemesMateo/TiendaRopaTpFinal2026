using TiendaRopa.Contracts.Catalogo;
using TiendaRopa.Shared.Services;

namespace TiendaRopa.Services;

/// <summary>
/// Selector de imágenes de MAUI.
/// - Android/iOS/MacCatalyst: selector nativo del sistema (galería).
/// - Windows: no hay selector nativo; la app usa el explorador de archivos
///   del sistema a través del &lt;InputFile&gt; del componente compartido.
/// </summary>
public sealed class SelectorImagenesMaui : ISelectorImagenes
{
#if ANDROID || IOS || MACCATALYST
    public bool UsaSelectorNativo => true;
#else
    public bool UsaSelectorNativo => false;
#endif

    public async Task<ResultadoSelectorImagenes> SeleccionarAsync(CancellationToken cancellationToken = default)
    {
#if ANDROID || IOS || MACCATALYST
        var opciones = new MediaPickerOptions
        {
            Title = "Agregar imágenes",
            SelectionLimit = ProductoImagenes.MaximoPorProducto,
        };

        // Abre la galería del sistema (Android) o el selector de fotos.
        var archivos = await MediaPicker.PickPhotosAsync(opciones);
        if (archivos.Count == 0)
        {
            // El usuario canceló el selector.
            return new ResultadoSelectorImagenes([], null);
        }

        var seleccion = new List<ImagenSeleccionada>();
        string? error = null;

        foreach (var archivo in archivos)
        {
            if (seleccion.Count >= ProductoImagenes.MaximoPorProducto)
            {
                error = $"Sólo se pueden agregar {ProductoImagenes.MaximoPorProducto} imágenes por producto.";
                break;
            }

            byte[] bytes;
            try
            {
                await using var stream = await archivo.OpenReadAsync();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                bytes = buffer.ToArray();
            }
            catch (IOException)
            {
                error = $"No se pudo leer \"{archivo.FileName}\".";
                continue;
            }

            var detalle = ImagenesValidador.Validar(
                archivo.ContentType, archivo.FileName, bytes, bytes.LongLength);

            if (detalle is not null)
            {
                error = detalle;
                continue;
            }

            seleccion.Add(new ImagenSeleccionada
            {
                NombreArchivo = archivo.FileName,
                ContentType = ImagenesValidador.NormalizarTipo(archivo.ContentType)!,
                Tamano = bytes.LongLength,
                Contenido = bytes,
            });
        }

        return new ResultadoSelectorImagenes(seleccion, error);
#else
        // En Windows la selección se hace con el explorador del sistema
        // mediante el <InputFile> del componente compartido.
        return new ResultadoSelectorImagenes([], null);
#endif
    }
}
