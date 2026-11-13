using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TiendaRopa.Api.Asistente;
using TiendaRopa.Contracts.Asistente;
using TiendaRopa.Tests.Infra;

namespace TiendaRopa.Tests;

/// <summary>
/// Pruebas de regresión del manejo del historial y las llamadas a herramientas.
///
/// El doble <see cref="GeminiFalsoServidor"/> replica la validación real de
/// Gemini 3: si una parte con <c>functionCall</c> llega sin
/// <c>thoughtSignature</c>, responde 400 con el mensaje exacto de Google.
///
/// Con la implementación anterior (que reconstruía los <c>Part</c>) estas
/// pruebas fallan; con la corrección pasan.
/// </summary>
public sealed class AsistenteGeminiFirmasTests
{
    /// <summary>Dispatcher que registra las herramientas invocadas.</summary>
    private sealed class RegistroHerramientas
    {
        public List<string> Invocadas { get; } = [];

        public List<(string Nombre, IReadOnlyDictionary<string, object?>? Args)> Llamadas { get; } = [];

        public Task<ResultadoHerramienta> Invocar(
            string nombre,
            IReadOnlyDictionary<string, object?>? argumentos,
            CancellationToken ct)
        {
            Invocadas.Add(nombre);
            Llamadas.Add((nombre, argumentos));

            return Task.FromResult(ResultadoHerramienta.Ok(new
            {
                ok = true,
                variantes = new[] { new { talle = "L", color = "Negro", stock = 3 } },
            }));
        }
    }

    /// <summary>
    /// Contents con rol "user" que llevan respuestas de herramienta. El
    /// mensaje inicial del cliente también es un Content "user", así que hay
    /// que filtrar por la presencia de partes functionResponse.
    /// </summary>
    private static List<JsonObject> ContenuesConRespuesta(JsonArray contents) =>
        contents
            .OfType<JsonObject>()
            .Where(c => c["role"]?.GetValue<string>() == "user")
            .Where(c => c["parts"] is JsonArray partes
                && partes.OfType<JsonObject>().Any(p => p["functionResponse"] is not null))
            .ToList();

    private static AsistenteGemini CrearGenerador(GeminiFalsoServidor servidor)
    {
        var opciones = new AsistenteOpciones
        {
            Gemini = new GeminiOpciones
            {
                ApiKey = "clave-de-prueba",
                BaseUrl = servidor.BaseUrl,
                Modelo = "gemini-3.5-flash-lite",
                TimeoutSegundos = 20,
                MaxLlamadasPorTurno = 6,
            },
        };

        return new AsistenteGemini(NullLogger<AsistenteGemini>.Instance, opciones);
    }

    [Fact]
    public async Task UnaLlamada_LaFirmaSeConservaEnElRequestSiguiente()
    {
        using var servidor = new GeminiFalsoServidor();
        servidor.EncolarLlamada("consultar_variantes", "llamada-1");
        servidor.EncolarTexto("Hay stock en negro talle L.");

        var generador = CrearGenerador(servidor);
        var registro = new RegistroHerramientas();

        var resultado = await generador.ProcesarTurnoAsync(
            [], "¿Este vestido viene en negro talle L?",
            null, null,
            registro.Invocar,
            CancellationToken.None);

        Assert.True(resultado.Exito, resultado.ErrorMensaje);
        Assert.Equal("Hay stock en negro talle L.", resultado.Mensaje);
        Assert.Equal(["consultar_variantes"], registro.Invocadas);

        // El segundo request es el que dispara el error 400 de Google si la
        // firma se perdió: debe traer la parte original con su firma.
        Assert.Equal(2, servidor.Requests.Count);

        var segundo = servidor.Requests[1];
        var contents = (JsonArray)segundo["contents"]!;

        var contenidoModelo = contents
            .OfType<JsonObject>()
            .Single(c => c["role"]?.GetValue<string>() == "model");

        var partes = (JsonArray)contenidoModelo["parts"]!;
        var parteLlamada = partes.OfType<JsonObject>().Single(p => p["functionCall"] is not null);

        // La firma debe estar en el MISMO Part que el functionCall.
        Assert.NotNull(parteLlamada["thoughtSignature"]);
        Assert.Equal(
            GeminiFalsoServidor.FirmaFalsa,
            parteLlamada["thoughtSignature"]!.GetValue<string>());

        // Y la respuesta de la herramienta debe referenciar el id de la llamada.
        var contenidoRespuesta = ContenuesConRespuesta(contents).Single();

        var partesRespuesta = (JsonArray)contenidoRespuesta["parts"]!;
        var parteRespuesta = partesRespuesta.OfType<JsonObject>().Single();

        var functionResponse = (JsonObject)parteRespuesta["functionResponse"]!;
        Assert.Equal("llamada-1", functionResponse["id"]!.GetValue<string>());
        Assert.Equal("consultar_variantes", functionResponse["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task DosHerramientasSecuenciales_SeEjecutanEnOrdenYConservanSusFirmas()
    {
        using var servidor = new GeminiFalsoServidor();
        servidor.EncolarLlamada("buscar_producto", "llamada-1");
        servidor.EncolarLlamada("consultar_variantes", "llamada-2");
        servidor.EncolarTexto("El vestido Satinado Noche viene en negro talle L.");

        var generador = CrearGenerador(servidor);
        var registro = new RegistroHerramientas();

        var resultado = await generador.ProcesarTurnoAsync(
            [], "Buscá el vestido y decime sus variantes.",
            null, null,
            registro.Invocar,
            CancellationToken.None);

        Assert.True(resultado.Exito, resultado.ErrorMensaje);
        Assert.Equal(["buscar_producto", "consultar_variantes"], registro.Invocadas);

        // Tres requests: llamada 1, llamada 2 y respuesta final.
        Assert.Equal(3, servidor.Requests.Count);

        // Cada request con una llamada debe traer la parte original firmada.
        for (var i = 1; i < servidor.Requests.Count; i++)
        {
            var contents = (JsonArray)servidor.Requests[i]["contents"]!;

            var contenidoModelo = contents
                .OfType<JsonObject>()
                .Last(c => c["role"]?.GetValue<string>() == "model");

            var partes = (JsonArray)contenidoModelo["parts"]!;
            var parteLlamada = partes.OfType<JsonObject>().Single(p => p["functionCall"] is not null);

            Assert.NotNull(parteLlamada["thoughtSignature"]);
        }

        // En el último request deben estar las dos respuestas de herramienta,
        // cada una con el id de su llamada.
        var ultimoContents = (JsonArray)servidor.Requests[^1]["contents"]!;
        var ids = ContenuesConRespuesta(ultimoContents)
            .SelectMany(c => (JsonArray)c["parts"]!)
            .OfType<JsonObject>()
            .Select(p => ((JsonObject)p["functionResponse"]!)["id"]!.GetValue<string>())
            .ToList();

        Assert.Contains("llamada-1", ids);
        Assert.Contains("llamada-2", ids);
    }

    [Fact]
    public async Task LlamadasEnParalelo_UnaSolaParteUserConTodasLasRespuestas()
    {
        using var servidor = new GeminiFalsoServidor();
        servidor.EncolarLlamadasParalelas(
        [
            ("buscar_producto", "llamada-a", new JsonObject { ["texto"] = "vestido" }),
            ("consultar_variantes", "llamada-b", new JsonObject { ["id_producto"] = 3 }),
        ]);
        servidor.EncolarTexto("Encontré el vestido y sus variantes.");

        var generador = CrearGenerador(servidor);
        var registro = new RegistroHerramientas();

        var resultado = await generador.ProcesarTurnoAsync(
            [], "¿Qué variantes hay disponibles?",
            null, null,
            registro.Invocar,
            CancellationToken.None);

        Assert.True(resultado.Exito, resultado.ErrorMensaje);
        Assert.Equal(2, registro.Invocadas.Count);

        // Las dos partes firmadas van en el mismo Content del modelo.
        var contents = (JsonArray)servidor.Requests[1]["contents"]!;
        var contenidoModelo = contents
            .OfType<JsonObject>()
            .Single(c => c["role"]?.GetValue<string>() == "model");

        var partes = (JsonArray)contenidoModelo["parts"]!;
        var partesLlamada = partes.OfType<JsonObject>().Where(p => p["functionCall"] is not null).ToList();

        Assert.Equal(2, partesLlamada.Count);
        Assert.All(partesLlamada, p => Assert.NotNull(p["thoughtSignature"]));

        // Las dos respuestas van en UN único turno "user".
        var contenidosUsuario = ContenuesConRespuesta(contents);

        Assert.Single(contenidosUsuario);

        var respuestas = (JsonArray)contenidosUsuario[0]["parts"]!;
        var ids = respuestas
            .OfType<JsonObject>()
            .Select(p => ((JsonObject)p["functionResponse"]!)["id"]!.GetValue<string>())
            .ToList();

        Assert.Equal(2, respuestas.Count);
        Assert.Contains("llamada-a", ids);
        Assert.Contains("llamada-b", ids);
    }

    [Fact]
    public async Task ElHistorialDelCliente_NoIntroducePartesDeFuncionIncompletas()
    {
        using var servidor = new GeminiFalsoServidor();
        servidor.EncolarLlamada("consultar_variantes", "llamada-1");
        servidor.EncolarTexto("Respuesta final.");

        var generador = CrearGenerador(servidor);
        var registro = new RegistroHerramientas();

        // Historial con texto de usuario y asistente (como lo guarda la UI).
        var historial = new List<ChatMensajeDto>
        {
            new() { Rol = ChatRoles.Usuario, Texto = "Hola" },
            new() { Rol = ChatRoles.Asistente, Texto = "Hola, ¿en qué te ayudo?" },
            new() { Rol = ChatRoles.Usuario, Texto = "¿Qué variantes hay?" },
        };

        await generador.ProcesarTurnoAsync(
            historial, "Y en talle L?",
            null, null,
            registro.Invocar,
            CancellationToken.None);

        // El primer request lleva el historial como texto plano: ninguna parte
        // de función (que iría sin firma y rompería la API).
        var primer = servidor.Requests[0];
        var contents = (JsonArray)primer["contents"]!;

        var partesConFuncion = contents
            .OfType<JsonObject>()
            .SelectMany(c => (c["parts"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            .Where(p => p["functionCall"] is not null)
            .ToList();

        Assert.Empty(partesConFuncion);

        // El historial conserva los roles y el orden.
        var roles = contents
            .OfType<JsonObject>()
            .Select(c => c["role"]?.GetValue<string>())
            .ToList();

        Assert.Equal(["user", "model", "user", "user"], roles);
    }

    [Fact]
    public async Task HistorialQueArrancaConElAsistente_SeDescarta()
    {
        using var servidor = new GeminiFalsoServidor();
        servidor.EncolarTexto("Hola, ¿en qué te ayudo?");

        var generador = CrearGenerador(servidor);
        var registro = new RegistroHerramientas();

        var historial = new List<ChatMensajeDto>
        {
            new() { Rol = ChatRoles.Asistente, Texto = "Mensaje previo del asistente" },
            new() { Rol = ChatRoles.Usuario, Texto = "Hola" },
        };

        await generador.ProcesarTurnoAsync(
            historial, "Hola",
            null, null,
            registro.Invocar,
            CancellationToken.None);

        var contents = (JsonArray)servidor.Requests[0]["contents"]!;
        var roles = contents
            .OfType<JsonObject>()
            .Select(c => c["role"]?.GetValue<string>())
            .ToList();

        // La conversación arranca con el usuario (un "model" inicial no es válido).
        Assert.Equal("user", roles[0]);
    }

    [Fact]
    public async Task SeRespetaElLimiteDeLlamadasPorTurno()
    {
        using var servidor = new GeminiFalsoServidor();
        for (var i = 0; i < 8; i++)
        {
            servidor.EncolarLlamada("consultar_variantes", $"llamada-{i}");
        }

        var generador = CrearGenerador(servidor);
        var registro = new RegistroHerramientas();

        var resultado = await generador.ProcesarTurnoAsync(
            [], "Consultá muchas variantes.",
            null, null,
            registro.Invocar,
            CancellationToken.None);

        Assert.True(resultado.Exito);
        Assert.True(registro.Invocadas.Count <= 6, $"llamadas={registro.Invocadas.Count}");
        Assert.Contains("reformules", resultado.Mensaje);
    }
}
