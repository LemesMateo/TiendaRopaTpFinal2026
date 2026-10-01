namespace TiendaRopa.Contracts.Catalogo;

public sealed class ProductoImagenDto
{
    public int Id { get; init; }

    public required string UrlImagen { get; set; }

    public int Orden { get; init; }

    public bool Principal { get; init; }
}
