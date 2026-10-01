namespace TiendaRopa.Contracts.Catalogo;

/// <summary>Ficha completa de un producto activo.</summary>
public sealed class ProductoDetalleDto
{
    public int Id { get; init; }

    public required string Nombre { get; init; }

    public required string Categoria { get; init; }

    public string? Descripcion { get; init; }

    public decimal PrecioBase { get; init; }

    public bool Disponible { get; init; }

    public IReadOnlyList<ProductoImagenDto> Imagenes { get; init; } = [];
}
