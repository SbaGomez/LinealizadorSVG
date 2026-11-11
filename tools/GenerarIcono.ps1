# Draws the app icon (blue rounded tile with a white curve drawn as straight segments, vertices in amber) and
# writes src/LinealizadorSVG.App/Assets/app.png (256 px) and app.ico (16-256 px).
# Run from the repo root with Windows PowerShell:  powershell -File tools/GenerarIcono.ps1
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot "..\src\LinealizadorSVG.App\Assets"
$tamanos = 16, 20, 24, 32, 40, 48, 64, 128, 256

function Redondeado([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($r -le 0) { $p.AddRectangle((New-Object System.Drawing.RectangleF $x, $y, $w, $h)); return $p }
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Dibujar([int]$lado) {
    $bmp = New-Object System.Drawing.Bitmap $lado, $lado
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $lado / 256.0
    $g.ScaleTransform($k, $k)

    # Tile: diagonal blue gradient (the app's accent), with a soft top highlight.
    $fondo = Redondeado 8 8 240 240 54
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 8, 8), (New-Object System.Drawing.PointF 248, 248), ([System.Drawing.ColorTranslator]::FromHtml("#2B8CF0")), ([System.Drawing.ColorTranslator]::FromHtml("#0D47A1"))
    $g.FillPath($grad, $fondo)
    if ($lado -ge 48) {
        $brillo = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, 256), ([System.Drawing.Color]::FromArgb(46, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
        $g.FillPath($brillo, $fondo)
    }

    $chico = $lado -le 32
    # An S curve sampled into a few straight segments: what the app does to every curve.
    $n = if ($chico) { 3 } else { 6 }
    $puntos = @()
    for ($i = 0; $i -le $n; $i++) {
        # One full sine wave, sampled evenly so the segments look alike.
        $x = 52 + 152 * $i / $n
        $y = 128 - 58 * [Math]::Sin(2 * [Math]::PI * $i / $n)
        $puntos += New-Object System.Drawing.PointF ([float]$x), ([float]$y)
    }
    $grosor = if ($chico) { 28 } else { 16 }
    $lapiz = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $grosor
    $lapiz.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $lapiz.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $lapiz.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLines($lapiz, [System.Drawing.PointF[]]$puntos)

    # Vertices: amber dots with a white rim (only the two ends at small sizes).
    $ambar = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml("#FFC107"))
    $blanco = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $r = if ($chico) { 22 } else { 12 }
    $vertices = if ($chico) { @($puntos[0], $puntos[-1]) } else { $puntos }
    foreach ($p in $vertices) {
        $g.FillEllipse($blanco, $p.X - $r - 5, $p.Y - $r - 5, 2 * ($r + 5), 2 * ($r + 5))
        $g.FillEllipse($ambar, $p.X - $r, $p.Y - $r, 2 * $r, 2 * $r)
    }
    $g.Dispose()
    return $bmp
}

# Each size as an ICO image: PNG for 256 (what Windows expects there), classic 32-bit DIB for the rest, which every
# reader (Explorer, GDI, WPF) decodes.
function Dib([System.Drawing.Bitmap]$bmp) {
    $t = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([UInt32]40); $w.Write([Int32]$t); $w.Write([Int32]($t * 2)) # height counts the AND mask too
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]0)
    $w.Write([UInt32]0); $w.Write([Int32]0); $w.Write([Int32]0); $w.Write([UInt32]0); $w.Write([UInt32]0)
    for ($y = $t - 1; $y -ge 0; $y--) { # bottom-up BGRA
        for ($x = 0; $x -lt $t; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $filaMascara = [int]([Math]::Ceiling($t / 32.0) * 4) # 1 bpp rows padded to 32 bits; all zero, alpha rules
    $w.Write((New-Object byte[] ($filaMascara * $t)))
    $w.Flush()
    return , $ms.ToArray()
}

$imagenes = @()
foreach ($t in $tamanos) {
    $bmp = Dibujar $t
    if ($t -eq 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $imagenes += , $ms.ToArray()
        $bmp.Save((Join-Path $assets "app.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    else { $imagenes += , (Dib $bmp) }
    $bmp.Dispose()
}

# ICO container: header, one directory entry per size, then the PNG data.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$tamanos.Count)
$offset = 6 + 16 * $tamanos.Count
for ($i = 0; $i -lt $tamanos.Count; $i++) {
    $t = $tamanos[$i]
    $b = if ($t -ge 256) { 0 } else { $t }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$imagenes[$i].Length); $w.Write([UInt32]$offset)
    $offset += $imagenes[$i].Length
}
foreach ($p in $imagenes) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets "app.ico"), $out.ToArray())
"Icono generado en $assets"
