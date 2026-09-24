# Genera el icono de la aplicación (un vinilo) en varios tamaños y lo empaqueta en vinyl.ico
# con entradas PNG. También deja vinyl-256.png para el README.
#
#   pwsh assets/make-icon.ps1
#
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$icoPath = Join-Path $root 'src\VinylRipper.Windows\Assets\vinyl.ico'
$pngPath = Join-Path $PSScriptRoot 'vinyl-256.png'
New-Item -ItemType Directory -Force (Split-Path $icoPath) | Out-Null

function New-VinylBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$size
    $pad = $s * 0.03
    $disc = New-Object System.Drawing.RectangleF $pad, $pad, ($s - 2 * $pad), ($s - 2 * $pad)

    # Disco: degradado radial suave para que no sea un círculo negro plano.
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($disc)
    $discBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
    $discBrush.CenterColor = [System.Drawing.Color]::FromArgb(255, 58, 58, 66)
    $discBrush.SurroundColors = @([System.Drawing.Color]::FromArgb(255, 22, 22, 26))
    $discBrush.CenterPoint = New-Object System.Drawing.PointF ($s * 0.38), ($s * 0.36)
    $g.FillEllipse($discBrush, $disc)

    # Surcos: anillos finos, alternando opacidad, sólo si el tamaño lo permite.
    if ($size -ge 24) {
        $labelR = $s * 0.19
        $outerR = ($s - 2 * $pad) / 2 - $s * 0.05
        $step = [Math]::Max(1.6, $s / 40)
        $groove = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, 140, 140, 150)), ([float][Math]::Max(0.6, $s / 256))
        $r = $labelR + $s * 0.06
        $i = 0
        while ($r -lt $outerR) {
            $groove.Color = if ($i % 3 -eq 0) { [System.Drawing.Color]::FromArgb(95, 150, 150, 160) } else { [System.Drawing.Color]::FromArgb(40, 120, 120, 130) }
            $g.DrawEllipse($groove, ($s / 2 - $r), ($s / 2 - $r), (2 * $r), (2 * $r))
            $r += $step; $i++
        }
        $groove.Dispose()

        # Reflejo: arco claro en la parte superior izquierda.
        $shineR = $outerR - $s * 0.02
        $shine = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(48, 255, 255, 255)), ([float]($s * 0.06))
        $g.DrawArc($shine, ($s / 2 - $shineR), ($s / 2 - $shineR), (2 * $shineR), (2 * $shineR), 200, 55)
        $shine.Dispose()
    }

    # Etiqueta central en el color de acento de la app.
    $labelR = $s * 0.19
    $label = New-Object System.Drawing.RectangleF ($s / 2 - $labelR), ($s / 2 - $labelR), (2 * $labelR), (2 * $labelR)
    $labelBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $label, ([System.Drawing.Color]::FromArgb(255, 242, 184, 90)), ([System.Drawing.Color]::FromArgb(255, 201, 138, 42)), 45
    $g.FillEllipse($labelBrush, $label)
    if ($size -ge 32) {
        $ring = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 27, 27, 31)), ([float][Math]::Max(1, $s / 128))
        $g.DrawEllipse($ring, $label)
        $ring.Dispose()
    }

    # Agujero.
    $holeR = [Math]::Max(1.2, $s * 0.035)
    $g.FillEllipse([System.Drawing.Brushes]::Black, ($s / 2 - $holeR), ($s / 2 - $holeR), (2 * $holeR), (2 * $holeR))

    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($size in $sizes) {
    $bmp = New-VinylBitmap $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($size -eq 256) { $bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
}

# Empaquetado ICO: cabecera (6 bytes) + directorio (16 bytes por imagen) + datos PNG.
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)   # ancho, alto (0 = 256)
    $bw.Write([byte]0); $bw.Write([byte]0)         # paleta, reservado
    $bw.Write([uint16]1); $bw.Write([uint16]32)    # planos, bits por píxel
    $bw.Write([uint32]$p.Bytes.Length); $bw.Write([uint32]$offset)
    $offset += $p.Bytes.Length
}
foreach ($p in $pngs) { $bw.Write($p.Bytes) }
$bw.Dispose(); $fs.Dispose()

"ICO: $icoPath ($((Get-Item $icoPath).Length) bytes, $($pngs.Count) tamaños)"
"PNG: $pngPath"
