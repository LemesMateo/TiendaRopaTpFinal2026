using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using TiendaRopa.Contracts.Catalogo;
using TiendaRopa.Contracts.Common;

namespace TiendaRopa.Shared.Services;

/// <summary>
/// Cliente HTTP del catálogo reutilizable por Blazor Web y MAUI.
/// Requiere un <see cref="HttpClient"/> con BaseAddress configurado en DI.
/// </summary>
public sealed class HttpCatalogoService(HttpClient http) : ICatalogoService
{
    public async Task<PagedResult<ProductoListItemDto>> GetProductosAsync(
        CatalogoQuery query, CancellationToken cancellationToken = default)
    {
        var resultado = await http.GetFromJsonAsync<PagedResult<ProductoListItemDto>>(
            ConstruirUrl("api/productos", query), cancellationToken);

        return resultado ?? new PagedResult<ProductoListItemDto>();
    }

    public async Task<ProductoDetalleDto?> GetProductoAsync(
        int id, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"api/productos/{id}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProductoDetalleDto>(cancellationToken);
    }

    public async Task<IReadOnlyList<CategoriaDto>> GetCategoriasAsync(
        CancellationToken cancellationToken = default)
    {
        return await http.GetFromJsonAsync<List<CategoriaDto>>(
            "api/categorias", cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<VarianteDto>> GetVariantesAsync(
        int productoId, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"api/productos/{productoId}/variantes", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<VarianteDto>>(cancellationToken) ?? [];
    }

    private static string ConstruirUrl(string path, CatalogoQuery query)
    {
        var parametros = new List<KeyValuePair<string, string>>
        {
            new("page", query.Page.ToString(CultureInfo.InvariantCulture)),
            new("pageSize", query.PageSize.ToString(CultureInfo.InvariantCulture)),
        };

        void Agregar(string clave, string? valor)
        {
            if (!string.IsNullOrWhiteSpace(valor))
            {
                parametros.Add(new KeyValuePair<string, string>(clave, valor.Trim()));
            }
        }

        Agregar("categoria", query.Categoria);
        Agregar("buscar", query.Buscar);
        Agregar("talle", query.Talle);
        Agregar("color", query.Color);
        Agregar("orden", query.Orden);
        Agregar("precioMin", query.PrecioMin?.ToString(CultureInfo.InvariantCulture));
        Agregar("precioMax", query.PrecioMax?.ToString(CultureInfo.InvariantCulture));

        var sb = new StringBuilder(path).Append('?');
        for (var i = 0; i < parametros.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('&');
            }

            sb.Append(Uri.EscapeDataString(parametros[i].Key))
              .Append('=')
              .Append(Uri.EscapeDataString(parametros[i].Value));
        }

        return sb.ToString();
    }
}
