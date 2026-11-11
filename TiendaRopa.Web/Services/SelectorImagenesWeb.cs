using TiendaRopa.Shared.Services;

namespace TiendaRopa.Web.Services;

/// <summary>
/// En Blazor Web la selección de archivos la hace el componente compartido
/// con &lt;InputFile&gt;, que abre el explorador del sistema. Por eso la
/// plataforma no usa el selector nativo del dispositivo.
/// </summary>
public sealed class SelectorImagenesWeb : ISelectorImagenes
{
    public bool UsaSelectorNativo => false;

    public Task<ResultadoSelectorImagenes> SeleccionarAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new ResultadoSelectorImagenes([], null));
}
