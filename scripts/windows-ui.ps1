# Captura e interacción de la ventana de MAUI Windows (smoke test).
# Uso: . .\scripts\windows-ui.ps1   ;  Capturar-Win 'nombre'  ;  Click-Win x y
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Dpi {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, IntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
}
"@

# Sin esto, PowerShell no es DPI-aware y las coordenadas de GetWindowRect/
# SetCursorPos se escalan en monitores con escala != 100% (clics perdidos).
[Win32Dpi]::SetProcessDPIAware() | Out-Null

$script:AppProc = $null

function Get-AppWindow {
    $script:AppProc = Get-Process -Name TiendaRopa -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    return $script:AppProc
}

function Activate-App {
    $p = Get-AppWindow
    if (-not $p) { return $null }
    [Win32Dpi]::ShowWindow($p.MainWindowHandle, 9) | Out-Null
    [Win32Dpi]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 800
    $rect = New-Object Win32Dpi+RECT
    [Win32Dpi]::GetWindowRect($p.MainWindowHandle, [ref]$rect) | Out-Null
    return $rect
}

function Click-Win {
    # Coordenadas RELATIVAS a la ventana (como aparecen en las capturas).
    param([int]$X, [int]$Y)
    $rect = Activate-App
    if (-not $rect) { return $false }
    $destinoX = $rect.Left + $X
    $destinoY = $rect.Top + $Y
    [Win32Dpi]::SetCursorPos($destinoX, $destinoY) | Out-Null
    Start-Sleep -Milliseconds 300
    # Verificación: ¿el cursor quedó donde se pidió? (DPI/monitores pueden
    # desplazar las coordenadas)
    $pt = New-Object Win32Dpi+POINT
    [Win32Dpi]::GetCursorPos([ref]$pt) | Out-Null
    Write-Host "    cursor: pedido=$destinoX,$destinoY real=$($pt.X),$($pt.Y)"
    [Win32Dpi]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)  # LEFTDOWN
    Start-Sleep -Milliseconds 80
    [Win32Dpi]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)  # LEFTUP
    Start-Sleep -Milliseconds 1400
    return $true
}

function Scroll-Win {
    # Rueda del ratón sobre el centro de la ventana. Negativo = hacia abajo.
    param([int]$Delta = -480)
    $rect = Activate-App
    if (-not $rect) { return $false }
    $x = $rect.Left + [int](($rect.Right - $rect.Left) / 2)
    $y = $rect.Top + [int](($rect.Bottom - $rect.Top) / 2)
    [Win32Dpi]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 300
    # mouse_event espera la rueda como DWORD con signo reinterpretado (p. ej. -600).
    $rueda = [BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$Delta), 0)
    [Win32Dpi]::mouse_event(0x0800, 0, 0, $rueda, [IntPtr]::Zero)  # WHEEL
    Start-Sleep -Milliseconds 900
    return $true
}

function Capturar-Win {
    param([string]$Nombre)
    $rect = Activate-App
    if (-not $rect) { return $null }
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $null }
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    New-Item -ItemType Directory -Force -Path 'test-evidence\windows' | Out-Null
    $ruta = "test-evidence\windows\$Nombre.png"
    $bmp.Save($ruta, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
    return $ruta
}
