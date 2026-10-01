using Microsoft.EntityFrameworkCore;
using TiendaRopa.Api.Data;
using TiendaRopa.Api.Entities;
using TiendaRopa.Contracts.Pedidos;

namespace TiendaRopa.Api.Services;

/// <summary>Resultado de una operación de negocio (sin excepciones al controller).</summary>
public sealed record Resultado<T>(T? Valor, string? Error, int StatusCode)
{
    public static Resultado<T> Ok(T valor) => new(valor, null, StatusCodes.Status200OK);
    public static Resultado<T> Fallido(int status, string error) => new(default, error, status);
}

/// <summary>
/// Confirmación y cancelación de pedidos.
///
/// Política de stock:
///  - El carrito NO reserva ni descuenta stock.
///  - Al confirmar: transacción única; se releen las variantes desde SQL Server,
///    se valida disponibilidad de TODAS las líneas y sólo entonces se descuenta.
///    Si alguna falta, se revierte todo (sin descuento parcial).
///
/// Concurrencia (SQL Server): cada descuento se hace con un UPDATE condicional
///     UPDATE ProductoVariante SET Stock = Stock - @c WHERE Id = @id AND Stock >= @c
/// que es atómico en la base: dos solicitudes concurrentes no pueden consumir la
/// misma unidad (la segunda afecta 0 filas → se revierte la transacción completa).
/// Además, el CHECK CK_ProductoVariante_Stock_NoNegativo actúa como red de seguridad.
///
/// Cancelación: la restitución de stock ocurre dentro de la misma transacción que
/// cambia el estado, y el cambio de estado es condicional
///     UPDATE Pedido SET IdEstadoPedido = @cancelado WHERE Id = @id AND IdEstadoPedido IN (…)
/// de modo que un segundo intento de cancelar afecta 0 filas y no restituye dos veces.
/// </summary>
public sealed class PedidoService(VelvetRoseDbContext db)
{
    // Estados desde los que se puede cancelar (todos descontaron stock al crearse).
    private static readonly string[] EstadosCancelables = ["Pendiente", "Confirmado", "Preparado"];

    // Transiciones válidas del flujo de pedidos.
    private static readonly Dictionary<string, string[]> Transiciones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pendiente"] = ["Confirmado", "Cancelado"],
        ["Confirmado"] = ["Preparado", "Cancelado"],
        ["Preparado"] = ["Entregado", "Cancelado"],
        ["Entregado"] = [],
        ["Cancelado"] = [],
    };

    public static IReadOnlyList<string> TransicionesPermitidas(string estado)
        => Transiciones.TryGetValue(estado, out var siguientes) ? siguientes : [];

    public static bool EsCancelacionPermitida(string estado) => EstadosCancelables.Contains(estado);

    public async Task<Resultado<PedidoDto>> CrearAsync(
        int idCliente, CrearPedidoRequest request, CancellationToken cancellationToken)
    {
        if (idCliente <= 0)
        {
            return Resultado<PedidoDto>.Fallido(
                StatusCodes.Status403Forbidden,
                "La sesión no tiene un cliente asociado.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Resultado<PedidoDto>.Fallido(
                StatusCodes.Status400BadRequest,
                "El pedido debe tener al menos un producto.");
        }

        // Agrupa líneas repetidas de la misma variante.
        var lineas = request.Items
            .GroupBy(i => i.IdProductoVariante)
            .Select(g => (IdVariante: g.Key, Cantidad: g.Sum(i => i.Cantidad)))
            .ToList();

        var estados = await db.EstadosPedido.AsNoTracking()
            .ToDictionaryAsync(e => e.Nombre, e => e.IdEstadoPedido, cancellationToken);

        if (!estados.TryGetValue("Pendiente", out var idEstadoPendiente))
        {
            return Resultado<PedidoDto>.Fallido(
                StatusCodes.Status500InternalServerError,
                "Falta el estado inicial de pedido.");
        }

        // EnableRetryOnFailure exige ejecutar la transacción dentro de la
        // execution strategy para que los reintentos sean seguros.
        var estrategia = db.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            await using var transaccion = await db.Database.BeginTransactionAsync(cancellationToken);
            var resultado = await CrearEnTransaccionAsync(
                idCliente, request, idEstadoPendiente, lineas, transaccion, cancellationToken);
            if (resultado.Valor is not null)
            {
                await transaccion.CommitAsync(cancellationToken);
            }

            return resultado;
        });
    }

    private async Task<Resultado<PedidoDto>> CrearEnTransaccionAsync(
        int idCliente,
        CrearPedidoRequest request,
        int idEstadoPendiente,
        List<(int IdVariante, int Cantidad)> lineas,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaccion,
        CancellationToken cancellationToken)
    {

        var pedido = new Pedido
        {
            IdCliente = idCliente,
            IdEstadoPedido = idEstadoPendiente,
            FechaPedido = DateTime.UtcNow,
            Observacion = string.IsNullOrWhiteSpace(request.Observacion)
                ? null
                : request.Observacion.Trim(),
            Detalles = [],
        };

        decimal total = 0;
        var faltantes = new List<string>();

        foreach (var (idVariante, cantidad) in lineas)
        {
            if (cantidad <= 0)
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                return Resultado<PedidoDto>.Fallido(
                    StatusCodes.Status400BadRequest, "La cantidad debe ser mayor a cero.");
            }

            var variante = await db.ProductoVariantes
                .Include(v => v.Producto)
                .FirstOrDefaultAsync(v => v.IdProductoVariante == idVariante, cancellationToken);

            if (variante?.Producto is not { Activo: true })
            {
                faltantes.Add($"La variante {idVariante} ya no está disponible.");
                continue;
            }

            // DESCUENTO CONDICIONAL: atómico en SQL Server (ver nota de concurrencia).
            var descontadas = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE [dbo].[ProductoVariante]
                    SET [Stock] = [Stock] - {cantidad}
                  WHERE [IdProductoVariante] = {idVariante}
                    AND [Stock] >= {cantidad}
                 """,
                cancellationToken);

            if (descontadas == 0)
            {
                faltantes.Add(
                    $"Stock insuficiente de \"{variante.Producto.Nombre}\" " +
                    $"(talle {variante.Talle?.Nombre}, color {variante.Color?.Nombre}): " +
                    $"quedan {variante.Stock} disponibles.");
                continue;
            }

            var precioVigente = variante.Precio ?? variante.Producto.PrecioBase;
            var subtotal = precioVigente * cantidad;
            total += subtotal;

            pedido.Detalles.Add(new PedidoDetalle
            {
                IdProductoVariante = idVariante,
                Cantidad = cantidad,
                PrecioUnitario = precioVigente,
                Subtotal = subtotal,
            });
        }

        if (faltantes.Count > 0)
        {
            // Ninguna línea se descuenta: la transacción se revierte completa.
            await transaccion.RollbackAsync(CancellationToken.None);
            return Resultado<PedidoDto>.Fallido(
                StatusCodes.Status409Conflict, string.Join(" ", faltantes));
        }

        pedido.Total = total;
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync(cancellationToken);

        var idPedido = pedido.IdPedido;
        // El commit lo hace el caller (execution strategy) sólo si no hubo rechazo.
        return Resultado<PedidoDto>.Ok(await MapearAsync(idPedido, cancellationToken));
    }

    public async Task<Resultado<PedidoDto>> CancelarAsync(
        int idPedido, int idCliente, bool esAdministrador, CancellationToken cancellationToken)
    {
        var estrategia = db.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            await using var transaccion = await db.Database.BeginTransactionAsync(cancellationToken);
            var resultado = await CancelarEnTransaccionAsync(
                idPedido, idCliente, esAdministrador, transaccion, cancellationToken);
            if (resultado.Valor is not null)
            {
                await transaccion.CommitAsync(cancellationToken);
            }

            return resultado;
        });
    }

    private async Task<Resultado<PedidoDto>> CancelarEnTransaccionAsync(
        int idPedido,
        int idCliente,
        bool esAdministrador,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaccion,
        CancellationToken cancellationToken)
    {

        var pedido = await db.Pedidos
            .FirstOrDefaultAsync(p => p.IdPedido == idPedido, cancellationToken);

        if (pedido is null || (!esAdministrador && pedido.IdCliente != idCliente))
        {
            await transaccion.RollbackAsync(cancellationToken);
            return Resultado<PedidoDto>.Fallido(
                StatusCodes.Status404NotFound, "Pedido no encontrado.");
        }

        var estados = await db.EstadosPedido.AsNoTracking()
            .ToDictionaryAsync(e => e.IdEstadoPedido, e => e.Nombre, cancellationToken);
        var estadoActual = estados.GetValueOrDefault(pedido.IdEstadoPedido, string.Empty);

        if (!EsCancelacionPermitida(estadoActual))
        {
            await transaccion.RollbackAsync(cancellationToken);
            return Resultado<PedidoDto>.Fallido(
                StatusCodes.Status409Conflict,
                $"Un pedido en estado \"{estadoActual}\" no se puede cancelar.");
        }

        var idCancelado = await db.EstadosPedido
            .Where(e => e.Nombre == "Cancelado")
            .Select(e => e.IdEstadoPedido)
            .FirstOrDefaultAsync(cancellationToken);

        // Cambio de estado condicional: sólo una cancelación puede pasar (sin doble restitución).
        // Los nombres son constantes de código, cada uno viaja como parámetro.
        var cambiado = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE [dbo].[Pedido]
                SET [IdEstadoPedido] = {idCancelado}
              WHERE [IdPedido] = {idPedido}
                AND [IdEstadoPedido] IN (
                    SELECT [IdEstadoPedido] FROM [dbo].[EstadoPedido]
                     WHERE [Nombre] IN ({"Pendiente"}, {"Confirmado"}, {"Preparado"}))
             """,
            cancellationToken);

        if (cambiado == 0)
        {
            await transaccion.RollbackAsync(cancellationToken);
            return Resultado<PedidoDto>.Fallido(
                StatusCodes.Status409Conflict,
                "El pedido ya fue cancelado o cambió de estado.");
        }

        // Restitución de stock de cada línea (sólo dentro de esta transacción).
        var lineas = await db.PedidoDetalles.AsNoTracking()
            .Where(d => d.IdPedido == idPedido)
            .Select(d => new { d.IdProductoVariante, d.Cantidad })
            .ToListAsync(cancellationToken);

        foreach (var linea in lineas)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE [dbo].[ProductoVariante]
                    SET [Stock] = [Stock] + {linea.Cantidad}
                  WHERE [IdProductoVariante] = {linea.IdProductoVariante}
                 """,
                cancellationToken);
        }

        // El commit lo hace el caller (execution strategy) sólo si no hubo rechazo:
        // MapearAsync lee dentro de esta misma transacción.
        return Resultado<PedidoDto>.Ok(await MapearAsync(idPedido, cancellationToken));
    }

    /// <summary>Pedidos de un cliente concreto (recientes primero).</summary>
    public async Task<IReadOnlyList<PedidoDto>> GetDeClienteAsync(
        int idCliente, CancellationToken cancellationToken)
    {
        var ids = await db.Pedidos.AsNoTracking()
            .Where(p => p.IdCliente == idCliente)
            .OrderByDescending(p => p.FechaPedido)
            .Select(p => p.IdPedido)
            .ToListAsync(cancellationToken);

        var resultado = new List<PedidoDto>(ids.Count);
        foreach (var id in ids)
        {
            resultado.Add(await MapearAsync(id, cancellationToken));
        }

        return resultado;
    }

    public async Task<PedidoDto?> GetAsync(int idPedido, CancellationToken cancellationToken)
    {
        var existe = await db.Pedidos.AsNoTracking()
            .AnyAsync(p => p.IdPedido == idPedido, cancellationToken);

        return existe ? await MapearAsync(idPedido, cancellationToken) : null;
    }

    /// <summary>True si el pedido pertenece al cliente indicado (autorización por datos, no por frontend).</summary>
    public Task<bool> PerteneceAAsync(int idPedido, int idCliente, CancellationToken cancellationToken)
        => db.Pedidos.AsNoTracking()
            .AnyAsync(p => p.IdPedido == idPedido && p.IdCliente == idCliente, cancellationToken);

    /// <summary>Mapea un pedido guardado a su DTO (detalles con datos de variante).</summary>
    public async Task<PedidoDto> MapearAsync(int idPedido, CancellationToken cancellationToken)
    {
        var pedido = await db.Pedidos.AsNoTracking()
            .Where(p => p.IdPedido == idPedido)
            .Select(p => new
            {
                p.IdPedido,
                p.FechaPedido,
                p.Total,
                p.Observacion,
                Estado = p.EstadoPedido!.Nombre,
                Detalles = p.Detalles
                    .OrderBy(d => d.IdPedidoDetalle)
                    .Select(d => new PedidoDetalleDto
                    {
                        IdProductoVariante = d.IdProductoVariante,
                        IdProducto = d.ProductoVariante!.IdProducto,
                        Producto = d.ProductoVariante.Producto!.Nombre,
                        UrlImagen = d.ProductoVariante.Producto.Imagenes
                            .OrderBy(i => i.Principal ? 0 : 1)
                            .ThenBy(i => i.Orden)
                            .Select(i => i.UrlImagen)
                            .FirstOrDefault(),
                        Talle = d.ProductoVariante.Talle!.Nombre,
                        Color = d.ProductoVariante.Color!.Nombre,
                        CodigoHex = d.ProductoVariante.Color.CodigoHex,
                        Cantidad = d.Cantidad,
                        PrecioUnitario = d.PrecioUnitario,
                        Subtotal = d.Subtotal,
                    })
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"No existe el pedido {idPedido}.");

        return new PedidoDto
        {
            Id = pedido.IdPedido,
            FechaPedido = pedido.FechaPedido,
            Estado = pedido.Estado,
            Total = pedido.Total,
            Observacion = pedido.Observacion,
            PermiteCancelacion = EsCancelacionPermitida(pedido.Estado),
            Detalles = pedido.Detalles,
        };
    }
}
