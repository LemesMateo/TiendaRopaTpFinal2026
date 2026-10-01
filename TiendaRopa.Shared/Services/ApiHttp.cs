using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TiendaRopa.Shared.Services;

/// <summary>
/// Utilidades comunes de los clientes HTTP de la tienda:
/// agrega el JWT (si existe) y traduce ProblemDetails de error a texto.
/// </summary>
public static class ApiHttp
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Prepara un request con Authorization: Bearer … cuando hay sesión.</summary>
    public static HttpRequestMessage Preparar(
        HttpMethod metodo, string ruta, SesionService sesion, object? cuerpo = null)
    {
        var request = new HttpRequestMessage(metodo, ruta);

        if (!string.IsNullOrWhiteSpace(sesion.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sesion.Token);
        }

        if (cuerpo is not null)
        {
            request.Content = JsonContent.Create(cuerpo, options: Json);
        }

        return request;
    }

    /// <summary>Extrae el campo "detail" de una respuesta ProblemDetails (o un texto genérico).</summary>
    public static async Task<string> LeerErrorAsync(HttpResponseMessage respuesta)
    {
        try
        {
            var texto = await respuesta.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(texto))
            {
                return $"Error del servidor ({(int)respuesta.StatusCode}).";
            }

            using var doc = JsonDocument.Parse(texto);
            if (doc.RootElement.TryGetProperty("detail", out var detalle)
                && detalle.ValueKind == JsonValueKind.String)
            {
                return detalle.GetString() ?? texto;
            }

            if (doc.RootElement.TryGetProperty("title", out var titulo)
                && titulo.ValueKind == JsonValueKind.String)
            {
                return titulo.GetString() ?? texto;
            }

            return texto;
        }
        catch (JsonException)
        {
            return $"Error del servidor ({(int)respuesta.StatusCode}).";
        }
    }

    /// <summary>True si la respuesta fue 401 (sesión vencida o inexistente).</summary>
    public static bool EsNoAutorizado(HttpResponseMessage respuesta)
        => respuesta.StatusCode == System.Net.HttpStatusCode.Unauthorized;
}
