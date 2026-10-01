using System.Text.Json;

namespace TiendaRopa.Shared.Services;

/// <summary>
/// Carrito reutilizable entre Web y MAUI.
///
/// El almacenamiento local difiere según el host, así que Shared sólo conoce
/// <see cref="IPreferenciasLocal"/> (localStorage en navegador y WebView).
/// La persistencia es diferida: se guarda sólo después de InicializarAsync
/// (en el prerender no hay JS disponible).
///
/// La política del negocio: el carrito NO reserva ni descuenta stock en SQL
/// Server; esa validación es exclusiva del backend al confirmar el pedido.
/// </summary>
public sealed class CarritoService(IPreferenciasLocal almacenamiento)
{
    private const string Clave = "velvet.carrito";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly List<ItemCarrito> _items = [];
    private bool inicializado;

    /// <summary>Se dispara cuando cambia el contenido del carrito.</summary>
    public event Action? Cambio;

    public IReadOnlyList<ItemCarrito> Items => _items;

    public int CantidadTotal => _items.Sum(i => i.Cantidad);

    public decimal Total => _items.Sum(i => i.Subtotal);

    /// <summary>Carga el carrito persistido. Llamar desde OnAfterRenderAsync (requiere JS).</summary>
    public async Task InicializarAsync()
    {
        if (inicializado)
        {
            return;
        }

        inicializado = true;
        var json = await almacenamiento.ObtenerAsync(Clave);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var cargados = JsonSerializer.Deserialize<List<ItemCarrito>>(json, Json);
            if (cargados is { Count: > 0 })
            {
                _items.Clear();
                _items.AddRange(cargados);
                Cambio?.Invoke();
            }
        }
        catch (JsonException)
        {
            // Corrupción local: se ignora y empieza vacío.
        }
    }

    /// <summary>Agrega una variante (o suma cantidad si ya existe la misma combinación).</summary>
    public async Task AgregarAsync(ItemCarrito item)
    {
        // Garantiza que el carrito persistido esté cargado antes de mutarlo:
        // sin esto, la página perdería lo agregado al navegar/recargar.
        await InicializarAsync();

        var existente = _items.FirstOrDefault(
            i => i.IdProductoVariante == item.IdProductoVariante);

        if (existente is not null)
        {
            existente.Cantidad += Math.Max(1, item.Cantidad);
        }
        else
        {
            item.Cantidad = Math.Max(1, item.Cantidad);
            _items.Add(item);
        }

        await PersistirAsync();
    }

    public async Task QuitarAsync(int idProductoVariante)
    {
        await InicializarAsync();
        _items.RemoveAll(i => i.IdProductoVariante == idProductoVariante);
        await PersistirAsync();
    }

    /// <summary>Cambia la cantidad (menor a 1 elimina la línea).</summary>
    public async Task CambiarCantidadAsync(int idProductoVariante, int cantidad)
    {
        await InicializarAsync();
        var item = _items.FirstOrDefault(i => i.IdProductoVariante == idProductoVariante);
        if (item is null)
        {
            return;
        }

        if (cantidad < 1)
        {
            _items.RemoveAll(i => i.IdProductoVariante == idProductoVariante);
        }
        else
        {
            item.Cantidad = cantidad;
        }

        await PersistirAsync();
    }

    public async Task VaciarAsync()
    {
        await InicializarAsync();
        _items.Clear();
        await PersistirAsync();
    }

    private async Task PersistirAsync()
    {
        Cambio?.Invoke();

        if (inicializado)
        {
            await almacenamiento.GuardarAsync(Clave, JsonSerializer.Serialize(_items, Json));
        }
    }
}
