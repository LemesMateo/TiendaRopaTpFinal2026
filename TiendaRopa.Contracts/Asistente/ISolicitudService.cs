using TiendaRopa.Contracts.Solicitudes;

namespace TiendaRopa.Contracts.Asistente;

/// <summary>
/// API de solicitudes y notificaciones para el cliente y el personal de tienda.
/// </summary>
public interface ISolicitudService
{
    /// <summary>Último mensaje de error de negocio.</summary>
    string? UltimoError { get; }

    /// <summary>Las solicitudes del cliente actual (con la respuesta de la tienda).</summary>
    Task<IReadOnlyList<SolicitudAtencionDto>> GetMisSolicitudesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Listado de solicitudes para el personal de tienda.</summary>
    Task<IReadOnlyList<SolicitudAtencionDto>> GetSolicitudesAsync(
        string? estado = null, CancellationToken cancellationToken = default);

    /// <summary>Detalle de una solicitud (personal de tienda).</summary>
    Task<SolicitudAtencionDto?> GetSolicitudAsync(
        int id, CancellationToken cancellationToken = default);

    /// <summary>Responde una solicitud (personal de tienda).</summary>
    Task<SolicitudAtencionDto?> ResponderAsync(
        int id, string respuesta, CancellationToken cancellationToken = default);

    /// <summary>Cambia el estado de una solicitud (personal de tienda).</summary>
    Task<SolicitudAtencionDto?> CambiarEstadoAsync(
        int id, string estado, CancellationToken cancellationToken = default);
}

/// <summary>Notificaciones internas del personal de tienda.</summary>
public interface INotificacionService
{
    /// <summary>Último mensaje de error de negocio.</summary>
    string? UltimoError { get; }

    /// <summary>Notificaciones del usuario actual + contador de pendientes.</summary>
    Task<NotificacionesResumenDto?> GetNotificacionesAsync(
        bool soloPendientes = false, CancellationToken cancellationToken = default);

    /// <summary>Marca una notificación como leída.</summary>
    Task<bool> MarcarLeidaAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Marca todas las notificaciones del usuario como leídas.</summary>
    Task<bool> MarcarTodasLeidasAsync(CancellationToken cancellationToken = default);
}
