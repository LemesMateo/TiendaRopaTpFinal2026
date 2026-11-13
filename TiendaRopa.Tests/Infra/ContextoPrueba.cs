using Microsoft.EntityFrameworkCore;
using TiendaRopa.Api.Auth;
using TiendaRopa.Api.Data;
using TiendaRopa.Api.Entities;

namespace TiendaRopa.Tests.Infra;

/// <summary>
/// Datos de prueba en memoria (SQLite no: el contexto usa SQL Server).
/// Sigue la convención del proyecto: PascalCase, tablas singulares.
/// </summary>
public static class ContextoPrueba
{
    /// <summary>Crea un contexto limpio con el catálogo mínimo de la tienda.</summary>
    public static VelvetRoseDbContext Crear(string? nombre = null)
    {
        // Cada prueba usa una base en memoria propia: el proveedor InMemory
        // comparte el almacén por nombre, así que un nombre único aísla las
        // pruebas entre sí.
        var baseDatos = nombre ?? $"prueba-{Guid.NewGuid():N}";

        var opciones = new DbContextOptionsBuilder<VelvetRoseDbContext>()
            .UseInMemoryDatabase(baseDatos)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var db = new VelvetRoseDbContext(opciones);
        Semilla(db);
        return db;
    }

    /// <summary>Catálogo y usuarios base: dos clientes, un administrador, 3 productos.</summary>
    private static void Semilla(VelvetRoseDbContext db)
    {
        var rolCliente = new Rol { Nombre = Roles.Cliente, Activo = true };
        var rolAdmin = new Rol { Nombre = Roles.Administrador, Activo = true };
        db.Roles.AddRange(rolCliente, rolAdmin);

        var talleS = new Talle { Nombre = "S", Activo = true };
        var talleM = new Talle { Nombre = "M", Activo = true };
        var talleL = new Talle { Nombre = "L", Activo = true };
        db.Talles.AddRange(talleS, talleM, talleL);

        var colorNegro = new Color { Nombre = "Negro", CodigoHex = "#1C1C1E", Activo = true };
        var colorCiruela = new Color { Nombre = "Ciruela", CodigoHex = "#5C2D69", Activo = true };
        db.Colores.AddRange(colorNegro, colorCiruela);

        var categoria = new Categoria { Nombre = "Vestidos", Activo = true };
        db.Categorias.Add(categoria);

        db.EstadosPedido.AddRange(
            new EstadoPedido { Nombre = "Pendiente" },
            new EstadoPedido { Nombre = "Confirmado" },
            new EstadoPedido { Nombre = "Preparado" },
            new EstadoPedido { Nombre = "Entregado" },
            new EstadoPedido { Nombre = "Cancelado" });

        // Producto 1: variantes con stock y sin stock.
        var vestido = new Producto
        {
            Nombre = "Vestido Satinado Noche",
            Descripcion = "Vestido de satén con cuello barco.",
            PrecioBase = 62990,
            Activo = true,
            Categoria = categoria,
        };

        vestido.Variantes.Add(new ProductoVariante
        {
            Talle = talleM, Color = colorNegro, Stock = 4, Producto = vestido,
        });
        vestido.Variantes.Add(new ProductoVariante
        {
            Talle = talleL, Color = colorCiruela, Stock = 0, Producto = vestido,
        });

        // Producto 2: sólo una variante (para probar combinación inexistente).
        var blusa = new Producto
        {
            Nombre = "Blusa Pompoir Marfil",
            Descripcion = "Blusa de pompoir con manga bombacha.",
            PrecioBase = 28500,
            Activo = true,
            Categoria = categoria,
        };
        blusa.Variantes.Add(new ProductoVariante
        {
            Talle = talleS, Color = colorNegro, Stock = 7, Producto = blusa,
        });

        db.Productos.AddRange(vestido, blusa);

        // Cliente A (el de las pruebas) y cliente B (ajeno).
        var usuarioA = new Usuario
        {
            Email = "cliente.a@velvetrose.test",
            PasswordHash = "hash",
            Activo = true,
            Rol = rolCliente,
        };
        var usuarioB = new Usuario
        {
            Email = "cliente.b@velvetrose.test",
            PasswordHash = "hash",
            Activo = true,
            Rol = rolCliente,
        };
        db.Usuarios.AddRange(usuarioA, usuarioB);

        // Personal de tienda (el Administrador ya existe arriba; se agrega el
        // Vendedor): destino de las notificaciones de solicitudes.
        var rolVendedor = new Rol { Nombre = Roles.Vendedor, Activo = true };
        db.Roles.Add(rolVendedor);

        db.Usuarios.AddRange(
            new Usuario
            {
                Email = "admin.test@velvetrose.test",
                PasswordHash = "hash",
                Activo = true,
                Rol = rolAdmin,
            },
            new Usuario
            {
                Email = "vendedor.test@velvetrose.test",
                PasswordHash = "hash",
                Activo = true,
                Rol = rolVendedor,
            });

        db.Clientes.AddRange(
            new Cliente
            {
                Nombre = "Carla",
                Apellido = "Gómez",
                Telefono = "341-555-0101",
                Direccion = "Av. Siempreviva 742, Rosario",
                Usuario = usuarioA,
            },
            new Cliente
            {
                Nombre = "Bruno",
                Apellido = "Pérez",
                Telefono = "341-555-0202",
                Direccion = "Calle Falsa 123, Rosario",
                Usuario = usuarioB,
            });

        db.SaveChanges();
    }

    /// <summary>Id del cliente del usuario con el email indicado.</summary>
    public static int IdCliente(this VelvetRoseDbContext db, string email)
        => db.Clientes.Include(c => c.Usuario).Single(c => c.Usuario!.Email == email).IdCliente;

    /// <summary>Id del usuario con el email indicado.</summary>
    public static int IdUsuario(this VelvetRoseDbContext db, string email)
        => db.Usuarios.Single(u => u.Email == email).IdUsuario;
}

/// <summary>
/// Usuario actual simulado: simula el claims del JWT para las pruebas.
/// </summary>
public sealed class UsuarioActualSimulado : IUsuarioActual
{
    public int IdUsuario { get; init; }
    public string? Email { get; init; }
    public string? Rol { get; init; }
    public int? IdCliente { get; init; }
    public bool EstaAutenticado { get; init; }
    public bool EsAdministrador { get; init; }

    /// <summary>True si el rol es del personal de tienda (Administrador o Vendedor).</summary>
    public bool EsPersonalTienda =>
        EsAdministrador || string.Equals(Rol, Roles.Vendedor, StringComparison.Ordinal);

    /// <summary>True si el rol tiene el permiso indicado.</summary>
    public bool TienePermiso(string permiso) => PermisosPorRol.Tiene(Rol, permiso);

    /// <summary>Usuario autenticado con perfil de cliente.</summary>
    public static UsuarioActualSimulado Cliente(int idUsuario, int idCliente, string email)
        => new()
        {
            IdUsuario = idUsuario,
            IdCliente = idCliente,
            Email = email,
            Rol = Roles.Cliente,
            EstaAutenticado = true,
        };

    /// <summary>Personal de tienda con permisos restringidos (Vendedor).</summary>
    public static UsuarioActualSimulado Vendedor(int idUsuario, string email)
        => new()
        {
            IdUsuario = idUsuario,
            Email = email,
            Rol = Roles.Vendedor,
            EstaAutenticado = true,
        };

    /// <summary>Administrador de la tienda.</summary>
    public static UsuarioActualSimulado Administrador(int idUsuario, string email)
        => new()
        {
            IdUsuario = idUsuario,
            Email = email,
            Rol = Roles.Administrador,
            EstaAutenticado = true,
            EsAdministrador = true,
        };

    /// <summary>Visitante sin sesión.</summary>
    public static UsuarioActualSimulado Anonimo => new() { EstaAutenticado = false };
}
