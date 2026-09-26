using TiendaRopa.Shared.Models;

namespace TiendaRopa.Shared.Samples;

/// <summary>
/// DATOS DE MUESTRA VISUAL — sólo para maquetar la UI hasta que exista la
/// API REST (ASP.NET Core). No representan inventario real ni stock real.
/// Reemplazar por consumo de servicios cuando el backend esté disponible.
/// </summary>
public static class SampleCatalog
{
    public static readonly IReadOnlyList<string> Categories =
        ["Vestidos", "Blusas", "Pantalones", "Abrigos"];

    public static readonly IReadOnlyList<string> Sizes = ["S", "M", "L"];

    public static readonly IReadOnlyList<string> Colors =
        ["Rosa", "Negro", "Marfil", "Ciruela"];

    public static IReadOnlyList<ProductCardItem> Products { get; } =
    [
        new()
        {
            Id = 1,
            Name = "Vestido Floral Midi",
            Category = "Vestidos",
            Price = "$42.990",
            SortPrice = 42990,
            Badge = "Nuevo",
            BadgeTone = "info",
            Sizes = ["S", "M", "L"],
            Colors = ["Rosa", "Marfil"],
        },
        new()
        {
            Id = 2,
            Name = "Vestido Lavanda",
            Category = "Vestidos",
            Price = "$54.990",
            OriginalPrice = "$64.990",
            SortPrice = 54990,
            Badge = "-15%",
            BadgeTone = "primary",
            Sizes = ["M", "L"],
            Colors = ["Ciruela"],
        },
        new()
        {
            Id = 3,
            Name = "Blusa Pompoir Marfil",
            Category = "Blusas",
            Price = "$28.500",
            SortPrice = 28500,
            Sizes = ["S", "M"],
            Colors = ["Marfil", "Negro"],
        },
        new()
        {
            Id = 4,
            Name = "Blusa Satinada Rosa",
            Category = "Blusas",
            Price = "$31.990",
            SortPrice = 31990,
            Badge = "Nuevo",
            BadgeTone = "info",
            Sizes = ["S", "M", "L"],
            Colors = ["Rosa"],
        },
        new()
        {
            Id = 5,
            Name = "Pantalón Wide Leg",
            Category = "Pantalones",
            Price = "$46.500",
            SortPrice = 46500,
            Sizes = ["S", "M", "L"],
            Colors = ["Negro", "Marfil"],
        },
        new()
        {
            Id = 6,
            Name = "Jeans Clásico Alto",
            Category = "Pantalones",
            Price = "$51.990",
            OriginalPrice = "$59.990",
            SortPrice = 51990,
            Badge = "-13%",
            BadgeTone = "primary",
            Sizes = ["M", "L"],
            Colors = ["Negro"],
        },
        new()
        {
            Id = 7,
            Name = "Abrigo Corte Redondo",
            Category = "Abrigos",
            Price = "$89.990",
            SortPrice = 89990,
            Badge = "Nuevo",
            BadgeTone = "info",
            Sizes = ["M", "L"],
            Colors = ["Ciruela", "Negro"],
        },
        new()
        {
            Id = 8,
            Name = "Campera Acolchada",
            Category = "Abrigos",
            Price = "$76.990",
            SortPrice = 76990,
            Sizes = ["S", "M"],
            Colors = ["Marfil"],
        },
    ];
}
