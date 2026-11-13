using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TiendaRopa.Api.Auth;
using TiendaRopa.Api.Entities;
using TiendaRopa.Api.Services;
using TiendaRopa.Contracts.Solicitudes;
using TiendaRopa.Tests.Infra;

namespace TiendaRopa.Tests;

/// <summary>
/// Pruebas del ciclo de vida de las solicitudes de atención:
/// propuesta, teléfono, confirmación, idempotencia, aislamiento y
/// notificaciones.
/// </summary>
public sealed class SolicitudServiceTests
{
    private const string EmailA = "cliente.a@velvetrose.test";
    private const string EmailB = "cliente.b@velvetrose.test";

    // ---------------- Propuestas ----------------

    [Fact]
    public async Task PrepararPropuesta_ConsultaDePedido_DevuelvePropuestaPendiente()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        var idPedido = CrearPedido(db, clienteA);

        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var (propuesta, error, status) = await servicio.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.ConsultaPedido, idPedido, null, null, null, null,
            "Quiero cambiar la dirección de entrega.", default);

        Assert.Equal(StatusCodes.Status201Created, status);
        Assert.Null(error);
        Assert.NotNull(propuesta);
        Assert.Equal("Pendiente", propuesta.Estado);
        Assert.Equal(idPedido, propuesta.IdPedido);
        Assert.Equal(SolicitudTipos.ConsultaPedido, propuesta.Tipo);
    }

    [Fact]
    public async Task PrepararPropuesta_ProductoInexistente_Rechaza()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);

        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var (propuesta, error, status) = await servicio.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.SolicitudReposicion, null, 999999, null, "L", "Negro",
            "Aviso que no hay stock.", default);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Null(propuesta);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task PrepararPropuesta_VarianteInexistente_UsaTalleYColorEnTexto()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        var idProducto = await db.Productos
            .Where(p => p.Nombre == "Vestido Satinado Noche")
            .Select(p => p.IdProducto)
            .FirstAsync();

        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var (propuesta, error, status) = await servicio.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.SolicitudReposicion, null, idProducto, null, "XXL", "Fucsia",
            "No existe esta combinación y la quiero.", default);

        Assert.Equal(StatusCodes.Status201Created, status);
        Assert.NotNull(propuesta);

        // No se inventa un IdProductoVariante: se describe en texto.
        Assert.Null(propuesta.IdProductoVariante);
        Assert.Equal("XXL", propuesta.TalleDescripcion);
        Assert.Equal("Fucsia", propuesta.ColorDescripcion);
    }

    [Fact]
    public async Task PrepararPropuesta_SinSesion_Rechaza()
    {
        using var db = ContextoPrueba.Crear();
        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Anonimo);

        var (propuesta, error, status) = await servicio.PrepararPropuestaAsync(
            1, SolicitudTipos.ConsultaGeneral, null, null, null, null, null, "Hola", default);

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.Null(propuesta);
    }

    // ---------------- Teléfono ----------------

    [Fact]
    public async Task PropostaSinTelefonoEnPerfil_QuedaPendienteDeConfirmacion()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);

        // El cliente de la semilla tiene teléfono; se lo quitamos para el caso.
        var cliente = await db.Clientes.FirstAsync(c => c.IdCliente == clienteA);
        cliente.Telefono = null;
        await db.SaveChangesAsync();

        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var (propuesta, _, _) = await servicio.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.ConsultaGeneral, null, null, null, null, null,
            "Necesito ayuda", default);

        Assert.NotNull(propuesta);
        Assert.Equal(TelefonoEstados.PendienteConfirmacion, propuesta.TelefonoEstado);
        Assert.Null(propuesta.TelefonoContacto);
    }

    [Fact]
    public async Task ConfirmarTelefono_LoGuardaEnLaPropuesta()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);

        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var (propuesta, _, _) = await servicio.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.ConsultaGeneral, null, null, null, null, null,
            "Necesito ayuda", default);

        var (actualizada, error, status) = await servicio.ConfirmarTelefonoAsync(
            propuesta!.IdPropuesta, "341-555-7777", guardarEnPerfil: false, default);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Null(error);
        Assert.NotNull(actualizada);
        Assert.Equal(TelefonoEstados.Confirmado, actualizada.TelefonoEstado);
        Assert.Equal("341-555-7777", actualizada.TelefonoContacto);
    }

    [Fact]
    public async Task ConfirmarTelefono_SinGuardarEnPerfil_NoModificaElPerfil()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);

        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var (propuesta, _, _) = await servicio.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.ConsultaGeneral, null, null, null, null, null,
            "Necesito ayuda", default);

        await servicio.ConfirmarTelefonoAsync(
            propuesta!.IdPropuesta, "341-555-7777", guardarEnPerfil: false, default);

        var telefono = await db.Clientes
            .Where(c => c.IdCliente == clienteA)
            .Select(c => c.Telefono)
            .FirstAsync();

        // El perfil sigue con el teléfono original.
        Assert.Equal("341-555-0101", telefono);
    }

    [Fact]
    public async Task ConfirmarTelefono_ConConsentimiento_ActualizaElPerfil()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);

        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var (propuesta, _, _) = await servicio.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.ConsultaGeneral, null, null, null, null, null,
            "Necesito ayuda", default);

        await servicio.ConfirmarTelefonoAsync(
            propuesta!.IdPropuesta, "341-555-7777", guardarEnPerfil: true, default);

        var telefono = await db.Clientes
            .Where(c => c.IdCliente == clienteA)
            .Select(c => c.Telefono)
            .FirstAsync();

        Assert.Equal("341-555-7777", telefono);
    }

    [Fact]
    public async Task ConfirmarTelefono_NumeroInvalido_Rechaza()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);

        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var (propuesta, _, _) = await servicio.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.ConsultaGeneral, null, null, null, null, null,
            "Necesito ayuda", default);

        var (actualizada, error, status) = await servicio.ConfirmarTelefonoAsync(
            propuesta!.IdPropuesta, "12", guardarEnPerfil: false, default);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Null(actualizada);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task ConfirmarTelefono_PropuestaDeOtroUsuario_NoLaToca()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var clienteB = db.IdCliente(EmailB);
        var usuarioA = db.IdUsuario(EmailA);
        var usuarioB = db.IdUsuario(EmailB);

        var (servicioA, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));
        var (servicioB, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioB, clienteB, EmailB));

        var (propuesta, _, _) = await servicioA.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.ConsultaGeneral, null, null, null, null, null,
            "Necesito ayuda", default);

        var (actualizada, _, status) = await servicioB.ConfirmarTelefonoAsync(
            propuesta!.IdPropuesta, "341-555-0000", guardarEnPerfil: false, default);

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Null(actualizada);
    }

    // ---------------- Confirmación ----------------

    [Fact]
    public async Task ConfirmarPropuesta_CreaLaSolicitudYLasNotificaciones()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);
        var idPedido = CrearPedido(db, clienteA);

        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var (propuesta, _, _) = await servicio.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.ConsultaPedido, idPedido, null, null, null, null,
            "Necesito cambiar la dirección.", default);

        await servicio.ConfirmarTelefonoAsync(
            propuesta!.IdPropuesta, "341-555-0101", guardarEnPerfil: false, default);

        var (solicitud, error, status) = await servicio.ConfirmarPropuestaAsync(
            propuesta.IdPropuesta, new ConfirmarPropuestaRequest(), default);

        Assert.Equal(StatusCodes.Status201Created, status);
        Assert.Null(error);
        Assert.NotNull(solicitud);
        Assert.Equal(SolicitudEstados.Abierta, solicitud.Estado);
        Assert.Equal(TelefonoEstados.Confirmado, solicitud.TelefonoEstado);
        Assert.Equal("341-555-0101", solicitud.TelefonoContacto);

        // Notificación interna para cada member del personal de tienda.
        var notificaciones = await db.Notificaciones.CountAsync();
        Assert.True(notificaciones >= 1, $"notificaciones={notificaciones}");
    }

    [Fact]
    public async Task ConfirmarPropuesta_SinPreConfirmar_UsaElTelefonoDelPerfil()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);

        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var (propuesta, _, _) = await servicio.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.ConsultaGeneral, null, null, null, null, null,
            "Consulta", default);

        // El teléfono del perfil alcanza: confirmar la solicitud ES
        // confirmar el número (que el chat mostró enmascarado).
        var (solicitud, error, status) = await servicio.ConfirmarPropuestaAsync(
            propuesta!.IdPropuesta, new ConfirmarPropuestaRequest(), default);

        Assert.Equal(StatusCodes.Status201Created, status);
        Assert.Null(error);
        Assert.NotNull(solicitud);
        Assert.Equal("341-555-0101", solicitud.TelefonoContacto);
        Assert.Equal(TelefonoEstados.Confirmado, solicitud.TelefonoEstado);
    }

    [Fact]
    public async Task ConfirmarPropuesta_SinTelefonoConfirmado_Rechaza()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);

        var cliente = await db.Clientes.FirstAsync(c => c.IdCliente == clienteA);
        cliente.Telefono = null;
        await db.SaveChangesAsync();

        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var (propuesta, _, _) = await servicio.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.ConsultaGeneral, null, null, null, null, null,
            "Necesito ayuda", default);

        var (solicitud, error, status) = await servicio.ConfirmarPropuestaAsync(
            propuesta!.IdPropuesta, new ConfirmarPropuestaRequest(), default);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Null(solicitud);
        Assert.Contains("teléfono", error);
    }

    [Fact]
    public async Task ConfirmarPropuesta_PedidoAjeno_Rechaza()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var clienteB = db.IdCliente(EmailB);
        var usuarioA = db.IdUsuario(EmailA);

        var idPedidoAjeno = CrearPedido(db, clienteB);

        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        // La propuesta con un pedido ajeno ni siquiera se prepara.
        var (propuesta, error, status) = await servicio.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.ConsultaPedido, idPedidoAjeno, null, null, null, null,
            "Consulta", default);

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.Null(propuesta);
        Assert.NotNull(error);
        Assert.Equal(0, await db.SolicitudesAtencion.CountAsync());
    }

    [Fact]
    public async Task ConfirmarPropuesta_DosVeces_NoDuplicaLaSolicitud()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);

        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var (propuesta, _, _) = await servicio.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.ConsultaGeneral, null, null, null, null, null,
            "Consulta", default);

        await servicio.ConfirmarTelefonoAsync(
            propuesta!.IdPropuesta, "341-555-0101", guardarEnPerfil: false, default);

        var (primera, _, _) = await servicio.ConfirmarPropuestaAsync(
            propuesta.IdPropuesta, new ConfirmarPropuestaRequest(), default);

        // Segundo clic (o reintento): la misma solicitud, no una nueva.
        var (segunda, _, status) = await servicio.ConfirmarPropuestaAsync(
            propuesta.IdPropuesta, new ConfirmarPropuestaRequest(), default);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.NotNull(primera);
        Assert.NotNull(segunda);
        Assert.Equal(primera.IdSolicitud, segunda.IdSolicitud);

        var total = await db.SolicitudesAtencion.CountAsync();
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task ConfirmarPropuesta_ConClaveDeIdempotencia_NoDuplica()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);

        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var (propuesta, _, _) = await servicio.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.ConsultaGeneral, null, null, null, null, null,
            "Consulta", default);

        await servicio.ConfirmarTelefonoAsync(
            propuesta!.IdPropuesta, "341-555-0101", guardarEnPerfil: false, default);

        var clave = "clave-fija-de-prueba";

        var (primera, _, _) = await servicio.ConfirmarPropuestaAsync(
            propuesta.IdPropuesta,
            new ConfirmarPropuestaRequest { ClaveIdempotencia = clave },
            default);

        var (segunda, _, status) = await servicio.ConfirmarPropuestaAsync(
            propuesta.IdPropuesta,
            new ConfirmarPropuestaRequest { ClaveIdempotencia = clave },
            default);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.Equal(primera!.IdSolicitud, segunda!.IdSolicitud);

        var total = await db.SolicitudesAtencion.CountAsync();
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task ConfirmarPropuesta_DeOtroUsuario_Rechaza()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var clienteB = db.IdCliente(EmailB);
        var usuarioA = db.IdUsuario(EmailA);
        var usuarioB = db.IdUsuario(EmailB);

        var (servicioA, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));
        var (servicioB, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioB, clienteB, EmailB));

        var (propuesta, _, _) = await servicioA.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.ConsultaGeneral, null, null, null, null, null,
            "Consulta", default);

        var (solicitud, _, status) = await servicioB.ConfirmarPropuestaAsync(
            propuesta!.IdPropuesta, new ConfirmarPropuestaRequest(), default);

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Null(solicitud);
    }

    [Fact]
    public async Task CancelarPropuesta_NoCreaSolicitud()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var usuarioA = db.IdUsuario(EmailA);

        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var (propuesta, _, _) = await servicio.PrepararPropuestaAsync(
            clienteA, SolicitudTipos.ConsultaGeneral, null, null, null, null, null,
            "Consulta", default);

        var (cancelada, _, status) = await servicio.CancelarPropuestaAsync(
            propuesta!.IdPropuesta, default);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.NotNull(cancelada);
        Assert.Equal("Cancelada", cancelada.Estado);
        Assert.Equal(0, await db.SolicitudesAtencion.CountAsync());
    }

    // ---------------- Aislamiento ----------------

    [Fact]
    public async Task MisSolicitudes_SoloDevuelveLasDelCliente()
    {
        using var db = ContextoPrueba.Crear();
        var clienteA = db.IdCliente(EmailA);
        var clienteB = db.IdCliente(EmailB);
        var usuarioA = db.IdUsuario(EmailA);

        db.SolicitudesAtencion.Add(new SolicitudAtencion
        {
            IdCliente = clienteA,
            IdUsuario = usuarioA,
            Tipo = SolicitudTipos.ConsultaGeneral,
            Mensaje = "Consulta de A",
            Estado = SolicitudEstados.Abierta,
        });

        db.SolicitudesAtencion.Add(new SolicitudAtencion
        {
            IdCliente = clienteB,
            IdUsuario = usuarioA,
            Tipo = SolicitudTipos.ConsultaGeneral,
            Mensaje = "Consulta de B",
            Estado = SolicitudEstados.Abierta,
        });

        await db.SaveChangesAsync();

        var (servicio, _) = CrearServicio(db, UsuarioActualSimulado.Cliente(usuarioA, clienteA, EmailA));

        var mias = await servicio.GetMisSolicitudesAsync(default);

        Assert.Single(mias);
        Assert.Equal("Consulta de A", mias[0].Mensaje);
    }

    // ---------------- Utilidades ----------------

    private static (SolicitudService Servicio, NotificacionService Notificaciones) CrearServicio(
        TiendaRopa.Api.Data.VelvetRoseDbContext db, UsuarioActualSimulado usuario)
    {
        var solicitudes = new SolicitudService(db, usuario, NullLogger<SolicitudService>.Instance);
        var notificaciones = new NotificacionService(db, usuario, NullLogger<NotificacionService>.Instance);
        return (solicitudes, notificaciones);
    }

    private static int CrearPedido(TiendaRopa.Api.Data.VelvetRoseDbContext db, int idCliente)
    {
        var producto = db.Productos
            .Include(p => p.Variantes)
            .Single(p => p.Nombre == "Vestido Satinado Noche");

        var idEstado = db.EstadosPedido.Single(e => e.Nombre == "Pendiente").IdEstadoPedido;

        var pedido = new Pedido
        {
            IdCliente = idCliente,
            IdEstadoPedido = idEstado,
            FechaPedido = DateTime.UtcNow,
            Total = 62990,
        };

        pedido.Detalles.Add(new PedidoDetalle
        {
            IdProductoVariante = producto.Variantes.First().IdProductoVariante,
            Cantidad = 1,
            PrecioUnitario = 62990,
            Subtotal = 62990,
        });

        db.Pedidos.Add(pedido);
        db.SaveChanges();

        return pedido.IdPedido;
    }
}
