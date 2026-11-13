using System.ComponentModel.DataAnnotations;

namespace TiendaRopa.Contracts.Solicitudes;

/// <summary>Tipos de solicitud de atención (los mismos que el backend).</summary>
public static class SolicitudTipos
{
    public const string ConsultaPedido = "consulta_pedido";
    public const string ConsultaGeneral = "consulta_general";
    public const string SolicitudReposicion = "solicitud_reposicion";

    public static readonly IReadOnlyList<string> Todos = [ConsultaPedido, ConsultaGeneral, SolicitudReposicion];
}

/// <summary>Estados de una solicitud.</summary>
public static class SolicitudEstados
{
    public const string Abierta = "Abierta";
    public const string EnRevision = "EnRevision";
    public const string Respondida = "Respondida";
    public const string Cerrada = "Cerrada";

    public static readonly IReadOnlyList<string> Todos = [Abierta, EnRevision, Respondida, Cerrada];
}

/// <summary>Estado del teléfono de contacto de una solicitud.</summary>
public static class TelefonoEstados
{
    /// <summary>Viene del perfil del cliente, todavía sin confirmar.</summary>
    public const string PendienteConfirmacion = "pendiente_confirmacion";

    /// <summary>Confirmado por el cliente.</summary>
    public const string Confirmado = "confirmado";
}

/// <summary>
/// Solicitud de atención del cliente tal como la expone la API.
/// Nunca incluye el teléfono completo del cliente: sólo el de la solicitud
/// (ya confirmado) y el enmascarado del perfil.
/// </summary>
public sealed class SolicitudAtencionDto
{
    public int Id { get; init; }

    public required string Tipo { get; init; }

    public required string Estado { get; init; }

    public required string Mensaje { get; init; }

    public DateTime FechaCreacion { get; init; }

    public DateTime FechaActualizacion { get; init; }

    /// <summary>Pedido relacionado, si aplica.</summary>
    public int? IdPedido { get; init; }

    /// <summary>Descripción del pedido (para el listado del equipo).</summary>
    public string? PedidoDescripcion { get; init; }

    /// <summary>Producto relacionado, si aplica.</summary>
    public int? IdProducto { get; init; }

    /// <summary>Nombre del producto, si aplica.</summary>
    public string? ProductoNombre { get; init; }

    /// <summary>Variante relacionada, si la combinación existe.</summary>
    public int? IdVariante { get; init; }

    /// <summary>Talle pedido (texto libre si la variante no existe).</summary>
    public string? Talle { get; init; }

    /// <summary>Color pedido (texto libre si la variante no existe).</summary>
    public string? Color { get; init; }

    /// <summary>Teléfono de contacto de ESTA solicitud (confirmado por el cliente).</summary>
    public string? TelefonoContacto { get; init; }

    /// <summary>Estado del teléfono de la solicitud.</summary>
    public string? TelefonoEstado { get; init; }

    /// <summary>Respuesta del equipo de la tienda.</summary>
    public string? Respuesta { get; init; }

    /// <summary>Fecha de la respuesta del equipo.</summary>
    public DateTime? FechaResolucion { get; init; }

    /// <summary>Nombre del cliente (sólo para el personal de tienda).</summary>
    public string? ClienteNombre { get; init; }

    /// <summary>Email del cliente (sólo para el personal de tienda).</summary>
    public string? ClienteEmail { get; init; }
}

/// <summary>
/// Propuesta de solicitud preparada por el asistente. Todavía NO es una
/// solicitud: el cliente tiene que confirmarla explícitamente.
/// </summary>
public sealed class SolicitudPropuestaDto
{
    public int Id { get; init; }

    public required string Tipo { get; init; }

    public required string Mensaje { get; init; }

    /// <summary>Estado: "Pendiente" | "Confirmada" | "Cancelada".</summary>
    public required string Estado { get; init; }

    public int? IdPedido { get; init; }

    public string? PedidoDescripcion { get; init; }

    public int? IdProducto { get; init; }

    public string? ProductoNombre { get; init; }

    public int? IdVariante { get; init; }

    public string? Talle { get; init; }

    public string? Color { get; init; }

    /// <summary>Teléfono de contacto enmascarado (nunca el completo).</summary>
    public string? TelefonoEnmascarado { get; init; }

    /// <summary>Estado del teléfono (ver <see cref="TelefonoEstados"/>).</summary>
    public required string TelefonoEstado { get; init; }

    /// <summary>True si el cliente pidió guardar el teléfono en su perfil.</summary>
    public bool GuardarTelefonoEnPerfil { get; init; }

    /// <summary>Solicitud generada al confirmar (si ya se confirmó).</summary>
    public int? IdSolicitudGenerada { get; init; }

    /// <summary>Resumen listo para mostrar en la tarjeta de vista previa.</summary>
    public required string Resumen { get; init; }
}

/// <summary>
/// Prepara una solicitud para la tienda. Crea una PROPUESTA pendiente:
/// el cliente tiene que confirmarla para que se envíe.
/// </summary>
public sealed class CrearPropuestaRequest
{
    /// <summary>Tipo de solicitud (ver <see cref="SolicitudTipos"/>).</summary>
    [Required(ErrorMessage = "Indicá el tipo de solicitud.")]
    [MaxLength(40)]
    public required string Tipo { get; init; }

    /// <summary>Texto del cliente para la tienda.</summary>
    [Required(ErrorMessage = "Escribí tu consulta.")]
    [MaxLength(1000, ErrorMessage = "El mensaje es demasiado largo (máximo 1000 caracteres).")]
    public required string Mensaje { get; init; }

    /// <summary>Pedido del cliente (opcional).</summary>
    public int? IdPedido { get; init; }

    /// <summary>Producto (opcional, para reposición).</summary>
    public int? IdProducto { get; init; }

    /// <summary>Variante (opcional): sólo si la combinación existe.</summary>
    public int? IdVariante { get; init; }

    /// <summary>Talle pedido (texto libre si la variante no existe).</summary>
    [MaxLength(30)]
    public string? Talle { get; init; }

    /// <summary>Color pedido (texto libre si la variante no existe).</summary>
    [MaxLength(50)]
    public string? Color { get; init; }
}

/// <summary>
/// Confirma una propuesta y crea la solicitud. Es la ÚNICA vía de escritura:
/// el modelo puede preparar una propuesta, pero nunca enviarla.
/// </summary>
public sealed class ConfirmarPropuestaRequest
{
    /// <summary>
    /// Teléfono alternativo que el cliente confirmó (opcional).
    /// Si no se informa, se usa el del perfil (que ya estaba confirmado).
    /// </summary>
    [MaxLength(50, ErrorMessage = "El teléfono es demasiado largo (máximo 50 caracteres).")]
    public string? Telefono { get; init; }

    /// <summary>
    /// Consentimiento expreso para guardar el teléfono en el perfil del cliente.
    /// Si es false, el teléfono se usa sólo para esta solicitud.
    /// </summary>
    public bool GuardarTelefonoEnPerfil { get; init; }

    /// <summary>
    /// Clave de idempotencia del cliente: dos confirmaciones con la misma clave
    /// devuelven la misma solicitud (protege contra doble clic y reintentos).
    /// </summary>
    [MaxLength(100, ErrorMessage = "La clave de idempotencia es demasiado larga.")]
    public string? ClaveIdempotencia { get; init; }
}

/// <summary>
/// Fija (o cambia) el teléfono de contacto de una propuesta pendiente.
/// </summary>
public sealed class ConfirmarTelefonoPropuestaRequest
{
    [Required(ErrorMessage = "Indicá un número de contacto.")]
    [MaxLength(50, ErrorMessage = "El teléfono es demasiado largo (máximo 50 caracteres).")]
    public required string Telefono { get; init; }

    /// <summary>Consentimiento expreso para actualizar el perfil del cliente.</summary>
    public bool GuardarEnPerfil { get; init; }
}

/// <summary>Respuesta del equipo de tienda a una solicitud.</summary>
public sealed class ResponderSolicitudRequest
{
    [Required(ErrorMessage = "Escribí la respuesta.")]
    [MaxLength(1000, ErrorMessage = "La respuesta es demasiado larga (máximo 1000 caracteres).")]
    public required string Respuesta { get; init; }
}

/// <summary>Cambio de estado de una solicitud.</summary>
public sealed class CambiarEstadoSolicitudRequest
{
    [Required(ErrorMessage = "Indicá el estado destino.")]
    public required string Estado { get; init; }
}
