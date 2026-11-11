using System.ComponentModel.DataAnnotations;
using TiendaRopa.Contracts.Catalogo;

namespace TiendaRopa.Contracts.Admin;

/// <summary>Resumen de producto para el panel administrativo.</summary>
public sealed class ProductoAdminDto
{
    public int Id { get; init; }

    public required string Nombre { get; init; }

    public int IdCategoria { get; init; }

    public required string Categoria { get; init; }

    public string? Descripcion { get; init; }

    public decimal PrecioBase { get; init; }

    public bool Activo { get; init; }

    /// <summary>Suma del stock de todas las variantes.</summary>
    public int StockTotal { get; init; }

    public int CantidadVariantes { get; init; }

    /// <summary>Relativa a la API (/img/…); el cliente la resuelve con su BaseAddress.</summary>
    public string? UrlImagen { get; set; }

    /// <summary>Imágenes del producto (la principal primero).</summary>
    public IReadOnlyList<ProductoImagenDto> Imagenes { get; init; } = [];

    public IReadOnlyList<VarianteAdminDto> Variantes { get; init; } = [];
}

/// <summary>Variante con su stock y precio.</summary>
public sealed class VarianteAdminDto
{
    public int Id { get; init; }

    public int IdTalle { get; init; }

    public required string Talle { get; init; }

    public int IdColor { get; init; }

    public required string Color { get; init; }

    public string? CodigoHex { get; init; }

    public int Stock { get; init; }

    public decimal? Precio { get; init; }
}
