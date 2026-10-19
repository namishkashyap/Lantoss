param([string]$OutputPath = (Join-Path $PSScriptRoot 'Lantoss.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$lantossIconImages = @()
foreach ($lantossSize in @(16,32,48,256)) {
    $lantossBitmap = New-Object System.Drawing.Bitmap($lantossSize,$lantossSize)
    $lantossGraphics = [System.Drawing.Graphics]::FromImage($lantossBitmap)
    $lantossGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $lantossGraphics.ScaleTransform(($lantossSize / 256.0),($lantossSize / 256.0))
    $lantossBox = New-Object System.Drawing.Drawing2D.GraphicsPath
    $lantossBox.AddArc(4,4,120,120,180,90); $lantossBox.AddArc(132,4,120,120,270,90); $lantossBox.AddArc(132,132,120,120,0,90); $lantossBox.AddArc(4,132,120,120,90,90); $lantossBox.CloseFigure()
    $lantossBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush([System.Drawing.Point]::new(32,224),[System.Drawing.Point]::new(220,20),[System.Drawing.Color]::FromArgb(8,116,93),[System.Drawing.Color]::FromArgb(145,229,180))
    $lantossGraphics.FillPath($lantossBrush,$lantossBox)
    $lantossPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White,9)
    $lantossPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $lantossPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $lantossWave = New-Object System.Drawing.Drawing2D.GraphicsPath
    $lantossWave.AddBezier(40,141,89,58,146,211,216,104); $lantossGraphics.DrawPath($lantossPen,$lantossWave)
    $lantossWave.Reset(); $lantossWave.AddBezier(40,170,94,89,149,240,216,134); $lantossGraphics.DrawPath($lantossPen,$lantossWave)
    $lantossPng = New-Object System.IO.MemoryStream
    $lantossBitmap.Save($lantossPng,[System.Drawing.Imaging.ImageFormat]::Png)
    $lantossIconImages += [pscustomobject]@{Size=$lantossSize;Bytes=$lantossPng.ToArray()}
    $lantossPng.Dispose(); $lantossWave.Dispose(); $lantossPen.Dispose(); $lantossBrush.Dispose(); $lantossBox.Dispose(); $lantossGraphics.Dispose(); $lantossBitmap.Dispose()
}
$lantossIconFile = [System.IO.File]::Create([System.IO.Path]::GetFullPath($OutputPath))
$lantossIconWriter = New-Object System.IO.BinaryWriter($lantossIconFile)
try {
    $lantossIconWriter.Write([uint16]0); $lantossIconWriter.Write([uint16]1); $lantossIconWriter.Write([uint16]$lantossIconImages.Count)
    $lantossIconOffset = 6 + 16 * $lantossIconImages.Count
    foreach ($lantossImage in $lantossIconImages) {
        $lantossDimension = if ($lantossImage.Size -eq 256) { 0 } else { $lantossImage.Size }
        $lantossIconWriter.Write([byte]$lantossDimension); $lantossIconWriter.Write([byte]$lantossDimension); $lantossIconWriter.Write([byte]0); $lantossIconWriter.Write([byte]0)
        $lantossIconWriter.Write([uint16]1); $lantossIconWriter.Write([uint16]32); $lantossIconWriter.Write([uint32]$lantossImage.Bytes.Length); $lantossIconWriter.Write([uint32]$lantossIconOffset)
        $lantossIconOffset += $lantossImage.Bytes.Length
    }
    foreach ($lantossImage in $lantossIconImages) { $lantossIconWriter.Write([byte[]]$lantossImage.Bytes) }
}
finally { $lantossIconWriter.Dispose(); $lantossIconFile.Dispose() }
