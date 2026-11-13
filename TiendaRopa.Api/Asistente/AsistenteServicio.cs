using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TiendaRopa.Api.Auth;
using TiendaRopa.Api.Data;
using TiendaRopa.Api.Services;
using TiendaRopa.Contracts.Asistente;
using TiendaRopa.Contracts.Solicitudes;

namespace TiendaRopa.Api.Asistente;

/// <summary>
/// Orquestador del asistente: arma el turno, ejecuta las herramientas pedidas
/// por el modelo, arma las opciones de pedido y propone acciones (sin ejecutarlas).
/// </summary>
public sealed class AsistenteServicio(
    IGeneradorChat gemini,
    HerramientasAsistente herramientas,
    VelvetRoseDbContext db,
    IUsuarioActual usuario,
    ILogger<AsistenteServicio> log)
{
    /// <summary>Sugerencias iniciales que la UI muestra como chips.</summary>
    public static IReadOnlyList<string> SugerenciasIniciales => ChatSugerencias.Iniciales;

    /// <summary>Procesa un turno de conversación completo.</summary>
    public async Task<ChatRespuestaDto> ResponderAsync(
        ChatSolicitudDto solicitud, CancellationToken cancellationToken)
    {
        var mensaje = solicitud.Mensaje?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(mensaje))
        {
            return new ChatRespuestaDto
            {
                Mensaje = "Escribime tu consulta y te ayudo.",
                Sugerencias = SugerenciasIniciales,
            };
        }

        // ---- Contexto validado (nunca se confía en el frontend) ----
        var idProducto = await ValidarProductoAsync(solicitud.IdProductoContexto, cancellationToken);
        var idPedido = await ValidarPedidoAsync(solicitud.IdPedidoContexto, cancellationToken);

        var historial = solicitud.Historial?
            .Where(h => h is not null && !string.IsNullOrWhiteSpace(h.Texto))
            .Select(h => new ChatMensajeDto { Rol = h.Rol, Texto = h.Texto })
            .ToList() ?? [];

        // ---- Datos del turno: pedidos activos y opciones de desambiguación ----
        var pedidosActivos = await ObtenerPedidosActivosAsync(cancellationToken);

        var opciones = pedidosActivos.Count > 1
            ? pedidosActivos.Select(p => new ChatPedidoOpcionDto
            {
                Id = p.Id,
                Etiqueta = p.Etiqueta,
                Fecha = p.Fecha,
                Estado = p.Estado,
            }).ToList()
            : [];

        // ---- Llamada al modelo con Function Calling ----
        //
        // Se registra si alguna herramienta pidió autenticación: es lo que
        // determina si la UI debe ofrecer "Iniciar sesión". Una consulta de
        // catálogo (que no necesita datos personales) no debe pedir la sesión.
        var pidioDatosPersonales = false;

        var resultado = await gemini.ProcesarTurnoAsync(
            historial,
            mensaje,
            idProducto,
            idPedido,
            async (nombre, argumentos, ct) =>
            {
                var salida = await EjecutarHerramientaAsync(nombre, argumentos, ct);
                if (salida.RequiereAutenticacion)
                {
                    pidioDatosPersonales = true;
                }

                return salida;
            },
            cancellationToken);

        // El CTA "Iniciar sesión" aparece sólo cuando el asistente intentó
        // consultar datos personales y no había sesión (no en consultas de
        // catálogo, que no las necesitan).
        var requiereAutenticacion = pidioDatosPersonales;

        if (!resultado.Exito)
        {
            return new ChatRespuestaDto
            {
                Mensaje = resultado.ErrorMensaje ?? "No pudimos responder en este momento.",
                Sugerencias = [],
                RequiereAutenticacion = requiereAutenticacion,
                ErrorCodigo = resultado.ErrorCodigo,
                ErrorMensaje = resultado.ErrorMensaje,
            };
        }

        // ---- Propuestas de acción ----
        // Incluye las solicitudes que el asistente prepara para el cliente:
        // aparecen como tarjetas con Confirmar / Cancelar. La propuesta
        // existe en el backend; la UI confirma con un endpoint protegido.
        var acciones = await ProponerAccionesAsync(mensaje, idPedido, idProducto, cancellationToken);
        acciones.AddRange(await ObtenerAccionesSolicitudAsync(cancellationToken));

        var sugerencias = pedidosActivos.Count switch
        {
            // Sin pedidos activos: se ofrecen las sugerencias iniciales.
            0 => ChatSugerencias.Iniciales.ToList(),
            // Con un pedido activo: se sugiere el seguimiento y el stock.
            1 => new List<string> { "¿Y el estado del envío?", "Consultar stock" },
            // Con varios: la UI muestra las opciones de pedido, no chips.
            _ => new List<string>(),
        };

        return new ChatRespuestaDto
        {
            Mensaje = resultado.Mensaje ?? string.Empty,
            Sugerencias = sugerencias,
            OpcionesPedido = opciones,
            Acciones = acciones,
            RequiereAutenticacion = requiereAutenticacion,
        };
    }

    /// <summary>
    /// Ejecuta la herramienta pedida por el modelo. La identidad se resuelve
    /// del JWT; el modelo nunca elige a quién se consulta.
    /// </summary>
    private Task<ResultadoHerramienta> EjecutarHerramientaAsync(
        string nombre, IReadOnlyDictionary<string, object?>? argumentos, CancellationToken cancellationToken)
    {
        if (!HerramientasAsistente.Permitidas.Contains(nombre))
        {
            log.LogWarning("El modelo intentó llamar a una herramienta no permitida: {Herramienta}", nombre);
            return Task.FromResult(ResultadoHerramienta.Fallido(
                $"La herramienta '{nombre}' no está disponible."));
        }

        return herramientas.EjecutarAsync(nombre, argumentos, cancellationToken);
    }

    /// <summary>
    /// Valida el producto de contexto: debe existir y estar activo.
    /// Si no, se ignora (no se lo inventa).
    /// </summary>
    private async Task<int?> ValidarProductoAsync(int? idProducto, CancellationToken cancellationToken)
    {
        if (idProducto is not { } id || id <= 0)
        {
            return null;
        }

        var existe = await db.Productos.AsNoTracking()
            .AnyAsync(p => p.IdProducto == id && p.Activo, cancellationToken);

        return existe ? id : null;
    }

    /// <summary>
    /// Valida el pedido de contexto: debe existir y ser del cliente.
    /// </summary>
    private async Task<int?> ValidarPedidoAsync(int? idPedido, CancellationToken cancellationToken)
    {
        if (idPedido is not { } id || id <= 0)
        {
            return null;
        }

        if (usuario.IdCliente is not { } idCliente)
        {
            return null;
        }

        var esPropio = await db.Pedidos.AsNoTracking()
            .AnyAsync(p => p.IdPedido == id && p.IdCliente == idCliente, cancellationToken);

        return esPropio ? id : null;
    }

    /// <summary>Pedidos activos del cliente, para desambiguar consultas.</summary>
    private async Task<List<(int Id, string Etiqueta, string Fecha, string Estado)>> ObtenerPedidosActivosAsync(
        CancellationToken cancellationToken)
    {
        if (usuario.IdCliente is not { } idCliente)
        {
            return [];
        }

        // Activos = no terminales (Pendiente, Confirmado, Preparado).
        var estadosTerminales = new[] { "Entregado", "Cancelado" };

        var pedidos = await db.Pedidos.AsNoTracking()
            .Where(p => p.IdCliente == idCliente && !estadosTerminales.Contains(p.EstadoPedido!.Nombre))
            .OrderByDescending(p => p.FechaPedido)
            .Select(p => new
            {
                p.IdPedido,
                p.FechaPedido,
                Estado = p.EstadoPedido!.Nombre,
                Productos = p.Detalles
                    .OrderBy(d => d.IdPedidoDetalle)
                    .Select(d => d.ProductoVariante!.Producto!.Nombre)
                    .ToList(),
                TieneNota = p.Notas.Any(),
            })
            .ToListAsync(cancellationToken);

        return pedidos.Select(p =>
        {
            var descripcion = p.Productos.Count == 0
                ? "sin detalle de productos"
                : string.Join(", ", p.Productos.Take(3));

            if (p.Productos.Count > 3)
            {
                descripcion += $" y {p.Productos.Count - 3} más";
            }

            var etiqueta = $"Pedido #{p.IdPedido} · {descripcion}";

            return (
                p.IdPedido,
                etiqueta,
                p.FechaPedido.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                p.Estado);
        }).ToList();
    }

    /// <summary>
    /// Traduce las solicitudes PREPARADAS por el asistente en tarjetas de
    /// acción para la UI (con el id de la propuesta para poder confirmarlas).
    /// </summary>
    private async Task<List<AccionPropuestaDto>> ObtenerAccionesSolicitudAsync(
        CancellationToken cancellationToken)
    {
        var acciones = new List<AccionPropuestaDto>();

        var propuestas = await db.SolicitudPropuestas.AsNoTracking()
            .Where(p => p.IdUsuario == usuario.IdUsuario && p.Estado == "Pendiente")
            .OrderByDescending(p => p.IdPropuesta)
            .Take(3)
            .ToListAsync(cancellationToken);

        foreach (var propuesta in propuestas)
        {
            var nombreProducto = propuesta.IdProducto is null
                ? null
                : await db.Productos.AsNoTracking()
                    .Where(p => p.IdProducto == propuesta.IdProducto)
                    .Select(p => p.Nombre)
                    .FirstOrDefaultAsync(cancellationToken);

            var titulo = propuesta.Tipo switch
            {
                SolicitudTipos.ConsultaPedido => "Consultar por un pedido",
                SolicitudTipos.SolicitudReposicion => "Pedir reposición",
                _ => "Dejar un mensaje a la tienda",
            };

            var detalle = new List<string> { propuesta.Mensaje };

            if (nombreProducto is not null)
            {
                detalle.Add(nombreProducto);
            }

            if (!string.IsNullOrWhiteSpace(propuesta.TalleDescripcion))
            {
                detalle.Add($"talle {propuesta.TalleDescripcion}");
            }

            if (!string.IsNullOrWhiteSpace(propuesta.ColorDescripcion))
            {
                detalle.Add($"color {propuesta.ColorDescripcion}");
            }

            var telefono = await db.Clientes.AsNoTracking()
                .Where(c => c.IdCliente == propuesta.IdCliente)
                .Select(c => c.Telefono)
                .FirstOrDefaultAsync(cancellationToken);

            acciones.Add(new AccionPropuestaDto
            {
                Tipo = AccionTipos.SolicitudAtencion,
                Titulo = titulo,
                Detalle = string.Join(" · ", detalle),
                IdPedido = propuesta.IdPedido,
                IdProducto = propuesta.IdProducto,
                IdVariante = propuesta.IdProductoVariante,
                IdPropuesta = propuesta.IdPropuesta,
                TelefonoEnmascarado = SolicitudService.EnmascararTelefono(propuesta.TelefonoContacto ?? telefono),
                RequiereConfirmarTelefono = propuesta.TelefonoEstado != TelefonoEstados.Confirmado,
            });
        }

        return acciones;
    }

    /// <summary>
    /// Detecta intenciones de acción y devuelve la propuesta para el segundo
    /// incremento. NO crea solicitudes ni escribe nada.
    /// </summary>
    private async Task<List<AccionPropuestaDto>> ProponerAccionesAsync(
        string mensaje, int? idPedidoContexto, int? idProductoContexto, CancellationToken cancellationToken)
    {
        var acciones = new List<AccionPropuestaDto>();
        var texto = mensaje.ToLowerInvariant();

        var pideReposicion = texto.Contains("reposición") || texto.Contains("reposicion")
            || texto.Contains("avisa") || texto.Contains("avisame") || texto.Contains("avísame")
            || (texto.Contains("vuelve") && texto.Contains("stock"))
            || texto.Contains("repongas") || texto.Contains("reponer");

        var pideConsulta = (texto.Contains("consulta") || texto.Contains("consultar")
                || texto.Contains("ampliar") || texto.Contains("detalle"))
            && texto.Contains("pedido");

        if (pideReposicion)
        {
            var idProducto = idProductoContexto;
            var idVariante = await BuscarVarianteSinStockAsync(idProducto, texto, cancellationToken);

            if (idVariante is { } variante)
            {
                var detalle = await db.ProductoVariantes.AsNoTracking()
                    .Where(v => v.IdProductoVariante == variante)
                    .Select(v => new
                    {
                        v.IdProductoVariante,
                        Producto = v.Producto!.Nombre,
                        Talle = v.Talle!.Nombre,
                        Color = v.Color!.Nombre,
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                if (detalle is not null)
                {
                    acciones.Add(new AccionPropuestaDto
                    {
                        Tipo = AccionTipos.ReposicionVariante,
                        Titulo = "Avisar cuando vuelva",
                        Detalle =
                            $"{detalle.Producto} · talle {detalle.Talle} · color {detalle.Color}. " +
                            "Te avisamos apenas vuelva a stock.",
                        IdProducto = idProducto,
                        IdVariante = variante,
                    });
                }
            }
            else if (idProducto is { } producto)
            {
                acciones.Add(new AccionPropuestaDto
                {
                    Tipo = AccionTipos.ReposicionVariante,
                    Titulo = "Avisar cuando vuelva",
                    Detalle = $"Producto {producto}: querés que avisemos por una talle o color específico?",
                    IdProducto = producto,
                });
            }
        }

        if (pideConsulta && idPedidoContexto is { } idPedido)
        {
            var estado = await db.Pedidos.AsNoTracking()
                .Where(p => p.IdPedido == idPedido && p.IdCliente == usuario.IdCliente)
                .Select(p => p.EstadoPedido!.Nombre)
                .FirstOrDefaultAsync(cancellationToken);

            if (estado is not null)
            {
                acciones.Add(new AccionPropuestaDto
                {
                    Tipo = AccionTipos.ConsultaPedido,
                    Titulo = "Consultar con el equipo",
                    Detalle = $"Pedido #{idPedido} · estado {estado}. Te contactamos para ampliar la información.",
                    IdPedido = idPedido,
                });
            }
        }

        return acciones;
    }

    /// <summary>
    /// Busca la variante sin stock que el cliente menciona (si el texto la
    /// identifica por talle/color dentro del producto de contexto).
    /// </summary>
    private async Task<int?> BuscarVarianteSinStockAsync(
        int? idProducto, string texto, CancellationToken cancellationToken)
    {
        if (idProducto is not { } id || id <= 0)
        {
            return null;
        }

        var variantes = await db.ProductoVariantes.AsNoTracking()
            .Where(v => v.IdProducto == id)
            .Select(v => new
            {
                v.IdProductoVariante,
                Talle = v.Talle!.Nombre,
                Color = v.Color!.Nombre,
                v.Stock,
            })
            .ToListAsync(cancellationToken);

        // Coincidencia por talle mencionado en el texto (si no hay, la primera
        // variante sin stock del producto).
        var talle = variantes
            .Select(v => v.Talle)
            .FirstOrDefault(t => t.Length > 1 && texto.Contains(t.ToLowerInvariant()));

        var candidatas = variantes.Where(v => v.Stock <= 0).ToList();

        if (talle is not null)
        {
            var porTalle = candidatas.Where(v =>
                string.Equals(v.Talle, talle, StringComparison.OrdinalIgnoreCase)).ToList();

            if (porTalle.Count > 0)
            {
                return porTalle[0].IdProductoVariante;
            }
        }

        return candidatas.Count > 0 ? candidatas[0].IdProductoVariante : null;
    }
}
