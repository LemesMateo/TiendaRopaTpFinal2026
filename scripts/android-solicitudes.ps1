# ============================================================================
# Prueba del flujo de solicitudes en MAUI Android (emulador + ADB).
# Escribe con keycodes de Android: el teclado del emulador (Gboard) manglea
# el texto si se usa "adb shell input text".
#
# Uso: powershell -ExecutionPolicy Bypass -File scripts\android-solicitudes.ps1
# ============================================================================
param(
    [string]$Email = 's3@velvetrose.dev',
    [string]$Password = 'velvetpass'
)

$ErrorActionPreference = 'Continue'
$adb = 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe'
$Repo = Split-Path -Parent $PSScriptRoot
$Evidencia = Join-Path $Repo 'test-evidence\android'
New-Item -ItemType Directory -Force -Path $Evidencia | Out-Null

# Keycodes de Android para letras y símbolos (sin dígitos: se perdían).
$map = @{
    'a'=29;'b'=30;'c'=31;'d'=32;'e'=33;'f'=34;'g'=35;'h'=36;'i'=37;'j'=38;'k'=39;'l'=40
    'm'=41;'n'=42;'o'=43;'p'=44;'q'=45;'r'=46;'s'=47;'t'=48;'u'=49;'v'=50;'w'=51;'x'=52
    'y'=53;'z'=54;'.'=56;'@'=77
}

function Type-Keycodes([string]$texto) {
    foreach ($ch in $texto.ToCharArray()) {
        $k = $map[[string]$ch]
        if ($k) {
            & $adb shell input keyevent $k | Out-Null
            Start-Sleep -Milliseconds 130
        }
    }
}

function Capturar([string]$nombre) {
    $ruta = Join-Path $Evidencia $nombre
    cmd /c "`"$adb`" exec-out screencap -p > `"$ruta`""
    Write-Host "  captura: $nombre"
}

# Espera a que la app esté en primer plano (el WebView tarda al arrancar).
function Esperar-App([int]$segundos = 45) {
    for ($i = 0; $i -lt $segundos; $i++) {
        $foco = (& $adb shell "dumpsys window | grep mCurrentFocus" 2>$null)
        if ("$foco" -match 'tiendaropa') { return $true }
        Start-Sleep -Seconds 1
    }
    return $false
}

# Requests del emulador registrados en el log de la API.
function Requests-Emulador([string]$patron) {
    Select-String -Path (Join-Path $Repo 'api.log') -Pattern $patron |
        Select-Object -Last 5 | ForEach-Object { $_.Line.Trim() }
}

# Coordenadas calibradas para el emulador 1080x2400 (API 36).
$Coords = @{
    FabChat   = @(998, 2013)
    CerrarChat= @(1006, 233)
    MiCuenta  = @(998, 2173)
    CampoEmail= @(540, 1153)
    CampoPass = @(540, 1358)
    BotonEntrar=@(540, 1643)
    Menu      = @(90, 170)
}

Write-Host "== 1. Login en la app ==" -ForegroundColor Cyan
& $adb shell am force-stop com.companyname.tiendaropa | Out-Null
& $adb shell monkey -p com.companyname.tiendaropa -c android.intent.category.LAUNCHER 1 2>&1 | Out-Null
if (-not (Esperar-App 60)) { Write-Host '  la app no arrancó' -ForegroundColor Red; exit 1 }
Start-Sleep -Seconds 4

& $adb shell input tap $Coords.MiCuenta[0] $Coords.MiCuenta[1]
Start-Sleep -Seconds 7

& $adb shell input tap $Coords.CampoEmail[0] $Coords.CampoEmail[1]
Start-Sleep -Seconds 3
Type-Keycodes $Email

& $adb shell input tap $Coords.CampoPass[0] $Coords.CampoPass[1]
Start-Sleep -Seconds 2
Type-Keycodes $Password

# Cierra el teclado (si no, tapa el botón) y envía.
& $adb shell input keyevent 4 | Out-Null
Start-Sleep -Seconds 2
& $adb shell input tap $Coords.BotonEntrar[0] $Coords.BotonEntrar[1]
Start-Sleep -Seconds 9
Capturar 'sol-06-logueado.png'

$login = Select-String -Path (Join-Path $Repo 'api.log') -Pattern '10.0.2.2:5100/api/auth/login' |
    Select-Object -Last 1 | ForEach-Object { $_.Line.Trim() }
Write-Host "  API: $login"

Write-Host "== 2. Chat con la propuesta pendiente ==" -ForegroundColor Cyan
& $adb shell input tap $Coords.FabChat[0] $Coords.FabChat[1]
Start-Sleep -Seconds 7
Capturar 'sol-07-tarjeta.png'

Write-Host '== 3. Confirmar y enviar ==' -ForegroundColor Cyan
# El botón "Confirmar y enviar" está en la tarjeta; se prueban varias alturas
# hasta que la API registre el POST de confirmación.
$antes = (Requests-Emulador '10.0.2.2:5100/api/asistente/propuestas/[0-9]+/confirmar').Count
foreach ($y in @(1164, 1130, 1190, 1090)) {
    & $adb shell input tap 322 $y | Out-Null
    Start-Sleep -Seconds 4
    $despues = (Requests-Emulador '10.0.2.2:5100/api/asistente/propuestas/[0-9]+/confirmar').Count
    if ($despues -gt $antes) { Write-Host "  POST de confirmacion en y=$y"; break }
}
Capturar 'sol-08-confirmada.png'
Requests-Emulador '10.0.2.2:5100/api/asistente/propuestas/[0-9]+/confirmar'

Write-Host "== 4. Panel de la tienda (vendedor) ==" -ForegroundColor Cyan
& $adb shell input tap $Coords.CerrarChat[0] $Coords.CerrarChat[1]
Start-Sleep -Seconds 2
& $adb shell input tap $Coords.Menu[0] $Coords.Menu[1]
Start-Sleep -Seconds 3
Capturar 'sol-09-menu.png'

Write-Host 'Listo.'
