using System.Net.Http.Json;
using System.Text.Json;
using TiendaRopa.Contracts.Identidad;

namespace TiendaRopa.Shared.Services;

/// <summary>Cliente HTTP de autenticación (registro/login/me) con JWT.</summary>
public sealed class HttpAuthService(HttpClient http, SesionService sesion) : IAuthService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Último mensaje de error de negocio (409, 401, validaciones…).</summary>
    public string? UltimoError { get; private set; }

    public async Task<AuthResponse?> RegistroAsync(
        RegistroRequest request, CancellationToken cancellationToken = default)
    {
        using var requestHttp = ApiHttp.Preparar(HttpMethod.Post, "api/auth/registro", sesion, request);
        using var respuesta = await http.SendAsync(requestHttp, cancellationToken);
        return await GuardarSesionAsync(respuesta, cancellationToken);
    }

    public async Task<AuthResponse?> LoginAsync(
        LoginRequest request, CancellationToken cancellationToken = default)
    {
        using var requestHttp = ApiHttp.Preparar(HttpMethod.Post, "api/auth/login", sesion, request);
        using var respuesta = await http.SendAsync(requestHttp, cancellationToken);
        return await GuardarSesionAsync(respuesta, cancellationToken);
    }

    public async Task<UsuarioActualDto?> GetUsuarioActualAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sesion.Token))
        {
            return null;
        }

        using var requestHttp = ApiHttp.Preparar(HttpMethod.Get, "api/auth/me", sesion);
        using var respuesta = await http.SendAsync(requestHttp, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            return null;
        }

        return await respuesta.Content.ReadFromJsonAsync<UsuarioActualDto>(Json, cancellationToken);
    }

    private async Task<AuthResponse?> GuardarSesionAsync(
        HttpResponseMessage respuesta, CancellationToken cancellationToken)
    {
        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        var auth = await respuesta.Content.ReadFromJsonAsync<AuthResponse>(Json, cancellationToken);

        if (auth is not null)
        {
            await sesion.EstablecerAsync(auth);
        }

        UltimoError = null;
        return auth;
    }
}
