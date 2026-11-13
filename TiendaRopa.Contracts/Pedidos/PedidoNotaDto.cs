namespace TiendaRopa.Contracts.Pedidos;

/// <summary>
/// Nota de estado pública de un pedido (historial de actualizaciones).
/// La puede crear el administrador desde el panel y la ve el cliente.
/// </summary>
public sealed class PedidoNotaDto
{
    public required int Id { get; init; }

    /// <summary>Fecha de la actualización (UTC).</summary>
    public required DateTime FechaActualizacion { get; init; }

    /// <summary>Texto público de la actualización.</summary>
    public required string Nota { get; init; }

    /// <summary>Empresa de transporte o courier (opcional).</summary>
    public string? Empresa { get; init; }

    /// <summary>Código de seguimiento (opcional).</summary>
    public string? CodigoSeguimiento { get; init; }
}
