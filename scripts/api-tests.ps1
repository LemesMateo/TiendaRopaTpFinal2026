# ============================================================================
# Pruebas obligatorias de API — Velvet Rose Boutique (incremento 2)
# Contra: http://localhost:5100  (SQL Server localhost / MSSQLSERVER)
# Uso:   powershell -ExecutionPolicy Bypass -File scripts\api-tests.ps1
# Reporta PASS/FAIL por prueba y sale con código 1 si algo falla.
# Nota: archivo UTF-8 con BOM (PowerShell 5.1 lo exige para leer acentos).
# ============================================================================
$ErrorActionPreference = 'Continue'
$BaseUrl = 'http://localhost:5100'
$script:Pass = 0
$script:Fail = 0
$script:Resultados = @()

function Report($nombre, $ok, $detalle = '') {
    if ($ok) { $script:Pass++; $estado = 'PASS' } else { $script:Fail++; $estado = 'FAIL' }
    $script:Resultados += [pscustomobject]@{ Estado = $estado; Prueba = $nombre; Detalle = $detalle }
    if ($ok) { Write-Host "[PASS] $nombre - $detalle" -ForegroundColor Green }
    else { Write-Host "[FAIL] $nombre - $detalle" -ForegroundColor Red }
}

# Llamada HTTP que no aborta ante status codes de error.
function Call($Metodo, $Ruta, $Body = $null, $Token = $null) {
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Method = $Metodo; Uri = "$BaseUrl$Ruta"; Headers = $headers; UseBasicParsing = $true }
    if ($null -ne $Body) {
        $params['ContentType'] = 'application/json'
        $params['Body'] = ($Body | ConvertTo-Json -Depth 6)
    }
    try {
        $r = Invoke-WebRequest @params
        return [pscustomobject]@{ Status = [int]$r.StatusCode; Body = $r.Content }
    } catch {
        $resp = $_.Exception.Response
        $status = if ($resp) { [int]$resp.StatusCode } else { 0 }
        $contenido = ''
        try {
            $stream = $resp.GetResponseStream()
            if ($stream) {
                $stream.Position = 0
                $reader = New-Object System.IO.StreamReader($stream)
                $contenido = $reader.ReadToEnd()
            }
        } catch { }
        return [pscustomobject]@{ Status = $status; Body = $contenido }
    }
}

function Get-Prop($json, $ruta) {
    try {
        $o = $json | ConvertFrom-Json
        foreach ($p in $ruta.Split('.')) { $o = $o.$p }
        return $o
    } catch { return $null }
}

# PS 5.1: '@($json | ConvertFrom-Json)' envuelve el array emitido como UN solo
# objeto (pipeline no lo despliega), por lo que Where/Select -First fallan.
# Además, 'return @()' despliega el array vacío y la función devuelve $null.
# Solución: asignar primero y devolver sin desplegar (Write-Output -NoEnumerate).
function Get-Lista($json) {
    try {
        $o = $json | ConvertFrom-Json
        Write-Output -NoEnumerate @($o)
    } catch {
        Write-Output -NoEnumerate @()
    }
}

function Get-SqlScalar($consulta) {
    # -d evita el mensaje "Changed database context..." que contamina la salida del USE.
    $out = & sqlcmd -S localhost -E -C -d VelvetRoseDev -W -h -1 -Q "SET NOCOUNT ON; $consulta" 2>&1
    $limpio = ("$out" -join "`n" -replace 'Changed database context.*', '').Trim()
    return $limpio
}

$marca = [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
$emailCliente = "test.cliente.$marca@velvetrose.dev"
$Password = 'Clave123!'

Write-Host ''
Write-Host '=== 1. AUTENTICACION ===' -ForegroundColor Cyan

# --- registro valido ---
$r = Call POST '/api/auth/registro' @{ nombre = 'Test'; apellido = 'Cliente'; email = $emailCliente; password = $Password }
$tokenCliente = Get-Prop $r.Body 'token'
Report 'Registro valido -> 201 + JWT' ($r.Status -eq 201 -and $tokenCliente) "status=$($r.Status)"

# --- registro duplicado ---
$r = Call POST '/api/auth/registro' @{ nombre = 'Test'; apellido = 'Cliente'; email = $emailCliente; password = $Password }
Report 'Registro duplicado -> 409' ($r.Status -eq 409) "status=$($r.Status)"

# --- registro invalido (email malo) ---
$r = Call POST '/api/auth/registro' @{ nombre = 'Test'; apellido = 'Cliente'; email = 'no-es-email'; password = $Password }
Report 'Registro email invalido -> 400' ($r.Status -eq 400) "status=$($r.Status)"

# --- login correcto ---
$r = Call POST '/api/auth/login' @{ email = $emailCliente; password = $Password }
$tokenCliente2 = Get-Prop $r.Body 'token'
Report 'Login correcto -> 200 + JWT' ($r.Status -eq 200 -and $tokenCliente2) "status=$($r.Status)"

# --- login incorrecto ---
$r = Call POST '/api/auth/login' @{ email = $emailCliente; password = 'otra-clave' }
Report 'Login incorrecto -> 401' ($r.Status -eq 401) "status=$($r.Status)"

# --- login usuario de desarrollo (cliente seed) ---
$r = Call POST '/api/auth/login' @{ email = 'cliente@velvetrose.dev'; password = 'Velvet123!' }
$tokenSeedCliente = Get-Prop $r.Body 'token'
Report 'Login seed cliente@velvetrose.dev -> 200' ($r.Status -eq 200 -and $tokenSeedCliente) "status=$($r.Status)"

# --- login usuario de desarrollo (admin seed) ---
$r = Call POST '/api/auth/login' @{ email = 'admin@velvetrose.dev'; password = 'Velvet123!' }
$tokenAdmin = Get-Prop $r.Body 'token'
$rolAdmin = Get-Prop $r.Body 'usuario.rol'
Report 'Login seed admin@velvetrose.dev -> 200 + rol Administrador' ($r.Status -eq 200 -and $tokenAdmin -and $rolAdmin -eq 'Administrador') "status=$($r.Status) rol=$rolAdmin"

# --- hash de contrasenas en BD (no texto plano) ---
$coincidencias = Get-SqlScalar "SELECT COUNT(*) FROM Usuario WHERE PasswordHash LIKE '%Velvet123%' OR PasswordHash = 'Velvet123!';"
Report 'Contrasenas sin texto plano en SQL Server' ($coincidencias -eq '0') "coincidencias=$coincidencias"

# --- /me sin token ---
$r = Call GET '/api/auth/me'
Report 'GET /api/auth/me sin token -> 401' ($r.Status -eq 401) "status=$($r.Status)"

# --- /me con token ---
$r = Call GET '/api/auth/me' $null $tokenCliente
$rolCliente = Get-Prop $r.Body 'rol'
Report 'GET /api/auth/me con token -> 200 + rol Cliente' ($r.Status -eq 200 -and $rolCliente -eq 'Cliente') "status=$($r.Status) rol=$rolCliente"

Write-Host ''
Write-Host '=== 2. ENDPOINT PROTEGIDO / ROLES ===' -ForegroundColor Cyan

$r = Call GET '/api/admin/productos'
Report 'GET /api/admin/productos sin token -> 401' ($r.Status -eq 401) "status=$($r.Status)"

$r = Call GET '/api/admin/productos' $null $tokenCliente
Report 'Cliente -> endpoint admin -> 403' ($r.Status -eq 403) "status=$($r.Status)"

$r = Call GET '/api/admin/productos?pageSize=5' $null $tokenAdmin
Report 'Admin -> GET /api/admin/productos -> 200' ($r.Status -eq 200) "status=$($r.Status)"

Write-Host ''
Write-Host '=== 3. CATALOGO ===' -ForegroundColor Cyan

$r = Call GET '/api/productos/1'
Report 'Detalle de producto -> 200' ($r.Status -eq 200) "status=$($r.Status)"

$r = Call GET '/api/productos/1/variantes'
$variantesLista = Get-Lista $r.Body
$nVariantes = $variantesLista.Count
Report 'Variantes de producto -> 200 + lista' ($r.Status -eq 200 -and $nVariantes -gt 0) "status=$($r.Status) n=$nVariantes"

Write-Host ''
Write-Host '=== 4. CARRITO NO MODIFICA STOCK ===' -ForegroundColor Cyan

$rVar = Call GET '/api/productos/1/variantes'
$todas = Get-Lista $rVar.Body
$variante = $todas | Where-Object { $_.stock -gt 2 } | Select-Object -First 1
$idVariante = $variante.id
$stockAntesSql = [int](Get-SqlScalar "SELECT Stock FROM ProductoVariante WHERE IdProductoVariante = $idVariante;")

# El carrito es 100% cliente: no existe endpoint que descuente stock.
Start-Sleep -Seconds 1
$stockDespuesSql = [int](Get-SqlScalar "SELECT Stock FROM ProductoVariante WHERE IdProductoVariante = $idVariante;")
Report 'Agregar al carrito NO altera SQL Server' ($stockAntesSql -eq $stockDespuesSql) "antes=$stockAntesSql despues=$stockDespuesSql"

Write-Host ''
Write-Host '=== 5. PEDIDOS ===' -ForegroundColor Cyan

$r = Call POST '/api/pedidos' @{ items = @(@{ idProductoVariante = $idVariante; cantidad = 1 }) }
Report 'POST /api/pedidos sin token -> 401' ($r.Status -eq 401) "status=$($r.Status)"

$stockAntes = $stockAntesSql
$r = Call POST '/api/pedidos' @{ items = @(@{ idProductoVariante = $idVariante; cantidad = 1 }) } $tokenCliente
$idPedido = Get-Prop $r.Body 'id'
$total = Get-Prop $r.Body 'total'
Report 'Crear pedido valido -> 201 + id' ($r.Status -eq 201 -and $idPedido) "status=$($r.Status) id=$idPedido total=$total"

$stockPost = [int](Get-SqlScalar "SELECT Stock FROM ProductoVariante WHERE IdProductoVariante = $idVariante;")
Report 'Stock descontado tras crear pedido' ($stockPost -eq ($stockAntes - 1)) "antes=$stockAntes despues=$stockPost"

# --- pedir mas stock del disponible ---
$disponible = $stockPost
$r = Call POST '/api/pedidos' @{ items = @(@{ idProductoVariante = $idVariante; cantidad = ($disponible + 5) }) } $tokenCliente
Report 'Pedir mas stock del disponible -> 409' ($r.Status -eq 409) "status=$($r.Status)"

$stockSigue = [int](Get-SqlScalar "SELECT Stock FROM ProductoVariante WHERE IdProductoVariante = $idVariante;")
Report 'Sin descuento parcial tras rechazo (1 variante)' ($stockSigue -eq $stockPost) "stock=$stockSigue (esperado $stockPost)"

# --- pedido con varias variantes donde una falla ---
$rVar = Call GET '/api/productos/1/variantes'
$todas = Get-Lista $rVar.Body
$okVar = $todas | Where-Object { $_.stock -ge 1 } | Select-Object -First 1
$sinVar = $todas | Where-Object { $_.stock -eq 0 } | Select-Object -First 1
if (-not $sinVar) { $sinVar = $todas | Select-Object -Last 1 }

$pre = Get-SqlScalar "SELECT CAST(SUM(Stock) AS VARCHAR(20)) FROM ProductoVariante WHERE IdProducto = 1;"

$r = Call POST '/api/pedidos' @{ items = @(
    @{ idProductoVariante = $okVar.id; cantidad = 1 },
    @{ idProductoVariante = $sinVar.id; cantidad = 99 }
) } $tokenCliente
Report 'Pedido mixto (variante sin stock) -> 409' ($r.Status -eq 409) "status=$($r.Status)"

$post = Get-SqlScalar "SELECT CAST(SUM(Stock) AS VARCHAR(20)) FROM ProductoVariante WHERE IdProducto = 1;"
Report 'Sin descuento parcial en pedido mixto' ($pre -eq $post) "suma_stock antes=$pre despues=$post"

# --- listar pedidos del cliente ---
$r = Call GET '/api/pedidos' $null $tokenCliente
$misPedidos = Get-Lista $r.Body
$nMisPedidos = @($misPedidos).Count
Report 'GET /api/pedidos (propios) -> 200 + lista' ($r.Status -eq 200 -and $nMisPedidos -ge 1) "status=$($r.Status) n=$nMisPedidos"

# --- impedir consultar pedidos ajenos ---
$otroEmail = "test.ajeno.$marca@velvetrose.dev"
Call POST '/api/auth/registro' @{ nombre = 'Ajeno'; apellido = 'Otro'; email = $otroEmail; password = $Password } | Out-Null
$rAjeno = Call POST '/api/auth/login' @{ email = $otroEmail; password = $Password }
$tokenAjeno = Get-Prop $rAjeno.Body 'token'
$r = Call GET "/api/pedidos/$idPedido" $null $tokenAjeno
Report 'Pedido ajeno -> 404 (no expone datos)' ($r.Status -eq 404) "status=$($r.Status)"

# --- cancelar pedido propio y restituir stock ---
$r = Call POST "/api/pedidos/$idPedido/cancelar" $null $tokenCliente
Report 'Cancelar pedido propio -> 200' ($r.Status -eq 200) "status=$($r.Status)"
$stockTrasCancelar = [int](Get-SqlScalar "SELECT Stock FROM ProductoVariante WHERE IdProductoVariante = $idVariante;")
Report 'Stock restituido tras cancelar' ($stockTrasCancelar -eq $stockAntesSql) "antes=$stockAntesSql despues=$stockTrasCancelar"

# --- impedir restitucion doble ---
$r = Call POST "/api/pedidos/$idPedido/cancelar" $null $tokenCliente
$stockDoble = [int](Get-SqlScalar "SELECT Stock FROM ProductoVariante WHERE IdProductoVariante = $idVariante;")
Report 'Segunda cancelacion -> 409 y stock sin doble restitucion' ($r.Status -eq 409 -and $stockDoble -eq $stockAntesSql) "status=$($r.Status) stock=$stockDoble (esperado $stockAntesSql)"

Write-Host ''
Write-Host '=== 6. ADMINISTRACION ===' -ForegroundColor Cyan

$r = Call POST '/api/admin/productos' @{ nombre = "Producto Test $marca"; idCategoria = 1; descripcion = 'Alta de prueba'; precioBase = 12345; activo = $true } $tokenAdmin
$idProductoNuevo = Get-Prop $r.Body 'id'
Report 'Admin crea producto -> 201' ($r.Status -eq 201 -and $idProductoNuevo) "status=$($r.Status) id=$idProductoNuevo"

$r = Call PUT "/api/admin/productos/$idProductoNuevo" @{ id = $idProductoNuevo; nombre = "Producto Test $marca (editado)"; idCategoria = 1; precioBase = 19999; activo = $true } $tokenAdmin
$nuevoNombre = Get-Prop $r.Body 'nombre'
Report 'Admin edita producto -> 200 con nombre nuevo' ($r.Status -eq 200 -and "$nuevoNombre" -like '*editado*') "status=$($r.Status) nombre=$nuevoNombre"

$idTalle = [int](Get-SqlScalar 'SELECT TOP 1 IdTalle FROM Talle ORDER BY IdTalle;')
$idColor = [int](Get-SqlScalar 'SELECT TOP 1 IdColor FROM Color ORDER BY IdColor;')
$r = Call POST '/api/admin/variantes' @{ idProducto = $idProductoNuevo; idTalle = $idTalle; idColor = $idColor; stock = 5; precio = 19999 } $tokenAdmin
$idVarianteNueva = Get-Prop $r.Body 'id'
Report 'Admin crea variante -> 200' ($r.Status -eq 200 -and $idVarianteNueva) "status=$($r.Status) id=$idVarianteNueva"

$r = Call POST '/api/admin/variantes' @{ idProducto = $idProductoNuevo; idTalle = $idTalle; idColor = $idColor; stock = 3 } $tokenAdmin
Report 'Duplicar variante (prod+talle+color) -> 409' ($r.Status -eq 409) "status=$($r.Status)"

$r = Call POST '/api/admin/variantes' @{ idProducto = $idProductoNuevo; idTalle = $idTalle; idColor = $idColor; stock = -1 } $tokenAdmin
Report 'Variante con stock negativo -> rechazado' ($r.Status -ge 400) "status=$($r.Status)"

$r = Call POST '/api/admin/categorias' @{ nombre = "Cat Test $marca"; activo = $true } $tokenAdmin
Report 'Admin crea categoria -> 200' ($r.Status -eq 200) "status=$($r.Status)"
$r = Call POST '/api/admin/talles' @{ nombre = "T$($marca.Substring($marca.Length - 4))"; activo = $true } $tokenAdmin
Report 'Admin crea talle -> 200' ($r.Status -eq 200) "status=$($r.Status)"
$r = Call POST '/api/admin/colores' @{ nombre = "Col Test $marca"; codigoHex = '#123456'; activo = $true } $tokenAdmin
Report 'Admin crea color -> 200' ($r.Status -eq 200) "status=$($r.Status)"

$r = Call GET '/api/admin/pedidos?pageSize=50' $null $tokenAdmin
Report 'Admin lista pedidos -> 200' ($r.Status -eq 200) "status=$($r.Status)"

$rPedido = Call POST '/api/pedidos' @{ items = @(@{ idProductoVariante = $idVariante; cantidad = 1 }) } $tokenSeedCliente
$idPedidoAdmin = Get-Prop $rPedido.Body 'id'
Report 'Pedido nuevo para flujo admin' ($rPedido.Status -eq 201 -and $idPedidoAdmin) "id=$idPedidoAdmin"

if ($idPedidoAdmin) {
    $r = Call PUT "/api/admin/pedidos/$idPedidoAdmin/estado" @{ estado = 'Confirmado' } $tokenAdmin
    Report 'Transicion Pendiente -> Confirmado -> 200' ($r.Status -eq 200) "status=$($r.Status)"

    $r = Call PUT "/api/admin/pedidos/$idPedidoAdmin/estado" @{ estado = 'Entregado' } $tokenAdmin
    Report 'Transicion invalida Confirmado -> Entregado -> 409' ($r.Status -eq 409) "status=$($r.Status)"

    $r = Call PUT "/api/admin/pedidos/$idPedidoAdmin/estado" @{ estado = 'Preparado' } $tokenAdmin
    Report 'Transicion Confirmado -> Preparado -> 200' ($r.Status -eq 200) "status=$($r.Status)"

    $r = Call PUT "/api/admin/pedidos/$idPedidoAdmin/estado" @{ estado = 'Entregado' } $tokenAdmin
    Report 'Transicion Preparado -> Entregado -> 200' ($r.Status -eq 200) "status=$($r.Status)"

    $r = Call PUT "/api/admin/pedidos/$idPedidoAdmin/estado" @{ estado = 'Cancelado' } $tokenAdmin
    Report 'Cancelar pedido Entregado -> 409' ($r.Status -eq 409) "status=$($r.Status)"

    $r = Call PUT "/api/admin/pedidos/$idPedidoAdmin/estado" @{ estado = 'Cancelado' } $tokenCliente
    Report 'Cliente intenta cambio de estado admin -> 403' ($r.Status -eq 403) "status=$($r.Status)"
}

$r = Call DELETE "/api/admin/productos/$idProductoNuevo" $null $tokenAdmin
$activoSigue = Get-SqlScalar "SELECT CAST(Activo AS INT) FROM Producto WHERE IdProducto = $idProductoNuevo;"
Report 'Desactivar producto -> 204 + Activo=0 (baja logica)' ($r.Status -eq 204 -and $activoSigue -eq '0') "status=$($r.Status) activo=$activoSigue"

Write-Host ''
Write-Host '=== 7. IDENTIDAD DE LA INSTANCIA ===' -ForegroundColor Cyan
$ident = & sqlcmd -S localhost -E -C -W -s '|' -Q "SET NOCOUNT ON; SELECT @@SERVERNAME AS srv, DB_NAME() AS db, SUSER_SNAME() AS usr;" 2>&1
$identTxt = ("$ident" -join ' ').Trim()
Report 'Pruebas contra localhost (no LocalDB)' (($identTxt -match 'Markhan|localhost') -and ($identTxt -notmatch 'MSSQLLocalDB')) $identTxt

Write-Host ''
Write-Host '=== 8. LIMPIEZA DE DATOS DE PRUEBA ===' -ForegroundColor Cyan
# Borra los registros creados por esta misma corrida para que la BD de
# desarrollo quede como la dejó el seeder (productos/categorías de prueba,
# usuarios test.* y sus pedidos, en ese orden por las FK).
$limpieza = @"
SET QUOTED_IDENTIFIER ON;  -- requerido por el índice único parcial de ProductoImagen
USE VelvetRoseDev;
DELETE d FROM PedidoDetalle d INNER JOIN Pedido p ON p.IdPedido=d.IdPedido
 WHERE p.IdCliente IN (SELECT IdCliente FROM Cliente c INNER JOIN Usuario u ON u.IdUsuario=c.IdUsuario WHERE u.Email LIKE 'test.%@velvetrose.dev');
DELETE p FROM Pedido p
 WHERE p.IdCliente IN (SELECT IdCliente FROM Cliente c INNER JOIN Usuario u ON u.IdUsuario=c.IdUsuario WHERE u.Email LIKE 'test.%@velvetrose.dev');
DELETE v FROM ProductoVariante v WHERE v.IdProducto IN (SELECT IdProducto FROM Producto WHERE Nombre LIKE 'Producto Test %');
DELETE i FROM ProductoImagen i WHERE i.IdProducto IN (SELECT IdProducto FROM Producto WHERE Nombre LIKE 'Producto Test %');
DELETE FROM Producto WHERE Nombre LIKE 'Producto Test %';
DELETE FROM Categoria WHERE Nombre LIKE 'Cat Test %';
DELETE FROM Talle WHERE Nombre LIKE 'T____' AND Nombre NOT IN ('S','M','L','XL');
DELETE FROM Color WHERE Nombre LIKE 'Col Test %';
DELETE FROM Cliente WHERE IdUsuario IN (SELECT IdUsuario FROM Usuario WHERE Email LIKE 'test.%@velvetrose.dev');
DELETE FROM Usuario WHERE Email LIKE 'test.%@velvetrose.dev';
"@
$limp = & sqlcmd -S localhost -E -C -W -Q $limpieza 2>&1
$quedan = Get-SqlScalar "SELECT COUNT(*) FROM Usuario WHERE Email LIKE 'test.%@velvetrose.dev';"
Report 'Datos de prueba eliminados' ($quedan -eq '0') "usuarios_test_restantes=$quedan"

Write-Host ''
Write-Host '=============================================='
$mensaje = "RESULTADO: $script:Pass PASS / $script:Fail FAIL"
if ($script:Fail -eq 0) { Write-Host $mensaje -ForegroundColor Green } else { Write-Host $mensaje -ForegroundColor Red }
$script:Resultados | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
if ($script:Fail -gt 0) { exit 1 }
exit 0
