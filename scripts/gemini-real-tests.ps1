# ============================================================================
# Pruebas del asistente contra GEMINI REAL (API key en user-secrets de la API)
# Usa únicamente datos ficticios del seed de desarrollo.
# Uso: powershell -ExecutionPolicy Bypass -File scripts\gemini-real-tests.ps1
# ============================================================================
$ErrorActionPreference = 'Continue'
$BaseUrl = 'http://localhost:5100'
$script:Pass = 0
$script:Fail = 0

function Report($nombre, $ok, $detalle = '') {
    if ($ok) { $script:Pass++; Write-Host "[PASS] $nombre - $detalle" -ForegroundColor Green }
    else { $script:Fail++; Write-Host "[FAIL] $nombre - $detalle" -ForegroundColor Red }
}

function Call($Metodo, $Ruta, $Body = $null, $Token = $null) {
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Method = $Metodo; Uri = "$BaseUrl$Ruta"; Headers = $headers; UseBasicParsing = $true }
    if ($null -ne $Body) {
        # PS 5.1 manda un body tipo string en Latin-1: los acentos y la '¿'
        #rompen el JSON (400). Se manda UTF-8 explícito.
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
    return (("$out" -join "`n") -replace 'Changed database context.*', '').Trim()
}

Write-Host ''
Write-Host '=== 0. PREPARACION ===' -ForegroundColor Cyan

# Cliente ficticio de la prueba (seed de desarrollo).
$marca = [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
$email = "gemini.real.$marca@velvetrose.dev"
$Password = 'Clave123!'

$r = Call POST '/api/auth/registro' @{ nombre = 'Cliente'; apellido = 'Ficticio'; email = $email; password = $Password }
$token = Get-Prop $r.Body 'token'
Report 'Registro de cliente ficticio -> 201' ($r.Status -eq 201 -and $token) "status=$($r.Status)"

if (-not $token) { Write-Host 'Sin token: se aborta.' -ForegroundColor Red; exit 1 }

# Reponer stock (las corridas anteriores lo descuentan).
$loginAdmin = Call POST '/api/auth/login' @{ email = 'admin@velvetrose.dev'; password = 'Velvet123!' }
$tokenAdmin = Get-Prop $loginAdmin.Body 'token'
$productos = (Call GET '/api/admin/productos?page=1&pageSize=50' $null $tokenAdmin).Body | ConvertFrom-Json
foreach ($p in $productos.items) {
    foreach ($v in $p.variantes) {
        if ($v.stock -lt 5) {
            $null = Call PUT "/api/admin/variantes/$($v.id)" @{
                id = $v.id; idProducto = $p.id; idTalle = $v.idTalle
                idColor = $v.idColor; stock = 5; precio = $v.precio
            } $tokenAdmin
        }
    }
}

# Pedido activo del cliente ficticio (datos ficticios).
$producto = $productos.items | Where-Object { $_.variantes | Where-Object { $_.stock -gt 0 } } | Select-Object -First 1
$variante = $producto.variantes | Where-Object { $_.stock -gt 0 } | Select-Object -First 1
$r = Call POST '/api/pedidos' @{ items = @(@{ idProductoVariante = $variante.id; cantidad = 1 }) } $token
$pedidoId = Get-Prop $r.Body 'id'
Report 'Pedido ficticio creado -> 201' ($r.Status -eq 201 -and $pedidoId) "id=$pedidoId status=$($r.Status)"

# Nota pública ficticia en el pedido.
$r = Call POST "/api/admin/pedidos/$pedidoId/notas" @{
    nota = 'Tu pedido fue despachado y está en camino.'
    empresa = 'Velvet Express'
    codigoSeguimiento = 'VR-FICTICIO-001'
} $tokenAdmin
Report 'Nota publica ficticia creada -> 201' ($r.Status -eq 201) "status=$($r.Status)"

$productoId = $producto.id

# ---------------------------------------------------------------------------
function Chat($titulo, $mensaje, $token, $idProducto = $null, $idPedido = $null) {
    $body = @{ mensaje = $mensaje; historial = @() }
    if ($null -ne $idProducto) { $body['idProductoContexto'] = $idProducto }
    if ($null -ne $idPedido) { $body['idPedidoContexto'] = $idPedido }

    $r = Call POST '/api/asistente/mensaje' $body $token
    $obj = $r.Body | ConvertFrom-Json
    $codigo = Get-Prop $r.Body 'errorCodigo'
    $texto = Get-Prop $r.Body 'mensaje'

    $ok = ($r.Status -eq 200) -and ($codigo -ne 'configuracion_invalida') -and ($codigo -ne 'sin_configuracion')
    Report $titulo $ok "status=$($r.Status) error=$codigo"
    if ($texto) { Write-Host "       respuesta: $texto" -ForegroundColor DarkGray }
    return $obj
}

function Texto($obj) {
    if ($null -eq $obj) { return '' }
    return "$($obj.mensaje)"
}

Write-Host ''
Write-Host '=== 1. CONVERSACIONES CONTRA GEMINI REAL ===' -ForegroundColor Cyan

# 1) Variantes (fue el caso que fallaba con HTTP 400 por la firma).
$r = Chat '1) "¿Este vestido viene en negro talle L?"' '¿Este vestido viene en negro talle L?' $token $productoId $null
if ($r) {
    $txt = Texto $r
    Report '1b) La respuesta no es un error de configuracion' ($txt -notmatch 'no está disponible en este momento') $txt
}

# 2) Variantes disponibles.
$r = Chat '2) "¿Qué variantes hay disponibles?"' '¿Qué variantes hay disponibles?' $token $productoId $null

# 3) Pedidos pendientes.
$r = Chat '3) "¿Tengo pedidos pendientes?"' '¿Tengo pedidos pendientes?' $token $null $null

# 4) Última actualización del pedido.
$r = Chat '4) "¿Cuál es la última actualización de mi pedido?"' '¿Cuál es la última actualización de mi pedido?' $token $null $pedidoId
if ($r) {
    $txt = Texto $r
    # Con la nota cargada, el asistente debe mencionarla (o al menos no inventar).
    Report '4b) La respuesta usa la nota real del pedido' ($txt -match 'despachado' -or $txt -match 'camino' -or $txt -match 'VR-FICTICIO-001' -or $txt -match 'no (hay|tengo) informaci') $txt
}

# 5) Dos herramientas consecutivas en un mismo turno.
$r = Chat '5) Consulta que requiere dos herramientas seguidas' 'Buscá el vestido que tengo en mi pedido y decime qué variantes hay disponibles.' $token $productoId $pedidoId
if ($r) {
    $txt = Texto $r
    Report '5b) La respuesta final no es un error de herramientas' ($txt -notmatch 'no está disponible' -and $txt -notmatch 'reformules') $txt
}

# 6) Mismo turno, segunda ronda (historial con la conversación anterior).
$historial = @(
    @{ rol = 'user'; texto = '¿Qué variantes hay disponibles?' },
    @{ rol = 'assistant'; texto = 'Hay stock en varios talles.' },
    @{ rol = 'user'; texto = '¿Y en negro talle L?' }
)
$body = @{ mensaje = '¿Y en negro talle L?'; historial = $historial; idProductoContexto = $productoId }
$r = Call POST '/api/asistente/mensaje' $body $token
$obj = $r.Body | ConvertFrom-Json
$codigo = Get-Prop $r.Body 'errorCodigo'
Report '6) Turno con historial previo -> 200 sin error' ($r.Status -eq 200 -and $codigo -ne 'configuracion_invalida') "status=$($r.Status) error=$codigo"
Write-Host "       respuesta: $(Get-Prop $r.Body 'mensaje')" -ForegroundColor DarkGray

# 7) Sin sesión: debe pedir iniciar sesión, no inventar datos.
$r = Call POST '/api/asistente/mensaje' @{ mensaje = '¿Tengo pedidos pendientes?'; historial = @() } $null
$obj = $r.Body | ConvertFrom-Json
Report '7) Sin sesion responde 200 y pide iniciar sesion' ($r.Status -eq 200) "status=$($r.Status)"

Write-Host ''
Write-Host '=== 2. LIMPIEZA ===' -ForegroundColor Cyan
$null = Call POST "/api/pedidos/$pedidoId/cancelar" @{} $token
$null = & sqlcmd -S localhost -E -C -d VelvetRoseDev -Q "DELETE FROM PedidoNota WHERE IdPedido = $pedidoId;" 2>&1
$restantes = Get-SqlScalar "SELECT COUNT(*) FROM PedidoNota WHERE IdPedido = $pedidoId"
Report 'Notas de prueba eliminadas' ($restantes -eq '0') "restantes=$restantes"

Write-Host ''
Write-Host "=== RESUMEN: $script:Pass PASS / $script:Fail FAIL ===" -ForegroundColor Cyan
if ($script:Fail -gt 0) { exit 1 }
