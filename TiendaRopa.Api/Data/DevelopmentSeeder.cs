using Microsoft.EntityFrameworkCore;
using TiendaRopa.Api.Entities;

namespace TiendaRopa.Api.Data;

/// <summary>
/// Carga de DATOS DE DESARROLLO — sólo en el entorno Development.
/// Categorías, talles, colores, estados y productos de prueba con variantes
/// y stock. No contiene imágenes de terceros: los productos quedan sin
/// imagen a propósito (la UI muestra un marcador) hasta tener activos aptos.
/// Idempotente: sólo inserta si la base está vacía de categorías.
/// </summary>
public static class DevelopmentSeeder
{
    public static async Task SeedAsync(VelvetRoseDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);

        if (await db.Categorias.AnyAsync(cancellationToken))
        {
            return; // Ya hay datos (posiblemente cargados por la tienda real)
        }

        // ---- Datos de referencia ----
        var roles = new[]
        {
            new Rol { Nombre = "Administrador", Descripcion = "Gestión de la tienda." },
            new Rol { Nombre = "Cliente", Descripcion = "Compra y consulta de pedidos." },
        };

        var estados = new[]
        {
            new EstadoPedido { Nombre = "Pendiente", Descripcion = "Pedido recibido." },
            new EstadoPedido { Nombre = "Confirmado", Descripcion = "Pago confirmado." },
            new EstadoPedido { Nombre = "Preparado", Descripcion = "Listo para enviar." },
            new EstadoPedido { Nombre = "Entregado", Descripcion = "Entregado al cliente." },
            new EstadoPedido { Nombre = "Cancelado", Descripcion = "Pedido cancelado." },
        };

        var talles = new[] { "S", "M", "L", "XL" }
            .Select(n => new Talle { Nombre = n })
            .ToArray();

        var colores = new[]
        {
            new Color { Nombre = "Rosa", CodigoHex = "#F3A6B6" },
            new Color { Nombre = "Negro", CodigoHex = "#1C1C1E" },
            new Color { Nombre = "Marfil", CodigoHex = "#F5EFE3" },
            new Color { Nombre = "Ciruela", CodigoHex = "#5C2D69" },
        };

        var categorias = new[]
        {
            new Categoria { Nombre = "Vestidos", Descripcion = "Vestidos casual y de evento." },
            new Categoria { Nombre = "Blusas", Descripcion = "Blusas y remeras." },
            new Categoria { Nombre = "Pantalones", Descripcion = "Pantalones y jeans." },
            new Categoria { Nombre = "Abrigos", Descripcion = "Abrigos y camperas." },
        };

        db.AddRange(roles);
        db.AddRange(estados);
        db.AddRange(talles);
        db.AddRange(colores);
        db.AddRange(categorias);
        await db.SaveChangesAsync(cancellationToken);

        // ---- Productos de prueba con variantes (sin imágenes) ----
        Talle Talle(string n) => talles.Single(t => t.Nombre == n);
        Color Color(string n) => colores.Single(c => c.Nombre == n);

        Producto CrearProducto(string categoria, string nombre, string descripcion, decimal precio,
            params (string Talle, string Color, int Stock)[] variantes)
        {
            var producto = new Producto
            {
                Categoria = categorias.Single(c => c.Nombre == categoria),
                Nombre = nombre,
                Descripcion = descripcion,
                PrecioBase = precio,
            };

            foreach (var (talle, color, stock) in variantes)
            {
                producto.Variantes.Add(new ProductoVariante
                {
                    Talle = Talle(talle),
                    Color = Color(color),
                    Stock = stock,
                });
            }

            return producto;
        }

        db.AddRange(
            CrearProducto("Vestidos", "Vestido Rococó Midi",
                "Vestido midi con vuelo y cinturón, tejido liviano para primavera.",
                45990, ("S", "Rosa", 6), ("M", "Rosa", 4), ("L", "Ciruela", 0)),
            CrearProducto("Vestidos", "Vestido Satinado Noche",
                "Vestido de satén con cuello barco, ideal para eventos.",
                62990, ("S", "Negro", 3), ("M", "Negro", 5), ("L", "Negro", 2)),
            CrearProducto("Vestidos", "Vestido Campo Cropped",
                "Vestido corto con encaje, cómodo y fresco.",
                34990, ("S", "Marfil", 0), ("M", "Marfil", 7)),
            CrearProducto("Blusas", "Blusa Pompoir Marfil",
                "Blusa de pompoir con manga bombacha.",
                28500, ("S", "Marfil", 5), ("M", "Marfil", 6), ("L", "Marfil", 3)),
            CrearProducto("Blusas", "Remera Oversize Ciruela",
                "Remera oversize de algodón peinado.",
                19990, ("M", "Ciruela", 8), ("L", "Ciruela", 6), ("XL", "Ciruela", 4)),
            CrearProducto("Pantalones", "Pantalón Wide Leg",
                "Pantalón de pierna anoda con cintura alta.",
                46500, ("S", "Negro", 4), ("M", "Negro", 5), ("L", "Negro", 0)),
            CrearProducto("Pantalones", "Jeans Clásico Alto",
                "Jeans de tiro alto, corte recto, denim con stretch.",
                51990, ("M", "Negro", 6), ("L", "Negro", 4), ("XL", "Negro", 2)),
            CrearProducto("Abrigos", "Campera Acolchada Marfil",
                "Campera acolchada liviana con capucha desmontable.",
                76990, ("S", "Marfil", 3), ("M", "Marfil", 4), ("L", "Ciruela", 3)),
            CrearProducto("Abrigos", "Abrigo Corte Redondo",
                "Abrigo de lana con corte redondo y botones forrados.",
                89990, ("M", "Ciruela", 2), ("L", "Negro", 2), ("XL", "Negro", 0)));

        await db.SaveChangesAsync(cancellationToken);
    }
}
