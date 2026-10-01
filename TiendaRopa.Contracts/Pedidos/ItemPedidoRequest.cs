using System.ComponentModel.DataAnnotations;

namespace TiendaRopa.Contracts.Pedidos;

/// <summary>Línea de pedido. El precio NO viaja desde el cliente: lo fija el backend.</summary>
public sealed class ItemPedidoRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "La variante es inválida.")]
    public int IdProductoVariante { get; init; }

    [Range(1, 100, ErrorMessage = "La cantidad debe estar entre 1 y 100.")]
    public int Cantidad { get; init; }
}
