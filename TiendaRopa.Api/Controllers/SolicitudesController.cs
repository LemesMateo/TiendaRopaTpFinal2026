using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TiendaRopa.Api.Auth;
using TiendaRopa.Api.Services;
using TiendaRopa.Contracts.Solicitudes;

namespace TiendaRopa.Api.Controllers;

/// <summary>
/// Solicitudes de atención del cliente.
///
/// Lectura y gestión: personal de tienda (Administrador o Vendedor) con el
/// permiso correspondiente. El cliente sólo ve SUS solicitudes.
/// </summary>
[ApiController]
[Tags("Solicitudes")]
public class SolicitudesController(
    SolicitudService solicitudes,
    NotificacionService notificaciones) : ControllerBase
{
    /// <summary>Las solicitudes del cliente actual (con la respuesta de la tienda).</summary>
    [HttpGet("api/mis-solicitudes")]
    [Authorize]
    [ProducesResponseType<IReadOnlyList<SolicitudAtencionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SolicitudAtencionDto>>> GetMisSolicitudes(
        CancellationToken cancellationToken)
        => Ok(await solicitudes.GetMisSolicitudesAsync(cancellationToken));

    /// <summary>Listado de solicitudes para el personal de tienda.</summary>
    [HttpGet("api/solicitudes")]
    [Authorize(Policy = Politicas.VerSolicitudes)]
    [ProducesResponseType<IReadOnlyList<SolicitudAtencionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SolicitudAtencionDto>>> GetSolicitudes(
        [FromQuery] string? estado, CancellationToken cancellationToken)
        => Ok(await solicitudes.GetSolicitudesAsync(estado, cancellationToken));

    /// <summary>Detalle de una solicitud (personal de tienda).</summary>
    [HttpGet("api/solicitudes/{id:int}")]
    [Authorize(Policy = Politicas.VerSolicitudes)]
    [ProducesResponseType<SolicitudAtencionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SolicitudAtencionDto>> GetSolicitud(
        int id, CancellationToken cancellationToken)
    {
        var solicitud = await solicitudes.GetSolicitudAsync(id, cancellationToken);

        return solicitud is null
            ? NotFound(new ProblemDetails { Title = "Solicitud no encontrada", Status = 404 })
            : Ok(solicitud);
    }

    /// <summary>Responde una solicitud. El cliente ve la respuesta en "Mis solicitudes".</summary>
    [HttpPost("api/solicitudes/{id:int}/responder")]
    [Authorize(Policy = Politicas.ResponderSolicitudes)]
    [ProducesResponseType<SolicitudAtencionDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SolicitudAtencionDto>> Responder(
        int id, [FromBody] ResponderSolicitudRequest request, CancellationToken cancellationToken)
    {
        var (solicitud, error, status) = await notificaciones.ResponderAsync(
            id, request.Respuesta, cancellationToken);

        if (solicitud is null)
        {
            return StatusCode(status, new ProblemDetails
            {
                Title = "No se pudo responder",
                Detail = error,
                Status = status,
            });
        }

        return Ok(await solicitudes.GetSolicitudAsync(solicitud.IdSolicitud, cancellationToken));
    }

    /// <summary>Cambia el estado de la solicitud (Abierta / EnRevision / Respondida / Cerrada).</summary>
    [HttpPost("api/solicitudes/{id:int}/estado")]
    [Authorize(Policy = Politicas.ResponderSolicitudes)]
    [ProducesResponseType<SolicitudAtencionDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SolicitudAtencionDto>> CambiarEstado(
        int id, [FromBody] CambiarEstadoSolicitudRequest request, CancellationToken cancellationToken)
    {
        var (solicitud, error, status) = await notificaciones.CambiarEstadoAsync(
            id, request.Estado, cancellationToken);

        if (solicitud is null)
        {
            return StatusCode(status, new ProblemDetails
            {
                Title = "No se pudo cambiar el estado",
                Detail = error,
                Status = status,
            });
        }

        return Ok(await solicitudes.GetSolicitudAsync(solicitud.IdSolicitud, cancellationToken));
    }
}
