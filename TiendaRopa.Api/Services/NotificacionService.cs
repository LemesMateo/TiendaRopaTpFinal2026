using Microsoft.EntityFrameworkCore;
using TiendaRopa.Api.Auth;
using TiendaRopa.Api.Data;
using TiendaRopa.Api.Entities;
using TiendaRopa.Contracts.Solicitudes;

namespace TiendaRopa.Api.Services;

/// <summary>
/// Notificaciones internas del personal de tienda y gestión del ciclo de vida
/// de las solicitudes (responder, cambiar estado, cerrar).
/// </summary>
public sealed class NotificacionService(
    VelvetRoseDbContext db,
    IUsuarioActual usuario,
    ILogger<NotificacionService> log)
{
    /// <summary>Notificaciones del usuario actual (más recientes primero).</summary>
    public async Task<NotificacionesResumenDto> GetNotificacionesAsync(
        bool soloPendientes, CancellationToken cancellationToken)
    {
        var q = db.Notificaciones.AsNoTracking()
            .Where(n => n.IdUsuario == usuario.IdUsuario);

        if (soloPendientes)
        {
            q = q.Where(n => !n.Leida);
        }

        var items = await q
            .OrderByDescending(n => n.FechaCreacion)
            .Select(n => new NotificacionDto
            {
                Id = n.IdNotificacion,
                IdSolicitud = n.IdSolicitud,
                Tipo = n.Tipo,
                Mensaje = n.Mensaje,
                FechaCreacion = n.FechaCreacion,
                Leida = n.Leida,
                SolicitudTipo = n.Solicitud!.Tipo,
                SolicitudEstado = n.Solicitud.Estado,
            })
            .ToListAsync(cancellationToken);

        var pendientes = await db.Notificaciones.AsNoTracking()
            .CountAsync(n => n.IdUsuario == usuario.IdUsuario && !n.Leida, cancellationToken);

        return new NotificacionesResumenDto { Items = items, Pendientes = pendientes };
    }

    /// <summary>Marca una notificación como leída (sólo la del propio usuario).</summary>
    public async Task<bool> MarcarLeidaAsync(int idNotificacion, CancellationToken cancellationToken)
    {
        var notificacion = await db.Notificaciones
            .FirstOrDefaultAsync(n => n.IdNotificacion == idNotificacion, cancellationToken);

        if (notificacion is null || notificacion.IdUsuario != usuario.IdUsuario)
        {
            return false;
        }

        if (!notificacion.Leida)
        {
            notificacion.Leida = true;
            notificacion.FechaLectura = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    /// <summary>Marca todas las notificaciones del usuario como leídas.</summary>
    public async Task<int> MarcarTodasLeidasAsync(CancellationToken cancellationToken)
    {
        var pendientes = await db.Notificaciones
            .Where(n => n.IdUsuario == usuario.IdUsuario && !n.Leida)
            .ToListAsync(cancellationToken);

        if (pendientes.Count == 0)
        {
            return 0;
        }

        var ahora = DateTime.UtcNow;
        foreach (var notificacion in pendientes)
        {
            notificacion.Leida = true;
            notificacion.FechaLectura = ahora;
        }

        await db.SaveChangesAsync(cancellationToken);
        return pendientes.Count;
    }

    /// <summary>
    /// Responde una solicitud. Notifica al cliente (queda registrada su
    /// respuesta) y avisa al resto del personal de tienda.
    /// </summary>
    public async Task<(SolicitudAtencion? Solicitud, string? Error, int StatusCode)> ResponderAsync(
        int idSolicitud, string respuesta, CancellationToken cancellationToken)
    {
        var solicitud = await db.SolicitudesAtencion
            .FirstOrDefaultAsync(s => s.IdSolicitud == idSolicitud, cancellationToken);

        if (solicitud is null)
        {
            return (null, "Solicitud no encontrada.", StatusCodes.Status404NotFound);
        }

        var texto = (respuesta ?? string.Empty).Trim();
        if (texto.Length == 0 || texto.Length > 1000)
        {
            return (null, "La respuesta no es válida.", StatusCodes.Status400BadRequest);
        }

        solicitud.Respuesta = texto;
        solicitud.Estado = SolicitudEstados.Respondida;
        solicitud.IdUsuarioResuelve = usuario.IdUsuario;
        solicitud.FechaResolucion = DateTime.UtcNow;
        solicitud.FechaActualizacion = DateTime.UtcNow;

        // El cliente ve la respuesta en "Mis solicitudes" (no hace falta
        // notificarlo: la consulta al panel ya la muestra).
        // Se avisa al resto del personal para que no quede sin resolver.
        var destino = await db.Usuarios.AsNoTracking()
            .Where(u => u.Activo
                && u.Rol!.Activo
                && u.Rol.Nombre == Roles.Vendedor)
            .Select(u => u.IdUsuario)
            .ToListAsync(cancellationToken);

        foreach (var idUsuario in destino)
        {
            if (idUsuario == usuario.IdUsuario)
            {
                continue;
            }

            db.Notificaciones.Add(new Notificacion
            {
                IdUsuario = idUsuario,
                IdSolicitud = solicitud.IdSolicitud,
                Tipo = NotificacionTipos.SolicitudRespondida,
                Mensaje = $"La solicitud #{solicitud.IdSolicitud} recibió respuesta",
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        log.LogInformation(
            "Solicitud {Id} respondida por el usuario {Usuario}", idSolicitud, usuario.IdUsuario);

        return (solicitud, null, StatusCodes.Status200OK);
    }

    /// <summary>Cambia el estado de una solicitud (validando la transición).</summary>
    public async Task<(SolicitudAtencion? Solicitud, string? Error, int StatusCode)> CambiarEstadoAsync(
        int idSolicitud, string estado, CancellationToken cancellationToken)
    {
        var solicitud = await db.SolicitudesAtencion
            .FirstOrDefaultAsync(s => s.IdSolicitud == idSolicitud, cancellationToken);

        if (solicitud is null)
        {
            return (null, "Solicitud no encontrada.", StatusCodes.Status404NotFound);
        }

        var destino = (estado ?? string.Empty).Trim();
        if (!SolicitudEstados.Todos.Contains(destino))
        {
            return (null, "Estado no válido.", StatusCodes.Status400BadRequest);
        }

        if (!EsTransicionValida(solicitud.Estado, destino))
        {
            return (null,
                $"No se puede pasar de \"{solicitud.Estado}\" a \"{destino}\".",
                StatusCodes.Status409Conflict);
        }

        solicitud.Estado = destino;
        solicitud.IdUsuarioResuelve = usuario.IdUsuario;
        solicitud.FechaActualizacion = DateTime.UtcNow;

        if (destino == SolicitudEstados.Cerrada)
        {
            solicitud.FechaResolucion = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (solicitud, null, StatusCodes.Status200OK);
    }

    /// <summary>Transiciones válidas del ciclo de vida de una solicitud.</summary>
    public static bool EsTransicionValida(string actual, string destino) => (actual, destino) switch
    {
        (SolicitudEstados.Abierta, SolicitudEstados.EnRevision) => true,
        (SolicitudEstados.Abierta, SolicitudEstados.Respondida) => true,
        (SolicitudEstados.Abierta, SolicitudEstados.Cerrada) => true,
        (SolicitudEstados.EnRevision, SolicitudEstados.Respondida) => true,
        (SolicitudEstados.EnRevision, SolicitudEstados.Cerrada) => true,
        (SolicitudEstados.Respondida, SolicitudEstados.Cerrada) => true,
        (SolicitudEstados.EnRevision, SolicitudEstados.Abierta) => true,
        (SolicitudEstados.Respondida, SolicitudEstados.EnRevision) => true,
        _ => actual == destino,
    };
}
