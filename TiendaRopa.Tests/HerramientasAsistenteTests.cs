using Microsoft.EntityFrameworkCore;
using TiendaRopa.Api.Asistente;
using TiendaRopa.Api.Entities;
using TiendaRopa.Tests.Infra;

namespace TiendaRopa.Tests;

/// <summary>
/// Pruebas de las herramientas de SOLO LECTURA del asistente.
/// Verifican los 10 escenarios pedidos en el enunciado del incremento:
/// un pedido activo, varios pedidos activos, ningún pedido activo, nota
/// pública ausente, número de seguimiento disponible, acceso al pedido de otro
/// cliente, variante con stock, variante agotada, variante inexistente y
/// falta de autenticación.
/// </summary>
public sealed class HerramientasAsistenteTests
{
    private const string EmailA = "cliente.a@velvetrose.test";
    private const string EmailB = "cliente.b@velvetrose.test";

    // ---------------- listar_mis_pedidos ----------------

    [Fact]
    public async Task ListarMisPedidos_UnPedidoActivo_DevuelveElPedido()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        CrearPedido(db, clienteA, "Vestido Satinado Noche", 1);

        var herramientas = CrearHerramientas(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var resultado = await herramientas.EjecutarAsync(
            HerramientasAsistente.ListarMisPedidos, null, default);

        Assert.True(resultado.Exito);
        Assert.NotNull(resultado.Datos);

        var json = System.Text.Json.JsonSerializer.Serialize(resultado.Datos);
        Assert.Contains("cantidad", json);
        Assert.Contains("Vestido Satinado Noche", json);
    }

    [Fact]
    public async Task ListarMisPedidos_VariosPedidosActivos_DevuelveTodos()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        CrearPedido(db, clienteA, "Vestido Satinado Noche", 1);
        CrearPedido(db, clienteA, "Blusa Pompoir Marfil", 2);

        var herramientas = CrearHerramientas(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var resultado = await herramientas.EjecutarAsync(
            HerramientasAsistente.ListarMisPedidos, null, default);

        Assert.True(resultado.Exito);
        var json = System.Text.Json.JsonSerializer.Serialize(resultado.Datos);
        Assert.Contains("\"cantidad\":2", json);
    }

    [Fact]
    public async Task ListarMisPedidos_SinPedidosActivos_DevuelveCantidadCero()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);

        var herramientas = CrearHerramientas(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var resultado = await herramientas.EjecutarAsync(
            HerramientasAsistente.ListarMisPedidos, null, default);

        Assert.True(resultado.Exito);
        var json = System.Text.Json.JsonSerializer.Serialize(resultado.Datos);
        Assert.Contains("\"cantidad\":0", json);
    }

    [Fact]
    public async Task ListarMisPedidos_NoMuestraPedidosDeOtroCliente()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var clienteB = db.IdCliente(EmailB);
        var usuarioA = db.IdUsuario(EmailA);
        CrearPedido(db, clienteB, "Blusa Pompoir Marfil", 1);

        var herramientas = CrearHerramientas(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var resultado = await herramientas.EjecutarAsync(
            HerramientasAsistente.ListarMisPedidos, null, default);

        Assert.True(resultado.Exito);
        var json = System.Text.Json.JsonSerializer.Serialize(resultado.Datos);
        Assert.Contains("\"cantidad\":0", json);
        Assert.DoesNotContain("Blusa Pompoir Marfil", json);
    }

    // ---------------- consultar_pedido ----------------

    [Fact]
    public async Task ConsultarPedido_NotaPublicaAusente_NoInventaActualizacion()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        var idPedido = CrearPedido(db, clienteA, "Vestido Satinado Noche", 1);

        var herramientas = CrearHerramientas(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var resultado = await herramientas.EjecutarAsync(
            HerramientasAsistente.ConsultarPedido,
            new Dictionary<string, object?> { ["id_pedido"] = idPedido },
            default);

        Assert.True(resultado.Exito);
        var json = System.Text.Json.JsonSerializer.Serialize(resultado.Datos);

        // Sin notas: la última actualización es null (no se inventa nada).
        Assert.Contains("\"ultima_actualizacion\":null", json);
        Assert.Contains("\"actualizaciones\":[]", json);
    }

    [Fact]
    public async Task ConsultarPedido_CodigoSeguimientoDisponible_LoDevuelve()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        var idPedido = CrearPedido(db, clienteA, "Vestido Satinado Noche", 1);

        db.PedidoNotas.Add(new PedidoNota
        {
            IdPedido = idPedido,
            FechaActualizacion = DateTime.UtcNow.AddHours(-2),
            Nota = "Tu pedido salió a reparto.",
            Empresa = "Velvet Express",
            CodigoSeguimiento = "VR-998877",
        });
        db.SaveChanges();

        var herramientas = CrearHerramientas(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var resultado = await herramientas.EjecutarAsync(
            HerramientasAsistente.ConsultarPedido,
            new Dictionary<string, object?> { ["id_pedido"] = idPedido },
            default);

        Assert.True(resultado.Exito);
        var json = System.Text.Json.JsonSerializer.Serialize(resultado.Datos);
        Assert.Contains("VR-998877", json);
        Assert.Contains("Velvet Express", json);
        Assert.Contains("reparto", json);
    }

    [Fact]
    public async Task ConsultarPedido_PedidoDeOtroCliente_NoLoEntrega()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var clienteB = db.IdCliente(EmailB);
        var usuarioA = db.IdUsuario(EmailA);
        var idPedidoAjeno = CrearPedido(db, clienteB, "Blusa Pompoir Marfil", 1);

        var herramientas = CrearHerramientas(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var resultado = await herramientas.EjecutarAsync(
            HerramientasAsistente.ConsultarPedido,
            new Dictionary<string, object?> { ["id_pedido"] = idPedidoAjeno },
            default);

        // No se confirma la existencia del pedido ajeno: se responde "no encontrado".
        Assert.False(resultado.Exito);
        Assert.NotNull(resultado.Error);
        Assert.DoesNotContain("Blusa Pompoir Marfil", resultado.Error);
    }

    // ---------------- consultar_variantes ----------------

    [Fact]
    public async Task ConsultarVariantes_VarianteConStock_DevuelveDisponible()
    {
        using var db = ContextoPrueba.Crear();
        var herramientas = CrearHerramientas(db, UsuarioActualSimulado.Anonimo);
        var idProducto = await db.Productos
            .Where(p => p.Nombre == "Vestido Satinado Noche")
            .Select(p => p.IdProducto)
            .FirstAsync();

        var resultado = await herramientas.EjecutarAsync(
            HerramientasAsistente.ConsultarVariantes,
            new Dictionary<string, object?> { ["id_producto"] = idProducto },
            default);

        Assert.True(resultado.Exito);
        var json = System.Text.Json.JsonSerializer.Serialize(resultado.Datos);
        Assert.Contains("\"estado\":\"con_stock\"", json);
        Assert.Contains("\"disponible\":true", json);
    }

    [Fact]
    public async Task ConsultarVariantes_VarianteAgotada_DistingueSinStock()
    {
        using var db = ContextoPrueba.Crear();
        var herramientas = CrearHerramientas(db, UsuarioActualSimulado.Anonimo);
        var idProducto = await db.Productos
            .Where(p => p.Nombre == "Vestido Satinado Noche")
            .Select(p => p.IdProducto)
            .FirstAsync();

        // Filtro por el talle sin stock (L / Ciruela).
        var resultado = await herramientas.EjecutarAsync(
            HerramientasAsistente.ConsultarVariantes,
            new Dictionary<string, object?> { ["id_producto"] = idProducto, ["talle"] = "L" },
            default);

        Assert.True(resultado.Exito);
        var json = System.Text.Json.JsonSerializer.Serialize(resultado.Datos);
        Assert.Contains("\"estado\":\"sin_stock\"", json);
        Assert.Contains("\"stock\":0", json);
        Assert.Contains("\"disponible\":false", json);
    }

    [Fact]
    public async Task ConsultarVariantes_CombinacionInexistente_DistingueInexistente()
    {
        using var db = ContextoPrueba.Crear();
        var herramientas = CrearHerramientas(db, UsuarioActualSimulado.Anonimo);
        var idProducto = await db.Productos
            .Where(p => p.Nombre == "Vestido Satinado Noche")
            .Select(p => p.IdProducto)
            .FirstAsync();

        // Combinación que no existe: talle S / Negro en el vestido.
        var resultado = await herramientas.EjecutarAsync(
            HerramientasAsistente.ConsultarVariantes,
            new Dictionary<string, object?> { ["id_producto"] = idProducto, ["talle"] = "S" },
            default);

        Assert.True(resultado.Exito);
        var json = System.Text.Json.JsonSerializer.Serialize(resultado.Datos);
        Assert.Contains("\"estado\":\"combinacion_inexistente\"", json);
        Assert.Contains("\"hay_variantes\":false", json);
    }

    [Fact]
    public async Task ConsultarVariantes_ProductoInexistente_DevuelveError()
    {
        using var db = ContextoPrueba.Crear();
        var herramientas = CrearHerramientas(db, UsuarioActualSimulado.Anonimo);

        var resultado = await herramientas.EjecutarAsync(
            HerramientasAsistente.ConsultarVariantes,
            new Dictionary<string, object?> { ["id_producto"] = 99999 },
            default);

        Assert.False(resultado.Exito);
        Assert.NotNull(resultado.Error);
    }

    // ---------------- Falta de autenticación ----------------

    [Theory]
    [InlineData(HerramientasAsistente.ListarMisPedidos)]
    [InlineData(HerramientasAsistente.ConsultarMisDatos)]
    public async Task HerramientasPersonales_SinAutenticacion_ExigenSesion(string herramienta)
    {
        using var db = ContextoPrueba.Crear();
        var herramientas = CrearHerramientas(db, UsuarioActualSimulado.Anonimo);

        var resultado = await herramientas.EjecutarAsync(herramienta, null, default);

        Assert.False(resultado.Exito);
        Assert.True(resultado.RequiereAutenticacion);
        Assert.Contains("sesión", resultado.Error);
    }

    [Fact]
    public async Task ConsultarMisDatos_EnmascaraTelefonoYDireccion()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        var herramientas = CrearHerramientas(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var resultado = await herramientas.EjecutarAsync(
            HerramientasAsistente.ConsultarMisDatos, null, default);

        Assert.True(resultado.Exito);
        var json = System.Text.Json.JsonSerializer.Serialize(resultado.Datos);

        // El teléfono completo y la dirección completa NO viajan al modelo.
        Assert.DoesNotContain("341-555-0101", json);
        Assert.DoesNotContain("Siempreviva", json);
        Assert.Contains("Carla", json);
    }

    // ---------------- Utilidades ----------------

    private static HerramientasAsistente CrearHerramientas(
        TiendaRopa.Api.Data.VelvetRoseDbContext db, UsuarioActualSimulado usuario)
        => new(db, usuario);

    /// <summary>Crea un pedido del cliente indicado con un producto del catálogo.</summary>
    private static int CrearPedido(
        TiendaRopa.Api.Data.VelvetRoseDbContext db, int idCliente, string nombreProducto, int cantidad)
    {
        var producto = db.Productos
            .Include(p => p.Variantes)
            .Single(p => p.Nombre == nombreProducto);

        var variante = producto.Variantes.First();
        var idEstado = db.EstadosPedido.Single(e => e.Nombre == "Pendiente").IdEstadoPedido;

        var pedido = new Pedido
        {
            IdCliente = idCliente,
            IdEstadoPedido = idEstado,
            FechaPedido = DateTime.UtcNow,
            Total = 62990 * cantidad,
        };

        pedido.Detalles.Add(new PedidoDetalle
        {
            IdProductoVariante = variante.IdProductoVariante,
            Cantidad = cantidad,
            PrecioUnitario = 62990,
            Subtotal = 62990 * cantidad,
        });

        db.Pedidos.Add(pedido);
        db.SaveChanges();

        return pedido.IdPedido;
    }
}
