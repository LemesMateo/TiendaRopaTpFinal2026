namespace TiendaRopa.Contracts.Asistente;

/// <summary>Tipos de acción que el asistente puede proponer.</summary>
public static class AccionTipos
{
    /// <summary>Consulta ampliada sobre un pedido existente.</summary>
    public const string ConsultaPedido = "consulta_pedido";

    /// <summary>Solicitud de reposición de una variante sin stock.</summary>
    public const string ReposicionVariante = "reposicion_variante";

    /// <summary>
    /// Solicitud preparada para la tienda. Requiere confirmación explícita
    /// del cliente antes de enviarse.
    /// </summary>
    public const string SolicitudAtencion = "solicitud_atencion";
}

/// <summary>
/// Propuesta de acción del asistente.
///
/// Si <see cref="IdPropuesta"/> viene informado, la acción es una SOLICITUD
/// PREPARADA: la UI la muestra con los botones Confirmar y Cancelar, y sólo
/// el botón Confirmar llama al endpoint de escritura (con idempotencia).
/// </summary>
public sealed class AccionPropuestaDto
{
    /// <summary>Tipo de acción (ver <see cref="AccionTipos"/>).</summary>
    public required string Tipo { get; init; }

    /// <summary>Título corto de la tarjeta.</summary>
    public required string Titulo { get; init; }

    /// <summary>Resumen legible de lo que se propondría.</summary>
    public required string Detalle { get; init; }

    /// <summary>Pedido relacionado, si aplica.</summary>
    public int? IdPedido { get; init; }

    /// <summary>Producto relacionado, si aplica.</summary>
    public int? IdProducto { get; init; }

    /// <summary>Variante relacionada, si aplica.</summary>
    public int? IdVariante { get; init; }

    /// <summary>
    /// Id de la propuesta preparada en el backend. Si viene informado, la
    /// acción se puede confirmar (y el backend valida la propiedad).
    /// </summary>
    public int? IdPropuesta { get; init; }

    /// <summary>
    /// Teléfono de contacto enmascarado (nunca el completo). Si el cliente
    /// tiene que confirmarlo, la UI lo pide antes de habilitar el envío.
    /// </summary>
    public string? TelefonoEnmascarado { get; set; }

    /// <summary>True si el teléfono todavía debe ser confirmado por el cliente.</summary>
    public bool RequiereConfirmarTelefono { get; set; }

    /// <summary>
    /// Estado de la propuesta: "propuesta", "confirmada" o "cancelada".
    /// </summary>
    public string Estado { get; set; } = "propuesta";
}

/// <summary>
/// Contrato de una futura consulta ampliada sobre un pedido. Existe para
/// dejar diseñado el sistema de solicitudes del segundo incremento; hoy no
/// se persiste ni se envía nada.
/// </summary>
public sealed class SolicitudConsultaPedidoDto
{
    public required int IdPedido { get; init; }

    public required string Motivo { get; init; }

    public DateTime FechaCreacion { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Contrato de una futura solicitud de reposición de una variante sin stock.
/// Mismo criterio: se diseña ahora, se implementa en el segundo incremento.
/// </summary>
public sealed class SolicitudReposicionVarianteDto
{
    public required int IdProducto { get; init; }

    public required int IdVariante { get; init; }

    public required string Talle { get; init; }

    public required string Color { get; init; }

    /// <summary>Correo del cliente: destino de la notificación (no se expone al modelo).</summary>
    public required string Email { get; init; }

    public required string Motivo { get; init; }

    public DateTime FechaCreacion { get; init; } = DateTime.UtcNow;
}
