namespace TiendaRopa.Api.Entities;

public class EstadoPedido
{
    public int IdEstadoPedido { get; set; }

    public required string Nombre { get; set; }

    public string? Descripcion { get; set; }

    public List<Pedido> Pedidos { get; set; } = [];
}

public class Pedido
{
    public int IdPedido { get; set; }

    public int IdCliente { get; set; }

    public int IdEstadoPedido { get; set; }

    public DateTime FechaPedido { get; set; } = DateTime.UtcNow;

    public decimal Total { get; set; }

    public string? Observacion { get; set; }

    public Cliente? Cliente { get; set; }

    public EstadoPedido? EstadoPedido { get; set; }

    public List<PedidoDetalle> Detalles { get; set; } = [];
}

public class PedidoDetalle
{
    public int IdPedidoDetalle { get; set; }

    public int IdPedido { get; set; }

    public int IdProductoVariante { get; set; }

    public int Cantidad { get; set; }

    public decimal PrecioUnitario { get; set; }

    public decimal Subtotal { get; set; }

    public Pedido? Pedido { get; set; }

    public ProductoVariante? ProductoVariante { get; set; }
}
