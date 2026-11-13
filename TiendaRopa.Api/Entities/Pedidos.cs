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

    /// <summary>Historial de actualizaciones públicas del pedido.</summary>
    public List<PedidoNota> Notas { get; set; } = [];
}

/// <summary>
/// Nota de estado pública de un pedido (historial de actualizaciones).
/// La crea el administrador y la ve el cliente en "Mis pedidos" y en el chat.
/// Nunca contiene datos de otros clientes: se valida la pertenencia del
/// pedido antes de exponerla.
/// </summary>
public class PedidoNota
{
    public int IdPedidoNota { get; set; }

    public int IdPedido { get; set; }

    /// <summary>Momento de la actualización (UTC).</summary>
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

    /// <summary>Texto público de la actualización.</summary>
    public required string Nota { get; set; }

    /// <summary>Empresa de transporte o courier (opcional).</summary>
    public string? Empresa { get; set; }

    /// <summary>Código de seguimiento (opcional).</summary>
    public string? CodigoSeguimiento { get; set; }

    public Pedido? Pedido { get; set; }
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
