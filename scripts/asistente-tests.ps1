# ============================================================================
# Pruebas del asistente conversacional — Velvet Rose Boutique (incremento 4)
# Contra: http://localhost:5100  (SQL Server localhost / MSSQLSERVER)
# Uso:   powershell -ExecutionPolicy Bypass -File scripts\asistente-tests.ps1
# Reporta PASS/FAIL por prueba y sale con código 1 si algo falla.
# Nota: archivo UTF-8 con BOM (PowerShell 5.1 lo exige para leer acentos).
#
# Cubre:
#   - endpoint de chat (validación, autenticación, contexto)
#   - herramientas de solo lectura sobre SQL Server real
#   - notas públicas de pedido (alta y lectura)
#   - aislamiento entre clientes (un cliente no ve pedidos de otro)
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

function Call($Metodo, $Ruta, $Body = $null, $Token = $null) {
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Method = $Metodo; Uri = "$BaseUrl$Ruta"; Headers = $headers; UseBasicParsing = $true }
    if ($null -ne $Body) {
        # PS 5.1 manda un body tipo string en Latin-1: los acentos y la '¿'
        # rompen el JSON (400). Se manda UTF-8 explícito.
        $params['ContentType'] = 'application/json; charset=utf-8'
        $params['Body'] = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 8 -Compress))
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

function Get-SqlScalar($consulta) {
    $limpia = $consulta.TrimEnd(';')
    $out = & sqlcmd -S localhost -E -C -d VelvetRoseDev -W -h -1 -Q "SET NOCOUNT ON; $limpia" 2>&1
    $limpio = ("$out" -join "`n" -replace 'Changed database context.*', '').Trim()
    return $limpio
}

$marca = [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
$emailA = "asistente.a.$marca@velvetrose.dev"
$emailB = "asistente.b.$marca@velvetrose.dev"
$Password = 'Clave123!'

# Los pedidos de esta suite descuentan stock REAL en SQL Server, así que las
# corridas sucesivas lo agotan. Se repone a 5 unidades por variante antes de
# empezar (igual que hace scripts\web-tests.mjs).
function ReponerStock($token) {
    $productos = (Call GET '/api/admin/productos?page=1&pageSize=50' $null $token).Body | ConvertFrom-Json
    $repuestas = 0
    foreach ($p in $productos.items) {
        foreach ($v in $p.variantes) {
            if ($v.stock -lt 5) {
                $resp = Call PUT "/api/admin/variantes/$($v.id)" @{
                    id = $v.id
                    idProducto = $p.id
                    idTalle = $v.idTalle
                    idColor = $v.idColor
                    stock = 5
                    precio = $v.precio
                } $token
                if ($resp.Status -eq 200) { $repuestas++ }
            }
        }
    }
    Write-Host "[setup] variantes repuestas a 5 unidades: $repuestas"
}

Write-Host ''
Write-Host '=== 1. AUTENTICACION DE LOS CLIENTES DE PRUEBA ===' -ForegroundColor Cyan

$r = Call POST '/api/auth/registro' @{ nombre = 'Ana'; apellido = 'Prueba'; email = $emailA; password = $Password }
$tokenA = Get-Prop $r.Body 'token'
Report 'Registro cliente A -> 201 + JWT' ($r.Status -eq 201 -and $tokenA) "status=$($r.Status)"

$r = Call POST '/api/auth/registro' @{ nombre = 'Beto'; apellido = 'Prueba'; email = $emailB; password = $Password }
$tokenB = Get-Prop $r.Body 'token'
Report 'Registro cliente B -> 201 + JWT' ($r.Status -eq 201 -and $tokenB) "status=$($r.Status)"

if (-not $tokenA -or -not $tokenB) {
    Write-Host 'No se pudieron crear los clientes de prueba: se aborta.' -ForegroundColor Red
    exit 1
}

$idClienteA = Get-Prop (Call GET '/api/auth/me' $null $tokenA).Body 'idCliente'
$idClienteB = Get-Prop (Call GET '/api/auth/me' $null $tokenB).Body 'idCliente'
Report 'Los dos clientes tienen IdCliente distinto' ($idClienteA -and $idClienteB -and $idClienteA -ne $idClienteB) "A=$idClienteA B=$idClienteB"

# ---------- Pedidos reales para el cliente A ----------
Write-Host ''
Write-Host '=== 2. PREPARACION: PEDIDOS Y NOTAS ===' -ForegroundColor Cyan

# Producto con stock del catálogo (id 1 del seed).
$loginAdmin = Call POST '/api/auth/login' @{ email = 'admin@velvetrose.dev'; password = 'Velvet123!' }
$tokenAdmin = Get-Prop $loginAdmin.Body 'token'
Report 'Login del administrador seed -> 200 + JWT' ($loginAdmin.Status -eq 200 -and $tokenAdmin) "status=$($loginAdmin.Status)"

$productos = (Call GET '/api/admin/productos?page=1&pageSize=50' $null $tokenAdmin).Body | ConvertFrom-Json
ReponerStock $tokenAdmin
$productos = (Call GET '/api/admin/productos?page=1&pageSize=50' $null $tokenAdmin).Body | ConvertFrom-Json
$producto = $productos.items | Where-Object { $_.variantes | Where-Object { $_.stock -gt 0 } } | Select-Object -First 1
$variante = $producto.variantes | Where-Object { $_.stock -gt 0 } | Select-Object -First 1
Report 'Catalogo: producto con stock disponible' ($null -ne $variante) "producto=$($producto.nombre) variante=$($variante.id)"

if (-not $variante) {
    Write-Host 'No hay producto con stock: se aborta la suite.' -ForegroundColor Red
    exit 1
}

# Pedido del cliente A (activo).
$r = Call POST '/api/pedidos' @{ items = @(@{ idProductoVariante = $variante.id; cantidad = 1 }) } $tokenA
$pedidoA = Get-Prop $r.Body 'id'
Report 'Cliente A crea un pedido activo -> 201' ($r.Status -eq 201 -and $pedidoA) "status=$($r.Status) id=$pedidoA"

# Pedido del cliente B (activo, para probar el aislamiento).
$r = Call POST '/api/pedidos' @{ items = @(@{ idProductoVariante = $variante.id; cantidad = 1 }) } $tokenB
$pedidoB = Get-Prop $r.Body 'id'
Report 'Cliente B crea un pedido activo -> 201' ($r.Status -eq 201 -and $pedidoB) "status=$($r.Status) id=$pedidoB"

if (-not $pedidoA -or -not $pedidoB) {
    Write-Host 'No se pudieron crear los pedidos de prueba: se aborta la suite.' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host '=== 3. NOTAS PUBLICAS DEL PEDIDO ===' -ForegroundColor Cyan

$r = Call GET "/api/admin/pedidos/$pedidoA/notas" $null $tokenAdmin
$notasIniciales = @(Get-Prop $r.Body 'length')
Report 'Listado de notas del pedido -> 200' ($r.Status -eq 200) "status=$($r.Status)"

$r = Call POST "/api/admin/pedidos/$pedidoA/notas" @{ nota = 'Tu pedido fue despachado.'; empresa = 'Velvet Express'; codigoSeguimiento = 'VR-TEST-001' } $tokenAdmin
$notaId = Get-Prop $r.Body 'id'
Report 'Admin crea una nota publica -> 201' ($r.Status -eq 201 -and $notaId) "status=$($r.Status) id=$notaId"

$r = Call GET "/api/admin/pedidos/$pedidoA/notas" $null $tokenAdmin
$notasJson = $r.Body | ConvertFrom-Json
$notas = @()
if ($null -ne $notasJson) { $notas = @($notasJson) }
Report 'La nota aparece en el historial del pedido' ($notas.Count -ge 1) "notas=$($notas.Count)"
# PS 5.1: un escalar no tiene .Count (devuelve $null) → hay que envolver en @().
$conSeguimiento = @($notas | Where-Object { $_.codigoSeguimiento -eq 'VR-TEST-001' })
Report 'La nota guarda el codigo de seguimiento' ($conSeguimiento.Count -ge 1) "valor=[$($notas[0].codigoSeguimiento)] notas=$($notas.Count)"

$r = Call GET "/api/pedidos/$pedidoA" $null $tokenA
$notasClienteJson = Get-Prop $r.Body 'notas'
$notaCliente = @()
if ($null -ne $notasClienteJson) { $notaCliente = @($notasClienteJson) }
Report 'El cliente ve la nota publica en su pedido' ($notaCliente.Count -ge 1 -and $notaCliente[0].nota -like '*despachado*') "notas=$($notaCliente.Count)"

# ---------- Cliente B no puede ver el pedido de A ----------
$r = Call GET "/api/pedidos/$pedidoA" $null $tokenB
Report 'Cliente B NO puede ver el pedido del cliente A -> 404' ($r.Status -eq 404) "status=$($r.Status)"

# ---------- Endpoint del asistente ----------
Write-Host ''
Write-Host '=== 4. ENDPOINT DEL ASISTENTE ===' -ForegroundColor Cyan

$r = Call POST '/api/asistente/mensaje' @{ mensaje = ''; historial = @() } $null
Report 'Chat con mensaje vacio -> 400' ($r.Status -eq 400) "status=$($r.Status)"

$r = Call POST '/api/asistente/mensaje' @{ mensaje = 'Hola, que tal?'; historial = @() } $null
$codigoSinSesion = Get-Prop $r.Body 'errorCodigo'
# 200 = el asistente respondió; 503 = no hay API key configurada (estado
# controlado y esperado cuando la clave todavía no se cargó).
$okSinSesion = ($r.Status -eq 200) -or ($r.Status -eq 503 -and $codigoSinSesion -eq 'sin_configuracion')
Report 'Chat sin sesion responde (200 o 503 controlado)' $okSinSesion "status=$($r.Status) error=$codigoSinSesion"

# Si el asistente no está configurado, todas las consultas de chat deben
# responder 503 con 'sin_configuracion' (nunca 500 ni datos inventados).
$script:ChatActivo = ($r.Status -eq 200)
function ChatOk([int]$status, [string]$codigo) {
    if ($script:ChatActivo) { return ($status -eq 200) }
    return ($status -eq 503 -and $codigo -eq 'sin_configuracion')
}

$r = Call POST '/api/asistente/mensaje' @{ mensaje = 'Hola, que tal?'; historial = @() } $tokenA
$codigo = Get-Prop $r.Body 'errorCodigo'
Report 'Chat con sesion responde 200 (o 503 sin configuracion)' (ChatOk $r.Status $codigo) "status=$($r.Status) error=$codigo"

# Contexto de producto válido e inválido.
$r = Call POST '/api/asistente/mensaje' @{ mensaje = 'Hay stock?'; historial = @(); idProductoContexto = 1 } $tokenA
Report 'Chat con idProductoContexto valido' (ChatOk $r.Status (Get-Prop $r.Body 'errorCodigo')) "status=$($r.Status)"

$r = Call POST '/api/asistente/mensaje' @{ mensaje = 'Hay stock?'; historial = @(); idProductoContexto = 999999 } $tokenA
Report 'Chat con idProductoContexto inexistente (se ignora)' (ChatOk $r.Status (Get-Prop $r.Body 'errorCodigo')) "status=$($r.Status)"

# Contexto de pedido ajeno: se ignora (no se filtra).
$r = Call POST '/api/asistente/mensaje' @{ mensaje = 'Revisa mi pedido'; historial = @(); idPedidoContexto = $pedidoB } $tokenA
Report 'Chat con idPedidoContexto ajeno (se ignora)' (ChatOk $r.Status (Get-Prop $r.Body 'errorCodigo')) "status=$($r.Status)"
$opcionesJson = Get-Prop $r.Body 'opcionesPedido'
$opciones = @()
if ($null -ne $opcionesJson) { $opciones = @($opcionesJson) }
$fugado = $false
foreach ($o in $opciones) { if ("$($o.id)" -eq "$pedidoB") { $fugado = $true } }
Report 'El pedido ajeno no aparece entre las opciones' (-not $fugado) "opciones=$($opciones.Count)"

# Historial: el cliente lo envía y el backend lo acepta.
$r = Call POST '/api/asistente/mensaje' @{ mensaje = 'Y el envio?'; historial = @(@{ rol = 'user'; texto = 'Hola' }, @{ rol = 'assistant'; texto = 'Hola, en que te ayudo?' }) } $tokenA
Report 'Chat con historial' (ChatOk $r.Status (Get-Prop $r.Body 'errorCodigo')) "status=$($r.Status)"

# Mensaje demasiado largo: el backend lo recorta o rechaza con 400.
$largo = 'a' * 2500
$r = Call POST '/api/asistente/mensaje' @{ mensaje = $largo; historial = @() } $tokenA
Report 'Chat con mensaje de 2500 caracteres -> 400' ($r.Status -eq 400) "status=$($r.Status)"

Write-Host ''
Write-Host '=== 5. AISLAMIENTO DE DATOS EN SQL SERVER ===' -ForegroundColor Cyan

$notasDeA = Get-SqlScalar "SELECT COUNT(*) FROM PedidoNota WHERE IdPedido = $pedidoA;"
$notasDeB = Get-SqlScalar "SELECT COUNT(*) FROM PedidoNota WHERE IdPedido = $pedidoB;"
Report 'Las notas quedan asociadas al pedido correcto' ($notasDeA -ge '1' -and $notasDeB -eq '0') "A=$notasDeA B=$notasDeB"

$hashPlano = Get-SqlScalar "SELECT COUNT(*) FROM Usuario WHERE PasswordHash LIKE '%Clave123%';"
Report 'Las contrasenas siguen hasheadas en SQL Server' ($hashPlano -eq '0') "coincidencias=$hashPlano"

# Limpieza de los datos de prueba.
Write-Host ''
Write-Host '=== 6. LIMPIEZA ===' -ForegroundColor Cyan

$null = Call POST "/api/pedidos/$pedidoA/cancelar" @{} $tokenA
$null = Call POST "/api/pedidos/$pedidoB/cancelar" @{} $tokenB

# Las notas son historial del pedido: cancelar NO las borra (se conservan).
$notasTrasCancelar = Get-SqlScalar "SELECT COUNT(*) FROM PedidoNota WHERE IdPedido IN ($pedidoA, $pedidoB)"
Report 'Cancelar el pedido conserva el historial de notas' ($notasTrasCancelar -eq '1') "notas=$notasTrasCancelar"

# Limpieza explícita de las notas creadas por la suite.
$null = & sqlcmd -S localhost -E -C -d VelvetRoseDev -Q "DELETE FROM PedidoNota WHERE IdPedido IN ($pedidoA, $pedidoB);" 2>&1
$notasRestantes = Get-SqlScalar "SELECT COUNT(*) FROM PedidoNota WHERE IdPedido IN ($pedidoA, $pedidoB)"
Report 'Las notas de prueba se eliminan al terminar la suite' ($notasRestantes -eq '0') "restantes=$notasRestantes"

Write-Host ''
Write-Host "=== RESUMEN: $script:Pass PASS / $script:Fail FAIL ===" -ForegroundColor Cyan
if ($script:Fail -gt 0) { exit 1 }
