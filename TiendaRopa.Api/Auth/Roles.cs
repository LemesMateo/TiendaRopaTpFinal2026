namespace TiendaRopa.Api.Auth;

/// <summary>Roles y políticas de autorización de la tienda.</summary>
public static class Roles
{
    public const string Administrador = "Administrador";
    public const string Cliente = "Cliente";

    /// <summary>Política que exige rol Administrador (validada en el JWT).</summary>
    public const string SoloAdministrador = "SoloAdministrador";
}
