# A partir de assets/splash.jpg genera:
#   - la pantalla de inicio de la app       src/VinylRipper.Windows/Assets/splash.jpg
#   - la imagen lateral del asistente       installer/VinylRipper.Windows.Installer/images/wizard-*.png
#     (bienvenida y final del instalador/desinstalador), con el fondo desenfocado para rellenar
#   - la imagen pequeña de la esquina       installer/VinylRipper.Windows.Installer/images/wizard-small-*.png
#
# Varios tamaños con las mismas proporciones: Inno Setup elige el que mejor encaja con el escalado
# de pantalla y lo estira al hueco.
#
#   pwsh assets/make-splash.ps1
#
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$sourcePath = Join-Path $PSScriptRoot 'splash.jpg'
$appSplashPath = Join-Path $root 'src\VinylRipper.Windows\Assets\splash.jpg'
$wizardDir = Join-Path $root 'installer\VinylRipper.Windows.Installer\images'
New-Item -ItemType Directory -Force (Split-Path $appSplashPath), $wizardDir | Out-Null

# La pantalla de inicio de WPF se muestra a su tamaño en píxeles: 1024 px ocuparía casi toda
# una pantalla de 1080p.
$AppSplashSize = 560
# Imagen lateral: 164x314 al 100 %; múltiplos para 150 %, 200 % y 250 %.
$WizardSizes = @(@(164, 314), @(246, 471), @(328, 628), @(410, 785))
# Imagen pequeña: 55x55 al 100 %.
$SmallSizes = @(55, 83, 110, 138)

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.SmoothingMode = 'HighQuality'
    $g.PixelOffsetMode = 'HighQuality'
    $g.CompositingQuality = 'HighQuality'
    return @($bmp, $g)
}

# Dibuja $img ocupando todo el rectángulo (recortando lo que sobre), centrado.
function Draw-Cover($g, $img, [int]$x, [int]$y, [int]$w, [int]$h) {
    $scale = [Math]::Max($w / $img.Width, $h / $img.Height)
    $sw = $w / $scale; $sh = $h / $scale
    $src = New-Object System.Drawing.RectangleF (($img.Width - $sw) / 2), (($img.Height - $sh) / 2), $sw, $sh
    $dst = New-Object System.Drawing.RectangleF $x, $y, $w, $h
    $attrs = New-Object System.Drawing.Imaging.ImageAttributes
    $attrs.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY) # sin bordes claros al escalar
    $g.DrawImage($img, [System.Drawing.Rectangle]::Round($dst), $src.X, $src.Y, $src.Width, $src.Height, [System.Drawing.GraphicsUnit]::Pixel, $attrs)
    $attrs.Dispose()
}

function Save-Jpeg($bmp, [string]$path, [long]$quality) {
    $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
    $params = New-Object System.Drawing.Imaging.EncoderParameters 1
    $params.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), $quality
    $bmp.Save($path, $codec, $params)
    $params.Dispose()
}

$source = [System.Drawing.Image]::FromFile($sourcePath)
try {
    # --- Pantalla de inicio de la app -----------------------------------------------------------
    $bmp, $g = New-Canvas $AppSplashSize $AppSplashSize
    Draw-Cover $g $source 0 0 $AppSplashSize $AppSplashSize
    $g.Dispose()
    Save-Jpeg $bmp $appSplashPath 92
    $bmp.Dispose()
    Write-Host "✔ $appSplashPath"

    # --- Imagen lateral del asistente -----------------------------------------------------------
    foreach ($size in $WizardSizes) {
        $w, $h = $size

        # Fondo: la misma imagen, muy desenfocada (reducir a 1/16 y volver a ampliar) y oscurecida.
        $tinyW = [Math]::Max(4, [int]($w / 16)); $tinyH = [Math]::Max(4, [int]($h / 16))
        $tiny, $tg = New-Canvas $tinyW $tinyH
        Draw-Cover $tg $source 0 0 $tinyW $tinyH
        $tg.Dispose()

        $bmp, $g = New-Canvas $w $h
        Draw-Cover $g $tiny 0 0 $w $h
        $tiny.Dispose()
        $shade = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(120, 8, 10, 14))
        $g.FillRectangle($shade, 0, 0, $w, $h)
        $shade.Dispose()

        # Delante: la imagen entera, a todo el ancho y centrada en vertical.
        Draw-Cover $g $source 0 ([int](($h - $w) / 2)) $w $w
        $g.Dispose()

        $path = Join-Path $wizardDir "wizard-$w.png"
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Write-Host "✔ $path"
    }

    # --- Imagen pequeña de la esquina ------------------------------------------------------------
    foreach ($s in $SmallSizes) {
        $bmp, $g = New-Canvas $s $s
        Draw-Cover $g $source 0 0 $s $s
        $g.Dispose()
        $path = Join-Path $wizardDir "wizard-small-$s.png"
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Write-Host "✔ $path"
    }
}
finally {
    $source.Dispose()
}
