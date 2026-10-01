namespace TiendaRopa.Contracts.Pedidos;

/// <summary>Error de negocio devuelto por la API al confirmar/cancelar pedidos.</summary>
public sealed class ErrorOperacionDto
{
    public required string Title { get; init; }

    public string? Detail { get; init; }

    /// <summary>Errores de validación campo → mensajes (si los hay).</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
}
