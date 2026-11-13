using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TiendaRopa.Api.Auth;
using TiendaRopa.Api.Services;
using TiendaRopa.Contracts.Solicitudes;

namespace TiendaRopa.Api.Controllers;

/// <summary>
/// Notificaciones internas del personal de tienda (campana del panel).
/// Persisten en SQL Server: el contador se recalcula al consultar.
/// </summary>
[ApiController]
[Tags("Notificaciones")]
public class NotificacionesController(
    NotificacionService notificaciones) : ControllerBase
{
    /// <summary>Notificaciones del usuario actual + contador de pendientes.</summary>
    [HttpGet("api/notificaciones")]
    [Authorize(Policy = Politicas.VerNotificaciones)]
    [ProducesResponseType<NotificacionesResumenDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificacionesResumenDto>> GetNotificaciones(
        [FromQuery] bool soloPendientes = false, CancellationToken cancellationToken = default)
        => Ok(await notificaciones.GetNotificacionesAsync(soloPendientes, cancellationToken));

    /// <summary>Sólo el contador de pendientes (para la campana).</summary>
    [HttpGet("api/notificaciones/pendientes")]
    [Authorize(Policy = Politicas.VerNotificaciones)]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> GetPendientes(CancellationToken cancellationToken)
    {
        var resumen = await notificaciones.GetNotificacionesAsync(true, cancellationToken);
        return Ok(resumen.Pendientes);
    }

    /// <summary>Marca una notificación como leída.</summary>
    [HttpPost("api/notificaciones/{id:int}/leer")]
    [Authorize(Policy = Politicas.VerNotificaciones)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarcarLeida(int id, CancellationToken cancellationToken)
        => await notificaciones.MarcarLeidaAsync(id, cancellationToken)
            ? NoContent()
            : NotFound(new ProblemDetails { Title = "Notificación no encontrada", Status = 404 });

    /// <summary>Marca todas las notificaciones del usuario como leídas.</summary>
    [HttpPost("api/notificaciones/leer-todas")]
    [Authorize(Policy = Politicas.VerNotificaciones)]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> MarcarTodasLeidas(CancellationToken cancellationToken)
        => Ok(await notificaciones.MarcarTodasLeidasAsync(cancellationToken));
}
