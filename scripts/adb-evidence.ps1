<#
    scripts\adb-evidence.ps1
    -------------------------------------------------------------------
    Recolecta evidencias de prueba del emulador Android en test-evidence/
    (carpeta ignorada por Git).

    Captura:
      - PNG de pantalla (via `adb exec-out screencap -p`, binario seguro)
      - dump de logcat (completo + solo errores)
      - info de dispositivo, actividad en primer plano y arbol UI

    Uso:
        powershell -ExecutionPolicy Bypass -File scripts\adb-evidence.ps1
        powershell -ExecutionPolicy Bypass -File scripts\adb-evidence.ps1 -Tag catalogo
        powershell -ExecutionPolicy Bypass -File scripts\adb-evidence.ps1 -Tag filtros -ClearLog

    Nota de encoding: los comentarios de este archivo son ASCII a proposito,
    porque PowerShell 5.1 lee los .ps1 sin BOM como ANSI y los acentos se
    rompen. Los textos en espanol van por README/docs, no por aqui.
#>
[CmdletBinding()]
param(
    # Etiqueta que se agrega al nombre de los ficheros.
    [string]$Tag = "captura",

    # Carpeta de salida.
    [string]$OutDir = "",

    # Limpiar el logcat antes de capturar (para aislar el flujo actual).
    [switch]$ClearLog,

    # Tambien volcar el arbol UI con uiautomator (lento, ~5-10 s).
    [switch]$DumpUi
)

# NOTA: "Continue" a proposito. En PowerShell 5.1 el stderr de adb/grep
# genera un ErrorRecord que con "Stop" aborta la recoleccion a mitad.
$ErrorActionPreference = "Continue"

# Ejecuta un comando nativo fusionando stderr en stdout.
function Invoke-Native([string]$CommandLine) {
    return (cmd /c "$CommandLine 2>&1" | Out-String)
}

# --- Localizar herramientas --------------------------------------------
$sdk = $env:ANDROID_HOME
if (-not $sdk) { $sdk = $env:ANDROID_SDK_ROOT }
if (-not $sdk) { $sdk = "C:\Program Files (x86)\Android\android-sdk" }
$adb = Join-Path $sdk "platform-tools\adb.exe"
if (-not (Test-Path $adb)) { Write-Error "No se encontro adb en: $adb"; exit 1 }

$root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $root "test-evidence" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# --- El emulador esta conectado? ---------------------------------------
$devices = @(& $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match "\bdevice$" })
if (-not $devices) {
    Write-Error "No hay ningun dispositivo 'device' conectado.`nAbre el emulador primero (ver emuladorAndroid.md seccion 2)."
    exit 1
}
$serial = ($devices[0] -split '\s+')[0]
Write-Host "Dispositivo: $serial" -ForegroundColor Green

# --- La pantalla esta encendida? ---------------------------------------
# Si el emulador duerme, `screencap` devuelve un PNG NEGRO (pocos KB) sin
# fallar. Se despierta antes de capturar.
$wake = Invoke-Native "`"$adb`" shell dumpsys power"
if ($wake -match "mWakefulness=Asleep") {
    Write-Host "Pantalla apagada -> despertando..." -ForegroundColor Yellow
    Invoke-Native "`"$adb`" shell input keyevent KEYCODE_WAKEUP" | Out-Null
    Start-Sleep -Milliseconds 800
    Invoke-Native "`"$adb`" shell input keyevent 82" | Out-Null   # desbloquear
    Start-Sleep -Seconds 1
}

# Timestamp corto y ordenable: AAAAMMDD-HHMMSS
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$base  = Join-Path $OutDir ("{0}-{1}" -f $stamp, ($Tag -replace '[^\w\-]', '_'))
$report = New-Object System.Collections.Generic.List[string]

function Add-Report($line) { $script:report.Add($line); Write-Host $line }

Add-Report "=== Evidencia Android: $stamp  (tag: $Tag) ==="

# --- 1. Info de dispositivo --------------------------------------------
$devInfo = @(
    "model      : " + (Invoke-Native "`"$adb`" shell getprop ro.product.model").Trim()
    "android    : " + (Invoke-Native "`"$adb`" shell getprop ro.build.version.release").Trim()
    "sdk        : " + (Invoke-Native "`"$adb`" shell getprop ro.build.version.sdk").Trim()
    "size       : " + (Invoke-Native "`"$adb`" shell wm size").Trim()
    "density    : " + (Invoke-Native "`"$adb`" shell wm density").Trim()
    "orientation: " + ((Invoke-Native "`"$adb`" shell dumpsys input") -split "`n" | Where-Object { $_ -match "SurfaceOrientation" } | Select-Object -First 1)
)
[System.IO.File]::WriteAllLines("$base-device.txt", $devInfo, (New-Object System.Text.UTF8Encoding($false)))
Add-Report "  device info  -> $base-device.txt"

# --- 2. Actividad en primer plano ---------------------------------------
$focus = Invoke-Native "`"$adb`" shell dumpsys activity activities"
$focus = @(($focus -split "`n") | Where-Object { $_ -match "ResumedActivity|topResumedActivity" })
[System.IO.File]::WriteAllLines("$base-focus.txt", @($focus), (New-Object System.Text.UTF8Encoding($false)))
Add-Report "  focus        -> $base-focus.txt"

# --- 3. Captura de pantalla (binario seguro) ----------------------------
# IMPORTANTE: en PowerShell 5.1 el operador '>' decodifica stdout y rompe el
# PNG. Se redirige a nivel de cmd.exe, que lo copia en binario.
$png = "$base.png"
cmd /c "`"$adb`" exec-out screencap -p > `"$png`""
$pngLen = if (Test-Path $png) { (Get-Item $png).Length } else { 0 }
if ($pngLen -gt 100000) {
    Add-Report ("  screenshot   -> {0}  ({1:N0} bytes)" -f (Split-Path $png -Leaf), $pngLen)
} elseif ($pngLen -gt 10000) {
    # Una captura real de esta pantalla pesa ~400-600 KB. Por debajo de
    # 100 KB algo raro hay (pantalla dormida, app cerrada, fondo liso).
    Add-Report ("  screenshot   -> {0}  ({1:N0} bytes)  <-- POCO, revisar" -f (Split-Path $png -Leaf), $pngLen)
} else {
    Add-Report "  screenshot   -> FALLO (fichero vacio o corrupto)"
}

# --- 4. Logcat -----------------------------------------------------------
if ($ClearLog) { & $adb logcat -c 2>$null; Add-Report "  logcat limpio antes de capturar" }

$all = (Invoke-Native "`"$adb`" logcat -d -v time") -split "`n"
[System.IO.File]::WriteAllLines("$base-logcat-full.txt", @($all), (New-Object System.Text.UTF8Encoding($false)))
Add-Report ("  logcat full  -> {0}  ({1} lineas)" -f (Split-Path "$base-logcat-full.txt" -Leaf), @($all).Count)

# Senales de fallo tipicas en apps .NET / MAUI / WebView
$errPattern = 'FATAL|AndroidRuntime|mono-|MonoDroid|Unhandled|Exception|ANR in|OpenGL|clr\.|tiendaropa'
$errors = @($all) | Select-String -Pattern $errPattern
[System.IO.File]::WriteAllLines("$base-logcat-errors.txt", @($errors | ForEach-Object { $_.Line }), (New-Object System.Text.UTF8Encoding($false)))
Add-Report ("  logcat errors-> {0}  ({1} coincidencias)" -f (Split-Path "$base-logcat-errors.txt" -Leaf), @($errors).Count)

# --- 5. Arbol UI opcional ------------------------------------------------
if ($DumpUi) {
    $xml = "$base-uiautomator.xml"
    cmd /c "`"$adb`" shell uiautomator dump /sdcard/ui.xml > nul 2>&1"
    cmd /c "`"$adb`" pull /sdcard/ui.xml `"$xml`" > nul 2>&1"
    if (Test-Path $xml) { Add-Report ("  uiautomator  -> {0}  ({1:N0} bytes)" -f (Split-Path $xml -Leaf), (Get-Item $xml).Length) }
    else { Add-Report "  uiautomator  -> no disponible en este dispositivo" }
}

# --- 6. Resumen -----------------------------------------------------------
Add-Report ""
Add-Report "Archivos en: $OutDir"
Get-ChildItem $OutDir -File | Sort-Object Name | ForEach-Object {
    Add-Report ("  {0,-52} {1,10:N0} B" -f $_.Name, $_.Length)
}
Add-Report ""
Add-Report "Siguiente paso: abrir el PNG con view_image y compararlo con docs/mockups/."
