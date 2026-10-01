namespace TiendaRopa.Contracts.Common;

/// <summary>Resultado paginado genérico compartido por API y clientes.</summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>Página actual (base 1).</summary>
    public int Page { get; init; }

    /// <summary>Tamaño de página solicitado.</summary>
    public int PageSize { get; init; }

    /// <summary>Total de elementos que cumplen el filtro.</summary>
    public int TotalItems { get; init; }

    public int TotalPages { get; init; }
}
