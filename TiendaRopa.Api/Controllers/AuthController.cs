using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TiendaRopa.Api.Services;
using TiendaRopa.Contracts.Identidad;

namespace TiendaRopa.Api.Controllers;

/// <summary>Registro, login y usuario actual (JWT).</summary>
[ApiController]
[Route("api/auth")]
[Tags("Autenticación")]
public class AuthController(AuthService auth) : ControllerBase
{
    /// <summary>Alta de cliente con rol Cliente. 409 si el email ya existe.</summary>
    [HttpPost("registro")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponse>> Registro(
        RegistroRequest request, CancellationToken cancellationToken)
    {
        var (resultado, error, status) = await auth.RegistroAsync(request, cancellationToken);

        if (resultado is null)
        {
            return StatusCode(status, new ProblemDetails
            {
                Title = "No se pudo crear la cuenta",
                Detail = error,
                Status = status,
            });
        }

        return CreatedAtAction(nameof(UsuarioActual), resultado);
    }

    /// <summary>Devuelve un JWT. 401 cuando las credenciales no coinciden.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request, CancellationToken cancellationToken)
    {
        var (resultado, error, status) = await auth.LoginAsync(request, cancellationToken);

        if (resultado is null)
        {
            return StatusCode(status, new ProblemDetails
            {
                Title = "No se pudo iniciar sesión",
                Detail = error,
                Status = status,
            });
        }

        return Ok(resultado);
    }

    /// <summary>Usuario del token vigente (requiere Authorization: Bearer …).</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UsuarioActualDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UsuarioActualDto>> UsuarioActual(CancellationToken cancellationToken)
    {
        var usuario = await auth.GetUsuarioActualAsync(cancellationToken);
        return usuario is null ? NotFound() : Ok(usuario);
    }
}
