using System.ComponentModel.DataAnnotations;

namespace TiendaRopa.Contracts.Admin;

/// <summary>
/// Alta de una nota de estado pública para un pedido.
/// La crea el administrador desde el panel; el cliente la ve en "Mis pedidos"
/// y en el chat.
/// </summary>
public sealed class CrearNotaPedidoRequest
{
    /// <summary>Texto de la actualización que verá el cliente.</summary>
    [Required(ErrorMessage = "Indicá el texto de la actualización.")]
    [MaxLength(500, ErrorMessage = "La actualización es demasiado larga (máximo 500 caracteres).")]
    public required string Nota { get; set; }

    /// <summary>Empresa de transporte o courier (opcional).</summary>
    [MaxLength(100, ErrorMessage = "La empresa es demasiado larga (máximo 100 caracteres).")]
    public string? Empresa { get; set; }

    /// <summary>Código de seguimiento (opcional).</summary>
    [MaxLength(60, ErrorMessage = "El código de seguimiento es demasiado largo (máximo 60 caracteres).")]
    public string? CodigoSeguimiento { get; set; }
}
