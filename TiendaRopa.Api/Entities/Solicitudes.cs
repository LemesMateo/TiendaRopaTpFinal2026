using TiendaRopa.Contracts.Solicitudes;

namespace TiendaRopa.Api.Entities;

// Las constantes de tipo, estado y teléfono viven en
// TiendaRopa.Contracts.Solicitudes (fuente única, compartida con la UI).

/// <summary>
/// Solicitud de atención del cliente (consulta, consulta de pedido o reposición).
/// Nace de una propuesta confirmada explícitamente por el cliente.
/// </summary>
public class SolicitudAtencion
{
    public int IdSolicitud { get; set; }

    /// <summary>Cliente que abrió la solicitud.</summary>
    public int IdCliente { get; set; }

    /// <summary>Usuario que la abrió (coincide con el cliente, pero queda trazado).</summary>
    public int IdUsuario { get; set; }

    /// <summary>Tipo de solicitud (ver <see cref="SolicitudTipos"/>).</summary>
    public required string Tipo { get; set; }

    /// <summary>Pedido relacionado (opcional: sólo para consulta de pedido).</summary>
    public int? IdPedido { get; set; }

    /// <summary>Producto relacionado (opcional: sólo para reposición).</summary>
    public int? IdProducto { get; set; }

    /// <summary>Variante relacionada, si la combinación existe en el catálogo.</summary>
    public int? IdProductoVariante { get; set; }

    /// <summary>Talle pedido por el cliente (texto libre si no existe la variante).</summary>
    public string? TalleDescripcion { get; set; }

    /// <summary>Color pedido por el cliente (texto libre si no existe la variante).</summary>
    public string? ColorDescripcion { get; set; }

    /// <summary>Texto del cliente.</summary>
    public required string Mensaje { get; set; }

    /// <summary>Teléfono de contacto confirmado (sólo para esta solicitud o el perfil).</summary>
    public string? TelefonoContacto { get; set; }

    /// <summary>Estado del teléfono (ver <see cref="TelefonoEstados"/>).</summary>
    public string? TelefonoEstado { get; set; }

    /// <summary>Estado de la solicitud (ver <see cref="SolicitudEstados"/>).</summary>
    public required string Estado { get; set; } = SolicitudEstados.Abierta;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

    /// <summary>Respuesta del equipo de la tienda.</summary>
    public string? Respuesta { get; set; }

    /// <summary>Usuario que respondió o cerró la solicitud.</summary>
    public int? IdUsuarioResuelve { get; set; }

    public DateTime? FechaResolucion { get; set; }

    /// <summary>
    /// Clave de idempotencia: evita crear dos solicitudes por la misma
    /// propuesta o por un reintento del cliente.
    /// </summary>
    public string? ClaveIdempotencia { get; set; }

    /// <summary>Propuesta del chatbot que originó esta solicitud.</summary>
    public int? IdPropuesta { get; set; }

    public Cliente? Cliente { get; set; }

    public Pedido? Pedido { get; set; }

    public Producto? Producto { get; set; }

    public List<Notificacion> Notificaciones { get; set; } = [];
}

/// <summary>
/// Propuesta de solicitud preparada por el asistente. NO es una solicitud:
/// queda en estado Pendiente hasta que el cliente la confirma explícitamente.
/// </summary>
public class SolicitudPropuesta
{
    public int IdPropuesta { get; set; }

    /// <summary>Usuario al que pertenece la propuesta (sólo él puede confirmarla).</summary>
    public int IdUsuario { get; set; }

    /// <summary>Cliente al que pertenece la propuesta.</summary>
    public int IdCliente { get; set; }

    public required string Tipo { get; set; }

    public int? IdPedido { get; set; }

    public int? IdProducto { get; set; }

    public int? IdProductoVariante { get; set; }

    public string? TalleDescripcion { get; set; }

    public string? ColorDescripcion { get; set; }

    public required string Mensaje { get; set; }

    /// <summary>Teléfono de contacto elegido (enmascarado en las respuestas al modelo).</summary>
    public string? TelefonoContacto { get; set; }

    /// <summary>Estado del teléfono (ver <see cref="TelefonoEstados"/>).</summary>
    public string TelefonoEstado { get; set; } = TelefonoEstados.PendienteConfirmacion;

    /// <summary>True si el cliente pidió guardar el teléfono en su perfil.</summary>
    public bool GuardarTelefonoEnPerfil { get; set; }

    /// <summary>Estado: "Pendiente" | "Confirmada" | "Cancelada".</summary>
    public required string Estado { get; set; } = "Pendiente";

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaConfirmacion { get; set; }

    /// <summary>Solicitud generada al confirmar (si ya se confirmó).</summary>
    public int? IdSolicitudGenerada { get; set; }
}

/// <summary>
/// Notificación interna para el personal de la tienda, vinculada a una
/// solicitud. Persisten en SQL Server: no se pierden al recargar la página.
/// </summary>
public class Notificacion
{
    public int IdNotificacion { get; set; }

    /// <summary>Usuario destinatario (Administrador o Vendedor).</summary>
    public int IdUsuario { get; set; }

    /// <summary>Solicitud relacionada.</summary>
    public int IdSolicitud { get; set; }

    /// <summary>Tipo de notificación (ver <see cref="NotificacionTipos"/>).</summary>
    public required string Tipo { get; set; }

    /// <summary>Texto breve para la campana.</summary>
    public required string Mensaje { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public bool Leida { get; set; }

    public DateTime? FechaLectura { get; set; }

    public SolicitudAtencion? Solicitud { get; set; }
}
