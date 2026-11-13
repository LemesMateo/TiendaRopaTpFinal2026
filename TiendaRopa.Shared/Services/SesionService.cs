using System.Text.Json;
using TiendaRopa.Contracts.Identidad;

namespace TiendaRopa.Shared.Services;

/// <summary>
/// Estado de sesión de la tienda: JWT + usuario autenticado.
/// El token se guarda en el almacenamiento local del cliente (localStorage)
/// y viaja en las llamadas a la API mediante <see cref="ApiHttp"/>.
/// </summary>
public sealed class SesionService(IPreferenciasLocal almacenamiento)
{
    private const string ClaveToken = "velvet.token";
    private const string ClaveUsuario = "velvet.usuario";

    /// <summary>Se dispara cuando cambia la sesión (login/logout).</summary>
    public event Action? Cambio;

    public string? Token { get; private set; }

    public UsuarioActualDto? Usuario { get; private set; }

    public bool Iniciada => Token is not null && Usuario is not null;

    public bool EsAdministrador => string.Equals(Usuario?.Rol, "Administrador", StringComparison.Ordinal);

    /// <summary>True si el usuario es del personal de tienda (Administrador o Vendedor).</summary>
    public bool EsPersonalTienda =>
        EsAdministrador || string.Equals(Usuario?.Rol, "Vendedor", StringComparison.Ordinal);

    /// <summary>
    /// True si el rol del usuario tiene el permiso indicado. La autorización
    /// real la decide el backend; esto sólo sirve para mostrar u ocultar UI.
    /// </summary>
    public bool TienePermiso(string permiso)
        => Usuario?.Permisos.Contains(permiso, StringComparer.Ordinal) == true;

    /// <summary>Carga la sesión persistida (llamar desde OnAfterRenderAsync: necesita JS).</summary>
    public async Task CargarAsync()
    {
        if (Iniciada)
        {
            return;
        }

        var token = await almacenamiento.ObtenerAsync(ClaveToken);
        var json = await almacenamiento.ObtenerAsync(ClaveUsuario);

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            Token = token;
            Usuario = JsonSerializer.Deserialize<UsuarioActualDto>(json, JsonOptions);
            Cambio?.Invoke();
        }
        catch (JsonException)
        {
            await CerrarSesionAsync();
        }
    }

    public async Task EstablecerAsync(AuthResponse sesion)
    {
        Token = sesion.Token;
        Usuario = sesion.Usuario;
        await almacenamiento.GuardarAsync(ClaveToken, sesion.Token);
        await almacenamiento.GuardarAsync(
            ClaveUsuario, JsonSerializer.Serialize(sesion.Usuario, JsonOptions));
        Cambio?.Invoke();
    }

    public async Task CerrarSesionAsync()
    {
        Token = null;
        Usuario = null;
        await almacenamiento.EliminarAsync(ClaveToken);
        await almacenamiento.EliminarAsync(ClaveUsuario);
        Cambio?.Invoke();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
