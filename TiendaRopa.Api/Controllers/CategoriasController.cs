using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TiendaRopa.Api.Data;
using TiendaRopa.Contracts.Catalogo;

namespace TiendaRopa.Api.Controllers;

/// <summary>Categorías activas del catálogo.</summary>
[ApiController]
[Route("api/categorias")]
[Tags("Catálogo")]
public class CategoriasController(VelvetRoseDbContext db) : ControllerBase
{
    /// <summary>Lista las categorías activas en orden de aparición.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoriaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoriaDto>>> GetCategorias(
        CancellationToken cancellationToken)
    {
        var categorias = await db.Categorias.AsNoTracking()
            .Where(c => c.Activo)
            .OrderBy(c => c.IdCategoria)
            .Select(c => new CategoriaDto
            {
                Id = c.IdCategoria,
                Nombre = c.Nombre,
                Descripcion = c.Descripcion,
            })
            .ToListAsync(cancellationToken);

        return Ok(categorias);
    }
}
