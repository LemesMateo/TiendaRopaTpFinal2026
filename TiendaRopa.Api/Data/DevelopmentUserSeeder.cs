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
    public const string PasswordDemo = "Velvet123!"; // DESARROLLO ÚNICAMENTE

    public static async Task SeedAsync(VelvetRoseDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Usuarios.AnyAsync(cancellationToken))
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
