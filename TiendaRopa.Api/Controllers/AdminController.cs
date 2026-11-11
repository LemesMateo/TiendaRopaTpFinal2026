using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TiendaRopa.Api.Auth;
using TiendaRopa.Api.Services;
using TiendaRopa.Contracts.Admin;
using TiendaRopa.Contracts.Catalogo;
using TiendaRopa.Contracts.Common;
using TiendaRopa.Contracts.Pedidos;

namespace TiendaRopa.Api.Controllers;

/// <summary>
/// Panel administrativo. Todos los endpoints exigen rol Administrador:
/// el JWT decide, nunca datos enviados por el frontend.
/// </summary>
[ApiController]
[Route("api/admin")]
[Tags("Administración")]
[Authorize(Policy = Roles.SoloAdministrador)]
public class AdminController(AdminService admin, ILogger<AdminController> log) : ControllerBase
{
    private const int MaxPageSize = 50;

    // ---------- Productos ----------

    /// <summary>Listado administrativo de productos (activos e inactivos).</summary>
    [HttpGet("productos")]
    public async Task<ActionResult<PagedResult<ProductoAdminDto>>> GetProductos(
        [FromQuery] string? buscar,
        [FromQuery] bool? activo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Parámetros inválidos",
                Detail = $"page ≥ 1 y pageSize entre 1 y {MaxPageSize}.",
                Status = 400,
            });
        }

        var (items, total) = await admin.GetProductosAsync(buscar, activo, page, pageSize, cancellationToken);
        return Ok(new PagedResult<ProductoAdminDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        });
    }

    [HttpGet("productos/{id:int}")]
    public async Task<ActionResult<ProductoAdminDto>> GetProducto(int id, CancellationToken cancellationToken)
    {
        var producto = await admin.GetProductoAsync(id, cancellationToken);
        return producto is null
            ? NotFound(new ProblemDetails { Title = "Producto no encontrado", Status = 404 })
            : Ok(producto);
    }

    /// <summary>Crea (id = 0) o actualiza un producto.</summary>
    [HttpPost("productos")]
    [HttpPut("productos/{id:int}")]
    public async Task<ActionResult<ProductoAdminDto>> GuardarProducto(
        int? id, ProductoUpsertRequest request, CancellationToken cancellationToken)
    {
        var conId = id is { } idRuta && request.Id == 0
            ? new ProductoUpsertRequest
            {
                Id = idRuta,
                Nombre = request.Nombre,
                IdCategoria = request.IdCategoria,
                Descripcion = request.Descripcion,
                PrecioBase = request.PrecioBase,
                Activo = request.Activo,
            }
            : request;

        var (valor, error, status) = await admin.GuardarProductoAsync(conId, cancellationToken);
        if (valor is null)
        {
            return StatusCode(status, new ProblemDetails
            {
                Title = "No se pudo guardar el producto",
                Detail = error,
                Status = status,
            });
        }

        log.LogInformation("Producto {Id} guardado por administrador", valor.Id);
        return StatusCode(status, valor);
    }

    /// <summary>Baja lógica del producto (conserva historial de pedidos).</summary>
    [HttpDelete("productos/{id:int}")]
    public async Task<IActionResult> DesactivarProducto(int id, CancellationToken cancellationToken)
        => await admin.DesactivarProductoAsync(id, cancellationToken)
            ? NoContent()
            : NotFound(new ProblemDetails { Title = "Producto no encontrado", Status = 404 });

    // ---------- Categorías / Talles / Colores ----------

    [HttpGet("categorias")]
    public async Task<ActionResult<IReadOnlyList<EntidadCatalogoDto>>> GetCategorias(
        [FromQuery] bool? activo, CancellationToken cancellationToken)
        => Ok(await admin.GetCategoriasAsync(activo, cancellationToken));

    [HttpGet("talles")]
    public async Task<ActionResult<IReadOnlyList<EntidadCatalogoDto>>> GetTalles(
        [FromQuery] bool? activo, CancellationToken cancellationToken)
        => Ok(await admin.GetTallesAsync(activo, cancellationToken));

    [HttpGet("colores")]
    public async Task<ActionResult<IReadOnlyList<EntidadCatalogoDto>>> GetColores(
        [FromQuery] bool? activo, CancellationToken cancellationToken)
        => Ok(await admin.GetColoresAsync(activo, cancellationToken));

    [HttpPost("categorias")]
    [HttpPut("categorias/{id:int}")]
    public async Task<ActionResult<EntidadCatalogoDto>> GuardarCategoria(
        int? id, NombreActivoRequest request, CancellationToken cancellationToken)
        => await Guardar(() => admin.GuardarCategoriaAsync(ConId(id, request), cancellationToken));

    [HttpPost("talles")]
    [HttpPut("talles/{id:int}")]
    public async Task<ActionResult<EntidadCatalogoDto>> GuardarTalle(
        int? id, NombreActivoRequest request, CancellationToken cancellationToken)
        => await Guardar(() => admin.GuardarTalleAsync(ConId(id, request), cancellationToken));

    [HttpPost("colores")]
    [HttpPut("colores/{id:int}")]
    public async Task<ActionResult<EntidadCatalogoDto>> GuardarColor(
        int? id, NombreActivoRequest request, CancellationToken cancellationToken)
        => await Guardar(() => admin.GuardarColorAsync(ConId(id, request), cancellationToken));

    [HttpDelete("categorias/{id:int}")]
    public async Task<IActionResult> DesactivarCategoria(int id, CancellationToken ct)
        => await admin.DesactivarCategoriaAsync(id, ct)
            ? NoContent()
            : NotFound(new ProblemDetails { Title = "Categoría no encontrada", Status = 404 });

    [HttpDelete("talles/{id:int}")]
    public async Task<IActionResult> DesactivarTalle(int id, CancellationToken ct)
        => await admin.DesactivarTalleAsync(id, ct)
            ? NoContent()
            : NotFound(new ProblemDetails { Title = "Talle no encontrado", Status = 404 });

    [HttpDelete("colores/{id:int}")]
    public async Task<IActionResult> DesactivarColor(int id, CancellationToken ct)
        => await admin.DesactivarColorAsync(id, ct)
            ? NoContent()
            : NotFound(new ProblemDetails { Title = "Color no encontrado", Status = 404 });

    // ---------- Variantes ----------

    /// <summary>Alta/edición de variante. 409 si duplica producto+talle+color.</summary>
    [HttpPost("variantes")]
    [HttpPut("variantes/{id:int}")]
    public async Task<ActionResult<VarianteAdminDto>> GuardarVariante(
        int? id, VarianteUpsertRequest request, CancellationToken cancellationToken)
    {
        var conId = id is { } idRuta && request.Id == 0
            ? new VarianteUpsertRequest
            {
                Id = idRuta,
                IdProducto = request.IdProducto,
                IdTalle = request.IdTalle,
                IdColor = request.IdColor,
                Stock = request.Stock,
                Precio = request.Precio,
            }
            : request;

        var (valor, error) = await admin.GuardarVarianteAsync(conId, cancellationToken);
        if (valor is null)
        {
            return Conflict(new ProblemDetails
            {
                Title = "No se pudo guardar la variante",
                Detail = error,
                Status = 409,
            });
        }

        return Ok(valor);
    }

    // ---------- Pedidos ----------

    [HttpGet("pedidos")]
    public async Task<ActionResult<PagedResult<PedidoAdminDto>>> GetPedidos(
        [FromQuery] string? estado,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Parámetros inválidos",
                Detail = $"page ≥ 1 y pageSize entre 1 y {MaxPageSize}.",
                Status = 400,
            });
        }

        var (items, total) = await admin.GetPedidosAsync(estado, page, pageSize, cancellationToken);
        return Ok(new PagedResult<PedidoAdminDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        });
    }

    [HttpGet("pedidos/{id:int}")]
    public async Task<ActionResult<PedidoDto>> GetPedido(int id, CancellationToken cancellationToken)
    {
        var resultado = await admin.GetPedidoAsync(id, cancellationToken);
        return resultado is null
            ? NotFound(new ProblemDetails { Title = "Pedido no encontrado", Status = 404 })
            : Ok(resultado);
    }

    /// <summary>Cambio de estado aplicando las transiciones válidas.</summary>
    [HttpPut("pedidos/{id:int}/estado")]
    public async Task<ActionResult<PedidoDto>> CambiarEstado(
        int id, CambiarEstadoPedidoRequest request, CancellationToken cancellationToken)
    {
        var (valor, error, status) = await admin.CambiarEstadoAsync(id, request.Estado, cancellationToken);
        if (valor is null)
        {
            return StatusCode(status, new ProblemDetails
            {
                Title = "Transición no permitida",
                Detail = error,
                Status = status,
            });
        }

        return Ok(valor);
    }

    // ---------- Imágenes ----------

    /// <summary>
    /// Sube una imagen (JPEG/PNG, hasta 5 MB) al producto. El archivo se
    /// escribe en disco por streaming y se valida su tipo y su firma.
    /// </summary>
    [HttpPost("productos/{id:int}/imagenes")]
    [RequestSizeLimit(ProductoImagenes.MaximoBytes + 64 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ProductoImagenDto>> SubirImagen(
        int id, IFormFile? archivo, CancellationToken cancellationToken)
    {
        var (valor, error, status) = await admin.SubirImagenAsync(id, archivo, cancellationToken);

        if (valor is null)
        {
            return StatusCode(status, new ProblemDetails
            {
                Title = "No se pudo subir la imagen",
                Detail = error,
                Status = status,
            });
        }

        return CreatedAtAction(nameof(GetProducto), new { id }, valor);
    }

    /// <summary>Reemplaza la lista de imágenes (orden + principal) de un producto.</summary>
    [HttpPut("productos/{id:int}/imagenes")]
    public async Task<IActionResult> GuardarImagenes(
        int id, IReadOnlyList<ProductoImagenDto> imagenes, CancellationToken cancellationToken)
    {
        var (ok, error, status) = await admin.GuardarImagenesAsync(id, imagenes, cancellationToken);

        if (!ok)
        {
            return StatusCode(status, new ProblemDetails
            {
                Title = "No se pudieron guardar las imágenes",
                Detail = error,
                Status = status,
            });
        }

        return NoContent();
    }

    // ---------- Helpers ----------

    private static NombreActivoRequest ConId(int? id, NombreActivoRequest request) => new()
    {
        Id = id ?? request.Id,
        Nombre = request.Nombre,
        Descripcion = request.Descripcion,
        CodigoHex = request.CodigoHex,
        Activo = request.Activo,
    };

    private async Task<ActionResult<EntidadCatalogoDto>> Guardar(
        Func<Task<EntidadCatalogoDto?>> operacion)
    {
        try
        {
            var entidad = await operacion();
            return entidad is null
                ? NotFound(new ProblemDetails { Title = "Entidad no encontrada", Status = 404 })
                : Ok(entidad);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = ex.Message, Status = 404 });
        }
        catch (DbUpdateException)
        {
            return Conflict(new ProblemDetails
            {
                Title = "No se pudo guardar",
                Detail = "Ya existe una entidad con ese nombre (índice único).",
                Status = 409,
            });
        }
    }
}
