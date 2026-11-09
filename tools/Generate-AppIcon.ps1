param([string]$OutputPath = (Join-Path $PSScriptRoot '..\assets\app-icon.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$frames = @()
foreach ($iconSize in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = [System.Drawing.Bitmap]::new($iconSize, $iconSize)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($iconSize / 256.0, $iconSize / 256.0)
    $backgroundBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#f4d96d'))
    $inkBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#43573f'))
    $inkPen = [System.Drawing.Pen]::new($inkBrush, 12)
    $inkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $inkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $outline = [System.Drawing.Drawing2D.GraphicsPath]::new()
    foreach ($corner in @(@(12,12,180),@(112,12,270),@(112,112,0),@(12,112,90))) { $outline.AddArc($corner[0], $corner[1], 132, 132, $corner[2], 90) }
    $outline.CloseFigure()
    $graphics.FillPath($backgroundBrush, $outline)
    $graphics.DrawEllipse($inkPen, 61, 58, 134, 134)
    $graphics.FillEllipse($inkBrush, 98, 103, 14, 14)
    $graphics.FillEllipse($inkBrush, 144, 103, 14, 14)
    $graphics.DrawArc($inkPen, 94, 119, 68, 50, 20, 140)
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@{ Size = $iconSize; Bytes = $stream.ToArray() }
    $stream.Dispose(); $outline.Dispose(); $inkPen.Dispose(); $inkBrush.Dispose(); $backgroundBrush.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$iconStream = [System.IO.File]::Create([System.IO.Path]::GetFullPath($OutputPath))
$writer = [System.IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $encodedSize = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$encodedSize); $writer.Write([byte]$encodedSize); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $iconStream.Dispose() }
Write-Output ([System.IO.Path]::GetFullPath($OutputPath))
