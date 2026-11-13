namespace TiendaRopa.Contracts.Pedidos;

/// <summary>Pedido del cliente con sus líneas y estado actual.</summary>
public sealed class PedidoDto
{
    public int Id { get; init; }

    public DateTime FechaPedido { get; init; }

    /// <summary>Nombre del estado: Pendiente | Confirmado | Preparado | Entregado | Cancelado.</summary>
    public required string Estado { get; init; }

    public decimal Total { get; init; }

    public string? Observacion { get; init; }

    /// <summary>Indica si al cancelar este pedido se restituye stock (pedido con stock descontado).</summary>
    public bool PermiteCancelacion { get; init; }

    public IReadOnlyList<PedidoDetalleDto> Detalles { get; init; } = [];

    /// <summary>Historial de actualizaciones públicas del pedido (la más reciente primero).</summary>
    public IReadOnlyList<PedidoNotaDto> Notas { get; init; } = [];
}
