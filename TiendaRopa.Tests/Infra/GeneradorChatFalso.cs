using TiendaRopa.Api.Asistente;
using TiendaRopa.Contracts.Asistente;

namespace TiendaRopa.Tests.Infra;

/// <summary>
/// Generador conversacional falso: no llama a Gemini.
/// Simula las respuestas del modelo, las llamadas a herramientas y los
/// errores (cuota agotada, timeout) sin gastar cuota ni tocar la red.
/// </summary>
public sealed class GeneradorChatFalso : IGeneradorChat
{
    /// <summary>Texto que devuelve el asistente si no hay guion configurado.</summary>
    public string RespuestaPorDefecto { get; set; } = "Hola, ¿en qué te ayudo?";

    /// <summary>Guion: por cada turno, las herramientas que el modelo pide.</summary>
    public List<List<string>> GuionHerramientas { get; } = [];

    /// <summary>Herramientas que se piden en el turno actual (se consume al usarlas).</summary>
    private readonly Queue<List<string>> _guion = new();

    /// <summary>Simula el error de cuota agotada (HTTP 429 de Gemini).</summary>
    public bool SimularCuotaAgotada { get; set; }

    /// <summary>Simula un timeout de la llamada al modelo.</summary>
    public bool SimularTimeout { get; set; }

    /// <summary>Simula un fallo de conexión.</summary>
    public bool SimularSinConexion { get; set; }

    /// <summary>Herramientas que el modelo pidió realmente (para verificar).</summary>
    public List<string> HerramientasInvocadas { get; } = [];

    /// <summary>Si es false, el asistente responde "sin_configuracion".</summary>
    public bool Habilitado { get; set; } = true;

    public string Modelo => "gemini-3.5-flash-lite";

    /// <summary>Encola el guion de herramientas para los próximos turnos.</summary>
    public GeneradorChatFalso ConHerramientas(params string[] herramientas)
    {
        GuionHerramientas.Add(herramientas.ToList());
        _guion.Enqueue(herramientas.ToList());
        return this;
    }

    public async Task<ResultadoTurno> ProcesarTurnoAsync(
        IReadOnlyList<ChatMensajeDto> historial,
        string mensaje,
        int? idProductoContexto,
        int? idPedidoContexto,
        Func<string, IReadOnlyDictionary<string, object?>?, CancellationToken, Task<ResultadoHerramienta>> ejecutarHerramienta,
        CancellationToken cancellationToken)
    {
        if (!Habilitado)
        {
            return ResultadoTurno.Fallido(
                "sin_configuracion",
                "El asistente todavía no está configurado. Probá de nuevo más tarde.");
        }

        if (SimularCuotaAgotada)
        {
            return ResultadoTurno.Fallido(
                "cuota_agotada",
                "Se alcanzó el límite de uso del asistente por ahora. Probá de nuevo en unos minutos.");
        }

        if (SimularTimeout)
        {
            return ResultadoTurno.Fallido(
                "timeout",
                "La consulta tardó demasiado. Probá con una pregunta más corta.");
        }

        if (SimularSinConexion)
        {
            return ResultadoTurno.Fallido(
                "sin_conexion",
                "No pudimos conectar con el asistente. Revisá tu conexión y probá de nuevo.");
        }

        // Si hay guion para este turno, se ejecutan las herramientas pedidas.
        if (_guion.Count > 0)
        {
            var herramientas = _guion.Dequeue();
            foreach (var herramienta in herramientas)
            {
                HerramientasInvocadas.Add(herramienta);
                await ejecutarHerramienta(herramienta, null, cancellationToken);
            }
        }

        await Task.Yield();
        return ResultadoTurno.Ok(RespuestaPorDefecto);
    }
}
