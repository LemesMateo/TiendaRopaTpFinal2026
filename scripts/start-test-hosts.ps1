# ============================================================================
# Levanta la API (:5100) y la Web (:5101) para las pruebas del asistente.
# El doble de Gemini (scripts\gemini-stub.mjs) corre en :5199 para que la
# suite sea determinista y no consuma cuota de Gemini.
#
# Uso:  powershell -ExecutionPolicy Bypass -File scripts\start-test-hosts.ps1
# ============================================================================
$ErrorActionPreference = 'Continue'
$Raiz = Split-Path -Parent $PSScriptRoot
Set-Location $Raiz

# Libera los puertos si quedó algo de una corrida anterior.
foreach ($proc in @('TiendaRopa.Api', 'TiendaRopa.Web')) {
    Get-Process $proc -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

Write-Host '[1/3] Doble de Gemini en :5199...' -ForegroundColor Cyan
$stub = Start-Process -FilePath 'node' -ArgumentList 'scripts/gemini-stub.mjs', '5199' `
    -WorkingDirectory $Raiz -PassThru -WindowStyle Hidden

Start-Sleep -Seconds 2

Write-Host '[2/3] API en :5100...' -ForegroundColor Cyan
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Asistente__Gemini__BaseUrl = 'http://127.0.0.1:5199'
# Clave ficticia SOLO por variable de entorno: el doble de Gemini de :5199 no
# la valida y nunca sale de esta máquina. No se toca el almacén de secretos.
$env:Asistente__Gemini__ApiKey = 'clave-ficticia-solo-para-pruebas-locales'
$api = Start-Process -FilePath 'dotnet' -ArgumentList 'run', '--project', 'TiendaRopa.Api/TiendaRopa.Api.csproj', '--launch-profile', 'http' `
    -WorkingDirectory $Raiz -PassThru -WindowStyle Hidden -RedirectStandardOutput "$Raiz/api.log" -RedirectStandardError "$Raiz/api.err.log"

Write-Host '[3/3] Web en :5101...' -ForegroundColor Cyan
$web = Start-Process -FilePath 'dotnet' -ArgumentList 'run', '--project', 'TiendaRopa.Web/TiendaRopa.Web.csproj' `
    -WorkingDirectory $Raiz -PassThru -WindowStyle Hidden -RedirectStandardOutput "$Raiz/web.log" -RedirectStandardError "$Raiz/web.err.log"

# Espera a que ambos respondan.
$ok = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 1
    try {
        $a = Invoke-WebRequest -Uri 'http://localhost:5100/api/productos?page=1&pageSize=1' -UseBasicParsing -TimeoutSec 3
        $w = Invoke-WebRequest -Uri 'http://localhost:5101/' -UseBasicParsing -TimeoutSec 3
        if ($a.StatusCode -eq 200 -and $w.StatusCode -eq 200) { $ok = $true; break }
    } catch { }
}

if ($ok) {
    Write-Host 'API y Web levantadas. PIDs: stub=' $stub.Id 'api=' $api.Id 'web=' $web.Id -ForegroundColor Green
} else {
    Write-Host 'No se pudieron levantar los servicios (revisar api.log y web.log).' -ForegroundColor Red
    exit 1
}
