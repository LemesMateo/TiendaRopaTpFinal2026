namespace TiendaRopa.Contracts.Catalogo;

/// <summary>Combinación talle + color de un producto, con su stock.</summary>
public sealed class VarianteDto
{
    public int Id { get; init; }

    public required string Talle { get; init; }

    public required string Color { get; init; }

    /// <summary>Código HEX del color (puede ser null si no está informado).</summary>
    public string? CodigoHex { get; init; }

    /// <summary>Stock interno de la variante. Nunca debe quedar negativo.</summary>
    public int Stock { get; init; }

    /// <summary>Precio de la variante; null cuando aplica el precio base.</summary>
    public decimal? Precio { get; init; }

    /// <summary>Stock &gt; 0.</summary>
    public bool Disponible => Stock > 0;
}
