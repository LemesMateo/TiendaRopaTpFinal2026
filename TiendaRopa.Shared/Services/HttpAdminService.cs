using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TiendaRopa.Contracts.Admin;
using TiendaRopa.Contracts.Catalogo;
using TiendaRopa.Contracts.Common;
using TiendaRopa.Contracts.Pedidos;

namespace TiendaRopa.Shared.Services;

/// <summary>Cliente HTTP del panel administrativo (JWT con rol Administrador).</summary>
public sealed class HttpAdminService(HttpClient http, SesionService sesion, AssetsBase assets) : IAdminService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Último mensaje de error de negocio (403, 409, validaciones…).</summary>
    public string? UltimoError { get; private set; }

    // ---------------- Productos ----------------

    public async Task<PagedResult<ProductoAdminDto>> GetProductosAsync(
        string? buscar, bool? activo, int page, int pageSize, CancellationToken ct = default)
    {
        var url = $"api/admin/productos?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            url += $"&buscar={Uri.EscapeDataString(buscar)}";
        }

        if (activo is { } a)
        {
            url += $"&activo={a.ToString().ToLowerInvariant()}";
        }

        using var request = ApiHttp.Preparar(HttpMethod.Get, url, sesion);
        using var respuesta = await http.SendAsync(request, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return new PagedResult<ProductoAdminDto>();
        }

        UltimoError = null;
        var resultado = await respuesta.Content.ReadFromJsonAsync<PagedResult<ProductoAdminDto>>(Json, ct)
            ?? new PagedResult<ProductoAdminDto>();

        foreach (var item in resultado.Items)
        {
            item.UrlImagen = assets.Resolver(item.UrlImagen);
        }

        return resultado;
    }

    public async Task<ProductoAdminDto?> GetProductoAsync(int id, CancellationToken ct = default)
    {
        using var request = ApiHttp.Preparar(HttpMethod.Get, $"api/admin/productos/{id}", sesion);
        using var respuesta = await http.SendAsync(request, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        var producto = await respuesta.Content.ReadFromJsonAsync<ProductoAdminDto>(Json, ct);
        if (producto is not null)
        {
            producto.UrlImagen = assets.Resolver(producto.UrlImagen);
        }

        return producto;
    }

    public Task<ProductoAdminDto?> GuardarProductoAsync(
        ProductoUpsertRequest request, CancellationToken ct = default)
        => EnviarProductoAsync(
            request.Id == 0 ? HttpMethod.Post : HttpMethod.Put,
            request.Id == 0 ? "api/admin/productos" : $"api/admin/productos/{request.Id}",
            request, ct);

    private async Task<ProductoAdminDto?> EnviarProductoAsync(
        HttpMethod metodo, string ruta, ProductoUpsertRequest cuerpo, CancellationToken ct)
    {
        using var request = ApiHttp.Preparar(metodo, ruta, sesion, cuerpo);
        using var respuesta = await http.SendAsync(request, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<ProductoAdminDto>(Json, ct);
    }

    public async Task<bool> DesactivarProductoAsync(int id, CancellationToken ct = default)
    {
        using var request = ApiHttp.Preparar(HttpMethod.Delete, $"api/admin/productos/{id}", sesion);
        using var respuesta = await http.SendAsync(request, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
        }

        return respuesta.IsSuccessStatusCode;
    }

    // ---------------- Categorías / Talles / Colores ----------------

    public Task<IReadOnlyList<EntidadCatalogoDto>> GetCategoriasAsync(bool? activo, CancellationToken ct = default)
        => GetEntidadesAsync("categorias", activo, ct);

    public Task<IReadOnlyList<EntidadCatalogoDto>> GetTallesAsync(bool? activo, CancellationToken ct = default)
        => GetEntidadesAsync("talles", activo, ct);

    public Task<IReadOnlyList<EntidadCatalogoDto>> GetColoresAsync(bool? activo, CancellationToken ct = default)
        => GetEntidadesAsync("colores", activo, ct);

    private async Task<IReadOnlyList<EntidadCatalogoDto>> GetEntidadesAsync(
        string recurso, bool? activo, CancellationToken ct)
    {
        var url = $"api/admin/{recurso}";
        if (activo is { } a)
        {
            url += $"?activo={a.ToString().ToLowerInvariant()}";
        }

        using var request = ApiHttp.Preparar(HttpMethod.Get, url, sesion);
        using var respuesta = await http.SendAsync(request, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return [];
        }

        return await respuesta.Content.ReadFromJsonAsync<List<EntidadCatalogoDto>>(Json, ct) ?? [];
    }

    public Task<EntidadCatalogoDto?> GuardarCategoriaAsync(NombreActivoRequest request, CancellationToken ct = default)
        => GuardarEntidadAsync("categorias", request, ct);

    public Task<EntidadCatalogoDto?> GuardarTalleAsync(NombreActivoRequest request, CancellationToken ct = default)
        => GuardarEntidadAsync("talles", request, ct);

    public Task<EntidadCatalogoDto?> GuardarColorAsync(NombreActivoRequest request, CancellationToken ct = default)
        => GuardarEntidadAsync("colores", request, ct);

    private async Task<EntidadCatalogoDto?> GuardarEntidadAsync(
        string recurso, NombreActivoRequest request, CancellationToken ct)
    {
        var metodo = request.Id == 0 ? HttpMethod.Post : HttpMethod.Put;
        var ruta = request.Id == 0
            ? $"api/admin/{recurso}"
            : $"api/admin/{recurso}/{request.Id}";

        using var http2 = ApiHttp.Preparar(metodo, ruta, sesion, request);
        using var respuesta = await http.SendAsync(http2, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<EntidadCatalogoDto>(Json, ct);
    }

    public Task<bool> DesactivarCategoriaAsync(int id, CancellationToken ct = default)
        => DesactivarEntidadAsync("categorias", id, ct);

    public Task<bool> DesactivarTalleAsync(int id, CancellationToken ct = default)
        => DesactivarEntidadAsync("talles", id, ct);

    public Task<bool> DesactivarColorAsync(int id, CancellationToken ct = default)
        => DesactivarEntidadAsync("colores", id, ct);

    private async Task<bool> DesactivarEntidadAsync(string recurso, int id, CancellationToken ct)
    {
        using var request = ApiHttp.Preparar(HttpMethod.Delete, $"api/admin/{recurso}/{id}", sesion);
        using var respuesta = await http.SendAsync(request, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
        }

        return respuesta.IsSuccessStatusCode;
    }

    // ---------------- Variantes ----------------

    public async Task<VarianteAdminDto?> GuardarVarianteAsync(
        VarianteUpsertRequest request, CancellationToken ct = default)
    {
        var metodo = request.Id == 0 ? HttpMethod.Post : HttpMethod.Put;
        var ruta = request.Id == 0
            ? "api/admin/variantes"
            : $"api/admin/variantes/{request.Id}";

        using var requestHttp = ApiHttp.Preparar(metodo, ruta, sesion, request);
        using var respuesta = await http.SendAsync(requestHttp, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<VarianteAdminDto>(Json, ct);
    }

    // ---------------- Pedidos ----------------

    public async Task<PagedResult<PedidoAdminDto>> GetPedidosAsync(
        string? estado, int page, int pageSize, CancellationToken ct = default)
    {
        var url = $"api/admin/pedidos?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(estado))
        {
            url += $"&estado={Uri.EscapeDataString(estado)}";
        }

        using var request = ApiHttp.Preparar(HttpMethod.Get, url, sesion);
        using var respuesta = await http.SendAsync(request, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return new PagedResult<PedidoAdminDto>();
        }

        return await respuesta.Content.ReadFromJsonAsync<PagedResult<PedidoAdminDto>>(Json, ct)
            ?? new PagedResult<PedidoAdminDto>();
    }

    public async Task<PedidoDto?> GetPedidoAsync(int id, CancellationToken ct = default)
    {
        using var request = ApiHttp.Preparar(HttpMethod.Get, $"api/admin/pedidos/{id}", sesion);
        using var respuesta = await http.SendAsync(request, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        return await respuesta.Content.ReadFromJsonAsync<PedidoDto>(Json, ct);
    }

    public async Task<PedidoDto?> CambiarEstadoPedidoAsync(
        int id, CambiarEstadoPedidoRequest request, CancellationToken ct = default)
    {
        using var requestHttp = ApiHttp.Preparar(
            HttpMethod.Put, $"api/admin/pedidos/{id}/estado", sesion, request);
        using var respuesta = await http.SendAsync(requestHttp, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<PedidoDto>(Json, ct);
    }

    // ---------------- Notas públicas del pedido ----------------

    public async Task<IReadOnlyList<PedidoNotaDto>> GetNotasPedidoAsync(
        int idPedido, CancellationToken ct = default)
    {
        using var request = ApiHttp.Preparar(
            HttpMethod.Get, $"api/admin/pedidos/{idPedido}/notas", sesion);
        using var respuesta = await http.SendAsync(request, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return [];
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<List<PedidoNotaDto>>(Json, ct) ?? [];
    }

    public async Task<PedidoNotaDto?> CrearNotaPedidoAsync(
        int idPedido, CrearNotaPedidoRequest request, CancellationToken ct = default)
    {
        using var requestHttp = ApiHttp.Preparar(
            HttpMethod.Post, $"api/admin/pedidos/{idPedido}/notas", sesion, request);
        using var respuesta = await http.SendAsync(requestHttp, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<PedidoNotaDto>(Json, ct);
    }

    // ---------------- Imágenes ----------------

    /// <summary>
    /// Sube una imagen al producto. El contenido se envía por streaming
    /// (multipart/form-data) sin cargarlo entero en memoria.
    /// </summary>
    public async Task<ProductoImagenDto?> SubirImagenAsync(
        int productoId,
        Stream contenido,
        string nombreArchivo,
        string contentType,
        CancellationToken ct = default)
    {
        using var request = ApiHttp.Preparar(
            HttpMethod.Post, $"api/admin/productos/{productoId}/imagenes", sesion);

        var archivo = new StreamContent(contenido);
        archivo.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var multipart = new MultipartFormDataContent();
        multipart.Add(archivo, "archivo", nombreArchivo);
        request.Content = multipart;

        using var respuesta = await http.SendAsync(request, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        UltimoError = null;
        return await respuesta.Content.ReadFromJsonAsync<ProductoImagenDto>(Json, ct);
    }

    public async Task<bool> GuardarImagenesAsync(
        int productoId, IReadOnlyList<ProductoImagenDto> imagenes, CancellationToken ct = default)
    {
        using var request = ApiHttp.Preparar(
            HttpMethod.Put, $"api/admin/productos/{productoId}/imagenes", sesion, imagenes);
        using var respuesta = await http.SendAsync(request, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
        }

        return respuesta.IsSuccessStatusCode;
    }

    /// <summary>
    /// Reemplaza la lista completa de imágenes vigentes en una sola operación:
    /// envía en multipart la lista final (orden + principal) y los archivos
    /// nuevos. El servidor aplica bajas, altas y orden de forma atómica y
    /// valida el límite sobre la lista final, así que reemplazar imágenes no
    /// consume plazas de las que se están eliminando.
    /// </summary>
    public async Task<IReadOnlyList<ProductoImagenDto>?> ReemplazarImagenesAsync(
        int productoId,
        IReadOnlyList<ImagenFinalDto> imagenes,
        IReadOnlyList<ImagenNuevaDto> nuevas,
        CancellationToken ct = default)
    {
        using var request = ApiHttp.Preparar(
            HttpMethod.Put, $"api/admin/productos/{productoId}/imagenes/lote", sesion);

        using var multipart = new MultipartFormDataContent
        {
            // La lista va como JSON: el orden del array es el orden definitivo.
            { new StringContent(JsonSerializer.Serialize(imagenes, Json), Encoding.UTF8, "application/json"), "imagenes" },
        };

        // Cada archivo se numera (archivo0, archivo1, …) y la lista lo referencia
        // por indiceNueva; así no depende de caracteres especiales del nombre.
        for (var i = 0; i < nuevas.Count; i++)
        {
            var archivo = new ByteArrayContent(nuevas[i].Contenido);
            archivo.Headers.ContentType = new MediaTypeHeaderValue(nuevas[i].ContentType);
            multipart.Add(archivo, $"archivo{i}", nuevas[i].NombreArchivo);
        }

        request.Content = multipart;

        using var respuesta = await http.SendAsync(request, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            UltimoError = await ApiHttp.LeerErrorAsync(respuesta);
            return null;
        }

        UltimoError = null;
        var valor = await respuesta.Content.ReadFromJsonAsync<List<ProductoImagenDto>>(Json, ct);

        return valor ?? [];
    }
}
