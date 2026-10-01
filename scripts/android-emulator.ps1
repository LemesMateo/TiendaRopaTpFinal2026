<#
    scripts\android-emulator.ps1
    -------------------------------------------------------------------
    Enciende un AVD existente y espera a que termine de arrancar.
    No crea ni instala nada: reutiliza los AVDs y el SDK que ya hay.

    Uso:
        powershell -ExecutionPolicy Bypass -File scripts\android-emulator.ps1
        powershell -ExecutionPolicy Bypass -File scripts\android-emulator.ps1 -Avd MAUI_Emulator_API_36
        powershell -ExecutionPolicy Bypass -File scripts\android-emulator.ps1 -StartApi

    Salida esperada:
        BOOT_OK  y  PORT_OPEN   -> listo para que el agente lo use.
#>
[CmdletBinding()]
param(
    # AVD a arrancar. El por defecto es el que se uso en sesiones anteriores.
    [string]$Avd = "pixel_9_pro_-_api_36_0",

    # Arrancar tambien la API local (http://localhost:5100) en una ventana nueva.
    [switch]$StartApi,

    # Maximo de segundos a esperar el boot.
    [int]$TimeoutSec = 180,

    # Forzar relanzamiento aunque ya haya un dispositivo conectado.
    [switch]$Force
)

# NOTA: se usa "Continue" a proposito. En PowerShell 5.1 cualquier stderr
# de un comando nativo (adb, nc, emulator) genera un ErrorRecord que con
# "Stop" aborta el script entero. Los fallos reales se comprueban a mano.
$ErrorActionPreference = "Continue"

# Ejecuta un comando nativo fusionando stderr en stdout, sin que PowerShell
# lo convierta en error terminante.
function Invoke-Native([string]$CommandLine) {
    return (cmd /c "$CommandLine 2>&1" | Out-String).Trim()
}

# --- Localizar herramientas sin asumir nada del PATH -------------------
$sdk = $env:ANDROID_HOME
if (-not $sdk) { $sdk = $env:ANDROID_SDK_ROOT }
if (-not $sdk) { $sdk = "C:\Program Files (x86)\Android\android-sdk" }

$adb      = Join-Path $sdk "platform-tools\adb.exe"
$emulator = Join-Path $sdk "emulator\emulator.exe"

foreach ($p in @($adb, $emulator)) {
    if (-not (Test-Path $p)) {
        Write-Error "No se encontro: $p`nRevisa ANDROID_HOME. Ver emuladorAndroid.md seccion 1."
        exit 1
    }
}

function Write-Step($msg) { Write-Host "`n== $msg" -ForegroundColor Cyan }

# --- 1. Ya hay un emulador encendido? ----------------------------------
Write-Step "Dispositivos conectados"
$lines = & $adb devices | Select-Object -Skip 1 | Where-Object { $_.Trim() }
$ready = $lines | Where-Object { $_ -match "\bdevice$" }

if ($ready -and -not $Force) {
    Write-Host "Ya hay un dispositivo listo:" -ForegroundColor Green
    $ready | ForEach-Object { Write-Host "  $_" -ForegroundColor Green }
} else {
    # --- 2. Listar AVDs y validar que el pedido exista --------------------
    Write-Step "AVDs disponibles"
    $avds = & $emulator -list-avds | Where-Object { $_.Trim() }
    $avds | ForEach-Object { Write-Host "  $_" }

    if ($avds -notcontains $Avd) {
        Write-Error "El AVD '$Avd' no existe. Elige uno de la lista de arriba."
        exit 1
    }

    # --- 3. Lanzar el emulador (proceso desacoplado para no bloquear) ------
    Write-Step "Arrancando emulador: $Avd"
    $emuArgs = @("-avd", $Avd, "-netdelay", "none", "-netspeed", "full")
    Start-Process -FilePath $emulator -ArgumentList $emuArgs -WindowStyle Normal
    Write-Host "Ventana del emulador lanzada. Esperando boot (max ${TimeoutSec}s)..."

    # --- 4. Esperar a que adb lo vea y termine el boot --------------------
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $booted = $false
    while ((Get-Date) -lt $deadline) {
        $d = & $adb devices | Select-String -Pattern "\bdevice$"
        if ($d) {
            $bc = Invoke-Native "`"$adb`" shell getprop sys.boot_completed"
            if ($bc -eq "1") { $booted = $true; break }
        }
        Start-Sleep -Seconds 3
    }

    if (-not $booted) {
        Write-Error "Timeout: el emulador no termino de arrancar en ${TimeoutSec}s."
        exit 1
    }
    Write-Host "BOOT_OK" -ForegroundColor Green
}

# --- 5. API opcional ----------------------------------------------------
if ($StartApi) {
    Write-Step "Arrancando API local (http://localhost:5100)"
    $root = Split-Path -Parent $PSScriptRoot
    Start-Process -FilePath "dotnet" `
        -ArgumentList @("run", "--project", (Join-Path $root "TiendaRopa.Api"), "--launch-profile", "http") `
        -WorkingDirectory $root
    Write-Host "API lanzada en una ventana nueva."
}

# --- 6. Checklist final --------------------------------------------------
Write-Step "Checklist"

$bc = Invoke-Native "`"$adb`" shell getprop sys.boot_completed"
Write-Host ("  boot_completed      : {0}" -f $bc)

try {
    $r = Invoke-WebRequest "http://localhost:5100/api/categorias" -UseBasicParsing -TimeoutSec 5
    Write-Host ("  API localhost:5100  : {0}" -f $r.StatusCode) -ForegroundColor Green
} catch {
    Write-Host "  API localhost:5100  : NO RESPONDE (ejecuta con -StartApi)" -ForegroundColor Yellow
}

$port = Invoke-Native "`"$adb`" shell `"nc -z -w 5 10.0.2.2 5100 && echo PORT_OPEN || echo PORT_CLOSED`""
$port = ($port -split "`n" | Where-Object { $_ -match "PORT_(OPEN|CLOSED)" } | Select-Object -First 1)
if (-not $port) { $port = "PORT_UNKNOWN" }
Write-Host ("  10.0.2.2:5100       : {0}" -f $port) -ForegroundColor $(if ($port -eq "PORT_OPEN") { "Green" } else { "Yellow" })

$size  = Invoke-Native "`"$adb`" shell wm size"
$dens  = Invoke-Native "`"$adb`" shell wm density"
Write-Host ("  pantalla            : {0} / {1}" -f $size, $dens)

if ($bc -eq "1" -and $port -eq "PORT_OPEN") {
    Write-Host "`nListo. Decile al agente: 'el emulador esta listo'." -ForegroundColor Green
    exit 0
} else {
    Write-Host "`nAun falta algo. Revisa los puntos amarillos de arriba." -ForegroundColor Yellow
    exit 2
}
