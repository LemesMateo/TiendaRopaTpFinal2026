using TiendaRopa.Contracts.Asistente;

namespace TiendaRopa.Api.Asistente;

/// <summary>
/// Abstracción del generador conversacional (Gemini).
/// Permite probar el orquestador (pedido con varios estados, cuota agotada,
/// timeout) sin llamar a la API real.
/// </summary>
public interface IGeneradorChat
{
    /// <summary>True si el generador está configurado y puede responder.</summary>
    bool Habilitado { get; }

    /// <summary>Nombre del modelo en uso (informativo).</summary>
    string Modelo { get; }

    /// <summary>
    /// Procesa un turno: llama al modelo, ejecuta las herramientas que pide
    /// (vía el delegate) y devuelve la respuesta final en texto.
    /// </summary>
    Task<ResultadoTurno> ProcesarTurnoAsync(
        IReadOnlyList<ChatMensajeDto> historial,
        string mensaje,
        int? idProductoContexto,
        int? idPedidoContexto,
        Func<string, IReadOnlyDictionary<string, object?>?, CancellationToken, Task<ResultadoHerramienta>> ejecutarHerramienta,
        CancellationToken cancellationToken);
}
