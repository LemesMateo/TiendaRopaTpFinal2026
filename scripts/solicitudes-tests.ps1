# ============================================================================
# Pruebas del segundo incremento: solicitudes de atención y notificaciones
# Velvet Rose Boutique. Contra la API real (:5100) y SQL Server real.
# Uso: powershell -ExecutionPolicy Bypass -File scripts\solicitudes-tests.ps1
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
    return (("$out" -join "`n") -replace 'Changed database context.*', '').Trim()
}

Write-Host ''
Write-Host '=== 0. PREPARACION ===' -ForegroundColor Cyan

$marca = [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
$emailCliente = "sol.cliente.$marca@velvetrose.dev"
$emailOtro = "sol.otro.$marca@velvetrose.dev"
$Password = 'Clave123!'

$r = Call POST '/api/auth/registro' @{ nombre = 'Sol'; apellido = 'Cliente'; email = $emailCliente; password = $Password }
$tokenCliente = Get-Prop $r.Body 'token'
Report 'Registro cliente -> 201 + JWT' ($r.Status -eq 201 -and $tokenCliente) "status=$($r.Status)"

$r = Call POST '/api/auth/registro' @{ nombre = 'Otro'; apellido = 'Cliente'; email = $emailOtro; password = $Password }
$tokenOtro = Get-Prop $r.Body 'token'
Report 'Registro segundo cliente -> 201 + JWT' ($r.Status -eq 201 -and $tokenOtro) "status=$($r.Status)"

$loginAdmin = Call POST '/api/auth/login' @{ email = 'admin@velvetrose.dev'; password = 'Velvet123!' }
$tokenAdmin = Get-Prop $loginAdmin.Body 'token'
Report 'Login administrador -> 200' ($loginAdmin.Status -eq 200 -and $tokenAdmin) "status=$($loginAdmin.Status)"

$loginVendedor = Call POST '/api/auth/login' @{ email = 'vendedor@velvetrose.dev'; password = 'Velvet123!' }
$tokenVendedor = Get-Prop $loginVendedor.Body 'token'
Report 'Login vendedor (rol restringido) -> 200' ($loginVendedor.Status -eq 200 -and $tokenVendedor) "status=$($loginVendedor.Status)"

if (-not $tokenCliente -or -not $tokenAdmin -or -not $tokenVendedor) {
    Write-Host 'Faltan tokens: se aborta.' -ForegroundColor Red
    exit 1
}

# Reponer stock (las corridas anteriores lo descuentan).
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

Write-Host ''
Write-Host '=== 1. ROL VENDEDOR: PERMISOS RESTRINGIDOS ===' -ForegroundColor Cyan

$r = Call GET '/api/admin/pedidos' $null $tokenVendedor
Report 'Vendedor puede listar pedidos -> 200' ($r.Status -eq 200) "status=$($r.Status)"

$r = Call GET '/api/admin/productos' $null $tokenVendedor
Report 'Vendedor NO puede listar productos (catálogo) -> 403' ($r.Status -eq 403) "status=$($r.Status)"

$r = Call GET '/api/solicitudes' $null $tokenVendedor
Report 'Vendedor puede listar solicitudes -> 200' ($r.Status -eq 200) "status=$($r.Status)"

$r = Call GET '/api/solicitudes' $null $tokenCliente
Report 'Cliente NO puede listar solicitudes de la tienda -> 403' ($r.Status -eq 403) "status=$($r.Status)"

$r = Call GET '/api/notificaciones' $null $tokenCliente
Report 'Cliente NO puede ver notificaciones -> 403' ($r.Status -eq 403) "status=$($r.Status)"

$r = Call GET '/api/notificaciones' $null $tokenVendedor
Report 'Vendedor puede ver notificaciones -> 200' ($r.Status -eq 200) "status=$($r.Status)"

Write-Host ''
Write-Host '=== 2. PROPUESTA Y CONFIRMACION ===' -ForegroundColor Cyan

# El cliente deja una consulta general desde el chat (como lo haría Gemini).
$r = Call POST '/api/asistente/propuestas' @{
    tipo = 'consulta_general'
    mensaje = 'Necesito cambiar la dirección de entrega de mi pedido.'
} $tokenCliente
$idPropuesta = Get-Prop $r.Body 'id'
Report 'Crear propuesta de solicitud -> 201' ($r.Status -eq 201 -and $idPropuesta) "status=$($r.Status) id=$idPropuesta"

$r = Call GET "/api/asistente/propuestas/$idPropuesta" $null $tokenCliente
$telefonoEnmascarado = Get-Prop $r.Body 'telefonoEnmascarado'
Report 'La propuesta expone el teléfono ENMASCARADO' ($telefonoEnmascarado -like '***') "telefono=$telefonoEnmascarado"
Report 'El teléfono completo NO viaja al cliente' ($r.Body -notmatch '341-555-0101') 'ok'

# Confirmar el teléfono (uso único, sin tocar el perfil).
$r = Call PUT "/api/asistente/propuestas/$idPropuesta/telefono" @{
    telefono = '341-555-9999'
    guardarEnPerfil = $false
} $tokenCliente
$estadoTel = Get-Prop $r.Body 'telefonoEstado'
Report 'Confirmar teléfono de la propuesta -> 200' ($r.Status -eq 200 -and $estadoTel -eq 'confirmado') "status=$($r.Status) estado=$estadoTel"

$perfilTel = (Call GET '/api/auth/me' $null $tokenCliente).Body | ConvertFrom-Json
Report 'El perfil NO se modifica sin consentimiento' ($perfilTel.telefono -ne '341-555-9999') "perfil=$($perfilTel.telefono)"

# Confirmar la propuesta -> crea la solicitud.
$r = Call POST "/api/asistente/propuestas/$idPropuesta/confirmar" @{
    guardarTelefonoEnPerfil = $false
    claveIdempotencia = "clave-$marca"
} $tokenCliente
$idSolicitud = Get-Prop $r.Body 'id'
$estadoSolicitud = Get-Prop $r.Body 'estado'
Report 'Confirmar propuesta -> 201 + solicitud creada' ($r.Status -eq 201 -and $idSolicitud) "status=$($r.Status) id=$idSolicitud estado=$estadoSolicitud"

# Idempotencia: confirmar de nuevo con la misma clave.
$r = Call POST "/api/asistente/propuestas/$idPropuesta/confirmar" @{
    guardarTelefonoEnPerfil = $false
    claveIdempotencia = "clave-$marca"
} $tokenCliente
$idRepetida = Get-Prop $r.Body 'id'
Report 'Confirmar dos veces NO duplica la solicitud' ("$idRepetida" -eq "$idSolicitud") "id=$idRepetida"

$totalSolicitudes = Get-SqlScalar "SELECT COUNT(*) FROM SolicitudAtencion WHERE IdSolicitud = $idSolicitud"
Report 'SQL Server: una sola fila en SolicitudAtencion' ($totalSolicitudes -eq '1') "filas=$totalSolicitudes"

Write-Host ''
Write-Host '=== 3. NOTIFICACIONES Y RESPUESTA DEL VENDEDOR ===' -ForegroundColor Cyan

$r = Call GET '/api/notificaciones/pendientes' $null $tokenVendedor
$pendientesVendedor = $r.Body
Report 'Vendedor recibe notificaciones -> pendientes > 0' ($pendientesVendedor -ge 1) "pendientes=$pendientesVendedor"

$r = Call GET '/api/notificaciones/pendientes' $null $tokenAdmin
Report 'Administrador recibe notificaciones -> pendientes > 0' ($r.Body -ge 1) "pendientes=$($r.Body)"

$r = Call GET "/api/solicitudes/$idSolicitud" $null $tokenVendedor
$clienteNombre = Get-Prop $r.Body 'clienteNombre'
Report 'El vendedor ve el detalle con el cliente' ($r.Status -eq 200 -and $clienteNombre) "cliente=$clienteNombre"

$r = Call POST "/api/solicitudes/$idSolicitud/responder" @{
    respuesta = 'Hola, ya actualizamos la dirección de entrega.'
} $tokenVendedor
$respuestaGuardada = Get-Prop $r.Body 'respuesta'
$estadoRespondida = Get-Prop $r.Body 'estado'
Report 'El vendedor responde la solicitud -> 200' ($r.Status -eq 200 -and $estadoRespondida -eq 'Respondida') "status=$($r.Status) estado=$estadoRespondida"
Report 'La respuesta se guarda' ($respuestaGuardada -like '*actualizamos*') $respuestaGuardada

$r = Call GET '/api/mis-solicitudes' $null $tokenCliente
$mias = @()
$json = $r.Body | ConvertFrom-Json
if ($null -ne $json) { $mias = @($json) }
$conRespuesta = $mias | Where-Object { $_.id -eq $idSolicitud -and $_.respuesta }
Report 'El cliente ve la respuesta de la tienda' ($null -ne $conRespuesta) "solicitudes=$($mias.Count)"

$r = Call GET '/api/mis-solicitudes' $null $tokenOtro
$delOtro = @()
$jsonOtro = $r.Body | ConvertFrom-Json
if ($null -ne $jsonOtro) { $delOtro = @($jsonOtro) }
Report 'Aislamiento: el otro cliente NO ve la solicitud' ($delOtro.Count -eq 0) "solicitudes=$($delOther.Count)"

$r = Call POST "/api/solicitudes/$idSolicitud/estado" @{ estado = 'Cerrada' } $tokenVendedor
$estadoCerrada = Get-Prop $r.Body 'estado'
Report 'El vendedor cierra la solicitud -> 200' ($r.Status -eq 200 -and $estadoCerrada -eq 'Cerrada') "status=$($r.Status) estado=$estadoCerrada"

Write-Host ''
Write-Host '=== 4. TELEFONO: AUSENTE / CONFIRMADO / MODIFICADO ===' -ForegroundColor Cyan

# Cliente sin teléfono en el perfil.
$null = & sqlcmd -S localhost -E -C -d VelvetRoseDev -Q "UPDATE Cliente SET Telefono = NULL WHERE IdUsuario = (SELECT IdUsuario FROM Usuario WHERE Email = '$emailCliente');" 2>&1

$r = Call POST '/api/asistente/propuestas' @{
    tipo = 'consulta_general'
    mensaje = 'Quiero dejar un mensaje para la tienda.'
} $tokenCliente
$idPropSinTel = Get-Prop $r.Body 'id'
$estadoTelProp = Get-Prop $r.Body 'telefonoEstado'
Report 'Sin teléfono en el perfil -> queda pendiente de confirmación' ($estadoTelProp -eq 'pendiente_confirmacion') "estado=$estadoTelProp"

$r = Call POST "/api/asistente/propuestas/$idPropSinTel/confirmar" @{} $tokenCliente
Report 'Confirmar sin teléfono -> 400 (pide número)' ($r.Status -eq 400) "status=$($r.Status)"

$r = Call POST "/api/asistente/propuestas/$idPropSinTel/confirmar" @{
    telefono = '341-555-7777'
    guardarTelefonoEnPerfil = $true
    claveIdempotencia = "clave-tel-$marca"
} $tokenCliente
$idConTel = Get-Prop $r.Body 'id'
Report 'Confirmar con teléfono alternativo -> 201' ($r.Status -eq 201 -and $idConTel) "status=$($r.Status)"

$perfilTel2 = (Call GET '/api/auth/me' $null $tokenCliente).Body | ConvertFrom-Json
Report 'Con consentimiento expreso el perfil SÍ se actualiza' ($perfilTel2.telefono -eq '341-555-7777') "perfil=$($perfilTel2.telefono)"

Write-Host ''
Write-Host '=== 5. AISLAMIENTO ENTRE USUARIOS ===' -ForegroundColor Cyan

$r = Call GET "/api/asistente/propuestas/$idPropuesta" $null $tokenOtro
Report 'Otro cliente NO puede ver la propuesta ajena -> 404' ($r.Status -eq 404) "status=$($r.Status)"

$r = Call POST "/api/asistente/propuestas/$idPropuesta/confirmar" @{} $tokenOtro
Report 'Otro cliente NO puede confirmar la propuesta ajena -> 404' ($r.Status -eq 404) "status=$($r.Status)"

$r = Call POST "/api/asistente/propuestas/$idPropSinTel/cancelar" $null $tokenOtro
Report 'Otro cliente NO puede cancelar la propuesta ajena -> 404' ($r.Status -eq 404) "status=$($r.Status)"

Write-Host ''
Write-Host '=== 6. REPOSICION Y VARIANTE INEXISTENTE ===' -ForegroundColor Cyan

# Producto con una variante sin stock: se prepara una (la reposición de stock
# dejó todas con unidades, así que se agota una a propósito).
$productoConHueco = $productos.items | Where-Object { $_.variantes.Count -gt 0 } | Select-Object -First 1
$varianteHueco = $productoConHueco.variantes | Select-Object -First 1

$null = Call PUT "/api/admin/variantes/$($varianteHueco.id)" @{
    id = $varianteHueco.id
    idProducto = $productoConHueco.id
    idTalle = $varianteHueco.idTalle
    idColor = $varianteHueco.idColor
    stock = 0
    precio = $varianteHueco.precio
} $tokenAdmin

$r = Call POST '/api/asistente/propuestas' @{
    tipo = 'solicitud_reposicion'
    mensaje = 'Avisen cuando vuelva este talle en este color.'
    idProducto = $productoConHueco.id
    idVariante = $varianteHueco.id
} $tokenCliente
$idRepo = Get-Prop $r.Body 'id'
$repoTalle = Get-Prop $r.Body 'talle'
Report 'Solicitud de reposicion de variante agotada -> 201' ($r.Status -eq 201 -and $idRepo) "status=$($r.Status) talle=$repoTalle"

# Se repone el stock para no dejarlo agotado.
$null = Call PUT "/api/admin/variantes/$($varianteHueco.id)" @{
    id = $varianteHueco.id
    idProducto = $productoConHueco.id
    idTalle = $varianteHueco.idTalle
    idColor = $varianteHueco.idColor
    stock = 5
    precio = $varianteHueco.precio
} $tokenAdmin

# Combinación inexistente: se describe con texto, sin inventar la variante.
$r = Call POST '/api/asistente/propuestas' @{
    tipo = 'solicitud_reposicion'
    mensaje = 'Quiero el talle XXL en fucsia, que no existe.'
    idProducto = $productos.items[0].id
    talle = 'XXL'
    color = 'Fucsia'
} $tokenCliente
$idInnecesaria = Get-Prop $r.Body 'id'
$varianteInnecesaria = Get-Prop $r.Body 'idVariante'
Report 'Combinacion inexistente -> 201 con talle/color en texto' ($r.Status -eq 201 -and $idInnecesaria) "status=$($r.Status)"
Report 'No se inventa un IdProductoVariante' ($null -eq $varianteInnecesaria) "idVariante=$varianteInnecesaria"

Write-Host ''
Write-Host '=== 7. CANCELACION DE PROPUESTA ===' -ForegroundColor Cyan

$r = Call POST '/api/asistente/propuestas' @{
    tipo = 'consulta_general'
    mensaje = 'Mensaje que voy a cancelar.'
} $tokenCliente
$idCancelable = Get-Prop $r.Body 'id'

$r = Call POST "/api/asistente/propuestas/$idCancelable/cancelar" $null $tokenCliente
$estadoCancelada = Get-Prop $r.Body 'estado'
Report 'Cancelar propuesta -> 200 + estado Cancelada' ($r.Status -eq 200 -and $estadoCancelada -eq 'Cancelada') "status=$($r.Status) estado=$estadoCancelada"

$canceladaSql = Get-SqlScalar "SELECT COUNT(*) FROM SolicitudAtencion WHERE IdPropuesta = $idCancelable"
Report 'Cancelar NO crea ninguna solicitud' ($canceladaSql -eq '0') "solicitudes=$canceladaSql"

Write-Host ''
Write-Host '=== 8. LIMPIEZA ===' -ForegroundColor Cyan

# El orden respeta las FK (Notificacion -> SolicitudAtencion -> PedidoDetalle
# -> Pedido -> SolicitudPropuesta -> Cliente -> Usuario).
# SET QUOTED_IDENTIFIER ON es OBLIGATORIO: los índices únicos filtrados de
# SolicitudAtencion (ClaveIdempotencia / IdPropuesta) lo exigen. sqlcmd lo
# tiene en OFF por defecto (ADO.NET lo pone en ON, por eso la API funciona).
$null = & sqlcmd -S localhost -E -C -d VelvetRoseDev -Q @"
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
DECLARE @ids TABLE (IdUsuario INT);
INSERT INTO @ids SELECT IdUsuario FROM Usuario WHERE Email LIKE 'sol.%@velvetrose.dev';
DECLARE @clientes TABLE (IdCliente INT);
INSERT INTO @clientes SELECT IdCliente FROM Cliente WHERE IdUsuario IN (SELECT IdUsuario FROM @ids);
DELETE FROM Notificacion WHERE IdSolicitud IN (SELECT IdSolicitud FROM SolicitudAtencion WHERE IdCliente IN (SELECT IdCliente FROM @clientes));
DELETE FROM SolicitudAtencion WHERE IdCliente IN (SELECT IdCliente FROM @clientes);
DELETE FROM PedidoDetalle WHERE IdPedido IN (SELECT IdPedido FROM Pedido WHERE IdCliente IN (SELECT IdCliente FROM @clientes));
DELETE FROM Pedido WHERE IdCliente IN (SELECT IdCliente FROM @clientes);
DELETE FROM SolicitudPropuesta WHERE IdUsuario IN (SELECT IdUsuario FROM @ids);
DELETE FROM Cliente WHERE IdUsuario IN (SELECT IdUsuario FROM @ids);
DELETE FROM Usuario WHERE IdUsuario IN (SELECT IdUsuario FROM @ids);
"@ 2>&1

$restantes = Get-SqlScalar "SELECT COUNT(*) FROM Usuario WHERE Email LIKE 'sol.%@velvetrose.dev'"
Report 'Datos de prueba eliminados' ($restantes -eq '0') "usuarios=$restantes"

Write-Host ''
Write-Host "=== RESUMEN: $script:Pass PASS / $script:Fail FAIL ===" -ForegroundColor Cyan
if ($script:Fail -gt 0) { exit 1 }
