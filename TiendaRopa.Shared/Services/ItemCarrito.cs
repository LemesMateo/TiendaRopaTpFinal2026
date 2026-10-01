namespace TiendaRopa.Shared.Services;

/// <summary>
/// Línea del carrito. Lo conserva el cliente: agregar al carrito NUNCA
/// modifica stock en el servidor; la validación ocurre al confirmar el pedido.
/// </summary>
public sealed class ItemCarrito
{
    public int IdProducto { get; init; }

    public int IdProductoVariante { get; init; }

    public required string Nombre { get; init; }

    public string? Imagen { get; init; }

    public required string Talle { get; init; }

    public required string Color { get; init; }

    public int Cantidad { get; set; }

    public decimal PrecioUnitario { get; init; }

    public decimal Subtotal => PrecioUnitario * Cantidad;
}
