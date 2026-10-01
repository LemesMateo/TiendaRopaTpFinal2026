using Microsoft.AspNetCore.Authorization;

namespace TiendaRopa.Api.Auth;

/// <summary>
/// Resuelve el usuario autenticado a partir de los claims del JWT.
/// La autorización NUNCA depende de un IdCliente enviado por el frontend.
/// </summary>
public interface IUsuarioActual
{
    int IdUsuario { get; }

    string? Email { get; }

    string? Rol { get; }

    /// <summary>Id del cliente asociado; null si el usuario no tiene perfil cliente.</summary>
    int? IdCliente { get; }

    bool EstaAutenticado { get; }

    bool EsAdministrador { get; }
}

public sealed class UsuarioActual(IHttpContextAccessor http) : IUsuarioActual
{
    public int IdUsuario =>
        int.TryParse(http.HttpContext?.User.FindFirst("idUsuario")?.Value, out var id) ? id : 0;

    public string? Email => http.HttpContext?.User.FindFirst("email")?.Value
        ?? http.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

    public string? Rol => http.HttpContext?.User.FindFirst(TokenService.RolClaim)?.Value;

    public int? IdCliente =>
        int.TryParse(http.HttpContext?.User.FindFirst(TokenService.IdClienteClaim)?.Value, out var id)
            ? id
            : null;

    public bool EstaAutenticado => http.HttpContext?.User.Identity?.IsAuthenticated == true;

    public bool EsAdministrador => string.Equals(Rol, Roles.Administrador, StringComparison.Ordinal);
}

/// <summary>Exige que el usuario esté autenticado con el rol Administrador.</summary>
public sealed class AdministradorRequirement : IAuthorizationRequirement;

public sealed class AdministradorHandler : AuthorizationHandler<AdministradorRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdministradorRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && string.Equals(
                context.User.FindFirst(TokenService.RolClaim)?.Value,
                Roles.Administrador,
                StringComparison.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
