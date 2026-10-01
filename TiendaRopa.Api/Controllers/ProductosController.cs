using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TiendaRopa.Api.Data;
using TiendaRopa.Api.Entities;
using TiendaRopa.Contracts.Catalogo;
using TiendaRopa.Contracts.Common;

namespace TiendaRopa.Api.Controllers;

/// <summary>Lectura del catálogo: productos, detalle y variantes.</summary>
[ApiController]
[Route("api/productos")]
[Tags("Catálogo")]
public class ProductosController(VelvetRoseDbContext db) : ControllerBase
{
    private const int MaxPageSize = 50;

    private static readonly string[] OrdenesValidos =
        ["relevancia", "precio-asc", "precio-desc", "nombre"];

    /// <summary>
    /// Precio a mostrar: menor precio de variante (si tiene) o precio base.
    /// Declarado como expresión para que EF Core lo traduzca a SQL.
    /// </summary>
    private static readonly Expression<Func<Producto, decimal>> PrecioAMostrar =
        p => p.Variantes
            .Where(v => v.Precio != null)
            .Min(v => (decimal?)v.Precio)
        ?? p.PrecioBase;

    /// <summary>Lista productos activos con paginación, filtros y orden.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ProductoListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ProductoListItemDto>>> GetProductos(
        [FromQuery] CatalogoQuery query, CancellationToken cancellationToken)
    {
        var errores = new Dictionary<string, string[]>();

        if (query.Page < 1)
        {
            errores["page"] = ["Debe ser mayor o igual a 1."];
        }

        if (query.PageSize < 1 || query.PageSize > MaxPageSize)
        {
            errores["pageSize"] = [$"Debe estar entre 1 y {MaxPageSize}."];
        }

        if (query.PrecioMin is < 0 || query.PrecioMax is < 0)
        {
            errores["precio"] = ["Los precios no pueden ser negativos."];
        }
        else if (query.PrecioMin is not null && query.PrecioMax is not null && query.PrecioMin > query.PrecioMax)
        {
            errores["precio"] = ["PrecioMin no puede ser mayor que PrecioMax."];
        }

        var orden = string.IsNullOrWhiteSpace(query.Orden)
            ? "relevancia"
            : query.Orden!.Trim().ToLowerInvariant();

        if (!OrdenesValidos.Contains(orden))
        {
            errores["orden"] = [$"Valores permitidos: {string.Join(", ", OrdenesValidos)}."];
        }

        if (errores.Count > 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Parámetros de consulta inválidos.",
                Detail = "Revisá los filtros indicados.",
                Status = StatusCodes.Status400BadRequest,
                Extensions = { ["errors"] = errores },
            });
        }

        var productos = db.Productos.AsNoTracking()
            .Where(p => p.Activo && p.Categoria!.Activo);

        if (!string.IsNullOrWhiteSpace(query.Categoria))
        {
            var categoria = query.Categoria.Trim();
            productos = productos.Where(p => p.Categoria!.Nombre == categoria);
        }

        if (!string.IsNullOrWhiteSpace(query.Buscar))
        {
            var texto = query.Buscar.Trim();
            productos = productos.Where(p =>
                p.Nombre.Contains(texto) || p.Categoria!.Nombre.Contains(texto));
        }

        if (!string.IsNullOrWhiteSpace(query.Talle))
        {
            var talle = query.Talle.Trim();
            productos = productos.Where(p => p.Variantes.Any(v => v.Talle!.Nombre == talle));
        }

        if (!string.IsNullOrWhiteSpace(query.Color))
        {
            var color = query.Color.Trim();
            productos = productos.Where(p => p.Variantes.Any(v => v.Color!.Nombre == color));
        }

        if (query.PrecioMin is { } min)
        {
            productos = productos.Where(p =>
                (p.Variantes.Where(v => v.Precio != null).Min(v => (decimal?)v.Precio) ?? p.PrecioBase) >= min);
        }

        if (query.PrecioMax is { } max)
        {
            productos = productos.Where(p =>
                (p.Variantes.Where(v => v.Precio != null).Min(v => (decimal?)v.Precio) ?? p.PrecioBase) <= max);
        }

        var totalItems = await productos.CountAsync(cancellationToken);

        IOrderedQueryable<Producto> ordenados = orden switch
        {
            "precio-asc" => productos.OrderBy(p =>
                p.Variantes.Where(v => v.Precio != null).Min(v => (decimal?)v.Precio) ?? p.PrecioBase),
            "precio-desc" => productos.OrderByDescending(p =>
                p.Variantes.Where(v => v.Precio != null).Min(v => (decimal?)v.Precio) ?? p.PrecioBase),
            "nombre" => productos.OrderBy(p => p.Nombre),
            // "relevancia": más recientes primero
            _ => productos.OrderByDescending(p => p.IdProducto),
        };

        var pagina = await ordenados
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => new ProductoListItemDto
            {
                Id = p.IdProducto,
                Nombre = p.Nombre,
                Categoria = p.Categoria!.Nombre,
                Precio = p.Variantes
                    .Where(v => v.Precio != null)
                    .Min(v => (decimal?)v.Precio)
                ?? p.PrecioBase,
                UrlImagen = p.Imagenes
                    .Where(i => i.Principal)
                    .Select(i => i.UrlImagen)
                    .FirstOrDefault()
                    ?? p.Imagenes
                        .OrderBy(i => i.Orden)
                        .Select(i => i.UrlImagen)
                        .FirstOrDefault(),
                Disponible = p.Variantes.Any(v => v.Stock > 0),
            })
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<ProductoListItemDto>
        {
            Items = pagina,
            Page = query.Page,
            PageSize = query.PageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)query.PageSize),
        });
    }

    /// <summary>Ficha de un producto activo. 404 si no existe o está inactivo.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<ProductoDetalleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductoDetalleDto>> GetProducto(
        int id, CancellationToken cancellationToken)
    {
        var producto = await db.Productos.AsNoTracking()
            .Where(p => p.IdProducto == id && p.Activo)
            .Select(p => new ProductoDetalleDto
            {
                Id = p.IdProducto,
                Nombre = p.Nombre,
                Categoria = p.Categoria!.Nombre,
                Descripcion = p.Descripcion,
                PrecioBase = p.PrecioBase,
                Disponible = p.Variantes.Any(v => v.Stock > 0),
                Imagenes = p.Imagenes
                    .OrderBy(i => i.Orden)
                    .Select(i => new ProductoImagenDto
                    {
                        Id = i.IdProductoImagen,
                        UrlImagen = i.UrlImagen,
                        Orden = i.Orden,
                        Principal = i.Principal,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (producto is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Producto no encontrado",
                Detail = $"No existe un producto activo con id {id}.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        return Ok(producto);
    }

    /// <summary>Variantes (talle, color, stock, disponibilidad) de un producto activo.</summary>
    [HttpGet("{id:int}/variantes")]
    [ProducesResponseType<IReadOnlyList<VarianteDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<VarianteDto>>> GetVariantes(
        int id, CancellationToken cancellationToken)
    {
        var existe = await db.Productos.AsNoTracking()
            .AnyAsync(p => p.IdProducto == id && p.Activo, cancellationToken);

        if (!existe)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Producto no encontrado",
                Detail = $"No existe un producto activo con id {id}.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        var variantes = await db.ProductoVariantes.AsNoTracking()
            .Where(v => v.IdProducto == id)
            .OrderBy(v => v.IdTalle)
            .ThenBy(v => v.IdColor)
            .Select(v => new VarianteDto
            {
                Id = v.IdProductoVariante,
                Talle = v.Talle!.Nombre,
                Color = v.Color!.Nombre,
                CodigoHex = v.Color.CodigoHex,
                Stock = v.Stock,
                Precio = v.Precio,
            })
            .ToListAsync(cancellationToken);

        return Ok(variantes);
    }
}
