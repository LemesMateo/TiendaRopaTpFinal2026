namespace TiendaRopa.Contracts.Pedidos;

/// <summary>
/// Cliente HTTP de pedidos. El carrito NO descuenta stock: el backend sólo
/// valida y descuenta al confirmar el pedido (transacción única).
/// </summary>
public interface IPedidoService
{
    /// <summary>Confirma un pedido. Devuelve null cuando el backend lo rechazó (409/400/401).</summary>
    Task<PedidoDto?> CrearPedidoAsync(CrearPedidoRequest request, CancellationToken cancellationToken = default);

    /// <summary>Pedidos del usuario autenticado (más recientes primero).</summary>
    Task<IReadOnlyList<PedidoDto>> GetMisPedidosAsync(CancellationToken cancellationToken = default);

    /// <summary>Detalle de un pedido propio; null si no existe o es de otro cliente (403/404).</summary>
    Task<PedidoDto?> GetPedidoAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Cancela un pedido propio y restituye stock si corresponde.</summary>
    Task<PedidoDto?> CancelarPedidoAsync(int id, CancellationToken cancellationToken = default);
}
