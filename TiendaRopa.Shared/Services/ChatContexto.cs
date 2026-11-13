namespace TiendaRopa.Shared.Services;

/// <summary>
/// Contexto de la conversación que se conserva durante la sesión:
/// qué producto está mirando el cliente y qué pedido viene consultando.
///
/// Es estado de sesión, no identidad: el backend valida ambos valores antes
/// de usarlos (un pedido ajeno se descarta).
/// </summary>
public sealed class ChatContexto
{
    /// <summary>Producto abierto en la página de detalle (opcional).</summary>
    public int? IdProducto { get; set; }

    /// <summary>Pedido que el cliente viene consultando (opcional).</summary>
    public int? IdPedido { get; set; }

    /// <summary>Limpia el contexto (por ejemplo, al salir de la tienda).</summary>
    public void Limpiar()
    {
        IdProducto = null;
        IdPedido = null;
    }
}
