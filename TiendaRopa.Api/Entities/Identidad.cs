namespace TiendaRopa.Api.Entities;

// Modelo de datos de Velvet Rose Boutique.
// Convención: PascalCase, PK "Id<Rol>", FK "Id<Relacion>", tablas singulares
// (ver docs/backend.md). Referencia: DER.png y las 12 entidades de la propuesta.

public class Rol
{
    public int IdRol { get; set; }

    public required string Nombre { get; set; }

    public string? Descripcion { get; set; }

    public bool Activo { get; set; } = true;

    public List<Usuario> Usuarios { get; set; } = [];
}

public class Usuario
{
    public int IdUsuario { get; set; }

    public required string Email { get; set; }

    /// <summary>Hash del password. El esquema de hash se definirá en la etapa de autenticación.</summary>
    public required string PasswordHash { get; set; }

    public int IdRol { get; set; }

    public bool Activo { get; set; } = true;

    public DateTime FechaAlta { get; set; } = DateTime.UtcNow;

    public Rol? Rol { get; set; }

    public Cliente? Cliente { get; set; }
}

public class Cliente
{
    public int IdCliente { get; set; }

    public int IdUsuario { get; set; }

    public required string Nombre { get; set; }

    public required string Apellido { get; set; }

    public string? Telefono { get; set; }

    public string? Direccion { get; set; }

    public Usuario? Usuario { get; set; }

    public List<Pedido> Pedidos { get; set; } = [];
}
