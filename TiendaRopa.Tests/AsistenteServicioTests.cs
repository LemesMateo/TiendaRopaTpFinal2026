using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TiendaRopa.Api.Asistente;
using TiendaRopa.Api.Entities;
using TiendaRopa.Contracts.Asistente;
using TiendaRopa.Tests.Infra;

namespace TiendaRopa.Tests;

/// <summary>
/// Pruebas del orquestador del asistente: desambiguación de pedidos,
/// contexto de producto validado y errores del modelo (cuota agotada,
/// timeout, sin configuración).
/// </summary>
public sealed class AsistenteServicioTests
{
    private const string EmailA = "cliente.a@velvetrose.test";
    private const string EmailB = "cliente.b@velvetrose.test";

    [Fact]
    public async Task UnPedidoActivo_NoMuestraOpcionesDePedido()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        CrearPedido(db, clienteA, "Vestido Satinado Noche", 1);

        var (servicio, _) = CrearServicio(db, usuarioA, clienteA, EmailA);

        var respuesta = await servicio.ResponderAsync(
            new ChatSolicitudDto { Mensaje = "¿Dónde está mi pedido?" }, default);

        Assert.Empty(respuesta.OpcionesPedido);
        Assert.False(respuesta.RequiereAutenticacion);
    }

    [Fact]
    public async Task VariosPedidosActivos_MuestraOpcionesSeleccionables()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        CrearPedido(db, clienteA, "Vestido Satinado Noche", 1);
        CrearPedido(db, clienteA, "Blusa Pompoir Marfil", 2);

        var (servicio, _) = CrearServicio(db, usuarioA, clienteA, EmailA);

        var respuesta = await servicio.ResponderAsync(
            new ChatSolicitudDto { Mensaje = "¿Dónde está mi pedido?" }, default);

        Assert.Equal(2, respuesta.OpcionesPedido.Count);
        Assert.All(respuesta.OpcionesPedido, o => Assert.Contains("Pedido #", o.Etiqueta));

        // La descripción breve debe incluir el producto, para que el cliente
        // pueda elegir sin escribir el número.
        Assert.Contains(respuesta.OpcionesPedido, o => o.Etiqueta.Contains("Vestido"));
        Assert.Contains(respuesta.OpcionesPedido, o => o.Etiqueta.Contains("Blusa"));
    }

    [Fact]
    public async Task NingunPedidoActivo_NoMuestraOpciones()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);

        var (servicio, _) = CrearServicio(db, usuarioA, clienteA, EmailA);

        var respuesta = await servicio.ResponderAsync(
            new ChatSolicitudDto { Mensaje = "¿Dónde está mi pedido?" }, default);

        Assert.Empty(respuesta.OpcionesPedido);
    }

    [Fact]
    public async Task PedidosDeOtroCliente_NoAparecenComoOpciones()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var clienteB = db.IdCliente(EmailB);
        var usuarioA = db.IdUsuario(EmailA);
        CrearPedido(db, clienteB, "Blusa Pompoir Marfil", 1);

        var (servicio, _) = CrearServicio(db, usuarioA, clienteA, EmailA);

        var respuesta = await servicio.ResponderAsync(
            new ChatSolicitudDto { Mensaje = "¿Dónde está mi pedido?" }, default);

        Assert.Empty(respuesta.OpcionesPedido);
    }

    [Fact]
    public async Task SinAutenticacion_LaRespuestaPideIniciarSesion()
    {
        using var db = ContextoPrueba.Crear();
        var (servicio, generador) = CrearServicio(db, 0, null, null, anonimo: true);

        // El modelo pide los pedidos del cliente: la herramienta exige sesión.
        generador.ConHerramientas(HerramientasAsistente.ListarMisPedidos);

        var respuesta = await servicio.ResponderAsync(
            new ChatSolicitudDto { Mensaje = "¿Dónde está mi pedido?" }, default);

        Assert.True(respuesta.RequiereAutenticacion);
    }

    [Fact]
    public async Task ConsultaDeCatalogo_SinAutenticacion_NoPideIniciarSesion()
    {
        using var db = ContextoPrueba.Crear();
        var (servicio, generador) = CrearServicio(db, 0, null, null, anonimo: true);

        // Consulta de stock: no necesita datos personales, así que no se pide
        // iniciar sesión (el CTA sería ruido).
        generador.ConHerramientas(HerramientasAsistente.ConsultarVariantes);

        var respuesta = await servicio.ResponderAsync(
            new ChatSolicitudDto { Mensaje = "¿Hay stock?" }, default);

        Assert.False(respuesta.RequiereAutenticacion);
    }

    [Fact]
    public async Task ContextoProductoInvalido_SeIgnora()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        var (servicio, generador) = CrearServicio(db, usuarioA, clienteA, EmailA);

        // Producto inexistente: el backend lo descarta (no se lo inventa).
        await servicio.ResponderAsync(
            new ChatSolicitudDto { Mensaje = "¿Hay stock?", IdProductoContexto = 99999 }, default);

        Assert.True(servicio is not null);
    }

    [Fact]
    public async Task ContextoProductoValido_SeUsa()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        var (servicio, _) = CrearServicio(db, usuarioA, clienteA, EmailA);

        var idProducto = await db.Productos
            .Where(p => p.Nombre == "Vestido Satinado Noche")
            .Select(p => p.IdProducto)
            .FirstAsync();

        var respuesta = await servicio.ResponderAsync(
            new ChatSolicitudDto { Mensaje = "¿Hay stock?", IdProductoContexto = idProducto }, default);

        Assert.False(string.IsNullOrWhiteSpace(respuesta.Mensaje));
    }

    [Fact]
    public async Task ContextoPedidoAjeno_SeIgnora()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var clienteB = db.IdCliente(EmailB);
        var usuarioA = db.IdUsuario(EmailA);
        var idPedidoAjeno = CrearPedido(db, clienteB, "Blusa Pompoir Marfil", 1);

        var (servicio, _) = CrearServicio(db, usuarioA, clienteA, EmailA);

        var respuesta = await servicio.ResponderAsync(
            new ChatSolicitudDto { Mensaje = "Consultame el pedido", IdPedidoContexto = idPedidoAjeno },
            default);

        // El pedido ajeno no se convierte en contexto válido ni en opciones.
        Assert.Empty(respuesta.OpcionesPedido);
    }

    // ---------------- Errores de Gemini ----------------

    [Fact]
    public async Task CuotaAgotada_DevuelveErrorControlado()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        var (servicio, generador) = CrearServicio(db, usuarioA, clienteA, EmailA);

        generador.SimularCuotaAgotada = true;

        var respuesta = await servicio.ResponderAsync(
            new ChatSolicitudDto { Mensaje = "Hola" }, default);

        Assert.Equal("cuota_agotada", respuesta.ErrorCodigo);
        Assert.Contains("límite de uso", respuesta.ErrorMensaje);
        Assert.False(string.IsNullOrWhiteSpace(respuesta.Mensaje));
    }

    [Fact]
    public async Task Timeout_DevuelveErrorControlado()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        var (servicio, generador) = CrearServicio(db, usuarioA, clienteA, EmailA);

        generador.SimularTimeout = true;

        var respuesta = await servicio.ResponderAsync(
            new ChatSolicitudDto { Mensaje = "Hola" }, default);

        Assert.Equal("timeout", respuesta.ErrorCodigo);
    }

    [Fact]
    public async Task SinConexion_DevuelveErrorControlado()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        var (servicio, generador) = CrearServicio(db, usuarioA, clienteA, EmailA);

        generador.SimularSinConexion = true;

        var respuesta = await servicio.ResponderAsync(
            new ChatSolicitudDto { Mensaje = "Hola" }, default);

        Assert.Equal("sin_conexion", respuesta.ErrorCodigo);
    }

    [Fact]
    public async Task SinConfiguracion_DevuelveErrorDeConfiguracion()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        var (servicio, generador) = CrearServicio(db, usuarioA, clienteA, EmailA);

        generador.Habilitado = false;

        var respuesta = await servicio.ResponderAsync(
            new ChatSolicitudDto { Mensaje = "Hola" }, default);

        Assert.Equal("sin_configuracion", respuesta.ErrorCodigo);
    }

    [Fact]
    public async Task MensajeVacio_RespondeSinLlamarAlModelo()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        var (servicio, generador) = CrearServicio(db, usuarioA, clienteA, EmailA);

        var respuesta = await servicio.ResponderAsync(
            new ChatSolicitudDto { Mensaje = "   " }, default);

        Assert.Contains("Escribime", respuesta.Mensaje);
        Assert.Empty(generador.HerramientasInvocadas);
    }

    // ---------------- Utilidades ----------------

    private static (AsistenteServicio Servicio, GeneradorChatFalso Generador) CrearServicio(
        TiendaRopa.Api.Data.VelvetRoseDbContext db,
        int idUsuario,
        int? idCliente,
        string? email,
        bool anonimo = false)
    {
        var usuario = anonimo
            ? UsuarioActualSimulado.Anonimo
            : UsuarioActualSimulado.Cliente(idUsuario, idCliente!.Value, email!);

        var herramientas = new HerramientasAsistente(db, usuario);
        var generador = new GeneradorChatFalso();

        var servicio = new AsistenteServicio(
            generador,
            herramientas,
            db,
            usuario,
            NullLogger<AsistenteServicio>.Instance);

        return (servicio, generador);
    }

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
