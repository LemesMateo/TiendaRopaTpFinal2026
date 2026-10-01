using System.Net.Http.Json;
using System.Text.Json;
using TiendaRopa.Contracts.Pedidos;

namespace TiendaRopa.Shared.Services;

/// <summary>Cliente HTTP de pedidos con JWT.</summary>
public sealed class HttpPedidoService(HttpClient http, SesionService sesion, AssetsBase assets) : IPedidoService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Último mensaje de negocio (409 por stock, 401, etc.).</summary>
    public string? UltimoError { get; private set; }

    public async Task<PedidoDto?> CrearPedidoAsync(
        CrearPedidoRequest request, CancellationToken cancellationToken = default)
    {
        using var requestHttp = ApiHttp.Preparar(HttpMethod.Post, "api/pedidos", sesion, request);
        using var respuesta = await http.SendAsync(requestHttp, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        UltimoError = null;
        var creado = await respuesta.Content.ReadFromJsonAsync<PedidoDto>(Json, cancellationToken);
        if (creado is not null)
        {
            ResolverImagenes([creado]);
        }

        return creado;
    }

    public async Task<IReadOnlyList<PedidoDto>> GetMisPedidosAsync(
        CancellationToken cancellationToken = default)
    {
        using var requestHttp = ApiHttp.Preparar(HttpMethod.Get, "api/pedidos", sesion);
        using var respuesta = await http.SendAsync(requestHttp, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return [];
        }

        var pedidos = await respuesta.Content.ReadFromJsonAsync<List<PedidoDto>>(Json, cancellationToken) ?? [];
        ResolverImagenes(pedidos);
        return pedidos;
    }

    public async Task<PedidoDto?> GetPedidoAsync(int id, CancellationToken cancellationToken = default)
    {
        using var requestHttp = ApiHttp.Preparar(HttpMethod.Get, $"api/pedidos/{id}", sesion);
        using var respuesta = await http.SendAsync(requestHttp, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        var pedido = await respuesta.Content.ReadFromJsonAsync<PedidoDto>(Json, cancellationToken);
        if (pedido is not null)
        {
            ResolverImagenes([pedido]);
        }

        return pedido;
    }

    /// <summary>Las imágenes vienen como ruta relativa a la API (/img/…).</summary>
    private void ResolverImagenes(IEnumerable<PedidoDto> pedidos)
    {
        foreach (var pedido in pedidos)
        {
            foreach (var detalle in pedido.Detalles)
            {
                detalle.UrlImagen = assets.Resolver(detalle.UrlImagen);
            }
        }
    }

    public async Task<PedidoDto?> CancelarPedidoAsync(int id, CancellationToken cancellationToken = default)
    {
        using var requestHttp = ApiHttp.Preparar(HttpMethod.Post, $"api/pedidos/{id}/cancelar", sesion);
        using var respuesta = await http.SendAsync(requestHttp, cancellationToken);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<PedidoDto>(Json, cancellationToken);
    }
}
