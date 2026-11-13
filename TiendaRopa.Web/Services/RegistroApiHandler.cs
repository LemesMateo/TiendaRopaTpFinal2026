namespace TiendaRopa.Web.Services;

/// <summary>
/// Registro diagnóstico de las llamadas que Blazor Server hace a su propia API.
///
/// Deja en el log del servidor la ruta solicitada (sin query string, para no
/// guardar texto que escribe el usuario), el código HTTP y la excepción real.
/// Nunca registra cabeceras de autorización, cookies, tokens ni cadenas de
/// conexión: sólo método, ruta, estado y tipo/mensaje de la excepción.
/// </summary>
internal sealed class RegistroApiHandler(ILogger log) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var ruta = request.RequestUri?.AbsolutePath ?? "(sin ruta)";

        try
        {
            var respuesta = await base.SendAsync(request, cancellationToken);

            if (!respuesta.IsSuccessStatusCode)
            {
                log.LogWarning(
                    "[VelvetRose] La API respondió {Codigo} a {Metodo} {Ruta}.",
                    (int)respuesta.StatusCode, request.Method.Method, ruta);
            }

            return respuesta;
        }
        catch (Exception ex)
        {
            log.LogError(
                ex,
                "[VelvetRose] Falló la llamada a la API {Metodo} {Ruta} ({Tipo}): {Mensaje}",
                request.Method.Method, ruta, ex.GetType().Name, ex.Message);

            throw;
        }
    }
}
