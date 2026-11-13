using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TiendaRopa.Api.Auth;
using TiendaRopa.Api.Data;
using TiendaRopa.Api.Entities;
using TiendaRopa.Contracts.Solicitudes;

namespace TiendaRopa.Api.Asistente;

/// <summary>
/// Resultado de ejecutar una herramienta del asistente.
/// <see cref="RequiereAutenticacion"/> indica que la consulta necesita que el
/// cliente inicie sesión antes de devolver datos.
/// </summary>
public sealed record ResultadoHerramienta(
    bool Exito,
    object? Datos,
    string? Error = null,
    bool RequiereAutenticacion = false)
{
    public static ResultadoHerramienta Ok(object datos) => new(true, datos);

    public static ResultadoHerramienta Fallido(string error, bool requiereAutenticacion = false)
        => new(false, null, error, requiereAutenticacion);
}

/// <summary>
/// Herramientas de SOLO LECTURA que el modelo puede invocar.
/// Reglas duras:
///  - la identidad se resuelve SIEMPRE del JWT (<see cref="IUsuarioActual"/>),
///    nunca de un argumento del modelo;
///  - un cliente nunca ve pedidos de otro (404-equivalente, no "prohibido");
///  - no se devuelven teléfonos ni direcciones completos: se enmascaran;
///  - no se ejecuta SQL arbitrario: todo pasa por EF Core con parámetros.
/// </summary>
public sealed class HerramientasAsistente(VelvetRoseDbContext db, IUsuarioActual usuario)
{
    /// <summary>Nombres de las herramientas expuestas al modelo.</summary>
    public const string ListarMisPedidos = "listar_mis_pedidos";
    public const string ConsultarPedido = "consultar_pedido";
    public const string ConsultarMisDatos = "consultar_mis_datos";
    public const string BuscarProducto = "buscar_producto";
    public const string ConsultarVariantes = "consultar_variantes";

    /// <summary>Prepara una solicitud (NO la envía: queda a la espera de confirmación).</summary>
    public const string PrepararSolicitud = "preparar_solicitud";

    /// <summary>Consulta el estado de las solicitudes del propio cliente.</summary>
    public const string ConsultarMisSolicitudes = "consultar_mis_solicitudes";

    /// <summary>Nombres de herramientas permitidos (allowlist dura).</summary>
    public static readonly IReadOnlySet<string> Permitidas = new HashSet<string>(StringComparer.Ordinal)
    {
        ListarMisPedidos,
        ConsultarPedido,
        ConsultarMisDatos,
        BuscarProducto,
        ConsultarVariantes,
        PrepararSolicitud,
        ConsultarMisSolicitudes,
    };

    /// <summary>Ejecuta una herramienta por nombre con sus argumentos.</summary>
    public async Task<ResultadoHerramienta> EjecutarAsync(
        string nombre, IReadOnlyDictionary<string, object?>? argumentos, CancellationToken cancellationToken)
    {
        if (!Permitidas.Contains(nombre))
        {
            return ResultadoHerramienta.Fallido($"La herramienta '{nombre}' no existe.");
        }

        return nombre switch
        {
            ListarMisPedidos => await ListarMisPedidosAsync(cancellationToken),
            ConsultarPedido => await ConsultarPedidoAsync(argumentos, cancellationToken),
            ConsultarMisDatos => await ConsultarMisDatosAsync(cancellationToken),
            BuscarProducto => await BuscarProductoAsync(argumentos, cancellationToken),
            ConsultarVariantes => await ConsultarVariantesAsync(argumentos, cancellationToken),
            PrepararSolicitud => await PrepararSolicitudAsync(argumentos, cancellationToken),
            ConsultarMisSolicitudes => await ConsultarMisSolicitudesAsync(cancellationToken),
            _ => ResultadoHerramienta.Fallido($"La herramienta '{nombre}' no existe."),
        };
    }

    // ------------------------------------------------------------------
    // listar_mis_pedidos
    // ------------------------------------------------------------------

    private async Task<ResultadoHerramienta> ListarMisPedidosAsync(CancellationToken cancellationToken)
    {
        if (usuario.IdCliente is not { } idCliente)
        {
            return ResultadoHerramienta.Fallido(
                "Necesitás iniciar sesión para ver tus pedidos.",
                requiereAutenticacion: true);
        }

        var pedidos = await db.Pedidos.AsNoTracking()
            .Where(p => p.IdCliente == idCliente)
            .OrderByDescending(p => p.FechaPedido)
            .Select(p => new
            {
                p.IdPedido,
                p.FechaPedido,
                Estado = p.EstadoPedido!.Nombre,
                p.Total,
                Detalles = p.Detalles
                    .OrderBy(d => d.IdPedidoDetalle)
                    .Select(d => new
                    {
                        Producto = d.ProductoVariante!.Producto!.Nombre,
                        Talle = d.ProductoVariante.Talle!.Nombre,
                        Color = d.ProductoVariante.Color!.Nombre,
                        d.Cantidad,
                    })
                    .ToList(),
                Notas = p.Notas
                    .OrderByDescending(n => n.FechaActualizacion)
                    .Select(n => new
                    {
                        n.Nota,
                        n.Empresa,
                        n.CodigoSeguimiento,
                        n.FechaActualizacion,
                    })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        // Sólo datos mínimos: sin dirección, sin teléfono, sin otros clientes.
        var datos = pedidos.Select(p => new
        {
            id = p.IdPedido,
            fecha = p.FechaPedido.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            estado = p.Estado,
            total = p.Total,
            productos = p.Detalles
                .Select(d => $"{d.Producto} (talle {d.Talle}, color {d.Color}) x{d.Cantidad}")
                .ToArray(),
            ultima_actualizacion = p.Notas.Count > 0
                ? new
                {
                    nota = p.Notas[0].Nota,
                    empresa = p.Notas[0].Empresa,
                    codigo_seguimiento = p.Notas[0].CodigoSeguimiento,
                    fecha = p.Notas[0].FechaActualizacion.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                }
                : null,
        }).ToList();

        return ResultadoHerramienta.Ok(new
        {
            cantidad = datos.Count,
            pedidos = datos,
        });
    }

    // ------------------------------------------------------------------
    // consultar_pedido
    // ------------------------------------------------------------------

    private async Task<ResultadoHerramienta> ConsultarPedidoAsync(
        IReadOnlyDictionary<string, object?>? argumentos, CancellationToken cancellationToken)
    {
        if (usuario.IdCliente is not { } idCliente)
        {
            return ResultadoHerramienta.Fallido(
                "Necesitás iniciar sesión para consultar tus pedidos.",
                requiereAutenticacion: true);
        }

        if (!TryGetInt(argumentos, "id_pedido", out var idPedido) || idPedido <= 0)
        {
            return ResultadoHerramienta.Fallido(
                "Indicá el número de pedido (id_pedido) que querés consultar.");
        }

        // La pertenencia se resuelve en el backend: si el pedido es de otro
        // cliente se responde "no encontrado" (no se confirma su existencia).
        var esPropio = await db.Pedidos.AsNoTracking()
            .AnyAsync(p => p.IdPedido == idPedido && p.IdCliente == idCliente, cancellationToken);

        if (!esPropio)
        {
            return ResultadoHerramienta.Fallido(
                $"No encontré ningún pedido tuyo con el número {idPedido}.");
        }

        var pedido = await db.Pedidos.AsNoTracking()
            .Where(p => p.IdPedido == idPedido)
            .Select(p => new
            {
                p.IdPedido,
                p.FechaPedido,
                Estado = p.EstadoPedido!.Nombre,
                p.Total,
                Detalles = p.Detalles
                    .OrderBy(d => d.IdPedidoDetalle)
                    .Select(d => new
                    {
                        Producto = d.ProductoVariante!.Producto!.Nombre,
                        Talle = d.ProductoVariante.Talle!.Nombre,
                        Color = d.ProductoVariante.Color!.Nombre,
                        d.Cantidad,
                        d.PrecioUnitario,
                        d.Subtotal,
                    })
                    .ToList(),
                Notas = p.Notas
                    .OrderByDescending(n => n.FechaActualizacion)
                    .Select(n => new
                    {
                        n.Nota,
                        n.Empresa,
                        n.CodigoSeguimiento,
                        n.FechaActualizacion,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (pedido is null)
        {
            return ResultadoHerramienta.Fallido(
                $"No encontré ningún pedido tuyo con el número {idPedido}.");
        }

        return ResultadoHerramienta.Ok(new
        {
            id = pedido.IdPedido,
            fecha = pedido.FechaPedido.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            estado = pedido.Estado,
            total = pedido.Total,
            productos = pedido.Detalles
                .Select(d => new
                {
                    producto = d.Producto,
                    talle = d.Talle,
                    color = d.Color,
                    cantidad = d.Cantidad,
                    precio_unitario = d.PrecioUnitario,
                    subtotal = d.Subtotal,
                })
                .ToArray(),
            actualizaciones = pedido.Notas
                .Select(n => new
                {
                    nota = n.Nota,
                    empresa = n.Empresa,
                    codigo_seguimiento = n.CodigoSeguimiento,
                    fecha = n.FechaActualizacion.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                })
                .ToArray(),
            ultima_actualizacion = pedido.Notas.Count > 0
                ? new
                {
                    nota = pedido.Notas[0].Nota,
                    empresa = pedido.Notas[0].Empresa,
                    codigo_seguimiento = pedido.Notas[0].CodigoSeguimiento,
                    fecha = pedido.Notas[0].FechaActualizacion.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                }
                : null,
        });
    }

    // ------------------------------------------------------------------
    // consultar_mis_datos
    // ------------------------------------------------------------------

    private async Task<ResultadoHerramienta> ConsultarMisDatosAsync(CancellationToken cancellationToken)
    {
        if (usuario.IdCliente is not { } idCliente)
        {
            return ResultadoHerramienta.Fallido(
                "Necesitás iniciar sesión para consultar tus datos.",
                requiereAutenticacion: true);
        }

        var cliente = await db.Clientes.AsNoTracking()
            .Where(c => c.IdCliente == idCliente)
            .Select(c => new
            {
                c.Nombre,
                c.Apellido,
                c.Telefono,
                c.Direccion,
                Email = c.Usuario!.Email,
                CantidadPedidos = c.Pedidos.Count,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (cliente is null)
        {
            return ResultadoHerramienta.Fallido("No encontramos tu perfil de cliente.");
        }

        // Datos mínimos y enmascarados: el modelo nunca ve el teléfono
        // completo ni la dirección completa.
        return ResultadoHerramienta.Ok(new
        {
            nombre = cliente.Nombre,
            apellido = cliente.Apellido,
            email = cliente.Email,
            telefono = EnmascararTelefono(cliente.Telefono),
            direccion = EnmascararDireccion(cliente.Direccion),
            cantidad_pedidos = cliente.CantidadPedidos,
        });
    }

    // ------------------------------------------------------------------
    // buscar_producto
    // ------------------------------------------------------------------

    private async Task<ResultadoHerramienta> BuscarProductoAsync(
        IReadOnlyDictionary<string, object?>? argumentos, CancellationToken cancellationToken)
    {
        var texto = TryGetString(argumentos, "texto") ?? string.Empty;
        var limite = Math.Clamp(TryGetInt(argumentos, "limite", out var l) ? l : 5, 1, 10);

        var consulta = db.Productos.AsNoTracking().Where(p => p.Activo);

        if (!string.IsNullOrWhiteSpace(texto))
        {
            var busqueda = texto.Trim();
            consulta = consulta.Where(p =>
                p.Nombre.Contains(busqueda) ||
                (p.Descripcion != null && p.Descripcion.Contains(busqueda)) ||
                p.Categoria!.Nombre.Contains(busqueda));
        }

        var productos = await consulta
            .OrderBy(p => p.Nombre)
            .Take(limite)
            .Select(p => new
            {
                p.IdProducto,
                p.Nombre,
                Categoria = p.Categoria!.Nombre,
                p.PrecioBase,
                PrecioMinimo = p.Variantes
                    .Where(v => v.Precio != null)
                    .Min(v => (decimal?)v.Precio) ?? p.PrecioBase,
                StockTotal = p.Variantes.Sum(v => v.Stock),
                Disponible = p.Variantes.Any(v => v.Stock > 0),
            })
            .ToListAsync(cancellationToken);

        return ResultadoHerramienta.Ok(new
        {
            cantidad = productos.Count,
            productos = productos.Select(p => new
            {
                id = p.IdProducto,
                nombre = p.Nombre,
                categoria = p.Categoria,
                precio = p.PrecioMinimo,
                stock_total = p.StockTotal,
                disponible = p.Disponible,
            }).ToArray(),
        });
    }

    // ------------------------------------------------------------------
    // consultar_variantes
    // ------------------------------------------------------------------

    private async Task<ResultadoHerramienta> ConsultarVariantesAsync(
        IReadOnlyDictionary<string, object?>? argumentos, CancellationToken cancellationToken)
    {
        if (!TryGetInt(argumentos, "id_producto", out var idProducto) || idProducto <= 0)
        {
            return ResultadoHerramienta.Fallido(
                "Indicá el producto (id_producto) cuyas variantes querés consultar.");
        }

        var producto = await db.Productos.AsNoTracking()
            .Where(p => p.IdProducto == idProducto && p.Activo)
            .Select(p => new
            {
                p.IdProducto,
                p.Nombre,
                p.PrecioBase,
                Variantes = p.Variantes
                    .OrderBy(v => v.Talle!.Nombre).ThenBy(v => v.Color!.Nombre)
                    .Select(v => new
                    {
                        v.IdProductoVariante,
                        Talle = v.Talle!.Nombre,
                        Color = v.Color!.Nombre,
                        v.Stock,
                        Precio = v.Precio ?? p.PrecioBase,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (producto is null)
        {
            return ResultadoHerramienta.Fallido(
                $"No encontré el producto {idProducto} en el catálogo.");
        }

        var talle = TryGetString(argumentos, "talle");
        var color = TryGetString(argumentos, "color");

        // Filtro opcional por talle/color para responder "¿hay stock talle M?"
        var filtradas = producto.Variantes.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(talle))
        {
            filtradas = filtradas.Where(v =>
                string.Equals(v.Talle, talle.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(color))
        {
            filtradas = filtradas.Where(v =>
                string.Equals(v.Color, color.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        var variantes = filtradas.ToList();

        return ResultadoHerramienta.Ok(new
        {
            id = producto.IdProducto,
            nombre = producto.Nombre,
            precio_base = producto.PrecioBase,
            hay_variantes = variantes.Count > 0,
            // El modelo debe distinguir entre "no existe la combinación" y
            // "existe pero sin stock": nunca prometer reposición.
            estado = variantes.Count == 0
                ? "combinacion_inexistente"
                : variantes.Any(v => v.Stock > 0) ? "con_stock" : "sin_stock",
            variantes = variantes.Select(v => new
            {
                id = v.IdProductoVariante,
                talle = v.Talle,
                color = v.Color,
                stock = v.Stock,
                precio = v.Precio,
                disponible = v.Stock > 0,
            }).ToArray(),
        });
    }

    // ------------------------------------------------------------------
    // preparar_solicitud
    // ------------------------------------------------------------------

    /// <summary>
    /// Prepara una solicitud para el cliente. NO escribe nada visible para la
    /// tienda: crea una PROPUESTA pendiente que el cliente tiene que confirmar
    /// desde el chat. El modelo nunca envía solicitudes.
    /// </summary>
    private async Task<ResultadoHerramienta> PrepararSolicitudAsync(
        IReadOnlyDictionary<string, object?>? argumentos, CancellationToken cancellationToken)
    {
        if (usuario.IdCliente is not { } idCliente)
        {
            return ResultadoHerramienta.Fallido(
                "Necesitás iniciar sesión para dejar una solicitud a la tienda.",
                requiereAutenticacion: true);
        }

        var tipo = (TryGetString(argumentos, "tipo") ?? string.Empty).Trim();

        if (!SolicitudTipos.Todos.Contains(tipo))
        {
            return ResultadoHerramienta.Fallido(
                "El tipo de solicitud no es válido. Usá consulta_pedido, consulta_general o solicitud_reposicion.");
        }

        var mensaje = (TryGetString(argumentos, "mensaje") ?? string.Empty).Trim();
        if (mensaje.Length == 0)
        {
            return ResultadoHerramienta.Fallido("La solicitud necesita un mensaje.");
        }

        // Pedido (opcional): tiene que ser del cliente.
        int? idPedido = null;
        if (TryGetInt(argumentos, "id_pedido", out var pedido) && pedido > 0)
        {
            var esPropio = await db.Pedidos.AsNoTracking()
                .AnyAsync(p => p.IdPedido == pedido && p.IdCliente == idCliente, cancellationToken);

            if (!esPropio)
            {
                return ResultadoHerramienta.Fallido(
                    $"No encontré un pedido tuyo con el número {pedido}.");
            }

            idPedido = pedido;
        }

        // Producto y variante (opcionales). Si la combinación no existe, se
        // describe con texto (talle/color) sin inventar un IdProductoVariante.
        int? idProducto = null;
        int? idVariante = null;
        string? talle = TryGetString(argumentos, "talle");
        string? color = TryGetString(argumentos, "color");

        if (TryGetInt(argumentos, "id_producto", out var producto) && producto > 0)
        {
            var existe = await db.Productos.AsNoTracking()
                .AnyAsync(p => p.IdProducto == producto && p.Activo, cancellationToken);

            if (!existe)
            {
                return ResultadoHerramienta.Fallido(
                    $"No encontré el producto {producto} en el catálogo.");
            }

            idProducto = producto;

            if (TryGetInt(argumentos, "id_variante", out var variante) && variante > 0)
            {
                var existeVariante = await db.ProductoVariantes.AsNoTracking()
                    .AnyAsync(v => v.IdProductoVariante == variante && v.IdProducto == producto, cancellationToken);

                if (!existeVariante)
                {
                    return ResultadoHerramienta.Fallido(
                        "Esa variante no existe para el producto. Podés describir el talle y el color que buscás.");
                }

                idVariante = variante;
            }
        }

        // Si es una reposición y no hay variante, se describe la combinación.
        if (tipo == SolicitudTipos.SolicitudReposicion && idVariante is null)
        {
            var existeCombinacion = await db.ProductoVariantes.AsNoTracking()
                .Where(v => idProducto == null || v.IdProducto == idProducto)
                .Where(v => talle == null || v.Talle!.Nombre == talle)
                .Where(v => color == null || v.Color!.Nombre == color)
                .AnyAsync(cancellationToken);

            if (!existeCombinacion && (talle is null || color is null))
            {
                return ResultadoHerramienta.Fallido(
                    "Para pedir la reposición necesito saber el talle y el color que buscás.");
            }
        }

        var propuesta = await db.SolicitudPropuestas
            .Where(p => p.IdUsuario == usuario.IdUsuario
                && p.Estado == "Pendiente"
                && p.Tipo == tipo
                && p.IdPedido == idPedido
                && p.IdProducto == idProducto
                && p.IdProductoVariante == idVariante
                && p.Mensaje == mensaje)
            .OrderByDescending(p => p.IdPropuesta)
            .FirstOrDefaultAsync(cancellationToken);

        if (propuesta is null)
        {
            var cliente = await db.Clientes.AsNoTracking()
                .FirstOrDefaultAsync(c => c.IdCliente == idCliente, cancellationToken);

            propuesta = new SolicitudPropuesta
            {
                IdUsuario = usuario.IdUsuario,
                IdCliente = idCliente,
                Tipo = tipo,
                IdPedido = idPedido,
                IdProducto = idProducto,
                IdProductoVariante = idVariante,
                TalleDescripcion = talle,
                ColorDescripcion = color,
                Mensaje = mensaje,
                // El teléfono NUNCA viaja completo al modelo: sólo se usa
                // enmascarado en la conversación.
                TelefonoContacto = cliente?.Telefono,
                TelefonoEstado = TelefonoEstados.PendienteConfirmacion,
                Estado = "Pendiente",
            };

            db.SolicitudPropuestas.Add(propuesta);
            await db.SaveChangesAsync(cancellationToken);
        }

        var nombreProducto = idProducto is null
            ? null
            : await db.Productos.AsNoTracking()
                .Where(p => p.IdProducto == idProducto)
                .Select(p => p.Nombre)
                .FirstOrDefaultAsync(cancellationToken);

        var telefonoPerfil = await db.Clientes.AsNoTracking()
            .Where(c => c.IdCliente == idCliente)
            .Select(c => c.Telefono)
            .FirstOrDefaultAsync(cancellationToken);

        return ResultadoHerramienta.Ok(new
        {
            id_propuesta = propuesta.IdPropuesta,
            estado = "Pendiente de confirmación del cliente",
            tipo = propuesta.Tipo,
            producto = nombreProducto,
            talle = talle,
            color = color,
            id_pedido = idPedido,
            mensaje = propuesta.Mensaje,
            telefono_registrado = EnmascararTelefono(telefonoPerfil),
            // Lo que el modelo debe decirle al cliente:
            instruccion = "Pedí al cliente que confirme la solicitud antes de enviarla. " +
                          "Ofrecé confirmar o cancelar. No digas que ya se envió.",
        });
    }

    // ------------------------------------------------------------------
    // consultar_mis_solicitudes
    // ------------------------------------------------------------------

    /// <summary>Estado de las solicitudes del propio cliente.</summary>
    private async Task<ResultadoHerramienta> ConsultarMisSolicitudesAsync(
        CancellationToken cancellationToken)
    {
        if (usuario.IdCliente is not { } idCliente)
        {
            return ResultadoHerramienta.Fallido(
                "Necesitás iniciar sesión para ver tus solicitudes.",
                requiereAutenticacion: true);
        }

        var solicitudes = await db.SolicitudesAtencion.AsNoTracking()
            .Where(s => s.IdCliente == idCliente)
            .OrderByDescending(s => s.FechaCreacion)
            .Select(s => new
            {
                id = s.IdSolicitud,
                tipo = s.Tipo,
                estado = s.Estado,
                mensaje = s.Mensaje,
                fecha = s.FechaCreacion.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                respuesta = s.Respuesta,
            })
            .ToListAsync(cancellationToken);

        return ResultadoHerramienta.Ok(new
        {
            cantidad = solicitudes.Count,
            solicitudes,
        });
    }

    // ------------------------------------------------------------------
    // Utilidades
    // ------------------------------------------------------------------

    /// <summary>True si el modelo pidió una herramienta de datos personales.</summary>
    public static bool RequiereCliente(string nombre) =>
        nombre is ListarMisPedidos or ConsultarPedido or ConsultarMisDatos;

    private static bool TryGetInt(
        IReadOnlyDictionary<string, object?>? argumentos, string clave, out int valor)
    {
        valor = 0;
        if (argumentos is null || !argumentos.TryGetValue(clave, out var crudo) || crudo is null)
        {
            return false;
        }

        switch (crudo)
        {
            case int i:
                valor = i;
                return true;
            case long l:
                valor = (int)l;
                return true;
            case double d when Math.Abs(d % 1) < double.Epsilon:
                valor = (int)d;
                return true;
            case decimal m when m % 1 == 0:
                valor = (int)m;
                return true;
            case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p):
                valor = p;
                return true;
            case JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var n):
                valor = n;
                return true;
            case JsonElement je when je.ValueKind == JsonValueKind.String &&
                                      int.TryParse(je.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t):
                valor = t;
                return true;
            default:
                return false;
        }
    }

    private static string? TryGetString(IReadOnlyDictionary<string, object?>? argumentos, string clave)
    {
        if (argumentos is null || !argumentos.TryGetValue(clave, out var crudo) || crudo is null)
        {
            return null;
        }

        return crudo switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.ToString(),
            _ => crudo.ToString(),
        };
    }

    /// <summary>Enmascara el teléfono: conserva los últimos 2 dígitos.</summary>
    internal static string? EnmascararTelefono(string? telefono)
    {
        if (string.IsNullOrWhiteSpace(telefono))
        {
            return null;
        }

        var digitos = new string(telefono.Where(char.IsDigit).ToArray());
        if (digitos.Length <= 2)
        {
            return "**";
        }

        return $"***{digitos[^2..]}";
    }

    /// <summary>
    /// Enmascara la dirección: conserva sólo el tipo de vía y la localidad
    /// (lo que va después de la última coma). El resto no viaja al modelo.
    /// </summary>
    internal static string? EnmascararDireccion(string? direccion)
    {
        if (string.IsNullOrWhiteSpace(direccion))
        {
            return null;
        }

        var partes = direccion.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length <= 1)
        {
            return "Dirección registrada";
        }

        var via = partes[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Calle";
        var localidad = partes[^1];

        return $"{via} • {localidad}";
    }
}
