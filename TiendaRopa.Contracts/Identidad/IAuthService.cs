namespace TiendaRopa.Contracts.Identidad;

/// <summary>
/// Operaciones de autenticación. La implementación HTTP agrega el JWT
/// automáticamente a través del handler configurado en cada host.
/// </summary>
public interface IAuthService
{
    /// <summary>Registra un cliente y devuelve la sesión iniciada.</summary>
    Task<AuthResponse?> RegistroAsync(RegistroRequest request, CancellationToken cancellationToken = default);

    /// <summary>Devuelve la sesión o null cuando las credenciales son inválidas (401).</summary>
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>Usuario del token vigente; null si no hay sesión.</summary>
    Task<UsuarioActualDto?> GetUsuarioActualAsync(CancellationToken cancellationToken = default);
}
