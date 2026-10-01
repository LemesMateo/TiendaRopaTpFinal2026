using TiendaRopa.Contracts.Common;

namespace TiendaRopa.Contracts.Catalogo;

/// <summary>
/// Contrato de lectura del catálogo. Lo implementa el cliente HTTP de la UI
/// (TiendaRopa.Shared) y puede implementarse con datos locales en pruebas.
/// </summary>
public interface ICatalogoService
{
    /// <summary>Lista productos activos con paginación y filtros.</summary>
    Task<PagedResult<ProductoListItemDto>> GetProductosAsync(
        CatalogoQuery query, CancellationToken cancellationToken = default);

    /// <summary>Devuelve el producto o null si no existe o está inactivo.</summary>
    Task<ProductoDetalleDto?> GetProductoAsync(
        int id, CancellationToken cancellationToken = default);

    /// <summary>Categorías activas.</summary>
    Task<IReadOnlyList<CategoriaDto>> GetCategoriasAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Variantes (talle/color/stock) de un producto activo.</summary>
    Task<IReadOnlyList<VarianteDto>> GetVariantesAsync(
        int productoId, CancellationToken cancellationToken = default);
}
