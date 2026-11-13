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

    /// <summary>True si el rol es del personal de tienda (Administrador o Vendedor).</summary>
    bool EsPersonalTienda { get; }

    /// <summary>True si el rol del usuario tiene el permiso indicado.</summary>
    bool TienePermiso(string permiso);
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

    public bool EsPersonalTienda =>
        EsAdministrador || string.Equals(Rol, Roles.Vendedor, StringComparison.Ordinal);

    public bool TienePermiso(string permiso) => PermisosPorRol.Tiene(Rol, permiso);
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

/// <summary>
/// Exige rol del personal de tienda (Administrador o Vendedor).
/// </summary>
public sealed class PersonalTiendaRequirement : IAuthorizationRequirement;

public sealed class PersonalTiendaHandler : AuthorizationHandler<PersonalTiendaRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PersonalTiendaRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && PermisosPorRol.De(context.User.FindFirst(TokenService.RolClaim)?.Value)
                .Any(p => p is Permisos.VerPedidos or Permisos.VerSolicitudes))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Exige un permiso concreto (ver <see cref="Permisos"/>). El permiso sale del
/// rol del JWT; el cliente nunca decide sus permisos.
/// </summary>
public sealed class PermisoRequirement : IAuthorizationRequirement
{
    public required string Permiso { get; init; }
}

public sealed class PermisoHandler : AuthorizationHandler<PermisoRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermisoRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && PermisosPorRol.Tiene(
                context.User.FindFirst(TokenService.RolClaim)?.Value,
                requirement.Permiso))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
