using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TiendaRopa.Api.Data;
using TiendaRopa.Api.Entities;
using TiendaRopa.Api.Services;
using TiendaRopa.Contracts.Catalogo;
using TiendaRopa.Tests.Infra;

namespace TiendaRopa.Tests;

/// <summary>
/// Pruebas del límite de 5 imágenes por producto y del reemplazo atómico:
/// el conteo se valida sobre la lista FINAL (vigentes), no sobre las filas que
/// todavía existen en la base. Un producto con 5 imágenes puede quitar 2 y
/// agregar 2 en la misma operación sin errores falsos.
/// </summary>
public sealed class ImagenesProductoTests : IDisposable
{
    private readonly string _carpeta;
    private readonly IWebHostEnvironment _entorno;

    public ImagenesProductoTests()
    {
        // WebRoot temporal por prueba: el servicio escribe los archivos aquí.
        _carpeta = Path.Combine(Path.GetTempPath(), $"velvet-img-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_carpeta, "img", "productos"));
        _entorno = new EntornoPrueba(_carpeta);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_carpeta))
            {
                Directory.Delete(_carpeta, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort: es una carpeta temporal.
        }
    }

    // ---------- Límite sobre la lista final ----------

    [Fact]
    public async Task Reemplazar_ConCincoVigentes_RechazaUnaSexta()
    {
        using var db = ContextoImagenes();
        var idProducto = await SembrarProductoAsync(db, cantidadImagenes: 5);
        var service = CrearServicio(db);

        var vigentes = db.ProductoImagenes
            .Where(i => i.IdProducto == idProducto)
            .OrderBy(i => i.Orden)
            .Select(i => new ImagenFinalDto { Id = i.IdProductoImagen, Principal = i.Principal })
            .ToList();

        // Se agrega una sexta: la lista final tendría 6 → rechazo correcto.
        vigentes.Add(new ImagenFinalDto { Id = 0, IndiceNueva = 0 });

        var (valor, error, status) = await service.ReemplazarImagenesAsync(
            idProducto, vigentes, [NuevaImagen()], default);

        Assert.Null(valor);
        Assert.Equal(400, status);
        Assert.Contains("hasta 5 imágenes", error);

        // La base conserva las 5 originales: el rechazo no borró nada.
        Assert.Equal(5, await db.ProductoImagenes.CountAsync(i => i.IdProducto == idProducto));
    }

    [Fact]
    public async Task Reemplazar_QuitarDosYAgregarDos_DejaExactamenteCinco()
    {
        using var db = ContextoImagenes();
        var idProducto = await SembrarProductoAsync(db, cantidadImagenes: 5);
        var service = CrearServicio(db);

        var filas = db.ProductoImagenes
            .Where(i => i.IdProducto == idProducto)
            .OrderBy(i => i.Orden)
            .ToList();

        // Escenario del reporte: 5 en la base, se conservan 3, se agregan 2.
        var finales = filas.Take(3)
            .Select((f, i) => new ImagenFinalDto { Id = f.IdProductoImagen, Principal = i == 0 })
            .ToList();

        finales.Add(new ImagenFinalDto { Id = 0, IndiceNueva = 0 });
        finales.Add(new ImagenFinalDto { Id = 0, IndiceNueva = 1 });

        var (valor, error, status) = await service.ReemplazarImagenesAsync(
            idProducto, finales, [NuevaImagen(), NuevaImagen()], default);

        Assert.Null(error);
        Assert.Equal(200, status);
        Assert.NotNull(valor);
        Assert.Equal(5, valor!.Count);
        Assert.Equal(5, await db.ProductoImagenes.CountAsync(i => i.IdProducto == idProducto));

        // Las dos filas eliminadas ya no existen: liberaron su lugar de forma real.
        var idsFinales = valor.Select(v => v.Id).ToHashSet();
        Assert.DoesNotContain(filas[3].IdProductoImagen, idsFinales);
        Assert.DoesNotContain(filas[4].IdProductoImagen, idsFinales);
    }

    [Fact]
    public async Task Reemplazar_QuitarTodasYAgregarCinco_Funciona()
    {
        using var db = ContextoImagenes();
        var idProducto = await SembrarProductoAsync(db, cantidadImagenes: 5);
        var service = CrearServicio(db);

        var finales = Enumerable.Range(0, 5)
            .Select(i => new ImagenFinalDto { Id = 0, IndiceNueva = i, Principal = i == 0 })
            .ToList();

        var nuevas = Enumerable.Range(0, 5).Select(_ => NuevaImagen()).ToList();

        var (valor, error, status) = await service.ReemplazarImagenesAsync(
            idProducto, finales, nuevas, default);

        Assert.Null(error);
        Assert.Equal(200, status);
        Assert.Equal(5, valor!.Count);
        Assert.All(valor, v => Assert.StartsWith(ProductoImagenes.PrefijoRuta, v.UrlImagen));
        Assert.Equal(5, await db.ProductoImagenes.CountAsync(i => i.IdProducto == idProducto));
    }

    [Fact]
    public async Task Reemplazar_ConservaOrdenYPrincipalElegida()
    {
        using var db = ContextoImagenes();
        var idProducto = await SembrarProductoAsync(db, cantidadImagenes: 4);
        var service = CrearServicio(db);

        var filas = db.ProductoImagenes
            .Where(i => i.IdProducto == idProducto)
            .OrderBy(i => i.Orden)
            .ToList();

        // Se invierte el orden y se elige la tercera original como principal.
        var finales = new List<ImagenFinalDto>
        {
            new() { Id = filas[3].IdProductoImagen },
            new() { Id = filas[2].IdProductoImagen, Principal = true },
            new() { Id = filas[1].IdProductoImagen },
            new() { Id = filas[0].IdProductoImagen },
        };

        var (valor, error, _) = await service.ReemplazarImagenesAsync(idProducto, finales, [], default);

        Assert.Null(error);
        Assert.NotNull(valor);
        Assert.Equal(filas[3].IdProductoImagen, valor![0].Id);
        Assert.Equal(1, valor[0].Orden);
        Assert.Equal(4, valor[3].Orden);

        var principal = Assert.Single(valor, v => v.Principal);
        Assert.Equal(filas[2].IdProductoImagen, principal.Id);

        // Nunca hay más de una principal aunque se marquen varias.
        Assert.Single(await db.ProductoImagenes
            .Where(i => i.IdProducto == idProducto && i.Principal)
            .ToListAsync());
    }

    [Fact]
    public async Task Reemplazar_SinPrincipalMarcada_AsignaLaPrimera()
    {
        using var db = ContextoImagenes();
        var idProducto = await SembrarProductoAsync(db, cantidadImagenes: 3);
        var service = CrearServicio(db);

        var finales = db.ProductoImagenes
            .Where(i => i.IdProducto == idProducto)
            .OrderByDescending(i => i.Orden)
            .Select(f => new ImagenFinalDto { Id = f.IdProductoImagen })
            .ToList();

        var (valor, error, _) = await service.ReemplazarImagenesAsync(idProducto, finales, [], default);

        Assert.Null(error);
        Assert.NotNull(valor);
        Assert.True(valor![0].Principal);
        Assert.Single(valor, v => v.Principal);
    }

    [Fact]
    public async Task Reemplazar_ImagenDeOtroProducto_SeRechaza()
    {
        using var db = ContextoImagenes();
        var productoA = await SembrarProductoAsync(db, cantidadImagenes: 2, nombre: "Producto A");
        var productoB = await SembrarProductoAsync(db, cantidadImagenes: 2, nombre: "Producto B");
        var service = CrearServicio(db);

        var ajeno = db.ProductoImagenes.First(i => i.IdProducto == productoB);

        var (valor, error, status) = await service.ReemplazarImagenesAsync(
            productoA, [new ImagenFinalDto { Id = ajeno.IdProductoImagen }], [], default);

        Assert.Null(valor);
        Assert.Equal(409, status);
        Assert.Contains("ya no pertenece al producto", error);

        // Ninguno de los dos productos perdió imágenes.
        Assert.Equal(2, await db.ProductoImagenes.CountAsync(i => i.IdProducto == productoA));
        Assert.Equal(2, await db.ProductoImagenes.CountAsync(i => i.IdProducto == productoB));
    }

    [Fact]
    public async Task Reemplazar_IndiceNuevaFueraDeRango_SeRechaza()
    {
        using var db = ContextoImagenes();
        var idProducto = await SembrarProductoAsync(db, cantidadImagenes: 1);
        var service = CrearServicio(db);

        var (valor, error, status) = await service.ReemplazarImagenesAsync(
            idProducto,
            [new ImagenFinalDto { Id = 0, IndiceNueva = 3 }],
            [NuevaImagen()],
            default);

        Assert.Null(valor);
        Assert.Equal(400, status);
        Assert.Contains("archivo que no se envió", error);
    }

    [Fact]
    public async Task Reemplazar_ArchivoSinFirmaValida_SeRechazaSinTocarLaBase()
    {
        using var db = ContextoImagenes();
        var idProducto = await SembrarProductoAsync(db, cantidadImagenes: 2);
        var service = CrearServicio(db);

        var falsa = new ImagenNuevaDto
        {
            Contenido = "no soy una imagen"u8.ToArray(),
            NombreArchivo = "falsa.png",
            ContentType = "image/png",
        };

        var (valor, error, status) = await service.ReemplazarImagenesAsync(
            idProducto, [new ImagenFinalDto { Id = 0, IndiceNueva = 0 }], [falsa], default);

        Assert.Null(valor);
        Assert.Equal(400, status);
        Assert.Contains("no es una imagen JPEG o PNG válida", error);
        Assert.Equal(2, await db.ProductoImagenes.CountAsync(i => i.IdProducto == idProducto));
    }

    [Fact]
    public async Task Reemplazar_ListaConImagenRepetida_SeRechaza()
    {
        using var db = ContextoImagenes();
        var idProducto = await SembrarProductoAsync(db, cantidadImagenes: 2);
        var service = CrearServicio(db);

        var id = db.ProductoImagenes.First(i => i.IdProducto == idProducto).IdProductoImagen;

        var (valor, error, status) = await service.ReemplazarImagenesAsync(
            idProducto,
            [new ImagenFinalDto { Id = id }, new ImagenFinalDto { Id = id }],
            [],
            default);

        Assert.Null(valor);
        Assert.Equal(400, status);
        Assert.Contains("repite", error);
    }

    // ---------- Subida individual (endpoint heredado) ----------

    [Fact]
    public async Task SubirImagen_ConCincoVigentes_Rechaza()
    {
        using var db = ContextoImagenes();
        var idProducto = await SembrarProductoAsync(db, cantidadImagenes: 5);
        var service = CrearServicio(db);

        var (valor, error, status) = await service.SubirImagenAsync(
            idProducto, ArchivoForm(NuevaImagen()), default);

        Assert.Null(valor);
        Assert.Equal(400, status);
        Assert.Contains("máximo de 5 imágenes", error);
    }

    [Fact]
    public async Task SubirImagen_ConEspacioLibre_Guarda()
    {
        using var db = ContextoImagenes();
        var idProducto = await SembrarProductoAsync(db, cantidadImagenes: 3);
        var service = CrearServicio(db);

        var (valor, error, status) = await service.SubirImagenAsync(
            idProducto, ArchivoForm(NuevaImagen()), default);

        Assert.Null(error);
        Assert.Equal(201, status);
        Assert.NotNull(valor);
        Assert.Equal(4, await db.ProductoImagenes.CountAsync(i => i.IdProducto == idProducto));
    }

    // ---------- Helpers ----------

    private static VelvetRoseDbContext ContextoImagenes() => ContextoPrueba.Crear();

    private AdminService CrearServicio(VelvetRoseDbContext db)
        => new(db, new PedidoService(db), _entorno);

    /// <summary>Crea un producto con la cantidad de imágenes indicada (orden 1..N).</summary>
    private static async Task<int> SembrarProductoAsync(
        VelvetRoseDbContext db, int cantidadImagenes, string nombre = "Abrigo Corte Redondo")
    {
        var categoriaId = db.Categorias.Select(c => c.IdCategoria).First();

        var producto = new Producto
        {
            Nombre = nombre,
            IdCategoria = categoriaId,
            PrecioBase = 89990,
            Activo = true,
        };

        db.Productos.Add(producto);
        await db.SaveChangesAsync();

        for (var i = 1; i <= cantidadImagenes; i++)
        {
            db.ProductoImagenes.Add(new ProductoImagen
            {
                IdProducto = producto.IdProducto,
                UrlImagen = $"{ProductoImagenes.PrefijoRuta}p{producto.IdProducto}-{i}.png",
                Orden = i,
                Principal = i == 1,
            });
        }

        await db.SaveChangesAsync();
        return producto.IdProducto;
    }

    /// <summary>Imagen nueva válida: cabecera PNG real + relleno.</summary>
    private static ImagenNuevaDto NuevaImagen() => new()
    {
        Contenido = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D],
        NombreArchivo = "nueva.png",
        ContentType = "image/png",
    };

    private static Microsoft.AspNetCore.Http.IFormFile ArchivoForm(ImagenNuevaDto imagen)
    {
        var stream = new MemoryStream(imagen.Contenido);
        return new Microsoft.AspNetCore.Http.FormFile(
            stream, 0, stream.Length, "archivo", imagen.NombreArchivo)
        {
            Headers = new Microsoft.AspNetCore.Http.HeaderDictionary(
                new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
                {
                    ["Content-Type"] = imagen.ContentType,
                }),
        };
    }

    /// <summary>Entorno mínimo para que el servicio resuelva wwwroot/img/productos.</summary>
    private sealed class EntornoPrueba(string raiz) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TiendaRopa.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = raiz;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = raiz;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
