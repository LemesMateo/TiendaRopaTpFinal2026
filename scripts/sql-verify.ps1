# ============================================================================
# Verificación directa de SQL Server — Velvet Rose Boutique (incremento 2)
# Contra: localhost / MSSQLSERVER  (NO LocalDB)
# Uso:   powershell -ExecutionPolicy Bypass -File scripts\sql-verify.ps1
# ============================================================================
$ErrorActionPreference = 'Continue'
$script:Pass = 0
$script:Fail = 0

function Report($nombre, $ok, $detalle = '') {
    if ($ok) { $script:Pass++; $estado = 'PASS' } else { $script:Fail++; $estado = 'FAIL' }
    if ($ok) { Write-Host "[PASS] $nombre - $detalle" -ForegroundColor Green }
    else { Write-Host "[FAIL] $nombre - $detalle" -ForegroundColor Red }
}

function Q($sql) {
    $out = & sqlcmd -S localhost -E -C -d VelvetRoseDev -W -h -1 -Q "SET NOCOUNT ON; $sql" 2>&1
    return ("$out" -join "`n").Trim()
}

Write-Host '=== 1. IDENTIDAD DE LA INSTANCIA ===' -ForegroundColor Cyan
# @@SERVERNAME puede ser NULL en esta instancia; se complementa con MachineName.
$srv = Q "SELECT COALESCE(@@SERVERNAME, CONVERT(VARCHAR(128), SERVERPROPERTY('MachineName')));"
$usr = Q "SELECT SUSER_SNAME();"
$db = Q "SELECT DB_NAME();"
$ver = Q "SELECT CONVERT(VARCHAR(20), SERVERPROPERTY('ProductVersion'));"
Report 'Conexión a instancia local (no LocalDB)' ($srv -notmatch 'MSSQLLocalDB' -and $srv -match 'Markhan|localhost') "srv=$srv"
Report 'Base activa VelvetRoseDev' ($db -eq 'VelvetRoseDev') "db=$db"
Report 'Usuario Windows Markhan\mateo' ($usr -eq 'Markhan\mateo') "usr=$usr"
Report 'Versión de SQL Server' ($ver -match '^1[5-9]\.') "ver=$ver"

$dbs = Q "SELECT name FROM sys.databases ORDER BY name;"
Report 'Existe VelvetRoseDev en la instancia' ($dbs -match 'VelvetRoseDev') ($dbs -replace "`n", ', ')
Report 'MSSQLLocalDB NO es la base principal' ($dbs -notmatch 'VelvetRoseDev' -or $srv -notmatch 'MSSQLLocalDB') "OK"

Write-Host '=== 2. ESQUEMA (12 TABLAS) ===' -ForegroundColor Cyan
$nTablas = [int](Q "SELECT COUNT(*) FROM sys.tables WHERE name <> '__EFMigrationsHistory';")
Report '12 tablas de negocio' ($nTablas -eq 12) "n=${nTablas}"

$esperadas = @('Categoria', 'Cliente', 'Color', 'EstadoPedido', 'Pedido', 'PedidoDetalle',
    'Producto', 'ProductoImagen', 'ProductoVariante', 'Rol', 'Talle', 'Usuario')
$tablas = Q "SELECT name FROM sys.tables ORDER BY name;"
$faltantes = @($esperadas | Where-Object { $tablas -notmatch [regex]::Escape($_) })
Report 'Todas las tablas esperadas presentes' ($faltantes.Count -eq 0) "faltantes=$($faltantes -join ',')"

Write-Host '=== 3. MIGRACIONES ===' -ForegroundColor Cyan
$mig = Q "SELECT MigrationId FROM __EFMigrationsHistory;"
Report 'Migración InitialCreate aplicada' ($mig -match 'InitialCreate') $mig

Write-Host '=== 4. INTEGRIDAD: FK / ÍNDICES / CHECKS ===' -ForegroundColor Cyan
$nFk = Q "SELECT COUNT(*) FROM sys.foreign_keys;"
Report 'Foreign keys (>= 11)' ([int]$nFk -ge 11) "n=$nFk"

$nChk = Q "SELECT COUNT(*) FROM sys.check_constraints;"
Report 'CHECK constraints (>= 2)' ([int]$nChk -ge 2) "n=$nChk"

$chkStock = Q "SELECT name FROM sys.check_constraints WHERE name = 'CK_ProductoVariante_Stock_NoNegativo';"
Report 'CHECK Stock >= 0 existe' ($chkStock -eq 'CK_ProductoVariante_Stock_NoNegativo') $chkStock

$chkCant = Q "SELECT name FROM sys.check_constraints WHERE name = 'CK_PedidoDetalle_Cantidad_Positiva';"
Report 'CHECK Cantidad > 0 existe' ($chkCant -eq 'CK_PedidoDetalle_Cantidad_Positiva') $chkCant

$idxUnico = Q "SELECT COUNT(*) FROM sys.indexes i JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id JOIN sys.columns c ON ic.object_id=c.object_id AND ic.column_id=c.column_id WHERE i.is_unique=1 AND i.name LIKE 'IX_ProductoVariante%' AND c.name='IdTalle';"
Report 'Índice único producto+talle+color' ([int]$idxUnico -ge 1) "n=$idxUnico"

$idxEmail = Q "SELECT COUNT(*) FROM sys.indexes WHERE is_unique=1 AND name LIKE 'IX_Usuario%Email%';"
Report 'Índice único de email de usuario' ([int]$idxEmail -ge 1) "n=$idxEmail"

# Índice único PARCIAL: una sola imagen principal por producto
$idxImg = Q "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('ProductoImagen') AND is_unique=1 AND has_filter=1 AND filter_definition LIKE '%Principal%';"
Report 'Índice único parcial de imagen principal' ([int]$idxImg -ge 1) "n=$idxImg"

Write-Host '=== 5. DATOS SEED ===' -ForegroundColor Cyan
$nCategorias = [int](Q 'SELECT COUNT(*) FROM Categoria;')
$nTalles = [int](Q 'SELECT COUNT(*) FROM Talle;')
$nColores = [int](Q 'SELECT COUNT(*) FROM Color;')
$nEstados = [int](Q 'SELECT COUNT(*) FROM EstadoPedido;')
$nRoles = [int](Q 'SELECT COUNT(*) FROM Rol;')
$nProductos = [int](Q 'SELECT COUNT(*) FROM Producto;')
$nVariantes = [int](Q 'SELECT COUNT(*) FROM ProductoVariante;')

Report '>= 4 categorías seed (las pruebas añaden más)' ($nCategorias -ge 4) "n=$nCategorias"
Report '>= 4 talles y >= 4 colores seed' ($nTalles -ge 4 -and $nColores -ge 4) "talles=$nTalles colores=$nColores"
Report '5 estados de pedido' ($nEstados -eq 5) "n=$nEstados"
Report '2 roles (Administrador y Cliente)' ($nRoles -eq 2) "n=$nRoles"
Report '>= 9 productos y >= 26 variantes seed' ($nProductos -ge 9 -and $nVariantes -ge 26) "productos=$nProductos variantes=$nVariantes"

$estadosNombre = Q "SELECT nombre FROM EstadoPedido ORDER BY IdEstadoPedido;"
Report 'Estados: Pendiente/Confirmado/Preparado/Entregado/Cancelado' (
    $estadosNombre -match 'Pendiente' -and $estadosNombre -match 'Confirmado' -and
    $estadosNombre -match 'Preparado' -and $estadosNombre -match 'Entregado' -and
    $estadosNombre -match 'Cancelado') ($estadosNombre -replace "`n", '/')

$rolesNombre = Q "SELECT nombre FROM Rol ORDER BY IdRol;"
Report 'Roles con nombre correcto' ($rolesNombre -match 'Administrador' -and $rolesNombre -match 'Cliente') ($rolesNombre -replace "`n", '/')

Write-Host '=== 6. USUARIOS Y HASH ===' -ForegroundColor Cyan
$nUsuarios = [int](Q 'SELECT COUNT(*) FROM Usuario;')
Report 'Usuarios seed presentes (>= 2)' ($nUsuarios -ge 2) "n=$nUsuarios"

$hashLargo = Q "SELECT COUNT(*) FROM Usuario WHERE LEN(PasswordHash) > 40 AND PasswordHash LIKE 'AQAAAA%';"
Report 'Hashes PBKDF2 de ASP.NET Identity (AQAAAA…)' ([int]$hashLargo -ge 2) "n=$hashLargo"

$hashPlano = Q "SELECT COUNT(*) FROM Usuario WHERE PasswordHash IN ('Velvet123!', 'Clave123!') OR PasswordHash LIKE '%Velvet123%';"
Report 'Ninguna contraseña en texto plano' ([int]$hashPlano -eq 0) "n=$hashPlano"

$adminRol = Q "SELECT r.Nombre FROM Usuario u JOIN Rol r ON r.IdRol=u.IdRol WHERE u.Email='admin@velvetrose.dev';"
Report 'admin@velvetrose.dev tiene rol Administrador' ($adminRol -eq 'Administrador') $adminRol

$clienteRol = Q "SELECT r.Nombre FROM Usuario u JOIN Rol r ON r.IdRol=u.IdRol WHERE u.Email='cliente@velvetrose.dev';"
Report 'cliente@velvetrose.dev tiene rol Cliente' ($clienteRol -eq 'Cliente') $clienteRol

$nClientes = [int](Q 'SELECT COUNT(*) FROM Cliente;')
Report 'Perfiles Cliente ligados a usuarios' ($nClientes -ge 2) "n=$nClientes"

Write-Host '=== 7. STOCK: NUNCA NEGATIVO ===' -ForegroundColor Cyan
$negativos = [int](Q 'SELECT COUNT(*) FROM ProductoVariante WHERE Stock < 0;')
Report 'Sin stock negativo' ($negativos -eq 0) "n=$negativos"

$sumaStock = Q 'SELECT SUM(Stock) FROM ProductoVariante;'
Report 'Stock total calculable' ($sumaStock -match '^\d+$') "total=$sumaStock"

Write-Host '=== 8. PEDIDOS Y DETALLES ===' -ForegroundColor Cyan
$nPedidos = [int](Q 'SELECT COUNT(*) FROM Pedido;')
$nDetalles = [int](Q 'SELECT COUNT(*) FROM PedidoDetalle;')
Report 'Pedidos creados por las pruebas' ($nPedidos -ge 1) "pedidos=$nPedidos"
Report 'Detalles de pedido creados' ($nDetalles -ge 1) "detalles=$nDetalles"

$totalOk = [int](Q 'SELECT COUNT(*) FROM Pedido p WHERE p.Total <> (SELECT ISNULL(SUM(Subtotal),0) FROM PedidoDetalle d WHERE d.IdPedido=p.IdPedido);')
Report 'Total = suma de subtotales en cada pedido' ($totalOk -eq 0) "inconsistentes=$totalOk"

$subtotalesOk = [int](Q 'SELECT COUNT(*) FROM PedidoDetalle WHERE Subtotal <> PrecioUnitario * Cantidad;')
Report 'Subtotal = precio x cantidad' ($subtotalesOk -eq 0) "inconsistentes=$subtotalesOk"

$cantNegativa = [int](Q 'SELECT COUNT(*) FROM PedidoDetalle WHERE Cantidad <= 0;')
Report 'Cantidades siempre positivas' ($cantNegativa -eq 0) "n=$cantNegativa"

$pagoSinDetalle = [int](Q 'SELECT COUNT(*) FROM Pedido p WHERE NOT EXISTS (SELECT 1 FROM PedidoDetalle d WHERE d.IdPedido = p.IdPedido);')
Report 'Todo pedido tiene al menos un detalle' ($pagoSinDetalle -eq 0) "pedidosSinDetalle=$pagoSinDetalle"

Write-Host '=== 9. REFERENCIALIDAD ===' -ForegroundColor Cyan
$huerfanos = [int](Q 'SELECT COUNT(*) FROM PedidoDetalle d LEFT JOIN ProductoVariante v ON v.IdProductoVariante=d.IdProductoVariante WHERE v.IdProductoVariante IS NULL;')
Report 'Sin detalles huérfanos de variante' ($huerfanos -eq 0) "n=$huerfanos"

$pedidosHuerfanos = [int](Q 'SELECT COUNT(*) FROM Pedido p LEFT JOIN Cliente c ON c.IdCliente=p.IdCliente WHERE c.IdCliente IS NULL;')
Report 'Sin pedidos huérfanos de cliente' ($pedidosHuerfanos -eq 0) "n=$pedidosHuerfanos"

$variantesHuerfanas = [int](Q 'SELECT COUNT(*) FROM ProductoVariante v LEFT JOIN Producto p ON p.IdProducto=v.IdProducto WHERE p.IdProducto IS NULL;')
Report 'Sin variantes huérfanas de producto' ($variantesHuerfanas -eq 0) "n=$variantesHuerfanas"

Write-Host ''
Write-Host '=============================================='
$mensaje = "RESULTADO SQL: $script:Pass PASS / $script:Fail FAIL"
if ($script:Fail -eq 0) { Write-Host $mensaje -ForegroundColor Green } else { Write-Host $mensaje -ForegroundColor Red }
if ($script:Fail -gt 0) { exit 1 }
exit 0
