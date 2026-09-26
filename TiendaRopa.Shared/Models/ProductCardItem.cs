namespace TiendaRopa.Shared.Models;

/// <summary>
/// Modelo de presentación de una tarjeta de producto.
/// Es una pieza de UI: cuando exista la API se mapeará desde los DTO reales.
/// </summary>
public sealed record ProductCardItem
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required string Category { get; init; }

    /// <summary>Precio formateado para mostrar, por ejemplo "$42.990".</summary>
    public required string Price { get; init; }

    public string? OriginalPrice { get; init; }

    /// <summary>Etiqueta visible: "Nuevo", "-20%", etc. Null = sin etiqueta.</summary>
    public string? Badge { get; init; }

    /// <summary>Tono de la etiqueta (StatusBadge.Tone).</summary>
    public string BadgeTone { get; init; } = "info";

    public string? ImageUrl { get; init; }

    /// <summary>Talles disponibles (muestra visual, vendrá de ProductoVariante).</summary>
    public IReadOnlyList<string> Sizes { get; init; } = [];

    /// <summary>Colores disponibles (muestra visual).</summary>
    public IReadOnlyList<string> Colors { get; init; } = [];

    /// <summary>Precio numérico sólo para ordenamiento de la muestra visual.</summary>
    public decimal SortPrice { get; init; }
}
