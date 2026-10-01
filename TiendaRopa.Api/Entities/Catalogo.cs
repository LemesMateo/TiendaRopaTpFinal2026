namespace TiendaRopa.Api.Entities;

public class Categoria
{
    public int IdCategoria { get; set; }

    public required string Nombre { get; set; }

    public string? Descripcion { get; set; }

    public bool Activo { get; set; } = true;

    public List<Producto> Productos { get; set; } = [];
}

public class Producto
{
    public int IdProducto { get; set; }

    public int IdCategoria { get; set; }

    public required string Nombre { get; set; }

    public string? Descripcion { get; set; }

    /// <summary>Precio de referencia; cada variante puede tener el suyo.</summary>
    public decimal PrecioBase { get; set; }

    /// <summary>Los productos desactivados se conservan (historial de pedidos).</summary>
    public bool Activo { get; set; } = true;

    public Categoria? Categoria { get; set; }

    public List<ProductoImagen> Imagenes { get; set; } = [];

    public List<ProductoVariante> Variantes { get; set; } = [];
}

public class ProductoImagen
{
    public int IdProductoImagen { get; set; }

    public int IdProducto { get; set; }

    public required string UrlImagen { get; set; }

    public int Orden { get; set; }

    public bool Principal { get; set; }

    public Producto? Producto { get; set; }
}

public class Talle
{
    public int IdTalle { get; set; }

    public required string Nombre { get; set; }

    public string? Descripcion { get; set; }

    public bool Activo { get; set; } = true;

    public List<ProductoVariante> Variantes { get; set; } = [];
}

public class Color
{
    public int IdColor { get; set; }

    public required string Nombre { get; set; }

    public string? CodigoHex { get; set; }

    public bool Activo { get; set; } = true;

    public List<ProductoVariante> Variantes { get; set; } = [];
}

/// <summary>Combinación producto + talle + color con stock propio.</summary>
public class ProductoVariante
{
    public int IdProductoVariante { get; set; }

    public int IdProducto { get; set; }

    public int IdTalle { get; set; }

    public int IdColor { get; set; }

    /// <summary>Stock interno; la política de reserva evita valores negativos.</summary>
    public int Stock { get; set; }

    /// <summary>Precio propio; null = aplicar Producto.PrecioBase.</summary>
    public decimal? Precio { get; set; }

    public Producto? Producto { get; set; }

    public Talle? Talle { get; set; }

    public Color? Color { get; set; }

    public List<PedidoDetalle> Detalles { get; set; } = [];
}
