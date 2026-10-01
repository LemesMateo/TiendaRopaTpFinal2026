using System.ComponentModel.DataAnnotations;

namespace TiendaRopa.Contracts.Admin;

/// <summary>Cambio de estado de un pedido desde el panel administrativo.</summary>
public sealed class CambiarEstadoPedidoRequest
{
    /// <summary>Estado destino: Pendiente | Confirmado | Preparado | Entregado | Cancelado.</summary>
    [Required(ErrorMessage = "Indicá el estado destino.")]
    public required string Estado { get; set; }
}

/// <summary>Item del listado administrativo de pedidos.</summary>
public sealed class PedidoAdminDto
{
    public int Id { get; init; }

    public DateTime FechaPedido { get; init; }

    public required string Estado { get; init; }

    public decimal Total { get; init; }

    public required string Cliente { get; init; }

    public string? Email { get; init; }

    public int CantidadItems { get; init; }

    /// <summary>Estados hacia los que se puede transicionar desde el actual.</summary>
    public IReadOnlyList<string> TransicionesPermitidas { get; set; } = [];
}
