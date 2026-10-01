using System.ComponentModel.DataAnnotations;

namespace TiendaRopa.Contracts.Pedidos;

/// <summary>Confirmación de pedido desde el carrito.</summary>
public sealed class CrearPedidoRequest
{
    [Required(ErrorMessage = "El pedido no tiene productos.")]
    [MinLength(1, ErrorMessage = "El pedido debe tener al menos un producto.")]
    public required List<ItemPedidoRequest> Items { get; init; }

    [StringLength(1000)]
    public string? Observacion { get; init; }
}
