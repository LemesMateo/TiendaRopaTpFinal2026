using System.Net.Http.Json;
using System.Text.Json;
using TiendaRopa.Contracts.Asistente;
using TiendaRopa.Contracts.Solicitudes;

namespace TiendaRopa.Shared.Services;

/// <summary>
/// Cliente HTTP de las solicitudes de atención.
/// El JWT se agrega por request; el backend decide si el usuario puede ver
/// o gestionar cada solicitud.
/// </summary>
public sealed class HttpSolicitudService(HttpClient http, SesionService sesion) : ISolicitudService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Último mensaje de error de negocio.</summary>
    public string? UltimoError { get; private set; }

    public async Task<IReadOnlyList<SolicitudAtencionDto>> GetMisSolicitudesAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = ApiHttp.Preparar(HttpMethod.Get, "api/mis-solicitudes", sesion);
        using var respuesta = await http.SendAsync(request, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return [];
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<List<SolicitudAtencionDto>>(Json, cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<SolicitudAtencionDto>> GetSolicitudesAsync(
        string? estado = null, CancellationToken cancellationToken = default)
    {
        var url = "api/solicitudes";
        if (!string.IsNullOrWhiteSpace(estado))
        {
            url += $"?estado={Uri.EscapeDataString(estado)}";
        }

        using var request = ApiHttp.Preparar(HttpMethod.Get, url, sesion);
        using var respuesta = await http.SendAsync(request, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return [];
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<List<SolicitudAtencionDto>>(Json, cancellationToken) ?? [];
    }

    public async Task<SolicitudAtencionDto?> GetSolicitudAsync(
        int id, CancellationToken cancellationToken = default)
    {
        using var request = ApiHttp.Preparar(HttpMethod.Get, $"api/solicitudes/{id}", sesion);
        using var respuesta = await http.SendAsync(request, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<SolicitudAtencionDto>(Json, cancellationToken);
    }

    public async Task<SolicitudAtencionDto?> ResponderAsync(
        int id, string respuesta, CancellationToken cancellationToken = default)
    {
        var cuerpo = new ResponderSolicitudRequest { Respuesta = respuesta };

        using var request = ApiHttp.Preparar(
            HttpMethod.Post, $"api/solicitudes/{id}/responder", sesion, cuerpo);
        using var httpRespuesta = await http.SendAsync(request, cancellationToken);

        if (!httpRespuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(httpRespuesta);
            return null;
        }

        UltimoError = null;
        return await httpRespuesta.Content.ReadFromJsonAsync<SolicitudAtencionDto>(Json, cancellationToken);
    }

    public async Task<SolicitudAtencionDto?> CambiarEstadoAsync(
        int id, string estado, CancellationToken cancellationToken = default)
    {
        var cuerpo = new CambiarEstadoSolicitudRequest { Estado = estado };

        using var request = ApiHttp.Preparar(
            HttpMethod.Post, $"api/solicitudes/{id}/estado", sesion, cuerpo);
        using var respuesta = await http.SendAsync(request, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<SolicitudAtencionDto>(Json, cancellationToken);
    }
}

/// <summary>Cliente HTTP de las notificaciones internas de la tienda.</summary>
public sealed class HttpNotificacionService(HttpClient http, SesionService sesion) : INotificacionService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Último mensaje de error de negocio.</summary>
    public string? UltimoError { get; private set; }

    public async Task<NotificacionesResumenDto?> GetNotificacionesAsync(
        bool soloPendientes = false, CancellationToken cancellationToken = default)
    {
        var url = soloPendientes ? "api/notificaciones?soloPendientes=true" : "api/notificaciones";

        using var request = ApiHttp.Preparar(HttpMethod.Get, url, sesion);
        using var respuesta = await http.SendAsync(request, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<NotificacionesResumenDto>(Json, cancellationToken);
    }

    public async Task<bool> MarcarLeidaAsync(int id, CancellationToken cancellationToken = default)
    {
        using var request = ApiHttp.Preparar(
            HttpMethod.Post, $"api/notificaciones/{id}/leer", sesion);
        using var respuesta = await http.SendAsync(request, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return false;
        }

        UltimoError = null;
        return true;
    }

    public async Task<bool> MarcarTodasLeidasAsync(CancellationToken cancellationToken = default)
    {
        using var request = ApiHttp.Preparar(
            HttpMethod.Post, "api/notificaciones/leer-todas", sesion);
        using var respuesta = await http.SendAsync(request, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return false;
        }

        UltimoError = null;
        return true;
    }
}
