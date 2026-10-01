using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TiendaRopa.Api.Auth;
using TiendaRopa.Api.Services;
using TiendaRopa.Contracts.Pedidos;

namespace TiendaRopa.Api.Controllers;

/// <summary>
/// Pedidos del cliente. El usuario se resuelve SIEMPRE desde el JWT
/// (nunca desde un IdCliente enviado por el frontend).
/// </summary>
[ApiController]
[Route("api/pedidos")]
[Tags("Pedidos")]
[Authorize]
public class PedidosController(PedidoService pedidos, IUsuarioActual usuario, ILogger<PedidosController> log)
    : ControllerBase
{
    /// <summary>Confirma el pedido y descuenta stock (transacción única). 409 si falta stock.</summary>
    [HttpPost]
    [ProducesResponseType<PedidoDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PedidoDto>> Crear(
        CrearPedidoRequest request, CancellationToken cancellationToken)
    {
        var idCliente = usuario.IdCliente;
        if (idCliente is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Sin perfil de cliente",
                Detail = "El usuario autenticado no tiene un cliente asociado.",
                Status = StatusCodes.Status403Forbidden,
            });
        }

        var resultado = await pedidos.CrearAsync(idCliente.Value, request, cancellationToken);
        return Presentar(resultado, creado: true);
    }

    /// <summary>Los pedidos propios, más recientes primero.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PedidoDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PedidoDto>>> GetMisPedidos(
        CancellationToken cancellationToken)
    {
        if (usuario.IdCliente is not { } idCliente)
        {
            return Ok(Array.Empty<PedidoDto>());
        }

        var pedidosPropios = await pedidos.GetDeClienteAsync(idCliente, cancellationToken);
        return Ok(pedidosPropios);
    }

    /// <summary>Detalle de un pedido propio (404 si es ajeno o no existe).</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<PedidoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PedidoDto>> GetPedido(int id, CancellationToken cancellationToken)
    {
        var pedido = await pedidos.GetAsync(id, cancellationToken);
        var esPropio = pedido is not null
            && usuario.IdCliente is { } idCliente
            && await pedidos.PerteneceAAsync(id, idCliente, cancellationToken);

        if (pedido is null || (!usuario.EsAdministrador && !esPropio))
        {
            return NotFound(new ProblemDetails
            {
                Title = "Pedido no encontrado",
                Detail = $"No existe un pedido propio con id {id}.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        return Ok(pedido);
    }

    /// <summary>Cancela un pedido propio y restituye stock en la misma transacción.</summary>
    [HttpPost("{id:int}/cancelar")]
    [ProducesResponseType<PedidoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PedidoDto>> Cancelar(int id, CancellationToken cancellationToken)
    {
        var resultado = await pedidos.CancelarAsync(
            id, usuario.IdCliente ?? 0, usuario.EsAdministrador, cancellationToken);

        log.LogInformation("Cancelación de pedido {Pedido} por usuario {Usuario}: {Estado}",
            id, usuario.IdUsuario, resultado.StatusCode);

        return Presentar(resultado);
    }

    private ActionResult<PedidoDto> Presentar(Resultado<PedidoDto> resultado, bool creado = false)
    {
        if (resultado.Valor is not null)
        {
            return creado
                ? CreatedAtAction(nameof(GetPedido), new { id = resultado.Valor.Id }, resultado.Valor)
                : Ok(resultado.Valor);
        }

        return StatusCode(resultado.StatusCode, new ProblemDetails
        {
            Title = resultado.StatusCode == StatusCodes.Status409Conflict
                ? "Operación rechazada"
                : "No se pudo procesar la operación",
            Detail = resultado.Error,
            Status = resultado.StatusCode,
        });
    }
}
