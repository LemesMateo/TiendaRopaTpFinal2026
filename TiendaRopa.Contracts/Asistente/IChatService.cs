using TiendaRopa.Contracts.Solicitudes;

namespace TiendaRopa.Contracts.Asistente;

/// <summary>
/// Cliente HTTP del asistente conversacional. La identidad la resuelve el
/// backend a partir del JWT: el frontend nunca envía IdCliente.
/// </summary>
public interface IChatService
{
    /// <summary>Último mensaje de error (401, 429, error de conexión…).</summary>
    string? UltimoError { get; }

    /// <summary>
    /// Envía un turno de conversación. Devuelve null si el backend rechazó la
    /// petición o no hay sesión iniciada cuando la consulta la requiere.
    /// </summary>
    Task<ChatRespuestaDto?> EnviarAsync(
        ChatSolicitudDto solicitud, CancellationToken cancellationToken = default);

    /// <summary>Propuestas de solicitud pendientes del usuario actual.</summary>
    Task<IReadOnlyList<SolicitudPropuestaDto>> GetPropuestasAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirma una propuesta y crea la solicitud real. Es la única vía de
    /// escritura; el modelo nunca la usa. Devuelve la solicitud creada.
    /// </summary>
    Task<SolicitudAtencionDto?> ConfirmarPropuestaAsync(
        int idPropuesta, ConfirmarPropuestaRequest request, CancellationToken cancellationToken = default);

    /// <summary>Cancela una propuesta pendiente.</summary>
    Task<SolicitudPropuestaDto?> CancelarPropuestaAsync(
        int idPropuesta, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fija el teléfono de contacto de una propuesta pendiente. Si
    /// <paramref name="guardarEnPerfil"/> es true, el backend actualiza el
    /// perfil del cliente (consentimiento expreso).
    /// </summary>
    Task<SolicitudPropuestaDto?> ConfirmarTelefonoAsync(
        int idPropuesta, string telefono, bool guardarEnPerfil, CancellationToken cancellationToken = default);
}
