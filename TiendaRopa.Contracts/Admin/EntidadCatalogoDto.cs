namespace TiendaRopa.Contracts.Admin;

/// <summary>Detalle de catálogo para selects del panel administrativo.</summary>
public sealed class EntidadCatalogoDto
{
    public int Id { get; init; }

    public required string Nombre { get; init; }

    public string? Descripcion { get; init; }

    /// <summary>Sólo colores.</summary>
    public string? CodigoHex { get; init; }

    public bool Activo { get; init; } = true;

    /// <summary>Cantidad de productos/variantes que referencian esta entidad.</summary>
    public int EnUso { get; init; }
}
