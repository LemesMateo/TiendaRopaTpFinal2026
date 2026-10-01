namespace TiendaRopa.Contracts.Catalogo;

/// <summary>
/// Parámetros de consulta del catálogo. Se usan tanto desde el cliente
/// (Web/MAUI) como desde la API (binding [FromQuery]).
/// Valores de Orden: "relevancia" | "precio-asc" | "precio-desc" | "nombre".
/// </summary>
public sealed class CatalogoQuery
{
    /// <summary>Número de página, base 1. Mínimo 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Elementos por página. Mínimo 1, máximo 50.</summary>
    public int PageSize { get; set; } = 12;

    /// <summary>Filtro por nombre exacto de categoría activa.</summary>
    public string? Categoria { get; set; }

    /// <summary>Búsqueda parcial (nombre de producto o categoría).</summary>
    public string? Buscar { get; set; }

    /// <summary>Filtro por nombre de talle.</summary>
    public string? Talle { get; set; }

    /// <summary>Filtro por nombre de color.</summary>
    public string? Color { get; set; }

    public decimal? PrecioMin { get; set; }

    public decimal? PrecioMax { get; set; }

    /// <summary>Criterio de orden: relevancia | precio-asc | precio-desc | nombre.</summary>
    public string? Orden { get; set; }
}
