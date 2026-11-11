namespace TiendaRopa;

/// <summary>Configuración de la app que se comparte entre varios servicios.</summary>
public static class ConfiguracionApp
{
    /// <summary>
    /// URL base de la API Velvet Rose por plataforma:
    ///  - Windows/macOS: la API corre en esta misma máquina -> localhost.
    ///  - Android (emulador): 10.0.2.2 apunta al host de la máquina.
    ///    En un dispositivo físico hay que usar la IP en LAN de la PC
    ///    (ajustar acá; verificar que la API escuche en esa red).
    /// Nota: en Android se habilitó usesCleartextTraffic sólo en desarrollo.
    /// </summary>
#if ANDROID
    public const string ApiBaseUrl = "http://10.0.2.2:5100";
#else
    public const string ApiBaseUrl = "http://localhost:5100";
#endif
}
