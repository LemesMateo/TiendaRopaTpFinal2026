namespace TiendaRopa.Contracts.Catalogo;

/// <summary>Resumen de producto para el listado del catálogo.</summary>
public sealed class ProductoListItemDto
{
    public int Id { get; init; }

    public required string Nombre { get; init; }

    public required string Categoria { get; init; }

    /// <summary>Precio a mostrar: menor precio de variante o precio base.</summary>
    public decimal Precio { get; init; }

    /// <summary>URL de la imagen principal; null cuando el producto no tiene imagen.</summary>
    public string? UrlImagen { get; init; }

    /// <summary>Indica si al menos una variante tiene stock disponible.</summary>
    public bool Disponible { get; init; }
}
