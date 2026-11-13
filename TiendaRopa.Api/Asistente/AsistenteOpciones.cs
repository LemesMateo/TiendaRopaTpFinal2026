namespace TiendaRopa.Api.Asistente;

/// <summary>
/// Configuración del asistente conversacional.
///
/// Estructura en appsettings.json:
/// {
///   "Asistente": {
///     "Gemini": {
///       "Modelo": "gemini-3.5-flash-lite",
///       "MaxLlamadasPorTurno": 6,
///       "TimeoutSegundos": 45
///     }
///   }
/// }
///
/// La API key NUNCA vive en appsettings.json ni en el repositorio: se lee de
/// los secretos de usuario de TiendaRopa.Api (dotnet user-secrets) o de la
/// variable de entorno Asistente__Gemini__ApiKey en producción.
/// </summary>
public sealed class AsistenteOpciones
{
    public const string Seccion = "Asistente";

    /// <summary>Sección de configuración del proveedor de IA.</summary>
    public const string SeccionGemini = "Asistente:Gemini";

    /// <summary>Clave de la API de Gemini dentro de la sección de configuración.</summary>
    public const string ClaveApiKey = "Asistente:Gemini:ApiKey";

    /// <summary>Configuración del proveedor Gemini.</summary>
    public GeminiOpciones Gemini { get; set; } = new();

    /// <summary>True si hay una API key configurada y el asistente puede operar.</summary>
    public bool Habilitado => Gemini.Habilitado;
}

/// <summary>Opciones del proveedor Gemini (modelo, límites y credenciales).</summary>
public sealed class GeminiOpciones
{
    /// <summary>
    /// Modelo de Gemini. Por defecto <c>gemini-3.5-flash-lite</c> (rápido y
    /// barato para el nivel gratuito). Para usar <c>gemini-3.8-flash</c> basta
    /// cambiar este valor en appsettings: no requiere tocar la arquitectura.
    /// </summary>
    public string Modelo { get; set; } = "gemini-3.5-flash-lite";

    /// <summary>
    /// Clave de la API de Gemini. Se resuelve desde secretos de usuario
    /// (<c>Asistente:Gemini:ApiKey</c>) o variable de entorno. Si falta, el
    /// asistente responde con un error controlado en lugar de fallar.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// URL base del endpoint de Gemini. Vacío = la oficial
    /// (generativelanguage.googleapis.com). Se puede cambiar para apuntar a un
    /// proxy corporativo o a un doble de prueba en las pruebas automatizadas.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Temperatura del modelo (0 = determinista, 1 = más creativo).</summary>
    public double Temperatura { get; set; } = 0.4;

    /// <summary>Máximo de tokens de salida por turno.</summary>
    public int MaxTokensSalida { get; set; } = 1024;

    /// <summary>
    /// Máximo de llamadas a herramientas por turno. Acota el trabajo del
    /// modelo y evita bucles de Function Calling.
    /// </summary>
    public int MaxLlamadasPorTurno { get; set; } = 6;

    /// <summary>
    /// Timeout del turno completo (segundos). Un turno con Function Calling
    /// puede encadenar varias llamadas al modelo (petición de herramienta +
    /// respuesta final), así que el margen debe ser holgado.
    /// </summary>
    public int TimeoutSegundos { get; set; } = 90;

    /// <summary>
    /// Máximo de turnos de historial que se envían al modelo por consulta.
    /// Mantiene el contexto breve y el costo acotado.
    /// </summary>
    public int MaxTurnosHistorial { get; set; } = 10;

    /// <summary>True si hay una API key configurada y el asistente puede operar.</summary>
    public bool Habilitado => !string.IsNullOrWhiteSpace(ApiKey);
}
