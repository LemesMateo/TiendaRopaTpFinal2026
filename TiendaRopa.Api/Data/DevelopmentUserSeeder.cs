using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TiendaRopa.Api.Auth;
using TiendaRopa.Api.Entities;

namespace TiendaRopa.Api.Data;

/// <summary>
/// Ampliación del seeder: usuarios de DESARROLLO (uno por rol).
/// Sólo corre en entorno Development y es idempotente: si ya hay usuarios,
/// no toca nada. Las contraseñas se guardan con PasswordHasher (PBKDF2), jamás
/// en texto plano — estas credenciales son SÓLO para probar en local.
/// </summary>
public static class DevelopmentUserSeeder
{
    public const string EmailAdmin = "admin@velvetrose.dev";
    public const string EmailCliente = "cliente@velvetrose.dev";
    public const string EmailVendedor = "vendedor@velvetrose.dev";
    public const string PasswordDemo = "Velvet123!"; // DESARROLLO ÚNICAMENTE

    public static async Task SeedAsync(VelvetRoseDbContext db, CancellationToken cancellationToken = default)
    {
        // El rol Vendedor siempre existe (idempotente): aunque la base ya tenga
        // usuarios, se asegura el rol para no dejar la tienda sin personal.
        var rolVendedor = await db.Roles.FirstOrDefaultAsync(
            r => r.Nombre == Roles.Vendedor, cancellationToken);

        if (rolVendedor is null)
        {
            rolVendedor = new Rol
            {
                Nombre = Roles.Vendedor,
                Descripcion = "Consulta y gestiona pedidos y solicitudes de la tienda.",
            };

            db.Roles.Add(rolVendedor);
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (!rolVendedor.Activo)
        {
            rolVendedor.Activo = true;
            await db.SaveChangesAsync(cancellationToken);
        }

        // El usuario vendedor de desarrollo también es idempotente: se crea si
        // no existe, para que la tienda siempre tenga personal con permisos
        // restringidos disponible (aunque la base ya tenga otros usuarios).
        var vendedorExiste = await db.Usuarios.AnyAsync(
            u => u.Email == EmailVendedor, cancellationToken);

        if (!vendedorExiste)
        {
            var hasherVendedor = new PasswordHasher<Usuario>();

            var vendedor = new Usuario
            {
                Email = EmailVendedor,
                PasswordHash = string.Empty,
                IdRol = rolVendedor.IdRol,
                Rol = rolVendedor,
            };
            vendedor.PasswordHash = hasherVendedor.HashPassword(vendedor, PasswordDemo);

            db.Usuarios.Add(vendedor);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (await db.Usuarios.AnyAsync(u => u.Email == EmailAdmin, cancellationToken))
        {
            return;
        }

        var rolAdmin = await db.Roles.FirstOrDefaultAsync(r => r.Nombre == Roles.Administrador, cancellationToken)
            ?? new Rol { Nombre = Roles.Administrador, Descripcion = "Gestión de la tienda." };
        var rolCliente = await db.Roles.FirstOrDefaultAsync(r => r.Nombre == Roles.Cliente, cancellationToken)
            ?? new Rol { Nombre = Roles.Cliente, Descripcion = "Compra y consulta de pedidos." };

        var hasher = new PasswordHasher<Usuario>();

        var admin = new Usuario
        {
            Email = EmailAdmin,
            PasswordHash = string.Empty,
            IdRol = rolAdmin.IdRol,
            Rol = rolAdmin,
        };
        admin.PasswordHash = hasher.HashPassword(admin, PasswordDemo);

        var cliente = new Usuario
        {
            Email = EmailCliente,
            PasswordHash = string.Empty,
            IdRol = rolCliente.IdRol,
            Rol = rolCliente,
        };
        cliente.PasswordHash = hasher.HashPassword(cliente, PasswordDemo);

        db.Usuarios.AddRange(admin, cliente);
        db.Clientes.AddRange(
            new Cliente
            {
                Nombre = "Admin",
                Apellido = "Velvet",
                Usuario = admin,
            },
            new Cliente
            {
                Nombre = "Carla",
                Apellido = "Gómez",
                Telefono = "341-555-0101",
                Direccion = "Av. Siempreviva 742, Rosario",
                Usuario = cliente,
            });

        await db.SaveChangesAsync(cancellationToken);
    }
}
