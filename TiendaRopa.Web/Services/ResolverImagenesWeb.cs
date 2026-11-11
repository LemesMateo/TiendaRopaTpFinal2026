using TiendaRopa.Shared.Services;

namespace TiendaRopa.Web.Services;

/// <summary>
/// En Blazor Web las imágenes las sirve la API, así que alcanza con resolver
/// la ruta relativa contra la base de la API.
/// </summary>
public sealed class ResolverImagenesWeb(AssetsBase assets) : IResolverImagenes
{
    public Task<string?> ResolverAsync(string? urlImagen, CancellationToken cancellationToken = default)
        => Task.FromResult(assets.Resolver(urlImagen));
}
