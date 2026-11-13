namespace TiendaRopa.Contracts.Identidad;

/// <summary>
/// Permisos del backend (espejo de TiendaRopa.Api.Auth.Permisos). Se declaran
/// acá para que la UI pueda mostrarlos sin depender del proyecto de la API.
/// </summary>
public static class Permisos
{
    public const string VerPedidos = "pedidos.ver";
    public const string GestionarPedidos = "pedidos.gestionar";
    public const string VerSolicitudes = "solicitudes.ver";
    public const string ResponderSolicitudes = "solicitudes.responder";
    public const string VerNotificaciones = "notificaciones.ver";
    public const string GestionarCatalogo = "catalogo.gestionar";
    public const string GestionarUsuarios = "usuarios.gestionar";

    /// <summary>Permisos del rol Administrador (todos).</summary>
    public static readonly IReadOnlyList<string> DeAdministrador =
    [
        VerPedidos, GestionarPedidos, VerSolicitudes, ResponderSolicitudes,
        VerNotificaciones, GestionarCatalogo, GestionarUsuarios,
    ];

    /// <summary>Permisos del rol Vendedor (restringidos: no toca el catálogo).</summary>
    public static readonly IReadOnlyList<string> DeVendedor =
    [
        VerPedidos, GestionarPedidos, VerSolicitudes, ResponderSolicitudes,
        VerNotificaciones,
    ];

    /// <summary>Permisos efectivos de un rol.</summary>
    public static IReadOnlyList<string> De(string? rol) => rol switch
    {
        "Administrador" => DeAdministrador,
        "Vendedor" => DeVendedor,
        _ => [],
    };
}

/// <summary>Usuario autenticado tal como lo devuelve la API.</summary>
public sealed class UsuarioActualDto
{
    public int Id { get; init; }

    public required string Email { get; init; }

    public required string Nombre { get; init; }

    public required string Apellido { get; init; }

    /// <summary>Rol: "Cliente" | "Administrador" | "Vendedor".</summary>
    public required string Rol { get; init; }

    /// <summary>
    /// Permisos efectivos del rol (los mismos que valida el backend).
    /// La UI los usa para mostrar u ocultar acciones; la autorización real
    /// siempre se decide en la API.
    /// </summary>
    public IReadOnlyList<string> Permisos { get; init; } = [];

    /// <summary>Id del Cliente asociado; null para usuarios sin perfil de cliente.</summary>
    public int? IdCliente { get; init; }

    public string? Telefono { get; init; }

    public string? Direccion { get; init; }
}
