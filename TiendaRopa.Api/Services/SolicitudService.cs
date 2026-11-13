using Microsoft.EntityFrameworkCore;
using TiendaRopa.Api.Auth;
using TiendaRopa.Api.Data;
using TiendaRopa.Api.Entities;
using TiendaRopa.Contracts.Solicitudes;

namespace TiendaRopa.Api.Services;

/// <summary>
/// Resultado de una operación de negocio (sin excepciones al controller).
/// </summary>
public sealed record ResultadoSolicitud<T>(T? Valor, string? Error, int StatusCode)
{
    public static ResultadoSolicitud<T> Ok(T valor) => new(valor, null, StatusCodes.Status200OK);

    public static ResultadoSolicitud<T> Creado(T valor) => new(valor, null, StatusCodes.Status201Created);

    public static ResultadoSolicitud<T> Fallido(int status, string error) => new(default, error, status);
}

/// <summary>
/// Solicitudes de atención del cliente y notificaciones internas.
///
/// Reglas duras:
///  - una propuesta la prepara el asistente, pero sólo el cliente dueño
///    puede confirmarla (nunca el modelo);
///  - al confirmar se revalidan en backend la pertenencia del pedido, la
///    existencia del producto/variante y el contenido de la solicitud;
///  - la idempotencia evita duplicados por doble clic o reintento;
///  - el teléfono de contacto se confirma explícitamente y nunca se envía
///    completo al modelo.
/// </summary>
public sealed class SolicitudService(VelvetRoseDbContext db, IUsuarioActual usuario, ILogger<SolicitudService> log)
{
    // ------------------------------------------------------------------
    // Propuestas
    // ------------------------------------------------------------------

    /// <summary>
    /// Crea (o reutiliza) la propuesta de una solicitud. Es lo único que el
    /// asistente puede lograr: la propuesta queda Pendiente hasta que el
    /// cliente la confirma.
    /// </summary>
    public async Task<(SolicitudPropuesta? Propuesta, string? Error, int StatusCode)> PrepararPropuestaAsync(
        int idCliente,
        string tipo,
        int? idPedido,
        int? idProducto,
        int? idProductoVariante,
        string? talle,
        string? color,
        string mensaje,
        CancellationToken cancellationToken)
    {
        if (usuario.IdCliente is not { } idClientePropio || idClientePropio != idCliente)
        {
            return (null, "Necesitás iniciar sesión para dejar una solicitud.", StatusCodes.Status401Unauthorized);
        }

        if (!SolicitudTipos.Todos.Contains(tipo))
        {
            return (null, "Tipo de solicitud no válido.", StatusCodes.Status400BadRequest);
        }

        var texto = (mensaje ?? string.Empty).Trim();
        if (texto.Length == 0)
        {
            return (null, "La solicitud necesita un mensaje.", StatusCodes.Status400BadRequest);
        }

        // El pedido, si viene, tiene que ser del cliente.
        if (idPedido is { } pedido)
        {
            var esPropio = await db.Pedidos.AsNoTracking()
                .AnyAsync(p => p.IdPedido == pedido && p.IdCliente == idCliente, cancellationToken);

            if (!esPropio)
            {
                return (null, "Ese pedido no pertenece a tu cuenta.", StatusCodes.Status403Forbidden);
            }
        }

        // El producto y la variante, si vienen, tienen que existir.
        if (idProducto is { } producto)
        {
            var existe = await db.Productos.AsNoTracking()
                .AnyAsync(p => p.IdProducto == producto && p.Activo, cancellationToken);

            if (!existe)
            {
                return (null, "El producto ya no está disponible.", StatusCodes.Status400BadRequest);
            }
        }

        if (idProductoVariante is { } variante)
        {
            var existeVariante = await db.ProductoVariantes.AsNoTracking()
                .AnyAsync(v => v.IdProductoVariante == variante
                    && (idProducto == null || v.IdProducto == idProducto), cancellationToken);

            if (!existeVariante)
            {
                return (null, "Esa variante no existe en el catálogo.",
                    StatusCodes.Status400BadRequest);
            }
        }

        // Reutiliza una propuesta pendiente equivalente (no duplica tarjetas).
        var pendiente = await db.SolicitudPropuestas
            .Where(p => p.IdUsuario == usuario.IdUsuario
                && p.Estado == "Pendiente"
                && p.Tipo == tipo
                && p.IdPedido == idPedido
                && p.IdProducto == idProducto
                && p.IdProductoVariante == idProductoVariante
                && p.Mensaje == texto)
            .OrderByDescending(p => p.IdPropuesta)
            .FirstOrDefaultAsync(cancellationToken);

        if (pendiente is not null)
        {
            return (pendiente, null, StatusCodes.Status200OK);
        }

        // El teléfono de contacto: el del perfil, todavía SIN confirmar.
        var cliente = await db.Clientes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.IdCliente == idCliente, cancellationToken);

        var propuesta = new SolicitudPropuesta
        {
            IdUsuario = usuario.IdUsuario,
            IdCliente = idCliente,
            Tipo = tipo,
            IdPedido = idPedido,
            IdProducto = idProducto,
            IdProductoVariante = idProductoVariante,
            TalleDescripcion = talle,
            ColorDescripcion = color,
            Mensaje = texto,
            TelefonoContacto = cliente?.Telefono,
            TelefonoEstado = string.IsNullOrWhiteSpace(cliente?.Telefono)
                ? TelefonoEstados.PendienteConfirmacion
                : TelefonoEstados.PendienteConfirmacion,
            Estado = "Pendiente",
        };

        db.SolicitudPropuestas.Add(propuesta);
        await db.SaveChangesAsync(cancellationToken);

        return (propuesta, null, StatusCodes.Status201Created);
    }

    /// <summary>Propuesta pendiente o ya resuelta del usuario actual.</summary>
    public async Task<SolicitudPropuesta?> ObtenerPropuestaAsync(int idPropuesta, CancellationToken cancellationToken)
        => await db.SolicitudPropuestas.AsNoTracking()
            .Where(p => p.IdPropuesta == idPropuesta && p.IdUsuario == usuario.IdUsuario)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Fija el teléfono de contacto de una propuesta pendiente. Si el cliente
    /// lo pide explícitamente, se actualiza también su perfil.
    /// </summary>
    public async Task<(SolicitudPropuesta? Propuesta, string? Error, int StatusCode)> ConfirmarTelefonoAsync(
        int idPropuesta, string telefono, bool guardarEnPerfil, CancellationToken cancellationToken)
    {
        var propuesta = await db.SolicitudPropuestas
            .FirstOrDefaultAsync(p => p.IdPropuesta == idPropuesta, cancellationToken);

        if (propuesta is null || propuesta.IdUsuario != usuario.IdUsuario)
        {
            return (null, "No encontramos esa propuesta.", StatusCodes.Status404NotFound);
        }

        if (propuesta.Estado != "Pendiente")
        {
            return (null, "La propuesta ya no está pendiente.", StatusCodes.Status409Conflict);
        }

        var normalizado = NormalizarTelefono(telefono);
        if (normalizado is null)
        {
            return (null, "El teléfono no es válido. Revisá el número.", StatusCodes.Status400BadRequest);
        }

        propuesta.TelefonoContacto = normalizado;
        propuesta.TelefonoEstado = TelefonoEstados.Confirmado;
        propuesta.GuardarTelefonoEnPerfil = guardarEnPerfil;

        if (guardarEnPerfil && usuario.IdCliente is { } idCliente)
        {
            // Consentimiento expreso: se actualiza el perfil del cliente.
            var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.IdCliente == idCliente, cancellationToken);
            if (cliente is not null)
            {
                cliente.Telefono = normalizado;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return (propuesta, null, StatusCodes.Status200OK);
    }

    /// <summary>Cancela una propuesta pendiente (no crea nada).</summary>
    public async Task<(SolicitudPropuesta? Propuesta, string? Error, int StatusCode)> CancelarPropuestaAsync(
        int idPropuesta, CancellationToken cancellationToken)
    {
        var propuesta = await db.SolicitudPropuestas
            .FirstOrDefaultAsync(p => p.IdPropuesta == idPropuesta, cancellationToken);

        if (propuesta is null || propuesta.IdUsuario != usuario.IdUsuario)
        {
            return (null, "No encontramos esa propuesta.", StatusCodes.Status404NotFound);
        }

        if (propuesta.Estado != "Pendiente")
        {
            return (null, "La propuesta ya no está pendiente.", StatusCodes.Status409Conflict);
        }

        propuesta.Estado = "Cancelada";
        await db.SaveChangesAsync(cancellationToken);
        return (propuesta, null, StatusCodes.Status200OK);
    }

    // ------------------------------------------------------------------
    // Confirmación (la única escritura)
    // ------------------------------------------------------------------

    /// <summary>
    /// Confirma una propuesta y crea la solicitud + las notificaciones.
    /// Idempotente: si la propuesta ya estaba confirmada devuelve la misma
    /// solicitud (doble clic o reintento no duplican nada).
    /// </summary>
    public async Task<(SolicitudAtencion? Solicitud, string? Error, int StatusCode)> ConfirmarPropuestaAsync(
        int idPropuesta, ConfirmarPropuestaRequest request, CancellationToken cancellationToken)
    {
        var propuesta = await db.SolicitudPropuestas
            .FirstOrDefaultAsync(p => p.IdPropuesta == idPropuesta, cancellationToken);

        if (propuesta is null || propuesta.IdUsuario != usuario.IdUsuario)
        {
            return (null, "No encontramos esa propuesta.", StatusCodes.Status404NotFound);
        }

        // Idempotencia por propuesta: ya confirmada -> misma solicitud.
        if (propuesta.Estado == "Confirmada" && propuesta.IdSolicitudGenerada is { } idExistente)
        {
            var yaExiste = await db.SolicitudesAtencion.AsNoTracking()
                .FirstOrDefaultAsync(s => s.IdSolicitud == idExistente, cancellationToken);

            if (yaExiste is not null)
            {
                return (yaExiste, null, StatusCodes.Status200OK);
            }
        }

        if (propuesta.Estado != "Pendiente")
        {
            return (null, "La propuesta ya no está pendiente.", StatusCodes.Status409Conflict);
        }

        // Idempotencia por clave explícita del cliente.
        if (!string.IsNullOrWhiteSpace(request.ClaveIdempotencia))
        {
            var clave = request.ClaveIdempotencia.Trim();
            var porClave = await db.SolicitudesAtencion.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ClaveIdempotencia == clave, cancellationToken);

            if (porClave is not null)
            {
                propuesta.Estado = "Confirmada";
                propuesta.IdSolicitudGenerada = porClave.IdSolicitud;
                propuesta.FechaConfirmacion = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return (porClave, null, StatusCodes.Status200OK);
            }
        }

        // ---- Revalidación en backend ----

        // 1) El teléfono de contacto.
        //
        //    - Si el clienteConfirmó uno en la propuesta (PUT /telefono), se usa ése.
        //    - Si no, se usa el del perfil del cliente: el botón "Confirmar y
        //      enviar" ES la confirmación de que ese número (mostrado
        //      enmascarado en el chat) es correcto.
        //    - Si no hay ninguno, se exige que se proporcione uno.
        var telefono = propuesta.TelefonoContacto;

        if (!string.IsNullOrWhiteSpace(request.Telefono))
        {
            telefono = NormalizarTelefono(request.Telefono);
            if (telefono is null)
            {
                return (null, "El teléfono no es válido. Revisá el número.", StatusCodes.Status400BadRequest);
            }
        }

        if (string.IsNullOrWhiteSpace(telefono))
        {
            // Último intento: el teléfono registrado en el perfil.
            telefono = await db.Clientes.AsNoTracking()
                .Where(c => c.IdCliente == propuesta.IdCliente)
                .Select(c => c.Telefono)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(telefono))
        {
            return (null,
                "Necesitás indicar un teléfono de contacto para que la tienda te responda.",
                StatusCodes.Status400BadRequest);
        }

        var guardarEnPerfil = request.GuardarTelefonoEnPerfil || propuesta.GuardarTelefonoEnPerfil;

        // 2) El pedido, si viene, tiene que ser del cliente.
        if (propuesta.IdPedido is { } idPedido)
        {
            var esPropio = await db.Pedidos.AsNoTracking()
                .AnyAsync(p => p.IdPedido == idPedido && p.IdCliente == propuesta.IdCliente, cancellationToken);

            if (!esPropio)
            {
                return (null, "Ese pedido no pertenece a tu cuenta.", StatusCodes.Status403Forbidden);
            }
        }

        // 3) El producto/variante, si viene, tiene que existir.
        if (propuesta.IdProducto is { } idProducto)
        {
            var existeProducto = await db.Productos.AsNoTracking()
                .AnyAsync(p => p.IdProducto == idProducto && p.Activo, cancellationToken);

            if (!existeProducto)
            {
                return (null, "El producto ya no está disponible.", StatusCodes.Status409Conflict);
            }
        }

        if (propuesta.IdProductoVariante is { } idVariante)
        {
            var existeVariante = await db.ProductoVariantes.AsNoTracking()
                .AnyAsync(v => v.IdProductoVariante == idVariante, cancellationToken);

            if (!existeVariante)
            {
                return (null, "La variante ya no existe en el catálogo.", StatusCodes.Status409Conflict);
            }
        }

        // 4) Contenido de la solicitud: no vacío y dentro del límite.
        var texto = propuesta.Mensaje.Trim();
        if (texto.Length == 0 || texto.Length > 1000)
        {
            return (null, "El mensaje de la solicitud no es válido.", StatusCodes.Status400BadRequest);
        }

        // 5) Destinatarios: al menos un usuario del personal de tienda.
        var destinatarios = await db.Usuarios.AsNoTracking()
            .Where(u => u.Activo
                && u.Rol!.Activo
                && (u.Rol.Nombre == Roles.Administrador || u.Rol.Nombre == Roles.Vendedor))
            .Select(u => u.IdUsuario)
            .ToListAsync(cancellationToken);

        if (destinatarios.Count == 0)
        {
            return (null, "No hay personal de tienda disponible para recibir la solicitud.",
                StatusCodes.Status503ServiceUnavailable);
        }

        // ---- Creación ----
        var solicitud = new SolicitudAtencion
        {
            IdCliente = propuesta.IdCliente,
            IdUsuario = propuesta.IdUsuario,
            Tipo = propuesta.Tipo,
            IdPedido = propuesta.IdPedido,
            IdProducto = propuesta.IdProducto,
            IdProductoVariante = propuesta.IdProductoVariante,
            TalleDescripcion = propuesta.TalleDescripcion,
            ColorDescripcion = propuesta.ColorDescripcion,
            Mensaje = texto,
            TelefonoContacto = telefono,
            // Confirmar la solicitud confirma también el teléfono de contacto.
            TelefonoEstado = TelefonoEstados.Confirmado,
            Estado = SolicitudEstados.Abierta,
            ClaveIdempotencia = string.IsNullOrWhiteSpace(request.ClaveIdempotencia)
                ? $"propuesta:{propuesta.IdPropuesta}"
                : request.ClaveIdempotencia.Trim(),
            IdPropuesta = propuesta.IdPropuesta,
        };

        db.SolicitudesAtencion.Add(solicitud);

        // Notificación interna para cada member del personal de tienda.
        // Se usa la relación de navegación para que EF resuelva el IdSolicitud
        // (aún es 0 hasta que se inserta la solicitud).
        var nombreCliente = await NombreClienteAsync(propuesta.IdCliente, cancellationToken);

        foreach (var idDestinatario in destinatarios)
        {
            solicitud.Notificaciones.Add(new Notificacion
            {
                IdUsuario = idDestinatario,
                Tipo = NotificacionTipos.SolicitudNueva,
                Mensaje = $"Nueva solicitud de {nombreCliente}",
            });
        }

        // Si el cliente pidió guardar el teléfono en su perfil (consentimiento).
        if (guardarEnPerfil)
        {
            var cliente = await db.Clientes.FirstOrDefaultAsync(
                c => c.IdCliente == propuesta.IdCliente, cancellationToken);

            if (cliente is not null)
            {
                cliente.Telefono = telefono;
            }
        }

        propuesta.Estado = "Confirmada";
        propuesta.TelefonoContacto = telefono;
        propuesta.TelefonoEstado = TelefonoEstados.Confirmado;
        propuesta.GuardarTelefonoEnPerfil = guardarEnPerfil;
        propuesta.FechaConfirmacion = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        propuesta.IdSolicitudGenerada = solicitud.IdSolicitud;
        await db.SaveChangesAsync(cancellationToken);

        log.LogInformation(
            "Solicitud {Id} creada por el usuario {Usuario} (tipo {Tipo})",
            solicitud.IdSolicitud, usuario.IdUsuario, solicitud.Tipo);

        return (solicitud, null, StatusCodes.Status201Created);
    }

    // ------------------------------------------------------------------
    // Lectura
    // ------------------------------------------------------------------

    /// <summary>Solicitudes del cliente actual (con la respuesta de la tienda).</summary>
    public async Task<IReadOnlyList<SolicitudAtencionDto>> GetMisSolicitudesAsync(CancellationToken cancellationToken)
    {
        if (usuario.IdCliente is not { } idCliente)
        {
            return [];
        }

        var solicitudes = await db.SolicitudesAtencion.AsNoTracking()
            .Where(s => s.IdCliente == idCliente)
            .OrderByDescending(s => s.FechaCreacion)
            .Select(s => new SolicitudAtencionDto
            {
                Id = s.IdSolicitud,
                Tipo = s.Tipo,
                Estado = s.Estado,
                Mensaje = s.Mensaje,
                FechaCreacion = s.FechaCreacion,
                FechaActualizacion = s.FechaActualizacion,
                IdPedido = s.IdPedido,
                IdProducto = s.IdProducto,
                ProductoNombre = s.Producto!.Nombre,
                IdVariante = s.IdProductoVariante,
                Talle = s.TalleDescripcion,
                Color = s.ColorDescripcion,
                TelefonoContacto = s.TelefonoContacto,
                TelefonoEstado = s.TelefonoEstado,
                Respuesta = s.Respuesta,
                FechaResolucion = s.FechaResolucion,
            })
            .ToListAsync(cancellationToken);

        return solicitudes;
    }

    /// <summary>Solicitudes del equipo de tienda (con datos del cliente).</summary>
    public async Task<IReadOnlyList<SolicitudAtencionDto>> GetSolicitudesAsync(
        string? estado, CancellationToken cancellationToken)
    {
        var q = db.SolicitudesAtencion.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(estado))
        {
            var filtro = estado.Trim();
            q = q.Where(s => s.Estado == filtro);
        }

        return await q
            .OrderByDescending(s => s.FechaCreacion)
            .Select(s => new SolicitudAtencionDto
            {
                Id = s.IdSolicitud,
                Tipo = s.Tipo,
                Estado = s.Estado,
                Mensaje = s.Mensaje,
                FechaCreacion = s.FechaCreacion,
                FechaActualizacion = s.FechaActualizacion,
                IdPedido = s.IdPedido,
                PedidoDescripcion = s.Pedido == null
                    ? null
                    : "#" + s.Pedido.IdPedido + " · " + s.Pedido.EstadoPedido!.Nombre,
                IdProducto = s.IdProducto,
                ProductoNombre = s.Producto!.Nombre,
                IdVariante = s.IdProductoVariante,
                Talle = s.TalleDescripcion,
                Color = s.ColorDescripcion,
                TelefonoContacto = s.TelefonoContacto,
                TelefonoEstado = s.TelefonoEstado,
                Respuesta = s.Respuesta,
                FechaResolucion = s.FechaResolucion,
                ClienteNombre = s.Cliente!.Nombre + " " + s.Cliente.Apellido,
                ClienteEmail = s.Cliente.Usuario!.Email,
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>Detalle de una solicitud para el personal de tienda.</summary>
    public async Task<SolicitudAtencionDto?> GetSolicitudAsync(int id, CancellationToken cancellationToken)
        => await db.SolicitudesAtencion.AsNoTracking()
            .Where(s => s.IdSolicitud == id)
            .Select(s => new SolicitudAtencionDto
            {
                Id = s.IdSolicitud,
                Tipo = s.Tipo,
                Estado = s.Estado,
                Mensaje = s.Mensaje,
                FechaCreacion = s.FechaCreacion,
                FechaActualizacion = s.FechaActualizacion,
                IdPedido = s.IdPedido,
                PedidoDescripcion = s.Pedido == null
                    ? null
                    : "#" + s.Pedido.IdPedido + " · " + s.Pedido.EstadoPedido!.Nombre,
                IdProducto = s.IdProducto,
                ProductoNombre = s.Producto!.Nombre,
                IdVariante = s.IdProductoVariante,
                Talle = s.TalleDescripcion,
                Color = s.ColorDescripcion,
                TelefonoContacto = s.TelefonoContacto,
                TelefonoEstado = s.TelefonoEstado,
                Respuesta = s.Respuesta,
                FechaResolucion = s.FechaResolucion,
                ClienteNombre = s.Cliente!.Nombre + " " + s.Cliente.Apellido,
                ClienteEmail = s.Cliente.Usuario!.Email,
            })
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Propuestas del usuario actual (para el chat).</summary>
    public async Task<IReadOnlyList<SolicitudPropuestaDto>> GetMisPropuestasAsync(CancellationToken cancellationToken)
    {
        var propuestas = await db.SolicitudPropuestas.AsNoTracking()
            .Where(p => p.IdUsuario == usuario.IdUsuario && p.Estado == "Pendiente")
            .OrderByDescending(p => p.IdPropuesta)
            .ToListAsync(cancellationToken);

        var resultado = new List<SolicitudPropuestaDto>(propuestas.Count);
        foreach (var propuesta in propuestas)
        {
            // El nombre del producto no vive en la propuesta: se resuelve.
            string? nombreProducto = null;
            if (propuesta.IdProducto is { } idProducto)
            {
                nombreProducto = await db.Productos.AsNoTracking()
                    .Where(p => p.IdProducto == idProducto)
                    .Select(p => p.Nombre)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var dto = MapearPropuesta(propuesta, nombreProducto);
            resultado.Add(dto);
        }

        return resultado;
    }

    /// <summary>Mapea una propuesta a su DTO (enmascarando el teléfono).</summary>
    public static SolicitudPropuestaDto MapearPropuesta(SolicitudPropuesta p, string? nombreProducto = null) => new()
    {
        Id = p.IdPropuesta,
        Tipo = p.Tipo,
        Mensaje = p.Mensaje,
        Estado = p.Estado,
        IdPedido = p.IdPedido,
        IdProducto = p.IdProducto,
        ProductoNombre = nombreProducto,
        IdVariante = p.IdProductoVariante,
        Talle = p.TalleDescripcion,
        Color = p.ColorDescripcion,
        TelefonoEnmascarado = EnmascararTelefono(p.TelefonoContacto),
        TelefonoEstado = p.TelefonoEstado,
        GuardarTelefonoEnPerfil = p.GuardarTelefonoEnPerfil,
        IdSolicitudGenerada = p.IdSolicitudGenerada,
        Resumen = ConstruirResumen(p, nombreProducto),
    };

    /// <summary>Resumen legible de una propuesta (para la tarjeta de la UI).</summary>
    public static string ConstruirResumen(SolicitudPropuesta p, string? nombreProducto = null)
    {
        var tipo = p.Tipo switch
        {
            SolicitudTipos.ConsultaPedido => $"Consulta sobre el pedido #{p.IdPedido}",
            SolicitudTipos.SolicitudReposicion => $"Reposición de {nombreProducto ?? "un producto"}",
            _ => "Consulta a la tienda",
        };

        var detalle = new List<string>();

        if (!string.IsNullOrWhiteSpace(nombreProducto))
        {
            detalle.Add(nombreProducto);
        }

        if (!string.IsNullOrWhiteSpace(p.TalleDescripcion))
        {
            detalle.Add($"talle {p.TalleDescripcion}");
        }

        if (!string.IsNullOrWhiteSpace(p.ColorDescripcion))
        {
            detalle.Add($"color {p.ColorDescripcion}");
        }

        return detalle.Count > 0 ? $"{tipo} ({string.Join(", ", detalle)})" : tipo;
    }

    /// <summary>Enmascara un teléfono: conserva los últimos 2 dígitos.</summary>
    public static string? EnmascararTelefono(string? telefono)
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
    /// Normaliza un teléfono: sólo dígitos, guiones, espacios y un '+' inicial.
    /// Devuelve null si no queda nada usable.
    /// </summary>
    public static string? NormalizarTelefono(string? telefono)
    {
        if (string.IsNullOrWhiteSpace(telefono))
        {
            return null;
        }

        var limpio = new string(telefono.Trim()
            .Where(c => char.IsDigit(c) || c is '+' or '-' or ' ')
            .ToArray());

        var digitos = limpio.Count(char.IsDigit);
        if (digitos < 6)
        {
            return null;
        }

        return limpio;
    }

    private async Task<string> NombreClienteAsync(int idCliente, CancellationToken cancellationToken)
        => await db.Clientes.AsNoTracking()
            .Where(c => c.IdCliente == idCliente)
            .Select(c => c.Nombre + " " + c.Apellido)
            .FirstOrDefaultAsync(cancellationToken) ?? "un cliente";
}
