using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TiendaRopa.Tests.Infra;

/// <summary>
/// Doble de prueba de la API de Gemini escrito en C# (sin dependencias).
///
/// Replica la validación REAL de Gemini 3 sobre las thought signatures:
///  - toda parte con <c>functionCall</c> DEBE traer <c>thoughtSignature</c>;
///    si falta, responde 400 con el mensaje exacto de la API de Google;
///  - toda <c>functionResponse</c> debe referenciar el <c>id</c> de una
///    llamada previa;
///  - el contenido del modelo debe llegar íntegro (rol, orden de las partes y
///    firmas), sin fusiones.
///
/// Sirve como prueba de regresión: con el código anterior (que reconstruía los
/// Part) el servidor responde 400 y la prueba falla.
/// </summary>
public sealed class GeminiFalsoServidor : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Queue<JsonObject> _respuestas = new();
    private readonly List<JsonObject> _requests = [];
    private readonly Lock _candado = new();

    /// <summary>Firma que el doble devuelve en las partes con functionCall.</summary>
    public const string FirmaFalsa = "c2tpcF90aG91Z2h0X3NpZ25hdHVyZV92YWxpZGF0b3I=";

    /// <summary>URL base para apuntar el cliente de Gemini a este servidor.</summary>
    public string BaseUrl { get; }

    public GeminiFalsoServidor()
    {
        var puerto = PuertoLibre();
        BaseUrl = $"http://127.0.0.1:{puerto}";
        _listener.Prefixes.Add($"http://127.0.0.1:{puerto}/");
        _listener.Start();
        _ = Task.Run(AtenderAsync);
    }

    /// <summary>Encola una respuesta con una llamada a herramienta (con firma).</summary>
    public void EncolarLlamada(string nombreHerramienta, string idLlamada, JsonObject? argumentos = null)
    {
        var partes = new JsonArray
        {
            new JsonObject
            {
                ["functionCall"] = new JsonObject
                {
                    ["id"] = idLlamada,
                    ["name"] = nombreHerramienta,
                    ["args"] = argumentos ?? new JsonObject(),
                },
                // La firma viaja en el MISMO Part que el functionCall.
                ["thoughtSignature"] = FirmaFalsa,
            },
        };

        EncolarRespuesta(partes);
    }

    /// <summary>Encola una respuesta con dos llamadas en paralelo (dos Partes firmados).</summary>
    public void EncolarLlamadasParalelas((string Nombre, string Id, JsonObject? Args)[] llamadas)
    {
        var partes = new JsonArray();
        foreach (var (nombre, id, args) in llamadas)
        {
            partes.Add(new JsonObject
            {
                ["functionCall"] = new JsonObject
                {
                    ["id"] = id,
                    ["name"] = nombre,
                    ["args"] = args ?? new JsonObject(),
                },
                ["thoughtSignature"] = FirmaFalsa,
            });
        }

        EncolarRespuesta(partes);
    }

    /// <summary>Encola una respuesta final de texto.</summary>
    public void EncolarTexto(string texto)
    {
        var partes = new JsonArray { new JsonObject { ["text"] = texto } };
        EncolarRespuesta(partes);
    }

    private void EncolarRespuesta(JsonArray partes)
    {
        var respuesta = new JsonObject
        {
            ["candidates"] = new JsonArray
            {
                new JsonObject
                {
                    ["content"] = new JsonObject
                    {
                        ["role"] = "model",
                        ["parts"] = partes,
                    },
                    ["finishReason"] = "STOP",
                    ["index"] = 0,
                },
            },
            ["usageMetadata"] = new JsonObject
            {
                ["promptTokenCount"] = 50,
                ["candidatesTokenCount"] = 20,
                ["totalTokenCount"] = 70,
            },
            ["modelVersion"] = "gemini-3.5-flash-lite",
        };

        lock (_candado)
        {
            _respuestas.Enqueue(respuesta);
        }
    }

    /// <summary>Requests recibidos, en orden.</summary>
    public IReadOnlyList<JsonObject> Requests
    {
        get
        {
            lock (_candado)
            {
                return [.. _requests];
            }
        }
    }

    private async Task AtenderAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext contexto;
            try
            {
                contexto = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                return;
            }

            try
            {
                using var lector = new StreamReader(contexto.Request.InputStream, Encoding.UTF8);
                var cuerpo = await lector.ReadToEndAsync();

                JsonObject? peticion = null;
                try
                {
                    peticion = JsonNode.Parse(cuerpo) as JsonObject;
                }
                catch (JsonException)
                {
                    // Se responde 400 más abajo.
                }

                if (peticion is not null)
                {
                    lock (_candado)
                    {
                        _requests.Add(peticion);
                    }
                }

                var error = Validar(peticion);

                if (error is not null)
                {
                    await ResponderErrorAsync(contexto, error);
                    continue;
                }

                JsonObject respuesta;
                lock (_candado)
                {
                    respuesta = _respuestas.Count > 0
                        ? _respuestas.Dequeue()
                        : RespuestaTexto("Sin más consultas.");
                }

                var json = respuesta.ToJsonString();
                var bytes = Encoding.UTF8.GetBytes(json);
                contexto.Response.StatusCode = 200;
                contexto.Response.ContentType = "application/json";
                contexto.Response.ContentLength64 = bytes.Length;
                await contexto.Response.OutputStream.WriteAsync(bytes);
                contexto.Response.Close();
            }
            catch (Exception)
            {
                try { contexto.Response.Abort(); } catch { /* ignorado */ }
            }
        }
    }

    /// <summary>
    /// Réplica la validación de Gemini 3. Devuelve null si el request es válido
    /// o el mensaje de error si no lo es.
    /// </summary>
    private static string? Validar(JsonObject? peticion)
    {
        if (peticion is null)
        {
            return "Request no válido.";
        }

        var contents = peticion["contents"] as JsonArray;
        if (contents is null || contents.Count == 0)
        {
            return "contents vacío.";
        }

        var idsConocidos = new HashSet<string>(StringComparer.Ordinal);

        foreach (var nodoContenido in contents)
        {
            if (nodoContenido is not JsonObject contenido)
            {
                continue;
            }

            if (contenido["parts"] is not JsonArray partes)
            {
                continue;
            }

            foreach (var nodoParte in partes)
            {
                if (nodoParte is not JsonObject parte)
                {
                    continue;
                }

                var tieneFirma = parte["thoughtSignature"] is JsonNode firma
                    && !string.IsNullOrEmpty(firma.GetValue<string>());

                if (parte["functionCall"] is JsonObject llamada)
                {
                    if (!tieneFirma)
                    {
                        return "Function call is missing a thought_signature in functionCall parts. " +
                               "This is required for tools to work correctly.";
                    }

                    var id = llamada["id"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(id))
                    {
                        idsConocidos.Add(id);
                    }
                }

                if (parte["functionResponse"] is JsonObject respuesta)
                {
                    var id = respuesta["id"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(id) && !idsConocidos.Contains(id))
                    {
                        return $"Function response with id '{id}' does not match any function call.";
                    }
                }
            }
        }

        return null;
    }

    private static JsonObject RespuestaTexto(string texto) => new()
    {
        ["candidates"] = new JsonArray
        {
            new JsonObject
            {
                ["content"] = new JsonObject
                {
                    ["role"] = "model",
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = texto } },
                },
                ["finishReason"] = "STOP",
            },
        },
    };

    private static async Task ResponderErrorAsync(HttpListenerContext contexto, string mensaje)
    {
        var cuerpo = new JsonObject
        {
            ["error"] = new JsonObject
            {
                ["code"] = 400,
                ["message"] = mensaje,
                ["status"] = "INVALID_ARGUMENT",
            },
        };

        var bytes = Encoding.UTF8.GetBytes(cuerpo.ToJsonString());
        contexto.Response.StatusCode = 400;
        contexto.Response.ContentType = "application/json";
        contexto.Response.ContentLength64 = bytes.Length;
        await contexto.Response.OutputStream.WriteAsync(bytes);
        contexto.Response.Close();
    }

    private static int PuertoLibre()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var puerto = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return puerto;
    }

    public void Dispose()
    {
        try
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
            }
        }
        catch (Exception)
        {
            // Ignorado: el servidor ya puede estar detenido.
        }

        _listener.Close();
    }
}
