namespace TiendaRopa.Api.Auth;

/// <summary>Roles y políticas de autorización de la tienda.</summary>
public static class Roles
{
    public const string Administrador = "Administrador";

    /// <summary>
    /// Personal de tienda con permisos restringidos: consulta y gestiona pedidos
    /// y solicitudes, pero NO el catálogo ni los usuarios.
    /// </summary>
    public const string Vendedor = "Vendedor";

    public const string Cliente = "Cliente";

    /// <summary>Política que exige rol Administrador (validada en el JWT).</summary>
    public const string SoloAdministrador = "SoloAdministrador";

    /// <summary>
    /// Política que exige rol del personal de tienda (Administrador o Vendedor).
    /// </summary>
    public const string PersonalTienda = "PersonalTienda";

    /// <summary>Prefijo de las políticas por permiso (ej. "Permiso:pedidos.ver").</summary>
    public const string PrefijoPermiso = "Permiso:";

    /// <summary>Nombre de la política que exige un permiso concreto.</summary>
    public static string Politica(string permiso) => $"{PrefijoPermiso}{permiso}";
}
