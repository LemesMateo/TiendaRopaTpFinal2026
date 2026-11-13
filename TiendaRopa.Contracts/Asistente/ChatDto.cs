using System.ComponentModel.DataAnnotations;

namespace TiendaRopa.Contracts.Asistente;

/// <summary>Roles válidos dentro del historial de conversación.</summary>
public static class ChatRoles
{
    public const string Usuario = "user";

    public const string Asistente = "assistant";
}

/// <summary>
/// Sugerencias iniciales del chat (chips clicables). Viven en el contrato
/// para que la UI y el backend muestren exactamente las mismas.
/// </summary>
public static class ChatSugerencias
{
    /// <summary>Sugerencias que la UI muestra al abrir el chat por primera vez.</summary>
    public static readonly IReadOnlyList<string> Iniciales =
    [
        "¿Dónde está mi pedido?",
        "Consultar stock",
        "Contactar a la tienda",
    ];
}

/// <summary>Un mensaje del historial de conversación del cliente.</summary>
public sealed class ChatMensajeDto
{
    /// <summary>Rol: <see cref="ChatRoles.Usuario"/> o <see cref="ChatRoles.Asistente"/>.</summary>
    [Required]
    public required string Rol { get; init; }

    /// <summary>Texto del mensaje (contenido ya generado, no instrucciones).</summary>
    [Required]
    [MaxLength(4000)]
    public required string Texto { get; init; }
}

/// <summary>Petición de un turno de conversación con el asistente.</summary>
public sealed class ChatSolicitudDto
{
    /// <summary>Mensaje del cliente.</summary>
    [Required(ErrorMessage = "Indicá un mensaje.")]
    [MaxLength(2000, ErrorMessage = "El mensaje es demasiado largo (máximo 2000 caracteres).")]
    public required string Mensaje { get; init; }

    /// <summary>
    /// Historial breve de la conversación. El backend lo recorta a los últimos
    /// turnos antes de enviarlo al modelo.
    /// </summary>
    public IReadOnlyList<ChatMensajeDto> Historial { get; init; } = [];

    /// <summary>
    /// IdProducto de la página de detalle desde donde se abrió el chat (opcional).
    /// El backend lo valida: si no existe o está inactivo, lo ignora.
    /// </summary>
    public int? IdProductoContexto { get; init; }

    /// <summary>
    /// Pedido que el cliente viene consultando en esta sesión (opcional).
    /// El backend valida que le pertenezca antes de usarlo.
    /// </summary>
    public int? IdPedidoContexto { get; init; }
}

/// <summary>Un pedido que el asistente ofrece como opción para desambiguar.</summary>
public sealed class ChatPedidoOpcionDto
{
    public required int Id { get; init; }

    /// <summary>Descripción breve: estado + productos principales.</summary>
    public required string Etiqueta { get; init; }

    /// <summary>Fecha del pedido (dd/MM/yyyy).</summary>
    public required string Fecha { get; init; }

    /// <summary>Estado actual del pedido.</summary>
    public required string Estado { get; init; }
}

/// <summary>Respuesta de un turno de conversación.</summary>
public sealed class ChatRespuestaDto
{
    /// <summary>Texto de la respuesta del asistente.</summary>
    public required string Mensaje { get; init; }

    /// <summary>Sugerencias clicables para continuar la conversación.</summary>
    public IReadOnlyList<string> Sugerencias { get; init; } = [];

    /// <summary>Pedidos que el cliente puede elegir (cuando hay varios activos).</summary>
    public IReadOnlyList<ChatPedidoOpcionDto> OpcionesPedido { get; init; } = [];

    /// <summary>Acciones propuestas por el asistente (sólo vista previa en este incremento).</summary>
    public IReadOnlyList<AccionPropuestaDto> Acciones { get; init; } = [];

    /// <summary>True cuando la consulta necesita que el cliente inicie sesión.</summary>
    public bool RequiereAutenticacion { get; init; }

    /// <summary>Código de error técnico (cuota_agotada, timeout, sin_configuracion…).</summary>
    public string? ErrorCodigo { get; init; }

    /// <summary>Mensaje de error legible para el cliente.</summary>
    public string? ErrorMensaje { get; init; }
}
