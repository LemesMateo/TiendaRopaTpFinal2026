namespace TiendaRopa.Contracts.Solicitudes;

/// <summary>Tipos de notificación interna de la tienda.</summary>
public static class NotificacionTipos
{
    /// <summary>Llegó una solicitud nueva de un cliente.</summary>
    public const string SolicitudNueva = "solicitud_nueva";

    /// <summary>El equipo respondió una solicitud del cliente.</summary>
    public const string SolicitudRespondida = "solicitud_respondida";
}

/// <summary>Notificación interna del personal de tienda.</summary>
public sealed class NotificacionDto
{
    public int Id { get; init; }

    /// <summary>Solicitud relacionada.</summary>
    public int IdSolicitud { get; init; }

    /// <summary>Tipo: "solicitud_nueva" | "solicitud_respondida".</summary>
    public required string Tipo { get; init; }

    /// <summary>Texto breve para la campana.</summary>
    public required string Mensaje { get; init; }

    public DateTime FechaCreacion { get; init; }

    public bool Leida { get; init; }

    /// <summary>Tipo de la solicitud relacionada (para el badge de la campana).</summary>
    public string? SolicitudTipo { get; init; }

    /// <summary>Estado de la solicitud relacionada.</summary>
    public string? SolicitudEstado { get; init; }
}

/// <summary>Notificaciones del usuario actual + contador de pendientes.</summary>
public sealed class NotificacionesResumenDto
{
    public required IReadOnlyList<NotificacionDto> Items { get; init; }

    /// <summary>Cantidad de notificaciones sin leer.</summary>
    public int Pendientes { get; init; }
}
