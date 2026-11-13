using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TiendaRopa.Api.Auth;
using TiendaRopa.Api.Services;
using TiendaRopa.Contracts.Admin;
using TiendaRopa.Contracts.Common;
using TiendaRopa.Contracts.Pedidos;

namespace TiendaRopa.Api.Controllers;

/// <summary>
/// Gestión de PEDIDOS para el personal de tienda.
///
/// Las URLs son las mismas que antes (/api/admin/pedidos…), pero la
/// autorización cambia a permisos: el rol Vendedor puede consultar y
/// gestionar pedidos (y notas de envío) sin tocar el catálogo, que sigue
/// siendo exclusivo del Administrador.
/// </summary>
[ApiController]
[Route("api/admin")]
[Tags("Pedidos (tienda)")]
[Authorize(Policy = Roles.PersonalTienda)]
public class TiendaController(AdminService admin, ILogger<TiendaController> log) : ControllerBase
{
    private const int MaxPageSize = 50;

    // ---------- Pedidos ----------

    /// <summary>Listado de pedidos (del personal de tienda).</summary>
    [HttpGet("pedidos")]
    [Authorize(Policy = Politicas.VerPedidos)]
    public async Task<ActionResult<PagedResult<PedidoAdminDto>>> GetPedidos(
        [FromQuery] string? estado,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Parámetros inválidos",
                Detail = $"page ≥ 1 y pageSize entre 1 y {MaxPageSize}.",
                Status = 400,
            });
        }

        var (items, total) = await admin.GetPedidosAsync(estado, page, pageSize, cancellationToken);
        return Ok(new PagedResult<PedidoAdminDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        });
    }

    /// <summary>Detalle de un pedido.</summary>
    [HttpGet("pedidos/{id:int}")]
    [Authorize(Policy = Politicas.VerPedidos)]
    public async Task<ActionResult<PedidoDto>> GetPedido(int id, CancellationToken cancellationToken)
    {
        var resultado = await admin.GetPedidoAsync(id, cancellationToken);
        return resultado is null
            ? NotFound(new ProblemDetails { Title = "Pedido no encontrado", Status = 404 })
            : Ok(resultado);
    }

    /// <summary>Cambio de estado aplicando las transiciones válidas.</summary>
    [HttpPut("pedidos/{id:int}/estado")]
    [Authorize(Policy = Politicas.GestionarPedidos)]
    public async Task<ActionResult<PedidoDto>> CambiarEstado(
        int id, CambiarEstadoPedidoRequest request, CancellationToken cancellationToken)
    {
        var (valor, error, status) = await admin.CambiarEstadoAsync(id, request.Estado, cancellationToken);
        if (valor is null)
        {
            return StatusCode(status, new ProblemDetails
            {
                Title = "Transición no permitida",
                Detail = error,
                Status = status,
            });
        }

        return Ok(valor);
    }

    // ---------- Notas públicas del pedido ----------

    /// <summary>Historial de actualizaciones públicas de un pedido.</summary>
    [HttpGet("pedidos/{id:int}/notas")]
    [Authorize(Policy = Politicas.GestionarPedidos)]
    [ProducesResponseType<IReadOnlyList<PedidoNotaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PedidoNotaDto>>> GetNotasPedido(
        int id, CancellationToken cancellationToken)
        => Ok(await admin.GetNotasPedidoAsync(id, cancellationToken));

    /// <summary>
    /// Crea una actualización pública del pedido. El cliente la ve en
    /// "Mis pedidos" y el chatbot la usa para responder consultas de envío.
    /// </summary>
    [HttpPost("pedidos/{id:int}/notas")]
    [Authorize(Policy = Politicas.GestionarPedidos)]
    [ProducesResponseType<PedidoNotaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PedidoNotaDto>> CrearNotaPedido(
        int id, [FromBody] CrearNotaPedidoRequest request, CancellationToken cancellationToken)
    {
        var (valor, error, status) = await admin.CrearNotaPedidoAsync(id, request, cancellationToken);

        if (valor is null)
        {
            return StatusCode(status, new ProblemDetails
            {
                Title = "No se pudo crear la actualización",
                Detail = error,
                Status = status,
            });
        }

        log.LogInformation(
            "Nota pública creada en el pedido {Pedido} por el personal de tienda", id);

        return CreatedAtAction(nameof(GetNotasPedido), new { id }, valor);
    }
}
