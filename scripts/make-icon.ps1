# Draws the rexplayer mark (a rounded frame holding a play triangle, in the REX signal colour on the
# ink tile) at every size Windows asks for and writes them into assets/rexplayer.ico. Run it again
# after changing the mark; the output is committed so builds need no drawing tools.
[CmdletBinding()]
param(
    [string]$Output
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if (-not $Output) {
    # Resolved here rather than as the parameter default: Windows PowerShell leaves $PSScriptRoot
    # empty while it binds parameters.
    $Output = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\assets\rexplayer.ico"
}
Add-Type -AssemblyName System.Drawing

$ink = [System.Drawing.Color]::FromArgb(255, 8, 10, 9)
$signal = [System.Drawing.Color]::FromArgb(255, 215, 255, 63)
$sizes = @(16, 24, 32, 48, 64, 128, 256)

function New-RoundedRectangle([single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Mark([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $scale = $size / 32.0

    $tile = New-RoundedRectangle 0 0 $size $size (7 * $scale)
    $graphics.FillPath((New-Object System.Drawing.SolidBrush $ink), $tile)

    $pen = New-Object System.Drawing.Pen $signal, ([Math]::Max(1.0, 2.2 * $scale))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $frame = New-RoundedRectangle (5 * $scale) (7 * $scale) (22 * $scale) (18 * $scale) (4 * $scale)
    $graphics.DrawPath($pen, $frame)

    $triangle = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (13.5 * $scale), (11.5 * $scale)),
        (New-Object System.Drawing.PointF (13.5 * $scale), (20.5 * $scale)),
        (New-Object System.Drawing.PointF (21 * $scale), (16 * $scale))
    )
    $graphics.FillPolygon((New-Object System.Drawing.SolidBrush $signal), $triangle)
    $graphics.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return , $stream.ToArray()
}

$images = foreach ($size in $sizes) { , (New-Mark $size) }

# ICONDIR, one ICONDIRENTRY per image, then the PNG payloads (PNG-compressed entries, Vista and later).
$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $out
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([Byte]$dimension)
    $writer.Write([Byte]$dimension)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$images[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()

$directory = Split-Path -Parent $Output
if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
[System.IO.File]::WriteAllBytes((Resolve-Path -LiteralPath $directory).Path + "\" + (Split-Path -Leaf $Output), $out.ToArray())
Write-Host "Wrote $Output ($($sizes.Count) sizes)."
