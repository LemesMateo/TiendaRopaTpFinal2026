namespace TiendaRopa.Contracts.Identidad;

/// <summary>Usuario autenticado tal como lo devuelve la API.</summary>
public sealed class UsuarioActualDto
{
    public int Id { get; init; }

    public required string Email { get; init; }

    public required string Nombre { get; init; }

    public required string Apellido { get; init; }

    /// <summary>Rol: "Cliente" | "Administrador".</summary>
    public required string Rol { get; init; }

    /// <summary>Id del Cliente asociado; null para usuarios sin perfil de cliente.</summary>
    public int? IdCliente { get; init; }

    public string? Telefono { get; init; }

    public string? Direccion { get; init; }
}
