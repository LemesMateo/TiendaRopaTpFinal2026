namespace TiendaRopa.Api.Auth;

/// <summary>
/// Permisos del backend. Cada acción sensible declara el permiso que exige;
/// la autorización SIEMPRE se decide en la API (nunca en el frontend).
/// </summary>
public static class Permisos
{
    /// <summary>Ver el listado y el detalle de los pedidos de la tienda.</summary>
    public const string VerPedidos = "pedidos.ver";

    /// <summary>Cambiar el estado de un pedido y publicar actualizaciones de envío.</summary>
    public const string GestionarPedidos = "pedidos.gestionar";

    /// <summary>Ver el listado y el detalle de las solicitudes de atención.</summary>
    public const string VerSolicitudes = "solicitudes.ver";

    /// <summary>Responder, cambiar el estado y cerrar una solicitud.</summary>
    public const string ResponderSolicitudes = "solicitudes.responder";

    /// <summary>Recibir las notificaciones internas de la tienda.</summary>
    public const string VerNotificaciones = "notificaciones.ver";

    /// <summary>Alta y edición del catálogo (productos, variantes, categorías…).</summary>
    public const string GestionarCatalogo = "catalogo.gestionar";

    /// <summary>Administración de usuarios y roles.</summary>
    public const string GestionarUsuarios = "usuarios.gestionar";
}

/// <summary>
/// Nombres de las políticas de autorización por permiso. Son constantes para
/// poder usarlas en los atributos <c>[Authorize(Policy = ...)]</c>.
/// </summary>
public static class Politicas
{
    public const string VerPedidos = "Permiso:pedidos.ver";
    public const string GestionarPedidos = "Permiso:pedidos.gestionar";
    public const string VerSolicitudes = "Permiso:solicitudes.ver";
    public const string ResponderSolicitudes = "Permiso:solicitudes.responder";
    public const string VerNotificaciones = "Permiso:notificaciones.ver";
    public const string GestionarCatalogo = "Permiso:catalogo.gestionar";
    public const string GestionarUsuarios = "Permiso:usuarios.gestionar";

    /// <summary>Todas las políticas por permiso (nombre → permiso).</summary>
    public static readonly IReadOnlyDictionary<string, string> PorPermiso =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [VerPedidos] = Permisos.VerPedidos,
            [GestionarPedidos] = Permisos.GestionarPedidos,
            [VerSolicitudes] = Permisos.VerSolicitudes,
            [ResponderSolicitudes] = Permisos.ResponderSolicitudes,
            [VerNotificaciones] = Permisos.VerNotificaciones,
            [GestionarCatalogo] = Permisos.GestionarCatalogo,
            [GestionarUsuarios] = Permisos.GestionarUsuarios,
        };
}

/// <summary>
/// Permisos asignados a cada rol. El Vendedor tiene un conjunto RESTRINGIDO:
/// puede consultar y gestionar pedidos y solicitudes, pero NO toca el catálogo
/// ni los usuarios (eso sigue siendo exclusivo del Administrador).
/// </summary>
public static class PermisosPorRol
{
    private static readonly string[] PermisosAdministrador =
    [
        Permisos.VerPedidos,
        Permisos.GestionarPedidos,
        Permisos.VerSolicitudes,
        Permisos.ResponderSolicitudes,
        Permisos.VerNotificaciones,
        Permisos.GestionarCatalogo,
        Permisos.GestionarUsuarios,
    ];

    private static readonly string[] PermisosVendedor =
    [
        Permisos.VerPedidos,
        Permisos.GestionarPedidos,
        Permisos.VerSolicitudes,
        Permisos.ResponderSolicitudes,
        Permisos.VerNotificaciones,
    ];

    private static readonly string[] PermisosCliente = [];

    /// <summary>Permisos efectivos de un rol.</summary>
    public static IReadOnlyList<string> De(string? rol) => rol switch
    {
        Roles.Administrador => PermisosAdministrador,
        Roles.Vendedor => PermisosVendedor,
        _ => PermisosCliente,
    };

    /// <summary>True si el rol tiene el permiso indicado.</summary>
    public static bool Tiene(string? rol, string permiso)
        => De(rol).Contains(permiso, StringComparer.Ordinal);
}
