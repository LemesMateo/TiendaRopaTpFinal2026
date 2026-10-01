# ============================================================================
# Helpers de UI para pruebas Android (uiautomator dump + tap por texto).
# Uso:   . .\scripts\android-ui.ps1
#        Tap-Texto 'Catálogo'
#        Get-NodoTexto 'Carrito'
# Gotcha PS5.1: siempre envolver adb en cmd /c para binarios y stderr.
# ============================================================================
$script:Adb = "C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe"

function Get-Ui {
    & $script:Adb shell uiautomator dump /sdcard/ui.xml 2>&1 | Out-Null
    # adb pull + lectura UTF-8: 'adb shell cat' decodifica como ANSI y rompe
    # los acentos ("Catálogo" → "Cat├ílogo"), invalidando las búsquedas.
    $tmp = Join-Path $env:TEMP 'velvet-ui.xml'
    cmd /c "`"$script:Adb`" pull /sdcard/ui.xml `"$tmp`" >nul 2>&1"
    if (-not (Test-Path $tmp)) { return '' }
    return [IO.File]::ReadAllText($tmp, [Text.UTF8Encoding]::new($false))
}

# Parsea el XML de uiautomator y devuelve el centro del PRIMER node cuyo
# text o content-desc contiene el texto indicado (mucho más fiable que regex
# sobre la línea plana: los bounds pueden pertenecer a nodos hijos).
function Get-Nodo {
    param([string]$Texto)
    $xml = Get-Ui
    if ([string]::IsNullOrWhiteSpace($xml)) { return $null }
    try {
        [xml]$doc = $xml
    } catch {
        return $null
    }

    $nodos = $doc.SelectNodes('//*')
    foreach ($n in $nodos) {
        $t = $n.GetAttribute('text')
        $d = $n.GetAttribute('content-desc')
        if (($t -and $t.Contains($Texto)) -or ($d -and $d.Contains($Texto))) {
            $b = $n.GetAttribute('bounds')
            $m = [regex]::Match($b, '\[(\d+),(\d+)\]\[(\d+),(\d+)\]')
            if ($m.Success) {
                $x1 = [int]$m.Groups[1].Value; $y1 = [int]$m.Groups[2].Value
                $x2 = [int]$m.Groups[3].Value; $y2 = [int]$m.Groups[4].Value
                return [pscustomobject]@{
                    X = [int](($x1 + $x2) / 2)
                    Y = [int](($y1 + $y2) / 2)
                    W = ($x2 - $x1)
                    H = ($y2 - $y1)
                    Texto = $t
                }
            }
        }
    }
    return $null
}

function Tap-Texto {
    param([string]$Texto, [int]$Reintentos = 3)
    for ($i = 0; $i -lt $Reintentos; $i++) {
        $n = Get-Nodo $Texto
        if ($n -and $n.W -gt 0) {
            & $script:Adb shell input tap $n.X $n.Y 2>&1 | Out-Null
            Start-Sleep -Milliseconds 900
            return $true
        }
        Start-Sleep -Milliseconds 800
    }
    Write-Host "  ! no encontré '$Texto'" -ForegroundColor Yellow
    return $false
}

function Tap-XY {
    param([int]$X, [int]$Y)
    & $script:Adb shell input tap $X $Y 2>&1 | Out-Null
    Start-Sleep -Milliseconds 900
}

function Invoke-Scroll {
    # Swipe lento dentro del área de contenido (evita la bottom nav en Y>2700).
    param([int]$Desde = 2400, [int]$Hasta = 1100)
    & $script:Adb shell input swipe 640 $Desde 640 $Hasta 800 2>&1 | Out-Null
    Start-Sleep -Milliseconds 900
}

function Get-Shot {
    param([string]$Nombre)
    $ruta = "test-evidence\android\$Nombre.png"
    cmd /c "`"$script:Adb`" exec-out screencap -p > $ruta"
    return $ruta
}

function Test-Wake {
    $w = & $script:Adb shell "dumpsys power | grep mWakefulness" 2>&1
    if ("$w" -match 'Asleep') {
        & $script:Adb shell input keyevent KEYCODE_WAKEUP 2>&1 | Out-Null
        & $script:Adb shell input keyevent 82 2>&1 | Out-Null
        Start-Sleep -Seconds 1
    }
}

function Buscar-Texto {
    param([string]$Texto)
    $xml = Get-Ui
    return $xml.Contains($Texto)
}

function Bring-Front {
    # La app puede ir a segundo plano sin morir; monkey la trae al frente
    # conservando el estado de Blazor (el proceso sigue vivo).
    & $script:Adb shell monkey -p com.companyname.tiendaropa -c android.intent.category.LAUNCHER 1 2>&1 | Out-Null
    Start-Sleep -Milliseconds 1500
}

function Close-Keyboard {
    # Cierra el teclado s�lo si est� abierto y re-verifica que la app siga
    # en primer plano (si el BACK la sac�, la vuelve a traer).
    $s = & $script:Adb shell "dumpsys input_method | grep mInputShown" 2>&1
    if ("$s" -match 'mInputShown=true') {
        & $script:Adb shell input keyevent 4 2>&1 | Out-Null
        Start-Sleep -Milliseconds 900
    }
    $top = & $script:Adb shell "dumpsys activity activities | grep topResumedActivity" 2>&1
    if ("$top" -notmatch 'tiendaropa') {
        Bring-Front
    }
}
