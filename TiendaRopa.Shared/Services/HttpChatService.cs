using System.Net.Http.Json;
using System.Text.Json;
using TiendaRopa.Contracts.Asistente;
using TiendaRopa.Contracts.Solicitudes;

namespace TiendaRopa.Shared.Services;

/// <summary>
/// Cliente HTTP del asistente conversacional.
/// El JWT se agrega por request (como en el resto de la tienda) y la
/// identidad la resuelve el backend: nunca se envía un IdCliente.
/// </summary>
public sealed class HttpChatService(HttpClient http, SesionService sesion) : IChatService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Último mensaje de error (401, 503, error de conexión…).</summary>
    public string? UltimoError { get; private set; }

    public async Task<ChatRespuestaDto?> EnviarAsync(
        ChatSolicitudDto solicitud, CancellationToken cancellationToken = default)
    {
        using var request = ApiHttp.Preparar(HttpMethod.Post, "api/asistente/mensaje", sesion, solicitud);
        using var respuesta = await http.SendAsync(request, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await LeerErrorAsync(respuesta);
            return null;
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<ChatRespuestaDto>(Json, cancellationToken);
    }

    public async Task<IReadOnlyList<SolicitudPropuestaDto>> GetPropuestasAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = ApiHttp.Preparar(HttpMethod.Get, "api/asistente/propuestas", sesion);
        using var respuesta = await http.SendAsync(request, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return [];
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<List<SolicitudPropuestaDto>>(Json, cancellationToken) ?? [];
    }

    public async Task<SolicitudAtencionDto?> ConfirmarPropuestaAsync(
        int idPropuesta, ConfirmarPropuestaRequest body, CancellationToken cancellationToken = default)
    {
        using var request = ApiHttp.Preparar(
            HttpMethod.Post, $"api/asistente/propuestas/{idPropuesta}/confirmar", sesion, body);
        using var respuesta = await http.SendAsync(request, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<SolicitudAtencionDto>(Json, cancellationToken);
    }

    public async Task<SolicitudPropuestaDto?> CancelarPropuestaAsync(
        int idPropuesta, CancellationToken cancellationToken = default)
    {
        using var request = ApiHttp.Preparar(
            HttpMethod.Post, $"api/asistente/propuestas/{idPropuesta}/cancelar", sesion);
        using var respuesta = await http.SendAsync(request, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<SolicitudPropuestaDto>(Json, cancellationToken);
    }

    public async Task<SolicitudPropuestaDto?> ConfirmarTelefonoAsync(
        int idPropuesta, string telefono, bool guardarEnPerfil, CancellationToken cancellationToken = default)
    {
        var cuerpo = new ConfirmarTelefonoPropuestaRequest
        {
            Telefono = telefono,
            GuardarEnPerfil = guardarEnPerfil,
        };

        using var request = ApiHttp.Preparar(
            HttpMethod.Put, $"api/asistente/propuestas/{idPropuesta}/telefono", sesion, cuerpo);
        using var respuesta = await http.SendAsync(request, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<SolicitudPropuestaDto>(Json, cancellationToken);
    }

    /// <summary>
    /// Traduce la respuesta del backend a un mensaje para la UI.
    /// 503 con el cuerpo del asistente: el mensaje es el que debe verse.
    /// </summary>
    private static async Task<string> LeerErrorAsync(HttpResponseMessage respuesta)
    {
        try
        {
            var texto = await respuesta.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(texto))
            {
                return $"Error del asistente ({respuesta.StatusCode}).";
            }

            // El backend puede devolver el ChatRespuestaDto con el mensaje
            // amigable (por ejemplo, cuando el asistente no está configurado).
            using var doc = JsonDocument.Parse(texto);
            if (doc.RootElement.TryGetProperty("mensaje", out var mensaje)
                && mensaje.ValueKind == JsonValueKind.String)
            {
                var valor = mensaje.GetString();
                if (!string.IsNullOrWhiteSpace(valor))
                {
                    return valor;
                }
            }

            if (doc.RootElement.TryGetProperty("errorMensaje", out var detalle)
                && detalle.ValueKind == JsonValueKind.String)
            {
                var valor = detalle.GetString();
                if (!string.IsNullOrWhiteSpace(valor))
                {
                    return valor;
                }
            }
        }
        catch (JsonException)
        {
            // Cuerpo no JSON: se cae al mensaje genérico.
        }

        return await ApiHttp.LeerErrorAsync(respuesta);
    }
}
