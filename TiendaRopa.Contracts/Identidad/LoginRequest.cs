using System.ComponentModel.DataAnnotations;

namespace TiendaRopa.Contracts.Identidad;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Ingresá tu email.")]
    [EmailAddress(ErrorMessage = "Ingresá un email válido.")]
    public required string Email { get; init; }

    [Required(ErrorMessage = "Ingresá tu contraseña.")]
    public required string Password { get; init; }
}
