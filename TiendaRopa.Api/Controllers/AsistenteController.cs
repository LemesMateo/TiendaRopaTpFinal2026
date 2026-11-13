using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TiendaRopa.Api.Asistente;
using TiendaRopa.Api.Auth;
using TiendaRopa.Api.Services;
using TiendaRopa.Contracts.Asistente;
using TiendaRopa.Contracts.Solicitudes;

namespace TiendaRopa.Api.Controllers;

/// <summary>
/// Asistente conversacional de atención al cliente.
///
/// El endpoint es accesible sin autenticación (para preguntas de catálogo),
/// pero las herramientas de pedidos y datos personales exigen sesión: la
/// identidad se resuelve del JWT, nunca de lo que envíe el cliente.
/// </summary>
[ApiController]
[Route("api/asistente")]
[Tags("Asistente")]
[AllowAnonymous]
public class AsistenteController(
    AsistenteServicio asistente,
    SolicitudService solicitudes,
    IUsuarioActual usuario,
    ILogger<AsistenteController> log) : ControllerBase
{
    /// <summary>Procesa un turno de conversación.</summary>
    [HttpPost("mensaje")]
    [ProducesResponseType<ChatRespuestaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ChatRespuestaDto>> Mensaje(
        [FromBody] ChatSolicitudDto solicitud, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(solicitud.Mensaje))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Mensaje vacío",
                Detail = "Escribí tu consulta antes de enviarla.",
                Status = StatusCodes.Status400BadRequest,
            });
        }

        try
        {
            var respuesta = await asistente.ResponderAsync(solicitud, cancellationToken);

            // El asistente no configurado no es un error del cliente: se
            // responde 503 con un mensaje claro para que la UI lo muestre.
            if (respuesta.ErrorCodigo == "sin_configuracion")
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, respuesta);
            }

            return Ok(respuesta);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Fallo inesperado en el asistente conversacional.");

            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ChatRespuestaDto
            {
                Mensaje = "El asistente no está disponible en este momento. Probá de nuevo en unos minutos.",
                ErrorCodigo = "error_interno",
                ErrorMensaje = "No pudimos completar la consulta.",
            });
        }
    }

    // ------------------------------------------------------------------
    // Propuestas de solicitud (el GEMINI prepara; el CLIENTE confirma)
    // ------------------------------------------------------------------

    /// <summary>Propuestas pendientes del usuario actual.</summary>
    [HttpGet("propuestas")]
    [Authorize]
    [ProducesResponseType<IReadOnlyList<SolicitudPropuestaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SolicitudPropuestaDto>>> GetPropuestas(
        CancellationToken cancellationToken)
        => Ok(await solicitudes.GetMisPropuestasAsync(cancellationToken));

    /// <summary>
    /// Prepara una solicitud para la tienda. Es lo mismo que hace la
    /// herramienta del asistente: crea una PROPUESTA pendiente que el
    /// cliente tiene que confirmar. Todavía no se envía nada a la tienda.
    /// </summary>
    [HttpPost("propuestas")]
    [Authorize]
    [ProducesResponseType<SolicitudPropuestaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SolicitudPropuestaDto>> CrearPropuesta(
        [FromBody] CrearPropuestaRequest request, CancellationToken cancellationToken)
    {
        var idCliente = usuario.IdCliente;
        if (idCliente is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Sin perfil de cliente",
                Detail = "Tu usuario no tiene un perfil de cliente asociado.",
                Status = StatusCodes.Status403Forbidden,
            });
        }

        var (propuesta, error, status) = await solicitudes.PrepararPropuestaAsync(
            idCliente.Value,
            request.Tipo,
            request.IdPedido,
            request.IdProducto,
            request.IdVariante,
            request.Talle,
            request.Color,
            request.Mensaje,
            cancellationToken);

        if (propuesta is null)
        {
            return StatusCode(status, new ProblemDetails
            {
                Title = "No se pudo preparar la solicitud",
                Detail = error,
                Status = status,
            });
        }

        var dto = await solicitudes.ObtenerPropuestaAsync(propuesta.IdPropuesta, cancellationToken);
        return StatusCode(status, dto is null
            ? SolicitudService.MapearPropuesta(propuesta)
            : SolicitudService.MapearPropuesta(dto));
    }

    /// <summary>Detalle de una propuesta propia (con el teléfono enmascarado).</summary>
    [HttpGet("propuestas/{id:int}")]
    [Authorize]
    [ProducesResponseType<SolicitudPropuestaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SolicitudPropuestaDto>> GetPropuesta(
        int id, CancellationToken cancellationToken)
    {
        var propuesta = await solicitudes.ObtenerPropuestaAsync(id, cancellationToken);

        return propuesta is null
            ? NotFound(new ProblemDetails { Title = "Propuesta no encontrada", Status = 404 })
            : Ok(SolicitudService.MapearPropuesta(propuesta));
    }

    /// <summary>
    /// Fija el teléfono de contacto de una propuesta pendiente. Si el cliente
    /// lo pide explícitamente, se actualiza también su perfil.
    /// </summary>
    [HttpPut("propuestas/{id:int}/telefono")]
    [Authorize]
    [ProducesResponseType<SolicitudPropuestaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SolicitudPropuestaDto>> ConfirmarTelefono(
        int id, [FromBody] ConfirmarTelefonoPropuestaRequest request, CancellationToken cancellationToken)
    {
        var (propuesta, error, status) = await solicitudes.ConfirmarTelefonoAsync(
            id, request.Telefono, request.GuardarEnPerfil, cancellationToken);

        if (propuesta is null)
        {
            return StatusCode(status, new ProblemDetails
            {
                Title = "No se pudo confirmar el teléfono",
                Detail = error,
                Status = status,
            });
        }

        return Ok(SolicitudService.MapearPropuesta(propuesta));
    }

    /// <summary>
    /// CONFIRMA la propuesta y crea la solicitud real. Es la única escritura:
    /// el modelo puede preparar la propuesta, pero nunca enviarla.
    /// Idempotente: confirmar dos veces devuelve la misma solicitud.
    ///
    /// Devuelve la **solicitud creada** (con su id), que es lo que el
    /// cliente necesita para seguirla en "Mis solicitudes".
    /// </summary>
    [HttpPost("propuestas/{id:int}/confirmar")]
    [Authorize]
    [ProducesResponseType<SolicitudAtencionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SolicitudAtencionDto>> Confirmar(
        int id, [FromBody] ConfirmarPropuestaRequest request, CancellationToken cancellationToken)
    {
        var (solicitud, error, status) = await solicitudes.ConfirmarPropuestaAsync(
            id, request, cancellationToken);

        if (solicitud is null)
        {
            return StatusCode(status, new ProblemDetails
            {
                Title = "No se pudo enviar la solicitud",
                Detail = error,
                Status = status,
            });
        }

        var dto = await solicitudes.GetSolicitudAsync(solicitud.IdSolicitud, cancellationToken);

        return StatusCode(status, dto ?? new SolicitudAtencionDto
        {
            Id = solicitud.IdSolicitud,
            Tipo = solicitud.Tipo,
            Estado = solicitud.Estado,
            Mensaje = solicitud.Mensaje,
            FechaCreacion = solicitud.FechaCreacion,
            FechaActualizacion = solicitud.FechaActualizacion,
            IdPedido = solicitud.IdPedido,
            IdProducto = solicitud.IdProducto,
            IdVariante = solicitud.IdProductoVariante,
            Talle = solicitud.TalleDescripcion,
            Color = solicitud.ColorDescripcion,
            TelefonoContacto = solicitud.TelefonoContacto,
            TelefonoEstado = solicitud.TelefonoEstado,
        });
    }

    /// <summary>Cancela una propuesta pendiente (no crea nada).</summary>
    [HttpPost("propuestas/{id:int}/cancelar")]
    [Authorize]
    [ProducesResponseType<SolicitudPropuestaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SolicitudPropuestaDto>> Cancelar(
        int id, CancellationToken cancellationToken)
    {
        var (propuesta, error, status) = await solicitudes.CancelarPropuestaAsync(id, cancellationToken);

        if (propuesta is null)
        {
            return StatusCode(status, new ProblemDetails
            {
                Title = "No se pudo cancelar la propuesta",
                Detail = error,
                Status = status,
            });
        }

        return Ok(SolicitudService.MapearPropuesta(propuesta));
    }
}
