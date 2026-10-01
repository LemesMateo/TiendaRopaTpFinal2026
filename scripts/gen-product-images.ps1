# ============================================================================
# Genera los placeholders PNG de productos para DESARROLLO.
# Activos locales controlados (sin fotos de terceros): cada imagen lleva el
# número de producto sobre un fondo de la paleta Velvet Rose.
# Salidas (los mismos PNG en ambos hosts):
#   - TiendaRopa.Api/wwwroot/img/productos/  → Blazor Web (URL absoluta a la API)
#   - TiendaRopa/wwwroot/img/productos/      → MAUI (origin local del WebView;
#     en Android el WebView carga desde https://0.0.0.1 y bloquearía las
#     imágenes HTTP de la API como "mixed content")
# Uso:   powershell -ExecutionPolicy Bypass -File scripts\gen-product-images.ps1
# ============================================================================
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$destinos = @(
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\TiendaRopa.Api\wwwroot\img\productos')),
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\TiendaRopa\wwwroot\img\productos'))
)
foreach ($salida in $destinos) {
    [IO.Directory]::CreateDirectory($salida) | Out-Null
}

# Paleta (misma que velvet-tokens.css)
$fondos = @('#F9E4E8', '#E7DDF3', '#FDEFF2', '#E2EFE0', '#FBEBDD', '#FDF3F4')
$tintos = @('#C51E3A', '#713A7D', '#F3A6B6', '#49702E', '#9A5B18', '#41284C')
$texto = '#2B1D2F'

$ancho = 800
$alto = 1000

foreach ($n in 1..9) {
    for ($i = 1; $i -le 3; $i++) {
        $bmp = New-Object System.Drawing.Bitmap($ancho, $alto)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'
        $g.TextRenderingHint = 'AntiAlias'

        $fondo = [System.Drawing.ColorTranslator]::FromHtml($fondos[($n + $i) % $fondos.Count])
        $tinte = [System.Drawing.ColorTranslator]::FromHtml($tintos[($n * $i) % $tintos.Count])

        $g.Clear($fondo)

        # Banda diagonal de acento
        $brocha = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(70, $tinte.R, $tinte.G, $tinte.B))
        $pts = @(
            (New-Object System.Drawing.PointF(0, 640)),
            (New-Object System.Drawing.PointF($ancho, 380)),
            (New-Object System.Drawing.PointF($ancho, 720)),
            (New-Object System.Drawing.PointF(0, 980))
        )
        $g.FillPolygon($brocha, $pts)

        # Número de producto grande
        $fuenteGrande = New-Object System.Drawing.Font('Georgia', 280, [System.Drawing.FontStyle]::Bold)
        $brochaTexto = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($texto))
        $formato = New-Object System.Drawing.StringFormat
        $formato.Alignment = 'Center'
        $formato.LineAlignment = 'Center'
        $rect = New-Object System.Drawing.RectangleF(0, 180, $ancho, 460)
        $g.DrawString("$n", $fuenteGrande, $brochaTexto, $rect, $formato)

        # Etiqueta "Vista i/3"
        $fuentePeque = New-Object System.Drawing.Font('Segoe UI', 44, [System.Drawing.FontStyle]::Regular)
        $rect2 = New-Object System.Drawing.RectangleF(0, 640, $ancho, 160)
        $g.DrawString("Vista $i/3", $fuentePeque, $brochaTexto, $rect2, $formato)

        # Borde de marca
        $lapiz = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#EDE5DB'), 8)
        $g.DrawRectangle($lapiz, 4, 4, ($ancho - 8), ($alto - 8))

        foreach ($destino in $destinos) {
            $ruta = Join-Path $destino ("p{0}-{1}.png" -f $n, $i)
            $bmp.Save($ruta, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        $g.Dispose()
        $bmp.Dispose()
        Write-Host ("OK p{0}-{1}.png en {2} destinos" -f $n, $i, $destinos.Count)
    }
}

Write-Host ("Listo: 27 imágenes x {0} destinos" -f $destinos.Count)
