$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing

$ProjectDir = [System.IO.Path]::GetFullPath($PSScriptRoot)
$Sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$Statuses = [ordered]@{
    green = [System.Drawing.Color]::FromArgb(22, 163, 74)
    yellow = [System.Drawing.Color]::FromArgb(202, 132, 0)
    red = [System.Drawing.Color]::FromArgb(220, 38, 38)
}

function New-RoundedRectanglePath([System.Drawing.RectangleF] $Rectangle, [single] $Radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($Rectangle.X, $Rectangle.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Rectangle.X, $Rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int] $Size, [System.Drawing.Color] $StatusColor) {
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $scale = $Size / 16.0
        $inset = if ($Size -le 24) { 0.7 * $scale } else { 0.45 * $scale }
        $background = [System.Drawing.RectangleF]::new([single]$inset, [single]$inset, [single]($Size - 2 * $inset), [single]($Size - 2 * $inset))
        $radius = if ($Size -le 24) { $Size * 0.28 } else { $Size * 0.25 }
        $backgroundPath = New-RoundedRectanglePath $background $radius
        $backgroundBrush = [System.Drawing.SolidBrush]::new($StatusColor)
        try { $graphics.FillPath($backgroundBrush, $backgroundPath) } finally { $backgroundBrush.Dispose(); $backgroundPath.Dispose() }

        $outer = if ($Size -le 24) {
            [System.Drawing.RectangleF]::new([single](4.0 * $scale), [single](4.35 * $scale), [single](8.0 * $scale), [single](8.15 * $scale))
        } else {
            [System.Drawing.RectangleF]::new([single](3.55 * $scale), [single](3.9 * $scale), [single](8.9 * $scale), [single](8.95 * $scale))
        }
        $outerRadius = [Math]::Max(0.6 * $scale, $(if ($Size -le 24) { 0.8 * $scale } else { 0.9 * $scale }))
        $whiteBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
        $outerPath = New-RoundedRectanglePath $outer $outerRadius
        try { $graphics.FillPath($whiteBrush, $outerPath) } finally { $outerPath.Dispose() }

        $inner = if ($Size -le 24) {
            [System.Drawing.RectangleF]::new([single](5.35 * $scale), [single](5.7 * $scale), [single](5.3 * $scale), [single](6.8 * $scale))
        } else {
            [System.Drawing.RectangleF]::new([single](4.9 * $scale), [single](5.25 * $scale), [single](6.2 * $scale), [single](7.0 * $scale))
        }
        $innerBrush = [System.Drawing.SolidBrush]::new($StatusColor)
        try { $graphics.FillRectangle($innerBrush, $inner) } finally { $innerBrush.Dispose() }
        $dividerWidth = if ($Size -le 24) { [Math]::Max(0.9, 0.95 * $scale) } else { [Math]::Max(1.0, 0.85 * $scale) }
        $dividerX = $inner.Left + $inner.Width / 2 - $dividerWidth / 2
        $graphics.FillRectangle($whiteBrush, $dividerX, $inner.Top, $dividerWidth, $inner.Height)
        $whiteBrush.Dispose()

    } finally {
        $graphics.Dispose()
    }
    return $bitmap
}

function Convert-BitmapToPngBytes([System.Drawing.Bitmap] $Bitmap) {
    $stream = [System.IO.MemoryStream]::new()
    try {
        $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    } finally {
        $stream.Dispose()
    }
}

function New-IcoBytes([System.Collections.Generic.List[byte[]]] $PngFrames) {
    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$PngFrames.Count)
        $dataOffset = 6 + 16 * $PngFrames.Count
        for ($index = 0; $index -lt $PngFrames.Count; $index++) {
            $frame = $PngFrames[$index]
            $size = [int]$frame.Length
            $iconSize = if ($index -eq $PngFrames.Count - 1) { 0 } else { $Sizes[$index] }
            $writer.Write([byte]$iconSize)
            $writer.Write([byte]$iconSize)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$size)
            $writer.Write([uint32]$dataOffset)
            $dataOffset += $size
        }
        foreach ($frame in $PngFrames) { $writer.Write($frame) }
        return $stream.ToArray()
    } finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

foreach ($status in $Statuses.Keys) {
    $frames = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($size in $Sizes) {
        $bitmap = New-IconBitmap $size $Statuses[$status]
        try { $frames.Add((Convert-BitmapToPngBytes $bitmap)) } finally { $bitmap.Dispose() }
    }
    $iconBytes = New-IcoBytes $frames
    $name = "LiftCall.WindowsHelper.$status.ico"
    [System.IO.File]::WriteAllBytes((Join-Path $ProjectDir $name), $iconBytes)
    if ($status -eq 'green') {
        [System.IO.File]::WriteAllBytes((Join-Path $ProjectDir 'LiftCall.WindowsHelper.ico'), $iconBytes)
    }
}

Write-Host "Wygenerowano ikony: $($Sizes -join ', ') px; statusy: green, yellow, red."
