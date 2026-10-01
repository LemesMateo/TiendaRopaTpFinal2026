namespace TiendaRopa.Contracts.Pedidos;

/// <summary>Línea de un pedido, con el precio vigente al momento de confirmarlo.</summary>
public sealed class PedidoDetalleDto
{
    public int IdProductoVariante { get; init; }

    public int IdProducto { get; init; }

    public required string Producto { get; init; }

    /// <summary>Relativa a la API (/img/…); el cliente la resuelve con su BaseAddress.</summary>
    public string? UrlImagen { get; set; }

    public required string Talle { get; init; }

    public required string Color { get; init; }

    public string? CodigoHex { get; init; }

    public int Cantidad { get; init; }

    public decimal PrecioUnitario { get; init; }

    public decimal Subtotal { get; init; }
}
