namespace TiendaRopa.Contracts.Catalogo;

public sealed class CategoriaDto
{
    public int Id { get; init; }

    public required string Nombre { get; init; }

    public string? Descripcion { get; init; }
}
