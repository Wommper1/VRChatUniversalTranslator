# Renders assets\app-icon-placeholder.svg's design into assets\app-icon.ico (16-256 px) and assets\app-icon-256.png.
# The shapes below are copied from the SVG: dark rounded square, speech bubble with a teal outline, two caption lines.
# Run from the repository root:  powershell -NoProfile -File scripts\make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
    $k = $size / 256.0
    # Small sizes get heavier strokes so the bubble stays readable at 16-32 px.
    $outline = if ($size -le 32) { 20 } else { 10 }
    $line = if ($size -le 32) { 24 } else { 12 }

    $round = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 52 * $k * 2
    $round.AddArc(0, 0, $r, $r, 180, 90); $round.AddArc($size - $r, 0, $r, $r, 270, 90)
    $round.AddArc($size - $r, $size - $r, $r, $r, 0, 90); $round.AddArc(0, $size - $r, $r, $r, 90, 90); $round.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#0b0f17'))), $round)

    $pts = @(@(48,65),@(208,65),@(208,169),@(111,169),@(69,200),@(69,169),@(48,169)) | ForEach-Object { New-Object System.Drawing.PointF ([single]($_[0]*$k)), ([single]($_[1]*$k)) }
    $bubble = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bubble.AddPolygon([System.Drawing.PointF[]]$pts)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#151b27'))), $bubble)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml('#66d6c5')), ([single]($outline*$k))
    $pen.LineJoin = 'Round'
    $g.DrawPath($pen, $bubble)

    $textPen = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml('#f3f6fb')), ([single]($line*$k))
    $textPen.StartCap = 'Round'; $textPen.EndCap = 'Round'
    $g.DrawLine($textPen, [single](77*$k), [single](98*$k), [single](179*$k), [single](98*$k))
    $g.DrawLine($textPen, [single](77*$k), [single](126*$k), [single](152*$k), [single](126*$k))
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$png = @{}
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $png[$s] = $ms.ToArray()
    if ($s -eq 256) { [System.IO.File]::WriteAllBytes((Join-Path $assets 'app-icon-256.png'), $png[$s]) }
    $bmp.Dispose(); $ms.Dispose()
}

# ICO container with PNG-compressed images (supported since Windows Vista).
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$png[$s].Length); $w.Write([uint32]$offset)
    $offset += $png[$s].Length
}
foreach ($s in $sizes) { $w.Write($png[$s]) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'app-icon.ico'), $out.ToArray())
"Wrote assets\app-icon.ico ($($out.Length) bytes) and assets\app-icon-256.png"
