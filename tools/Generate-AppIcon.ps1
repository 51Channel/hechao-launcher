[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\src\Hechao.Launcher\Assets\hechao-launcher.ico"),
    [string]$PreviewPath = (Join-Path $PSScriptRoot "..\src\Hechao.Launcher\Assets\hechao-launcher-icon.png"),
    [switch]$WritePreview
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$iconCanvasSize = 180.0
$iconShapes = @(
    [pscustomobject]@{ X = 8.0; Y = 8.0; Width = 164.0; Height = 164.0; Color = "#D74735" },
    [pscustomobject]@{ X = 18.0; Y = 18.0; Width = 144.0; Height = 144.0; Color = "#FFFBF5" },
    [pscustomobject]@{ X = 36.0; Y = 36.0; Width = 108.0; Height = 108.0; Color = "#24211F" },
    [pscustomobject]@{ X = 32.0; Y = 32.0; Width = 36.0; Height = 36.0; Color = "#D74735" },
    [pscustomobject]@{ X = 112.0; Y = 32.0; Width = 36.0; Height = 36.0; Color = "#D74735" },
    [pscustomobject]@{ X = 32.0; Y = 112.0; Width = 36.0; Height = 36.0; Color = "#D74735" },
    [pscustomobject]@{ X = 112.0; Y = 112.0; Width = 36.0; Height = 36.0; Color = "#D74735" },
    [pscustomobject]@{ X = 76.0; Y = 76.0; Width = 28.0; Height = 28.0; Color = "#FFFBF5" }
)

function Convert-IconBoundary {
    param(
        [Parameter(Mandatory)][double]$Coordinate,
        [Parameter(Mandatory)][int]$Size
    )

    return [int][Math]::Round(
        ($Coordinate * $Size) / $iconCanvasSize,
        [MidpointRounding]::AwayFromZero)
}

function New-IconBitmap {
    param([Parameter(Mandatory)][int]$Size)

    $bitmap = [Drawing.Bitmap]::new(
        $Size,
        $Size,
        [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CompositingMode =
            [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::None
        $graphics.InterpolationMode =
            [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::None
        $graphics.Clear([Drawing.Color]::Transparent)

        foreach ($shape in $iconShapes) {
            $left = Convert-IconBoundary -Coordinate $shape.X -Size $Size
            $top = Convert-IconBoundary -Coordinate $shape.Y -Size $Size
            $right = Convert-IconBoundary `
                -Coordinate ($shape.X + $shape.Width) `
                -Size $Size
            $bottom = Convert-IconBoundary `
                -Coordinate ($shape.Y + $shape.Height) `
                -Size $Size
            $brush = [Drawing.SolidBrush]::new(
                [Drawing.ColorTranslator]::FromHtml($shape.Color))
            try {
                $graphics.FillRectangle(
                    $brush,
                    $left,
                    $top,
                    [Math]::Max(1, $right - $left),
                    [Math]::Max(1, $bottom - $top))
            }
            finally {
                $brush.Dispose()
            }
        }
    }
    finally {
        $graphics.Dispose()
    }

    return $bitmap
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $bitmap = New-IconBitmap -Size $size
    try {
        $memory = [IO.MemoryStream]::new()
        try {
            $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
            $images += , ([byte[]]$memory.ToArray())
        }
        finally {
            $memory.Dispose()
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

$file = [IO.File]::Open($OutputPath, [IO.FileMode]::Create)
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)

    $offset = 6 + (16 * $sizes.Count)
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $sizeByte = if ($sizes[$index] -ge 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$sizeByte)
        $writer.Write([byte]$sizeByte)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }

    foreach ($image in $images) {
        $writer.Write([byte[]]$image)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

if ($WritePreview) {
    $previewDirectory = Split-Path -Parent $PreviewPath
    New-Item -ItemType Directory -Force -Path $previewDirectory | Out-Null
    $preview = New-IconBitmap -Size 2048
    try {
        $preview.Save($PreviewPath, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $preview.Dispose()
    }
}
elseif (-not (Test-Path -LiteralPath $PreviewPath -PathType Leaf)) {
    throw "The approved high-resolution preview is missing: $PreviewPath"
}

Write-Output $OutputPath
Write-Output $PreviewPath
