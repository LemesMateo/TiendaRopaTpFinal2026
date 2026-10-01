using TiendaRopa.Contracts.Catalogo;
using TiendaRopa.Contracts.Common;
using TiendaRopa.Contracts.Pedidos;

namespace TiendaRopa.Contracts.Admin;

/// <summary>
/// API administrativa. Todos los endpoints exigen rol Administrador:
/// el backend valida el rol desde el JWT, nunca desde datos del frontend.
/// </summary>
public interface IAdminService
{
    /// <summary>Último mensaje de error de negocio (403, 409, validaciones…).</summary>
    string? UltimoError { get; }

    // ---- Productos ----
    Task<PagedResult<ProductoAdminDto>> GetProductosAsync(
        string? buscar, bool? activo, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<ProductoAdminDto?> GetProductoAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Crea o actualiza; devuelve null cuando el backend rechaza la operación.</summary>
    Task<ProductoAdminDto?> GuardarProductoAsync(ProductoUpsertRequest request, CancellationToken cancellationToken = default);

    /// <summary>Baja lógica (Activo = false) para conservar el historial.</summary>
    Task<bool> DesactivarProductoAsync(int id, CancellationToken cancellationToken = default);

    // ---- Categorías / Talles / Colores ----
    Task<IReadOnlyList<EntidadCatalogoDto>> GetCategoriasAsync(bool? activo, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EntidadCatalogoDto>> GetTallesAsync(bool? activo, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EntidadCatalogoDto>> GetColoresAsync(bool? activo, CancellationToken cancellationToken = default);

    Task<EntidadCatalogoDto?> GuardarCategoriaAsync(NombreActivoRequest request, CancellationToken cancellationToken = default);
    Task<EntidadCatalogoDto?> GuardarTalleAsync(NombreActivoRequest request, CancellationToken cancellationToken = default);
    Task<EntidadCatalogoDto?> GuardarColorAsync(NombreActivoRequest request, CancellationToken cancellationToken = default);

    Task<bool> DesactivarCategoriaAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> DesactivarTalleAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> DesactivarColorAsync(int id, CancellationToken cancellationToken = default);

    // ---- Variantes ----
    /// <summary>Alta/edición. Rechaza duplicar IdProducto + IdTalle + IdColor y stock negativo.</summary>
    Task<VarianteAdminDto?> GuardarVarianteAsync(VarianteUpsertRequest request, CancellationToken cancellationToken = default);

    // ---- Pedidos ----
    Task<PagedResult<PedidoAdminDto>> GetPedidosAsync(
        string? estado, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<PedidoDto?> GetPedidoAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Aplica las transiciones válidas; devuelve null si la transición no está permitida.</summary>
    Task<PedidoDto?> CambiarEstadoPedidoAsync(int id, CambiarEstadoPedidoRequest request, CancellationToken cancellationToken = default);

    // ---- Imágenes ----
    /// <summary>Reemplaza el orden/principal de las imágenes de un producto.</summary>
    Task<bool> GuardarImagenesAsync(int productoId, IReadOnlyList<ProductoImagenDto> imagenes, CancellationToken cancellationToken = default);
}
