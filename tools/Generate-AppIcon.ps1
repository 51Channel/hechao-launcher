param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\src\Hechao.Launcher\Assets\hechao-launcher.ico"),
    [string]$PreviewPath = (Join-Path $PSScriptRoot "..\src\Hechao.Launcher\Assets\hechao-launcher-icon.png")
)

Add-Type -AssemblyName System.Drawing

function New-RoundedPath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    )
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    $ink = [System.Drawing.ColorTranslator]::FromHtml("#171310")
    $red = [System.Drawing.ColorTranslator]::FromHtml("#AB251E")
    $paper = [System.Drawing.ColorTranslator]::FromHtml("#FFF9F3")
    $white = [System.Drawing.Color]::White

    $shadowPath = New-RoundedPath ($Size * 0.12) ($Size * 0.13) ($Size * 0.77) ($Size * 0.77) ($Size * 0.075)
    $cardPath = New-RoundedPath ($Size * 0.07) ($Size * 0.06) ($Size * 0.78) ($Size * 0.78) ($Size * 0.075)
    $letterPath = New-RoundedPath ($Size * 0.29) ($Size * 0.20) ($Size * 0.38) ($Size * 0.52) ($Size * 0.15)
    $letterCutoutPath = New-RoundedPath ($Size * 0.39) ($Size * 0.31) ($Size * 0.19) ($Size * 0.30) ($Size * 0.085)

    $inkBrush = [System.Drawing.SolidBrush]::new($ink)
    $redBrush = [System.Drawing.SolidBrush]::new($red)
    $paperBrush = [System.Drawing.SolidBrush]::new($paper)
    $whiteBrush = [System.Drawing.SolidBrush]::new($white)

    $graphics.FillPath($inkBrush, $shadowPath)
    $graphics.FillPath($redBrush, $cardPath)
    $graphics.FillRectangle(
        $paperBrush,
        $Size * 0.72,
        $Size * 0.06,
        $Size * 0.15,
        $Size * 0.085
    )

    $graphics.FillPath($whiteBrush, $letterPath)
    $graphics.FillPath($redBrush, $letterCutoutPath)
    $graphics.FillRectangle(
        $redBrush,
        $Size * 0.52,
        $Size * 0.31,
        $Size * 0.18,
        $Size * 0.30
    )

    $shadowPath.Dispose()
    $cardPath.Dispose()
    $letterPath.Dispose()
    $letterCutoutPath.Dispose()
    $inkBrush.Dispose()
    $redBrush.Dispose()
    $paperBrush.Dispose()
    $whiteBrush.Dispose()
    $graphics.Dispose()

    return $bitmap
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    $memory = [System.IO.MemoryStream]::new()
    $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += , ([byte[]]$memory.ToArray())
    $memory.Dispose()
    $bitmap.Dispose()
}

$file = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create)
$writer = [System.IO.BinaryWriter]::new($file)
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

$writer.Dispose()
$file.Dispose()

$preview = New-IconBitmap 512
$preview.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()

Write-Output $OutputPath
Write-Output $PreviewPath
