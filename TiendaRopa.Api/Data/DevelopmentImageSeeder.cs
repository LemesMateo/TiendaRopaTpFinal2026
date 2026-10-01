using Microsoft.EntityFrameworkCore;
using TiendaRopa.Api.Entities;

namespace TiendaRopa.Api.Data;

/// <summary>
/// Asigna IMÁGENES DE DESARROLLO a los productos seed (idempotente).
///
/// Son placeholders PNG generados localmente por
/// scripts/gen-product-images.ps1 (activos controlados, sin fotos de terceros).
/// Se guardan con RUTA RELATIVA (/img/productos/…); la API los convierte en
/// URL absolutas según el host de cada petición, de modo que funcionan igual
/// desde Blazor (localhost:5101), MAUI Windows y el emulador Android
/// (10.0.2.2:5100) sin guardar URLs de máquinas en la base.
/// </summary>
public static class DevelopmentImageSeeder
{
    public static async Task SeedAsync(VelvetRoseDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.ProductoImagenes.AnyAsync(cancellationToken))
        {
            return; // Ya hay imágenes (cargadas por la tienda o en pruebas previas)
        }

        var productos = await db.Productos.AsNoTracking()
            .OrderBy(p => p.IdProducto)
            .Take(9)
            .Select(p => new { p.IdProducto })
            .ToListAsync(cancellationToken);

        foreach (var producto in productos)
        {
            // 3 vistas por producto: la primera es la principal (índice único parcial).
            for (var vista = 1; vista <= 3; vista++)
            {
                db.ProductoImagenes.Add(new ProductoImagen
                {
                    IdProducto = producto.IdProducto,
                    UrlImagen = $"/img/productos/p{producto.IdProducto}-{vista}.png",
                    Orden = vista,
                    Principal = vista == 1,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
