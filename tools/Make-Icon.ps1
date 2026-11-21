# Baut src/JsonConfigEditor.Wpf/Assets/app.ico aus docs/icon.jpg.
# Schneidet die Kachel (dunkler Rahmen) aus dem Bild, macht die Ecken außerhalb
# des abgerundeten Rahmens transparent und legt alle Windows-Größen als PNG ins ICO.
#   pwsh tools/Make-Icon.ps1
param(
    [string]$Source = "$PSScriptRoot/../docs/icon.jpg",
    [string]$Target = "$PSScriptRoot/../src/JsonConfigEditor.Wpf/Assets/app.ico",
    [string]$Preview = "$PSScriptRoot/../docs/icon-256.png",
    [int]$DarkThreshold = 110,
    [double]$CornerRatio = 0.21
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Bitmap]::new((Resolve-Path $Source).Path)
try {
    # 1. Kachel finden: dunkle Rahmenpixel in der oberen Bildhälfte (Schriftzug darunter ignorieren)
    $minX = $src.Width; $minY = $src.Height; $maxX = 0; $maxY = 0
    $scanBottom = [int]($src.Height * 0.75)
    for ($y = 0; $y -lt $scanBottom; $y += 1) {
        for ($x = 0; $x -lt $src.Width; $x += 1) {
            $c = $src.GetPixel($x, $y)
            if ((0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B) -lt $DarkThreshold) {
                if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -le $minX) { throw "Keine Kachel gefunden (DarkThreshold anpassen)" }
    $w = $maxX - $minX + 1; $h = $maxY - $minY + 1
    Write-Host "Dunkle Pixel: x=$minX y=$minY ${w}x$h"
    # Die Kachel ist quadratisch. Der obere Rahmen ist durch den Farbverlauf heller und wird oft nicht
    # erkannt, links/rechts/unten sind zuverlässig dunkel: Quadrat unten und seitlich ausrichten.
    $side = [Math]::Max($w, $h)
    $top = if ($h -lt $w) { $maxY - $side + 1 } else { $minY }
    $left = if ($w -lt $h) { $minX - [int](($side - $w) / 2) } else { $minX }
    $crop = [System.Drawing.Rectangle]::new($left, $top, $side, $side)
    Write-Host "Kachel: x=$left y=$top ${side}x$side"

    # 2. Pro Größe: Kachel skaliert als Textur in ein abgerundetes Rechteck (Kanten geglättet, Rest transparent)
    function New-IconPng([int]$size) {
        $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

        $scaled = [System.Drawing.Bitmap]::new($size, $size)
        $gs = [System.Drawing.Graphics]::FromImage($scaled)
        $gs.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $gs.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $gs.DrawImage($src, [System.Drawing.Rectangle]::new(0, 0, $size, $size), $crop, [System.Drawing.GraphicsUnit]::Pixel)
        $gs.Dispose()

        $inset = [Math]::Max(0.5, $size / 256.0)          # heller Hintergrund am Rand weg
        $r = $size * $CornerRatio
        $rect = [System.Drawing.RectangleF]::new($inset, $inset, $size - 2 * $inset, $size - 2 * $inset)
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $d = 2 * $r
        $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
        $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
        $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
        $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
        $path.CloseFigure()
        $brush = [System.Drawing.TextureBrush]::new($scaled)
        $g.FillPath($brush, $path)
        $brush.Dispose(); $path.Dispose(); $scaled.Dispose(); $g.Dispose()

        $ms = [System.IO.MemoryStream]::new()
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        if ($size -eq 256 -and $Preview) { $bmp.Save([System.IO.Path]::GetFullPath($Preview), [System.Drawing.Imaging.ImageFormat]::Png) }
        $bmp.Dispose()
        return , $ms.ToArray()
    }

    # 3. ICO schreiben: Header, Verzeichnis, PNG-Daten (geht ab Windows Vista für alle Größen)
    $sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
    $images = foreach ($s in $sizes) { , (New-IconPng $s) }
    $out = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($out)
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $data = $images[$i]
        $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256))   # 256 = 0
        $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
        $offset += $data.Length
    }
    foreach ($data in $images) { $bw.Write($data) }
    $bw.Flush()
    $full = [System.IO.Path]::GetFullPath($Target)
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($full)) | Out-Null
    [System.IO.File]::WriteAllBytes($full, $out.ToArray())
    Write-Host "Geschrieben: $full ($($out.Length) Bytes, Größen $($sizes -join ', '))"
}
finally { $src.Dispose() }
