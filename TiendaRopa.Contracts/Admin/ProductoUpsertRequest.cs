using System.ComponentModel.DataAnnotations;

namespace TiendaRopa.Contracts.Admin;

/// <summary>Alta/edición de producto (el Id sólo se usa en edición).</summary>
public sealed class ProductoUpsertRequest
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Ingresá el nombre del producto.")]
    [StringLength(200, MinimumLength = 2)]
    public required string Nombre { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Seleccioná una categoría.")]
    public int IdCategoria { get; set; }

    [StringLength(2000)]
    public string? Descripcion { get; set; }

    [Range(0, 99999999, ErrorMessage = "El precio no puede ser negativo.")]
    public decimal PrecioBase { get; set; }

    public bool Activo { get; set; } = true;
}

/// <summary>Alta/edición de categoría, talle o color.</summary>
public sealed class NombreActivoRequest
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Ingresá el nombre.")]
    [StringLength(100, MinimumLength = 1)]
    public required string Nombre { get; set; }

    [StringLength(500)]
    public string? Descripcion { get; set; }

    /// <summary>Sólo aplica a colores (código HEX); ignorado en categorías y talles.</summary>
    [StringLength(7)]
    public string? CodigoHex { get; set; }

    public bool Activo { get; set; } = true;
}

/// <summary>Alta/edición de variante producto + talle + color.</summary>
public sealed class VarianteUpsertRequest
{
    public int Id { get; set; }

    [Range(1, int.MaxValue)]
    public int IdProducto { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Seleccioná un talle.")]
    public int IdTalle { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Seleccioná un color.")]
    public int IdColor { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "El stock no puede ser negativo.")]
    public int Stock { get; set; }

    /// <summary>null = aplicar el precio base del producto.</summary>
    [Range(0, 99999999, ErrorMessage = "El precio no puede ser negativo.")]
    public decimal? Precio { get; set; }
}
