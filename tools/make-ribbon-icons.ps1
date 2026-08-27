#requires -Version 5.1
# Generates every ribbon icon of the RiveTT panel, at the sizes Revit expects:
# 32x32 for LargeImage and 16x16 for Image.
#
#   .\tools\make-ribbon-icons.ps1
#
# The status icon is drawn here. The lock and unlock icons are hand-authored
# artwork, kept at high resolution under Resources\source\ and RESAMPLED here.
#
# That split is the whole point of this script owning all six files. It used to
# disclaim the hand-authored pair -- "this script does not touch them" -- and they
# drifted: lock-32.png was committed at 128x128 and lock-16.png at 64x64, four
# times their nominal size, under names that said otherwise. status-*.png, the
# only pair this script generated, was the only pair at the right size. Nothing
# failed loudly; the buttons simply had no visible icon.
#
# Resources\source\ is deliberately a SUBfolder: the .csproj embeds Resources\*.png
# without recursion, so the high-resolution originals stay out of the assembly.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\RiveTT.Plugin\Resources'
$srcDir = Join-Path $outDir 'source'
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

# Mid-saturation tone stays legible on both the light and the dark Revit theme;
# a pure pastel disappears on light grey, a pure dark tone on the dark theme.
$blue = [System.Drawing.Color]::FromArgb(255, 6, 150, 215)   # status

# Revit wants exactly these two per button.
$sizes = 32, 16

function New-Bitmap([int] $size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)
    return @{ Bitmap = $bmp; Graphics = $g; Size = $size }
}

function Save-Bitmap($ctx, [string] $name) {
    $path = Join-Path $outDir $name
    $ctx.Graphics.Dispose()
    $ctx.Bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $ctx.Bitmap.Dispose()
    Write-Host ("  {0,-18} {1}x{1}" -f $name, $ctx.Size)
}

# Status: a filled disc carrying a white "i". Recognisable as information at both
# sizes, and it does not compete with the lock/unlock artwork for meaning.
function Draw-Status($ctx, $colour) {
    $s = [double] $ctx.Size
    $g = $ctx.Graphics
    $brush = New-Object System.Drawing.SolidBrush($colour)
    $inset = $s * 0.06
    $g.FillEllipse($brush, [single]$inset, [single]$inset,
        [single]($s - $inset * 2), [single]($s - $inset * 2))

    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $dot = [math]::Max(1.5, $s * 0.13)
    $g.FillEllipse($white, [single]($s * 0.5 - $dot / 2), [single]($s * 0.22), [single]$dot, [single]$dot)
    $barW = [math]::Max(1.5, $s * 0.13)
    $g.FillRectangle($white, [single]($s * 0.5 - $barW / 2), [single]($s * 0.42),
        [single]$barW, [single]($s * 0.34))

    $white.Dispose(); $brush.Dispose()
}

# Resamples one high-resolution original down to a nominal ribbon size.
# HighQualityBicubic with a half-pixel-inset destination rectangle: without the
# inset, GDI+ samples past the edge and leaves a faint halo on the alpha channel,
# which shows as a grey fringe on the dark Revit theme.
function Resize-Artwork([string] $sourcePath, [int] $size, [string] $name) {
    $source = [System.Drawing.Image]::FromFile($sourcePath)
    try {
        $ctx = New-Bitmap $size
        $g = $ctx.Graphics
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

        $attr = New-Object System.Drawing.Imaging.ImageAttributes
        $attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
        $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
        $g.DrawImage($source, $rect, 0, 0, $source.Width, $source.Height,
            [System.Drawing.GraphicsUnit]::Pixel, $attr)
        $attr.Dispose()

        Save-Bitmap $ctx $name
    }
    finally { $source.Dispose() }
}

Write-Host 'Icones ecrites dans src\RiveTT.Plugin\Resources :'

foreach ($size in $sizes) {
    $ctx = New-Bitmap $size
    Draw-Status $ctx $blue
    Save-Bitmap $ctx "status-$size.png"
}

foreach ($art in 'lock', 'unlock') {
    $sourcePath = Join-Path $srcDir "$art.png"
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Artwork source introuvable : $sourcePath"
    }
    foreach ($size in $sizes) {
        Resize-Artwork $sourcePath $size "$art-$size.png"
    }
}

# The bug this script shipped was a size that did not match the name, so the
# script verifies its own output rather than trusting it.
Write-Host ''
Write-Host 'Verification des dimensions :'
$bad = 0
foreach ($file in Get-ChildItem $outDir -Filter '*.png') {
    if ($file.Name -notmatch '-(\d+)\.png$') { continue }
    $expected = [int] $Matches[1]
    $img = [System.Drawing.Image]::FromFile($file.FullName)
    $actual = $img.Width
    $square = ($img.Width -eq $img.Height)
    $img.Dispose()
    if ($actual -ne $expected -or -not $square) {
        Write-Host ("  ECHEC {0} : {1}x{2}, attendu {3}x{3}" -f $file.Name, $actual, $img.Height, $expected) -ForegroundColor Red
        $bad++
    }
    else {
        Write-Host ("  ok    {0,-18} {1}x{1}" -f $file.Name, $actual) -ForegroundColor Green
    }
}
if ($bad -gt 0) { throw "$bad icone(s) a une taille qui ne correspond pas a son nom." }
