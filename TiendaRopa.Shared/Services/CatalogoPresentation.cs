using System.Globalization;
using TiendaRopa.Contracts.Catalogo;
using TiendaRopa.Shared.Models;

namespace TiendaRopa.Shared.Services;

/// <summary>Mapeo de DTOs del catálogo a modelos de presentación de la UI.</summary>
public static class CatalogoPresentation
{
    private static readonly CultureInfo FormatoArgentino = new("es-AR");

    /// <summary>Precio "$42.990" con separador de miles argentino.</summary>
    public static string FormatearPrecio(decimal precio)
        => "$" + precio.ToString("N0", FormatoArgentino);

    /// <summary>Tarjeta de producto para grillas de catálogo e inicio.</summary>
    public static ProductCardItem ToCardItem(this ProductoListItemDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Nombre,
        Category = dto.Categoria,
        Price = FormatearPrecio(dto.Precio),
        SortPrice = dto.Precio,
        ImageUrl = dto.UrlImagen,
        Badge = dto.Disponible ? null : "Sin stock",
        BadgeTone = dto.Disponible ? "info" : "danger",
    };
}
