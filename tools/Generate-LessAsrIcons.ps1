param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\src\LocalAsrClient.App\Assets\Brand")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing.Common

function New-RoundedRectanglePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Canvas {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    return @($bitmap, $graphics)
}

function New-RoundedPen {
    param(
        [System.Drawing.Color]$Color,
        [float]$Width
    )

    $pen = [System.Drawing.Pen]::new($Color, $Width)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    return $pen
}

function ConvertTo-PngBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $stream = [System.IO.MemoryStream]::new()
    try {
        $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

function New-AppIconPng {
    param([int]$Size)

    $canvas = New-Canvas -Size $Size
    $bitmap = $canvas[0]
    $graphics = $canvas[1]
    $scale = $Size / 128.0

    try {
        $background = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml("#FAFAF8"))
        $border = [System.Drawing.Pen]::new(
            [System.Drawing.ColorTranslator]::FromHtml("#D9DADD"),
            [Math]::Max(0.75, 2 * $scale))
        $ink = New-RoundedPen -Color ([System.Drawing.ColorTranslator]::FromHtml("#202124")) -Width ([Math]::Max(1.1, 8 * $scale))
        $shapeParameters = @{
            X = 7 * $scale
            Y = 7 * $scale
            Width = 114 * $scale
            Height = 114 * $scale
            Radius = 27 * $scale
        }
        $shape = New-RoundedRectanglePath @shapeParameters

        try {
            $graphics.FillPath($background, $shape)
            $graphics.DrawPath($border, $shape)
            $graphics.DrawLine($ink, 30 * $scale, 39 * $scale, 98 * $scale, 39 * $scale)
            $waveform = [System.Drawing.PointF[]]@(
                [System.Drawing.PointF]::new(30 * $scale, 65 * $scale),
                [System.Drawing.PointF]::new(40 * $scale, 65 * $scale),
                [System.Drawing.PointF]::new(48 * $scale, 56 * $scale),
                [System.Drawing.PointF]::new(56 * $scale, 74 * $scale),
                [System.Drawing.PointF]::new(64 * $scale, 49 * $scale),
                [System.Drawing.PointF]::new(72 * $scale, 81 * $scale),
                [System.Drawing.PointF]::new(80 * $scale, 56 * $scale),
                [System.Drawing.PointF]::new(88 * $scale, 65 * $scale),
                [System.Drawing.PointF]::new(98 * $scale, 65 * $scale))
            $graphics.DrawLines($ink, $waveform)
            $graphics.DrawLine($ink, 30 * $scale, 91 * $scale, 64 * $scale, 91 * $scale)
            return ,(ConvertTo-PngBytes -Bitmap $bitmap)
        }
        finally {
            $shape.Dispose()
            $ink.Dispose()
            $border.Dispose()
            $background.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function New-TrayIconPng {
    param(
        [int]$Size,
        [System.Drawing.Color]$Color
    )

    $canvas = New-Canvas -Size $Size
    $bitmap = $canvas[0]
    $graphics = $canvas[1]
    $scale = $Size / 32.0

    try {
        $ink = New-RoundedPen -Color $Color -Width ([Math]::Max(1.25, 2.5 * $scale))
        try {
            $graphics.DrawLine($ink, 6 * $scale, 8 * $scale, 26 * $scale, 8 * $scale)
            $waveform = [System.Drawing.PointF[]]@(
                [System.Drawing.PointF]::new(6 * $scale, 16 * $scale),
                [System.Drawing.PointF]::new(10 * $scale, 16 * $scale),
                [System.Drawing.PointF]::new(12 * $scale, 12 * $scale),
                [System.Drawing.PointF]::new(15 * $scale, 20 * $scale),
                [System.Drawing.PointF]::new(18 * $scale, 11 * $scale),
                [System.Drawing.PointF]::new(21 * $scale, 21 * $scale),
                [System.Drawing.PointF]::new(23 * $scale, 13 * $scale),
                [System.Drawing.PointF]::new(26 * $scale, 16 * $scale))
            $graphics.DrawLines($ink, $waveform)
            $graphics.DrawLine($ink, 6 * $scale, 24 * $scale, 16 * $scale, 24 * $scale)
            return ,(ConvertTo-PngBytes -Bitmap $bitmap)
        }
        finally {
            $ink.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Write-Ico {
    param(
        [string]$Path,
        [int[]]$Sizes,
        [byte[][]]$Images
    )

    if ($Sizes.Count -ne $Images.Count) {
        throw "Icon size and image counts differ."
    }

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$Sizes.Count)

        $offset = 6 + (16 * $Sizes.Count)
        for ($index = 0; $index -lt $Sizes.Count; $index++) {
            $size = $Sizes[$index]
            $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
            $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$Images[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $Images[$index].Length
        }

        foreach ($image in $Images) {
            $writer.Write($image)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$appSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$traySizes = @(16, 20, 24, 32)

$appImages = [byte[][]]@(
    foreach ($size in $appSizes) {
        ,(New-AppIconPng -Size $size)
    }
)
$darkTrayImages = [byte[][]]@(
    foreach ($size in $traySizes) {
        ,(New-TrayIconPng -Size $size -Color ([System.Drawing.ColorTranslator]::FromHtml("#202124")))
    }
)
$lightTrayImages = [byte[][]]@(
    foreach ($size in $traySizes) {
        ,(New-TrayIconPng -Size $size -Color ([System.Drawing.ColorTranslator]::FromHtml("#F7F7F5")))
    }
)

Write-Ico -Path (Join-Path $resolvedOutput "LessASR.ico") -Sizes $appSizes -Images $appImages
Write-Ico -Path (Join-Path $resolvedOutput "LessASR.Tray.Dark.ico") -Sizes $traySizes -Images $darkTrayImages
Write-Ico -Path (Join-Path $resolvedOutput "LessASR.Tray.Light.ico") -Sizes $traySizes -Images $lightTrayImages

Write-Output "Generated LessASR icon resources in $resolvedOutput"
