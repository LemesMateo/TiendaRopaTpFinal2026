using System.ComponentModel.DataAnnotations;

namespace TiendaRopa.Contracts.Identidad;

/// <summary>Alta de cliente. La contraseña jamás viaja en texto plano a storage.</summary>
public sealed class RegistroRequest
{
    [Required(ErrorMessage = "Ingresá tu nombre.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 100 caracteres.")]
    public required string Nombre { get; init; }

    [Required(ErrorMessage = "Ingresá tu apellido.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "El apellido debe tener entre 2 y 100 caracteres.")]
    public required string Apellido { get; init; }

    [Required(ErrorMessage = "Ingresá tu email.")]
    [EmailAddress(ErrorMessage = "Ingresá un email válido.")]
    [StringLength(150)]
    public required string Email { get; init; }

    [Required(ErrorMessage = "Ingresá una contraseña.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")]
    public required string Password { get; init; }

    [Phone(ErrorMessage = "El teléfono no es válido.")]
    [StringLength(50)]
    public string? Telefono { get; init; }

    [StringLength(300)]
    public string? Direccion { get; init; }
}
