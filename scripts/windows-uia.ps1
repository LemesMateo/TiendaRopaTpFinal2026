# Smoke test MAUI Windows vía UI Automation (System.Windows.Automation).
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-UiAWindow {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        (Get-Process -Name TiendaRopa -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1).Id)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}

function Find-UiA {
    param([string]$Name, [string]$ControlType = $null)
    $win = Get-UiAWindow
    if (-not $win) { return $null }
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    if ($ControlType) {
        $ct = [System.Windows.Automation.ControlType]::$ControlType
        $cond2 = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct)
        $cond = New-Object System.Windows.Automation.AndCondition($cond, $cond2)
    }
    return $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Invoke-UiA {
    param([string]$Name, [string]$ControlType = $null)
    $el = Find-UiA $Name $ControlType
    if (-not $el) { Write-Host "    ! no encontré '$Name'"; return $false }
    # Los enlaces de WebView2 suelen exponer Invoke o LegacyIAccessible.
    $pat = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($pat) {
        $pat.Invoke()
        Start-Sleep -Milliseconds 1500
        return $true
    }
    # Fallback: mover el ratón al centro del elemento y clic real.
    $r = $el.Current.BoundingRectangle
    if ($r.Width -gt 0) {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(
            [int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
        Start-Sleep -Milliseconds 300
        [System.Windows.Forms.Mouse]::Click([System.Windows.Forms.MouseButtons]::Left)
        Start-Sleep -Milliseconds 1500
        return $true
    }
    return $false
}

function Dump-UiA {
    param([int]$Max = 25)
    $win = Get-UiAWindow
    if (-not $win) { return @() }
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $lista = New-Object System.Collections.ArrayList
    $nodo = $walker.GetFirstChild($win)
    while ($nodo -and $lista.Count -lt $Max) {
        $n = $nodo.Current.Name
        $ct = $nodo.Current.ControlType.ProgrammaticName
        if ($n) { [void]$lista.Add("$ct : $n") }
        $siguiente = $walker.GetFirstChild($nodo)
        if (-not $siguiente) { $siguiente = $walker.GetNextSibling($nodo) }
        while (-not $siguiente -and $nodo) {
            $nodo = $walker.GetParent($nodo)
            if ($nodo -and $nodo -ne $win) { $siguiente = $walker.GetNextSibling($nodo) }
        }
        $nodo = $siguiente
    }
    return $lista
}
