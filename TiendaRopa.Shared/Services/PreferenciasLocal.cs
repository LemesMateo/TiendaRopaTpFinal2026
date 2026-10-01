using Microsoft.JSInterop;

namespace TiendaRopa.Shared.Services;

/// <summary>
/// Almacenamiento clave/valor persistente del cliente.
/// Abstracción para que Shared no dependa de APIs específicas de MAUI o Web:
/// la implementación por defecto usa localStorage del navegador/WebView,
/// que existe tanto en Blazor Server como en MAUI Blazor Hybrid.
/// </summary>
public interface IPreferenciasLocal
{
    Task<string?> ObtenerAsync(string clave);
    Task GuardarAsync(string clave, string valor);
    Task EliminarAsync(string clave);
}

public sealed class PreferenciasLocal(IJSRuntime js) : IPreferenciasLocal
{
    // Durante el prerender no hay JS disponible: se degrada a "sin valor"
    // y la página vuelve a cargar en el render interactivo.
    public async Task<string?> ObtenerAsync(string clave)
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", clave);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (JSException)
        {
            return null;
        }
    }

    public async Task GuardarAsync(string clave, string valor)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", clave, valor);
        }
        catch (InvalidOperationException)
        {
            // Prerender: se ignora; el valor se guardará en el render interactivo.
        }
        catch (JSException)
        {
        }
    }

    public async Task EliminarAsync(string clave)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", clave);
        }
        catch (InvalidOperationException)
        {
        }
        catch (JSException)
        {
        }
    }
}
