using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TiendaRopa.Api.Auth;
using TiendaRopa.Api.Data;
using TiendaRopa.Api.Entities;
using TiendaRopa.Contracts.Identidad;

namespace TiendaRopa.Api.Services;

/// <summary>
/// Registro, login y usuario actual. Contraseñas con <see cref="PasswordHasher{TUser}"/>
/// de ASP.NET Core (PBKDF2 con salt aleatorio) — jamás en texto plano.
/// </summary>
public sealed class AuthService(
    VelvetRoseDbContext db,
    TokenService tokens,
    PasswordHasher<Usuario> hasher,
    IUsuarioActual usuarioActual)
{
    public async Task<(AuthResponse? Resultado, string? Error, int StatusCode)> RegistroAsync(
        RegistroRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await db.Usuarios.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return (null, "Ya existe una cuenta con ese email.", StatusCodes.Status409Conflict);
        }

        var rolCliente = await db.Roles
            .FirstOrDefaultAsync(r => r.Nombre == Roles.Cliente && r.Activo, cancellationToken);

        if (rolCliente is null)
        {
            return (null, "El rol Cliente no está disponible.", StatusCodes.Status500InternalServerError);
        }

        var usuario = new Usuario
        {
            Email = email,
            PasswordHash = string.Empty, // se asigna abajo con el hasher
            IdRol = rolCliente.IdRol,
        };
        usuario.PasswordHash = hasher.HashPassword(usuario, request.Password);

        var cliente = new Cliente
        {
            Nombre = request.Nombre.Trim(),
            Apellido = request.Apellido.Trim(),
            Telefono = string.IsNullOrWhiteSpace(request.Telefono) ? null : request.Telefono.Trim(),
            Direccion = string.IsNullOrWhiteSpace(request.Direccion) ? null : request.Direccion.Trim(),
            Usuario = usuario,
        };

        db.Usuarios.Add(usuario);
        db.Clientes.Add(cliente);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Carrera contra otro registro con el mismo email (índice único).
            return (null, "Ya existe una cuenta con ese email.", StatusCodes.Status409Conflict);
        }

        return (CrearSesion(usuario, Roles.Cliente, cliente.IdCliente), null, StatusCodes.Status201Created);
    }

    public async Task<(AuthResponse? Resultado, string? Error, int StatusCode)> LoginAsync(
        LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var usuario = await db.Usuarios.AsNoTracking()
            .Include(u => u.Rol)
            .Include(u => u.Cliente)
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        var verificar = hasher.VerifyHashedPassword(
            usuario ?? new Usuario { Email = email, PasswordHash = string.Empty },
            usuario?.PasswordHash ?? string.Empty,
            request.Password);

        if (usuario is null
            || !usuario.Activo
            || verificar == PasswordVerificationResult.Failed
            || usuario.Rol is not { Activo: true })
        {
            return (null, "Email o contraseña incorrectos.", StatusCodes.Status401Unauthorized);
        }

        return (CrearSesion(usuario, usuario.Rol.Nombre, usuario.Cliente?.IdCliente), null, StatusCodes.Status200OK);
    }

    public async Task<UsuarioActualDto?> GetUsuarioActualAsync(CancellationToken cancellationToken)
    {
        if (!usuarioActual.EstaAutenticado || usuarioActual.IdUsuario == 0)
        {
            return null;
        }

        var usuario = await db.Usuarios.AsNoTracking()
            .Include(u => u.Cliente)
            .FirstOrDefaultAsync(u => u.IdUsuario == usuarioActual.IdUsuario, cancellationToken);

        if (usuario is null)
        {
            return null;
        }

        return Mapear(usuario, usuarioActual.Rol ?? Roles.Cliente, usuario.Cliente?.IdCliente);
    }

    private AuthResponse CrearSesion(Usuario usuario, string rol, int? idCliente)
    {
        var (token, expira) = tokens.Crear(
            usuario.IdUsuario,
            usuario.Email,
            usuario.Cliente?.Nombre ?? usuario.Email,
            usuario.Cliente?.Apellido ?? string.Empty,
            rol,
            idCliente);

        return new AuthResponse
        {
            Token = token,
            ExpiresAtUtc = expira,
            Usuario = Mapear(usuario, rol, idCliente),
        };
    }

    private static UsuarioActualDto Mapear(Usuario usuario, string rol, int? idCliente) => new()
    {
        Id = usuario.IdUsuario,
        Email = usuario.Email,
        Nombre = usuario.Cliente?.Nombre ?? usuario.Email,
        Apellido = usuario.Cliente?.Apellido ?? string.Empty,
        Rol = rol,
        Permisos = PermisosPorRol.De(rol),
        IdCliente = idCliente,
        Telefono = usuario.Cliente?.Telefono,
        Direccion = usuario.Cliente?.Direccion,
    };
}
