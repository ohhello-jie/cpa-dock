$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$assetDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\CPALauncher.Wpf\Assets'
[xml]$svg = Get-Content -LiteralPath (Join-Path $assetDirectory 'logo.svg') -Raw
$brush = [System.Windows.Media.BrushConverter]::new().ConvertFromString($svg.svg.rect.fill)
$geometry = [System.Windows.Media.Geometry]::Parse($svg.svg.path.d)
$pen = [System.Windows.Media.Pen]::new([System.Windows.Media.Brushes]::White, 4.5)
$pen.StartLineCap = $pen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
$pen.LineJoin = [System.Windows.Media.PenLineJoin]::Round
$images = @()
$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
foreach ($size in $sizes) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform([System.Windows.Media.ScaleTransform]::new($size / 64.0, $size / 64.0))
    $drawing.DrawRoundedRectangle($brush, $null, [System.Windows.Rect]::new(0, 0, 64, 64), 12, 12)
    $drawing.DrawGeometry($null, $pen, $geometry)
    $drawing.Pop()
    $drawing.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.MemoryStream]::new()
    $encoder.Save($stream)
    $images += ,$stream.ToArray()
    if ($size -eq 256) { [System.IO.File]::WriteAllBytes((Join-Path $assetDirectory 'logo.png'), $stream.ToArray()) }
    $stream.Dispose()
}
$stream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
[System.IO.File]::WriteAllBytes((Join-Path $assetDirectory 'app.ico'), $stream.ToArray())
$writer.Dispose(); $stream.Dispose()
Write-Host 'Generated logo.png and app.ico from logo.svg.'
