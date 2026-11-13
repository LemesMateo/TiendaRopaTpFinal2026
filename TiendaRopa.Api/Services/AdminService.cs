using Microsoft.EntityFrameworkCore;
using TiendaRopa.Api.Data;
using TiendaRopa.Api.Entities;
using TiendaRopa.Contracts.Admin;
using TiendaRopa.Contracts.Catalogo;
using TiendaRopa.Contracts.Pedidos;

namespace TiendaRopa.Api.Services;

/// <summary>
/// Operaciones administrativas de catálogo y pedidos.
/// Baja lógica para todo lo que pueda tener historial (productos, categorías,
/// talles, colores): nunca se borran filas referenciadas por pedidos.
/// </summary>
public sealed class AdminService(VelvetRoseDbContext db, PedidoService pedidos, IWebHostEnvironment entorno)
{
    // ---------------- Productos ----------------

    public async Task<(List<ProductoAdminDto> Items, int Total)> GetProductosAsync(
        string? buscar, bool? activo, int page, int pageSize, CancellationToken cancellationToken)
    {
        var q = db.Productos.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var texto = buscar.Trim();
            q = q.Where(p => p.Nombre.Contains(texto) || p.Categoria!.Nombre.Contains(texto));
        }

        if (activo is { } activoValor)
        {
            q = q.Where(p => p.Activo == activoValor);
        }

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderBy(p => p.Nombre)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductoAdminDto
            {
                Id = p.IdProducto,
                Nombre = p.Nombre,
                IdCategoria = p.IdCategoria,
                Categoria = p.Categoria!.Nombre,
                Descripcion = p.Descripcion,
                PrecioBase = p.PrecioBase,
                Activo = p.Activo,
                StockTotal = p.Variantes.Sum(v => v.Stock),
                CantidadVariantes = p.Variantes.Count,
                UrlImagen = p.Imagenes
                    .Where(i => i.Principal)
                    .Select(i => i.UrlImagen)
                    .FirstOrDefault()
                    ?? p.Imagenes.OrderBy(i => i.Orden).Select(i => i.UrlImagen).FirstOrDefault(),
                Imagenes = p.Imagenes
                    .OrderBy(i => i.Principal).ThenBy(i => i.Orden)
                    .Select(i => new ProductoImagenDto
                    {
                        Id = i.IdProductoImagen,
                        UrlImagen = i.UrlImagen,
                        Orden = i.Orden,
                        Principal = i.Principal,
                    })
                    .ToList(),
                Variantes = p.Variantes
                    .OrderBy(v => v.IdTalle).ThenBy(v => v.IdColor)
                    .Select(v => new VarianteAdminDto
                    {
                        Id = v.IdProductoVariante,
                        IdTalle = v.IdTalle,
                        Talle = v.Talle!.Nombre,
                        IdColor = v.IdColor,
                        Color = v.Color!.Nombre,
                        CodigoHex = v.Color.CodigoHex,
                        Stock = v.Stock,
                        Precio = v.Precio,
                    })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<ProductoAdminDto?> GetProductoAsync(int id, CancellationToken cancellationToken)
    {
        var detalle = await db.Productos.AsNoTracking()
            .Where(p => p.IdProducto == id)
            .Select(p => new ProductoAdminDto
            {
                Id = p.IdProducto,
                Nombre = p.Nombre,
                IdCategoria = p.IdCategoria,
                Categoria = p.Categoria!.Nombre,
                Descripcion = p.Descripcion,
                PrecioBase = p.PrecioBase,
                Activo = p.Activo,
                StockTotal = p.Variantes.Sum(v => v.Stock),
                CantidadVariantes = p.Variantes.Count,
                UrlImagen = p.Imagenes
                    .Where(i => i.Principal)
                    .Select(i => i.UrlImagen)
                    .FirstOrDefault()
                    ?? p.Imagenes.OrderBy(i => i.Orden).Select(i => i.UrlImagen).FirstOrDefault(),
                Imagenes = p.Imagenes
                    .OrderBy(i => i.Principal).ThenBy(i => i.Orden)
                    .Select(i => new ProductoImagenDto
                    {
                        Id = i.IdProductoImagen,
                        UrlImagen = i.UrlImagen,
                        Orden = i.Orden,
                        Principal = i.Principal,
                    })
                    .ToList(),
                Variantes = p.Variantes
                    .OrderBy(v => v.IdTalle).ThenBy(v => v.IdColor)
                    .Select(v => new VarianteAdminDto
                    {
                        Id = v.IdProductoVariante,
                        IdTalle = v.IdTalle,
                        Talle = v.Talle!.Nombre,
                        IdColor = v.IdColor,
                        Color = v.Color!.Nombre,
                        CodigoHex = v.Color.CodigoHex,
                        Stock = v.Stock,
                        Precio = v.Precio,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return detalle;
    }

    public async Task<(ProductoAdminDto? Valor, string? Error, int Status)> GuardarProductoAsync(
        ProductoUpsertRequest request, CancellationToken cancellationToken)
    {
        var categoriaExiste = await db.Categorias
            .AnyAsync(c => c.IdCategoria == request.IdCategoria, cancellationToken);
        if (!categoriaExiste)
        {
            return (null, "La categoría indicada no existe.", 400);
        }

        Producto producto;

        if (request.Id == 0)
        {
            producto = new Producto
            {
                Nombre = request.Nombre.Trim(),
                IdCategoria = request.IdCategoria,
                Descripcion = request.Descripcion,
                PrecioBase = request.PrecioBase,
                Activo = request.Activo,
            };
            db.Productos.Add(producto);
        }
        else
        {
            producto = await db.Productos.FirstOrDefaultAsync(
                p => p.IdProducto == request.Id, cancellationToken)
                ?? throw new KeyNotFoundException($"No existe el producto {request.Id}.");

            producto.Nombre = request.Nombre.Trim();
            producto.IdCategoria = request.IdCategoria;
            producto.Descripcion = request.Descripcion;
            producto.PrecioBase = request.PrecioBase;
            producto.Activo = request.Activo;
        }

        await db.SaveChangesAsync(cancellationToken);
        var dto = await GetProductoAsync(producto.IdProducto, cancellationToken);
        return (dto, null, request.Id == 0 ? 201 : 200);
    }

    /// <summary>Baja lógica: conserva variantes y vínculos con pedidos históricos.</summary>
    public async Task<bool> DesactivarProductoAsync(int id, CancellationToken cancellationToken)
    {
        var producto = await db.Productos.FirstOrDefaultAsync(
            p => p.IdProducto == id, cancellationToken);

        if (producto is null)
        {
            return false;
        }

        producto.Activo = false;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ---------------- Categorías / Talles / Colores ----------------

    public Task<List<EntidadCatalogoDto>> GetCategoriasAsync(bool? activo, CancellationToken ct) =>
        db.Categorias.AsNoTracking()
            .Where(c => activo == null || c.Activo == activo)
            .OrderBy(c => c.Nombre)
            .Select(c => new EntidadCatalogoDto
            {
                Id = c.IdCategoria,
                Nombre = c.Nombre,
                Descripcion = c.Descripcion,
                Activo = c.Activo,
                EnUso = c.Productos.Count(p => p.Activo),
            })
            .ToListAsync(ct);

    public Task<List<EntidadCatalogoDto>> GetTallesAsync(bool? activo, CancellationToken ct) =>
        db.Talles.AsNoTracking()
            .Where(t => activo == null || t.Activo == activo)
            .OrderBy(t => t.Nombre)
            .Select(t => new EntidadCatalogoDto
            {
                Id = t.IdTalle,
                Nombre = t.Nombre,
                Descripcion = t.Descripcion,
                Activo = t.Activo,
                EnUso = t.Variantes.Count,
            })
            .ToListAsync(ct);

    public Task<List<EntidadCatalogoDto>> GetColoresAsync(bool? activo, CancellationToken ct) =>
        db.Colores.AsNoTracking()
            .Where(c => activo == null || c.Activo == activo)
            .OrderBy(c => c.Nombre)
            .Select(c => new EntidadCatalogoDto
            {
                Id = c.IdColor,
                Nombre = c.Nombre,
                Descripcion = null,
                CodigoHex = c.CodigoHex,
                Activo = c.Activo,
                EnUso = c.Variantes.Count,
            })
            .ToListAsync(ct);

    public async Task<EntidadCatalogoDto?> GuardarCategoriaAsync(NombreActivoRequest request, CancellationToken ct)
    {
        var entidad = request.Id == 0
            ? new Categoria { Nombre = request.Nombre.Trim(), Descripcion = request.Descripcion, Activo = request.Activo }
            : await db.Categorias.FirstOrDefaultAsync(c => c.IdCategoria == request.Id, ct)
                ?? throw new KeyNotFoundException($"No existe la categoría {request.Id}.");

        if (request.Id != 0)
        {
            entidad.Nombre = request.Nombre.Trim();
            entidad.Descripcion = request.Descripcion;
            entidad.Activo = request.Activo;
        }
        else
        {
            db.Categorias.Add(entidad);
        }

        await db.SaveChangesAsync(ct);
        return new EntidadCatalogoDto
        {
            Id = entidad.IdCategoria,
            Nombre = entidad.Nombre,
            Descripcion = entidad.Descripcion,
            Activo = entidad.Activo,
        };
    }

    public async Task<EntidadCatalogoDto?> GuardarTalleAsync(NombreActivoRequest request, CancellationToken ct)
    {
        var entidad = request.Id == 0
            ? new Talle { Nombre = request.Nombre.Trim(), Descripcion = request.Descripcion, Activo = request.Activo }
            : await db.Talles.FirstOrDefaultAsync(t => t.IdTalle == request.Id, ct)
                ?? throw new KeyNotFoundException($"No existe el talle {request.Id}.");

        if (request.Id != 0)
        {
            entidad.Nombre = request.Nombre.Trim();
            entidad.Descripcion = request.Descripcion;
            entidad.Activo = request.Activo;
        }
        else
        {
            db.Talles.Add(entidad);
        }

        await db.SaveChangesAsync(ct);
        return new EntidadCatalogoDto
        {
            Id = entidad.IdTalle,
            Nombre = entidad.Nombre,
            Descripcion = entidad.Descripcion,
            Activo = entidad.Activo,
        };
    }

    public async Task<EntidadCatalogoDto?> GuardarColorAsync(NombreActivoRequest request, CancellationToken ct)
    {
        var entidad = request.Id == 0
            ? new Color { Nombre = request.Nombre.Trim(), CodigoHex = request.CodigoHex, Activo = request.Activo }
            : await db.Colores.FirstOrDefaultAsync(c => c.IdColor == request.Id, ct)
                ?? throw new KeyNotFoundException($"No existe el color {request.Id}.");

        if (request.Id != 0)
        {
            entidad.Nombre = request.Nombre.Trim();
            entidad.CodigoHex = request.CodigoHex;
            entidad.Activo = request.Activo;
        }
        else
        {
            db.Colores.Add(entidad);
        }

        await db.SaveChangesAsync(ct);
        return new EntidadCatalogoDto
        {
            Id = entidad.IdColor,
            Nombre = entidad.Nombre,
            CodigoHex = entidad.CodigoHex,
            Activo = entidad.Activo,
        };
    }

    public async Task<bool> DesactivarCategoriaAsync(int id, CancellationToken ct)
    {
        var entidad = await db.Categorias.FirstOrDefaultAsync(c => c.IdCategoria == id, ct);
        if (entidad is null) return false;
        entidad.Activo = false;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DesactivarTalleAsync(int id, CancellationToken ct)
    {
        var entidad = await db.Talles.FirstOrDefaultAsync(t => t.IdTalle == id, ct);
        if (entidad is null) return false;
        entidad.Activo = false;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DesactivarColorAsync(int id, CancellationToken ct)
    {
        var entidad = await db.Colores.FirstOrDefaultAsync(c => c.IdColor == id, ct);
        if (entidad is null) return false;
        entidad.Activo = false;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---------------- Variantes ----------------

    public async Task<(VarianteAdminDto? Valor, string? Error)> GuardarVarianteAsync(
        VarianteUpsertRequest request, CancellationToken ct)
    {
        var productoExiste = await db.Productos.AnyAsync(p => p.IdProducto == request.IdProducto, ct);
        var talleExiste = await db.Talles.AnyAsync(t => t.IdTalle == request.IdTalle && t.Activo, ct);
        var colorExiste = await db.Colores.AnyAsync(c => c.IdColor == request.IdColor && c.Activo, ct);

        if (!productoExiste || !talleExiste || !colorExiste)
        {
            return (null, "El producto, talle o color indicado no existe.");
        }

        if (request.Stock < 0)
        {
            return (null, "El stock no puede ser negativo.");
        }

        // Regla única: no se admite duplicar producto + talle + color.
        var duplicada = await db.ProductoVariantes.AsNoTracking()
            .Where(v => v.IdProducto == request.IdProducto
                && v.IdTalle == request.IdTalle
                && v.IdColor == request.IdColor
                && v.IdProductoVariante != request.Id)
            .FirstOrDefaultAsync(ct);

        if (duplicada is not null)
        {
            return (null, "Ya existe una variante con ese producto, talle y color.");
        }

        ProductoVariante variante;

        if (request.Id == 0)
        {
            variante = new ProductoVariante
            {
                IdProducto = request.IdProducto,
                IdTalle = request.IdTalle,
                IdColor = request.IdColor,
                Stock = request.Stock,
                Precio = request.Precio,
            };
            db.ProductoVariantes.Add(variante);
        }
        else
        {
            variante = await db.ProductoVariantes.FirstOrDefaultAsync(
                    v => v.IdProductoVariante == request.Id, ct)
                ?? throw new KeyNotFoundException($"No existe la variante {request.Id}.");

            // Mantener coherencia si cambió el producto de la variante.
            variante.IdProducto = request.IdProducto;
            variante.IdTalle = request.IdTalle;
            variante.IdColor = request.IdColor;
            variante.Stock = request.Stock;
            variante.Precio = request.Precio;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (EsViolacionUnicaOVarianteNegativa(ex))
        {
            return (null, "No se pudo guardar: variante duplicada o stock inválido.");
        }

        var dto = await db.ProductoVariantes.AsNoTracking()
            .Where(v => v.IdProductoVariante == variante.IdProductoVariante)
            .Select(v => new VarianteAdminDto
            {
                Id = v.IdProductoVariante,
                IdTalle = v.IdTalle,
                Talle = v.Talle!.Nombre,
                IdColor = v.IdColor,
                Color = v.Color!.Nombre,
                CodigoHex = v.Color.CodigoHex,
                Stock = v.Stock,
                Precio = v.Precio,
            })
            .FirstAsync(ct);

        return (dto, null);
    }

    private static bool EsViolacionUnicaOVarianteNegativa(DbUpdateException ex)
        => ex.InnerException is not null
           && (ex.InnerException.Message.Contains("CK_ProductoVariante_Stock_NoNegativo")
               || ex.InnerException.Message.Contains("IX_ProductoVariante")
               || ex.InnerException.Message.Contains("duplicate key"));

    // ---------------- Pedidos ----------------

    public async Task<(List<PedidoAdminDto> Items, int Total)> GetPedidosAsync(
        string? estado, int page, int pageSize, CancellationToken ct)
    {
        var q = db.Pedidos.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(estado))
        {
            var nombre = estado.Trim();
            q = q.Where(p => p.EstadoPedido!.Nombre == nombre);
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(p => p.FechaPedido)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PedidoAdminDto
            {
                Id = p.IdPedido,
                FechaPedido = p.FechaPedido,
                Estado = p.EstadoPedido!.Nombre,
                Total = p.Total,
                Cliente = p.Cliente!.Apellido + ", " + p.Cliente.Nombre,
                Email = p.Cliente.Usuario!.Email,
                CantidadItems = p.Detalles.Count,
            })
            .ToListAsync(ct);

        foreach (var item in items)
        {
            item.TransicionesPermitidas = PedidoService.TransicionesPermitidas(item.Estado);
        }

        return (items, total);
    }

    public Task<PedidoDto?> GetPedidoAsync(int idPedido, CancellationToken cancellationToken)
        => pedidos.GetAsync(idPedido, cancellationToken);

    public async Task<(PedidoDto? Valor, string? Error, int Status)> CambiarEstadoAsync(
        int idPedido, string estadoDestino, CancellationToken ct)
    {
        var pedido = await db.Pedidos.FirstOrDefaultAsync(p => p.IdPedido == idPedido, ct);
        if (pedido is null)
        {
            return (null, "Pedido no encontrado.", 404);
        }

        var estadoActual = await db.EstadosPedido.AsNoTracking()
            .Where(e => e.IdEstadoPedido == pedido.IdEstadoPedido)
            .Select(e => e.Nombre)
            .FirstOrDefaultAsync(ct);

        if (!PedidoService.TransicionesPermitidas(estadoActual ?? string.Empty)
                .Contains(estadoDestino, StringComparer.OrdinalIgnoreCase))
        {
            return (null,
                $"No se puede pasar de \"{estadoActual}\" a \"{estadoDestino}\". " +
                $"Permitidos: {string.Join(", ", PedidoService.TransicionesPermitidas(estadoActual ?? string.Empty))}.",
                409);
        }

        if (string.Equals(estadoDestino, "Cancelado", StringComparison.OrdinalIgnoreCase))
        {
            // Reutiliza la cancelación transaccional (restock + protección doble restitución).
            var resultado = await pedidos.CancelarAsync(idPedido, 0, esAdministrador: true, ct);
            return resultado.Valor is not null
                ? (resultado.Valor, null, 200)
                : (null, resultado.Error, resultado.StatusCode);
        }

        var idNuevo = await db.EstadosPedido.AsNoTracking()
            .Where(e => e.Nombre == estadoDestino)
            .Select(e => e.IdEstadoPedido)
            .FirstAsync(ct);

        pedido.IdEstadoPedido = idNuevo;
        await db.SaveChangesAsync(ct);

        return (await pedidos.MapearAsync(idPedido, ct), null, 200);
    }

    // ---------------- Notas públicas del pedido ----------------

    /// <summary>
    /// Crea una nota de estado pública para un pedido. La ve el cliente en
    /// "Mis pedidos" y el chatbot la usa para responder consultas de envío.
    /// </summary>
    public async Task<(PedidoNotaDto? Valor, string? Error, int Status)> CrearNotaPedidoAsync(
        int idPedido, CrearNotaPedidoRequest request, CancellationToken ct)
    {
        var pedidoExiste = await db.Pedidos
            .AnyAsync(p => p.IdPedido == idPedido, ct);

        if (!pedidoExiste)
        {
            return (null, "Pedido no encontrado.", 404);
        }

        var nota = new PedidoNota
        {
            IdPedido = idPedido,
            FechaActualizacion = DateTime.UtcNow,
            Nota = request.Nota.Trim(),
            Empresa = string.IsNullOrWhiteSpace(request.Empresa) ? null : request.Empresa.Trim(),
            CodigoSeguimiento = string.IsNullOrWhiteSpace(request.CodigoSeguimiento)
                ? null
                : request.CodigoSeguimiento.Trim(),
        };

        db.PedidoNotas.Add(nota);
        await db.SaveChangesAsync(ct);

        return (new PedidoNotaDto
        {
            Id = nota.IdPedidoNota,
            FechaActualizacion = nota.FechaActualizacion,
            Nota = nota.Nota,
            Empresa = nota.Empresa,
            CodigoSeguimiento = nota.CodigoSeguimiento,
        }, null, 201);
    }

    /// <summary>Notas públicas de un pedido (historial, más reciente primero).</summary>
    public async Task<List<PedidoNotaDto>> GetNotasPedidoAsync(int idPedido, CancellationToken ct)
    {
        return await db.PedidoNotas.AsNoTracking()
            .Where(n => n.IdPedido == idPedido)
            .OrderByDescending(n => n.FechaActualizacion)
            .Select(n => new PedidoNotaDto
            {
                Id = n.IdPedidoNota,
                FechaActualizacion = n.FechaActualizacion,
                Nota = n.Nota,
                Empresa = n.Empresa,
                CodigoSeguimiento = n.CodigoSeguimiento,
            })
            .ToListAsync(ct);
    }

    // ---------------- Imágenes ----------------

    /// <summary>Carpeta donde la API guarda las imágenes de producto.</summary>
    private string CarpetaImagenes =>
        Path.Combine(entorno.WebRootPath ?? Path.Combine(entorno.ContentRootPath, "wwwroot"), "img", "productos");

    /// <summary>
    /// Sube una imagen al producto: valida tipo, tamaño y límite por producto,
    /// la escribe en disco por streaming (sin cargarla entera en memoria) y
    /// registra la fila con su ruta relativa.
    /// </summary>
    public async Task<(ProductoImagenDto? Valor, string? Error, int Status)> SubirImagenAsync(
        int productoId, IFormFile? archivo, CancellationToken ct)
    {
        var producto = await db.Productos
            .Include(p => p.Imagenes)
            .FirstOrDefaultAsync(p => p.IdProducto == productoId, ct);

        if (producto is null)
        {
            return (null, "Producto no encontrado.", 404);
        }

        if (archivo is null || archivo.Length == 0)
        {
            return (null, "No se recibió ningún archivo.", 400);
        }

        if (producto.Imagenes.Count >= ProductoImagenes.MaximoPorProducto)
        {
            return (null,
                $"El producto ya tiene el máximo de {ProductoImagenes.MaximoPorProducto} imágenes.",
                400);
        }

        if (archivo.Length > ProductoImagenes.MaximoBytes)
        {
            return (null,
                $"La imagen supera el máximo de {ProductoImagenes.MaximoBytes / (1024 * 1024)} MB.",
                400);
        }

        var contentType = archivo.ContentType;
        if (!ProductoImagenes.EsTipoPermitido(contentType))
        {
            return (null, "Sólo se aceptan imágenes JPEG o PNG.", 400);
        }

        var extension = ProductoImagenes.ExtensionPorTipo(contentType);
        if (extension is null)
        {
            return (null, "Sólo se aceptan imágenes JPEG o PNG.", 400);
        }

        // Se copia el archivo a disco por streaming y se valida la firma
        // (los primeros bytes) para no confiar sólo en el Content-Type.
        var nombreArchivo = $"p{productoId}-{Guid.NewGuid():N}.{extension}";
        var ruta = Path.Combine(CarpetaImagenes, nombreArchivo);

        Directory.CreateDirectory(CarpetaImagenes);

        long escritos;
        try
        {
            await using (var origen = archivo.OpenReadStream())
            await using (var destino = new FileStream(ruta, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await origen.CopyToAsync(destino, ct);
            }

            escritos = new FileInfo(ruta).Length;
        }
        catch (IOException)
        {
            return (null, "No se pudo guardar la imagen en el servidor.", 500);
        }

        if (escritos != archivo.Length)
        {
            TryEliminarArchivo(ruta);
            return (null, "La imagen se subió incompleta. Volvé a intentarlo.", 400);
        }

        // Validación de la firma sobre el archivo ya escrito.
        // La lectura se cierra ANTES de borrar el archivo: en Windows no se
        // puede eliminar un archivo con el stream abierto.
        bool firmaValida;
        await using (var stream = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var cabecera = new byte[8];
            var leidos = await stream.ReadAsync(cabecera, ct);
            firmaValida = ProductoImagenes.TieneFirmaValida(cabecera.AsSpan(0, leidos));
        }

        if (!firmaValida)
        {
            TryEliminarArchivo(ruta);
            return (null, "El archivo no es una imagen JPEG o PNG válida.", 400);
        }

        var nueva = new ProductoImagen
        {
            IdProducto = productoId,
            UrlImagen = $"{ProductoImagenes.PrefijoRuta}{nombreArchivo}",
            Orden = producto.Imagenes.Count + 1,
            Principal = producto.Imagenes.Count == 0,
        };

        db.ProductoImagenes.Add(nueva);
        await db.SaveChangesAsync(ct);

        return (new ProductoImagenDto
        {
            Id = nueva.IdProductoImagen,
            UrlImagen = nueva.UrlImagen,
            Orden = nueva.Orden,
            Principal = nueva.Principal,
        }, null, 201);
    }

    /// <summary>
    /// Reemplaza la lista de imágenes del producto (orden + principal).
    /// Sólo se borran del disco los archivos que subió el administrador y
    /// quedaron sin referencia: los placeholders de desarrollo no se tocan.
    /// </summary>
    public async Task<(bool Ok, string? Error, int Status)> GuardarImagenesAsync(
        int productoId, IReadOnlyList<ProductoImagenDto> imagenes, CancellationToken ct)
    {
        if (imagenes.Count > ProductoImagenes.MaximoPorProducto)
        {
            return (false,
                $"Un producto admite hasta {ProductoImagenes.MaximoPorProducto} imágenes.",
                400);
        }

        foreach (var imagen in imagenes)
        {
            if (string.IsNullOrWhiteSpace(imagen.UrlImagen)
                || !imagen.UrlImagen.StartsWith(ProductoImagenes.PrefijoRuta, StringComparison.Ordinal))
            {
                return (false, "Ruta de imagen inválida.", 400);
            }
        }

        var producto = await db.Productos
            .Include(p => p.Imagenes)
            .FirstOrDefaultAsync(p => p.IdProducto == productoId, ct);

        if (producto is null)
        {
            return (false, "Producto no encontrado.", 404);
        }

        // Sólo una imagen puede ser principal: si no se marca ninguna, la
        // primera de la lista lo es.
        var hayPrincipal = imagenes.Any(i => i.Principal);
        var orden = 1;
        var principalTomada = false;

        db.ProductoImagenes.RemoveRange(producto.Imagenes);

        foreach (var imagen in imagenes)
        {
            var principal = imagen.Principal && !principalTomada;
            principalTomada |= principal;

            db.ProductoImagenes.Add(new ProductoImagen
            {
                IdProducto = productoId,
                UrlImagen = imagen.UrlImagen.Trim(),
                Orden = orden++,
                Principal = principal,
            });
        }

        if (!principalTomada && orden > 1)
        {
            // La primera imagen queda como principal.
            var primera = await db.ProductoImagenes
                .Where(i => i.IdProducto == productoId)
                .OrderBy(i => i.Orden)
                .FirstOrDefaultAsync(ct);

            if (primera is not null)
            {
                primera.Principal = true;
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Índice parcial: sólo una imagen principal por producto.
            return (false, "No se pudo guardar la lista de imágenes.", 409);
        }

        LimpiarArchivosHuerfanos(productoId, imagenes.Select(i => i.UrlImagen));

        return (true, null, 200);
    }

    /// <summary>
    /// Reemplaza la lista COMPLETA de imágenes vigentes del producto en una
    /// sola operación atómica: bajas, altas de archivos nuevos y orden final.
    ///
    /// El límite de <see cref="ProductoImagenes.MaximoPorProducto"/> se evalúa
    /// sobre la lista FINAL: una imagen que se elimina en la misma operación
    /// libera su lugar, así que "5 → quitar 2 → agregar 2" es válido aunque
    /// las dos filas eliminadas todavía existan en la base al comenzar.
    ///
    /// Si algo falla no se persiste nada (transacción) y los archivos nuevos ya
    /// escritos se borran, de modo que un error no deja imágenes huérfanas ni
    /// pierde las que estaban.
    /// </summary>
    public async Task<(List<ProductoImagenDto>? Valor, string? Error, int Status)> ReemplazarImagenesAsync(
        int productoId,
        IReadOnlyList<ImagenFinalDto> imagenes,
        IReadOnlyList<ImagenNuevaDto> nuevas,
        CancellationToken cancellationToken)
    {
        imagenes ??= [];
        nuevas ??= [];

        // ---- Validaciones de forma, antes de tocar disco o base ----
        if (imagenes.Count > ProductoImagenes.MaximoPorProducto)
        {
            return (null,
                $"Un producto admite hasta {ProductoImagenes.MaximoPorProducto} imágenes " +
                $"y la lista final tiene {imagenes.Count}.",
                400);
        }

        var idsEnviados = new HashSet<int>();
        var indicesUsados = new HashSet<int>();

        foreach (var imagen in imagenes)
        {
            if (imagen.Id > 0)
            {
                if (!idsEnviados.Add(imagen.Id))
                {
                    return (null, "La lista de imágenes repite una misma imagen.", 400);
                }

                continue;
            }

            var indice = imagen.IndiceNueva ?? -1;
            if (indice < 0 || indice >= nuevas.Count)
            {
                return (null, "La lista de imágenes hace referencia a un archivo que no se envió.", 400);
            }

            if (!indicesUsados.Add(indice))
            {
                return (null, "La lista de imágenes usa el mismo archivo nuevo dos veces.", 400);
            }
        }

        if (indicesUsados.Count != nuevas.Count)
        {
            return (null, "Se enviaron archivos que no forman parte de la lista de imágenes.", 400);
        }

        for (var i = 0; i < nuevas.Count; i++)
        {
            var nueva = nuevas[i];
            if (nueva.Contenido is null || nueva.Contenido.LongLength == 0)
            {
                return (null, $"El archivo \"{nueva.NombreArchivo}\" está vacío.", 400);
            }

            if (nueva.Contenido.LongLength > ProductoImagenes.MaximoBytes)
            {
                return (null,
                    $"\"{nueva.NombreArchivo}\" supera el máximo de " +
                    $"{ProductoImagenes.MaximoBytes / (1024 * 1024)} MB.",
                    400);
            }

            if (!ProductoImagenes.EsTipoPermitido(nueva.ContentType))
            {
                return (null, "Sólo se aceptan imágenes JPEG o PNG.", 400);
            }

            if (!ProductoImagenes.TieneFirmaValida(nueva.Contenido))
            {
                return (null, $"\"{nueva.NombreArchivo}\" no es una imagen JPEG o PNG válida.", 400);
            }
        }

        // ---- Estado vigente en la base ----
        var producto = await db.Productos
            .Include(p => p.Imagenes)
            .FirstOrDefaultAsync(p => p.IdProducto == productoId, cancellationToken);

        if (producto is null)
        {
            return (null, "Producto no encontrado.", 404);
        }

        var idsVigentes = producto.Imagenes.Select(i => i.IdProductoImagen).ToHashSet();
        if (idsEnviados.Any(id => !idsVigentes.Contains(id)))
        {
            return (null,
                "Alguna de las imágenes ya no pertenece al producto. " +
                "Volvé a abrir el producto para actualizar la lista.",
                409);
        }

        // ---- Archivos nuevos a disco (se borran si algo falla) ----
        var carpeta = CarpetaImagenes;
        var escritas = new List<string>();
        var urlsNuevas = new Dictionary<ImagenFinalDto, string>(ReferenceEqualityComparer.Instance);

        try
        {
            Directory.CreateDirectory(carpeta);

            foreach (var imagen in imagenes.Where(i => i.Id == 0))
            {
                var origen = nuevas[imagen.IndiceNueva!.Value];
                var extension = ProductoImagenes.ExtensionPorTipo(origen.ContentType)!;
                var nombreArchivo = $"p{productoId}-{Guid.NewGuid():N}.{extension}";
                var ruta = Path.Combine(carpeta, nombreArchivo);

                await using (var destino = new FileStream(
                    ruta, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await destino.WriteAsync(origen.Contenido, cancellationToken);
                }

                escritas.Add(ruta);
                urlsNuevas[imagen] = $"{ProductoImagenes.PrefijoRuta}{nombreArchivo}";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var ruta in escritas)
            {
                TryEliminarArchivo(ruta);
            }

            return (null, "No se pudieron guardar las imágenes en el servidor.", 500);
        }

        // ---- Persistencia atómica ----
        var estrategia = db.Database.CreateExecutionStrategy();

        try
        {
            return await estrategia.ExecuteAsync<(List<ProductoImagenDto>? Valor, string? Error, int Status)>(
                async () =>
            {
                await using var transaccion = await db.Database.BeginTransactionAsync(cancellationToken);

                // El índice único filtrado ([Principal] = 1) sólo admite una
                // principal por producto: se limpian todas antes de asignarla.
                foreach (var fila in producto.Imagenes)
                {
                    fila.Principal = false;
                }

                foreach (var fila in producto.Imagenes
                    .Where(f => !idsEnviados.Contains(f.IdProductoImagen))
                    .ToList())
                {
                    db.ProductoImagenes.Remove(fila);
                    producto.Imagenes.Remove(fila);
                }

                var vigentesPorId = producto.Imagenes.ToDictionary(f => f.IdProductoImagen);
                var orden = 1;
                ProductoImagen? principal = null;

                foreach (var imagen in imagenes)
                {
                    ProductoImagen fila;

                    if (imagen.Id > 0)
                    {
                        fila = vigentesPorId[imagen.Id];
                        fila.Orden = orden++;
                    }
                    else
                    {
                        fila = new ProductoImagen
                        {
                            IdProducto = productoId,
                            UrlImagen = urlsNuevas[imagen],
                            Orden = orden++,
                            Principal = false,
                        };

                        db.ProductoImagenes.Add(fila);
                        producto.Imagenes.Add(fila);
                    }

                    if (imagen.Principal)
                    {
                        principal = fila;
                    }
                }

                // Primero bajas, altas y orden (todas las filas sin principal).
                await db.SaveChangesAsync(cancellationToken);

                // Después, una única principal: la elegida o, si no se indicó
                // ninguna, la primera de la lista (nunca se queda sin principal).
                principal ??= imagenes.Count > 0
                    ? producto.Imagenes.OrderBy(f => f.Orden).First()
                    : null;

                if (principal is not null)
                {
                    principal.Principal = true;
                    await db.SaveChangesAsync(cancellationToken);
                }

                await transaccion.CommitAsync(cancellationToken);

                LimpiarArchivosHuerfanos(
                    productoId, imagenes.Select(i => i.Id > 0
                        ? vigentesPorId[i.Id].UrlImagen
                        : urlsNuevas[i]));

                var persistidas = await db.ProductoImagenes.AsNoTracking()
                    .Where(i => i.IdProducto == productoId)
                    .OrderBy(i => i.Orden)
                    .Select(i => new ProductoImagenDto
                    {
                        Id = i.IdProductoImagen,
                        UrlImagen = i.UrlImagen,
                        Orden = i.Orden,
                        Principal = i.Principal,
                    })
                    .ToListAsync(cancellationToken);

                return (persistidas, null, 200);
            });
        }
        catch (DbUpdateException)
        {
            foreach (var ruta in escritas)
            {
                TryEliminarArchivo(ruta);
            }

            return (null, "No se pudo guardar la lista de imágenes.", 409);
        }
        catch
        {
            foreach (var ruta in escritas)
            {
                TryEliminarArchivo(ruta);
            }

            throw;
        }
    }

    /// <summary>Borra del disco las imágenes subidas que ya no se referencian.</summary>
    private void LimpiarArchivosHuerfanos(int productoId, IEnumerable<string> urlsActuales)
    {
        var actuales = urlsActuales
            .Select(u => Path.GetFileName(u))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            var carpeta = CarpetaImagenes;
            if (!Directory.Exists(carpeta))
            {
                return;
            }

            foreach (var archivo in Directory.EnumerateFiles(carpeta))
            {
                var nombre = Path.GetFileName(archivo);
                if (actuales.Contains(nombre))
                {
                    continue;
                }

                // Sólo se borran los archivos propios (nombre con GUID) y los
                // que corresponden a este producto.
                if (!ProductoImagenes.EsArchivoSubido(nombre))
                {
                    continue;
                }

                var idTexto = nombre.StartsWith("p", StringComparison.OrdinalIgnoreCase)
                    ? nombre[1..].Split('-')[0]
                    : string.Empty;

                if (!int.TryParse(idTexto, out var id) || id != productoId)
                {
                    continue;
                }

                TryEliminarArchivo(archivo);
            }
        }
        catch (IOException)
        {
            // La limpieza es best-effort: no debe fallar el guardado.
        }
    }

    private static void TryEliminarArchivo(string ruta)
    {
        try
        {
            if (File.Exists(ruta))
            {
                File.Delete(ruta);
            }
        }
        catch (IOException)
        {
            // Best-effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort.
        }
    }
}
