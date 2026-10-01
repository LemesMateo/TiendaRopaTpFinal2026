namespace TiendaRopa.Contracts.Identidad;

/// <summary>Respuesta de registro/login: JWT + usuario.</summary>
public sealed class AuthResponse
{
    public required string Token { get; init; }

    public DateTime ExpiresAtUtc { get; init; }

    public required UsuarioActualDto Usuario { get; init; }
}
