param([string]$OutputPath = (Join-Path $PSScriptRoot 'Lantoss.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$lantossIconImages = @()
foreach ($lantossSize in @(16,32,48,256)) {
    $lantossBitmap = New-Object System.Drawing.Bitmap($lantossSize,$lantossSize)
    $lantossGraphics = [System.Drawing.Graphics]::FromImage($lantossBitmap)
    $lantossGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $lantossGraphics.ScaleTransform(($lantossSize / 256.0),($lantossSize / 256.0))
    # hopo mark (256 x 256 design space): dark rounded tile, faint purple and green glows, amber centre, two rings and a mint satellite.
    $lantossBox = New-Object System.Drawing.Drawing2D.GraphicsPath
    $lantossBox.AddArc(8,8,112,112,180,90); $lantossBox.AddArc(136,8,112,112,270,90); $lantossBox.AddArc(136,136,112,112,0,90); $lantossBox.AddArc(8,136,112,112,90,90); $lantossBox.CloseFigure()
    $lantossFill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(11,12,18)); $lantossGraphics.FillPath($lantossFill,$lantossBox)
    $lantossGraphics.SetClip($lantossBox)
    foreach ($lantossGlow in @(@(-60,-60,250,140,60,255,120), @(70,70,250,40,240,150,90))) {
        $lantossGlowPath = New-Object System.Drawing.Drawing2D.GraphicsPath; $lantossGlowPath.AddEllipse($lantossGlow[0],$lantossGlow[1],$lantossGlow[2],$lantossGlow[2])
        $lantossGlowBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush($lantossGlowPath); $lantossGlowBrush.CenterColor = [System.Drawing.Color]::FromArgb($lantossGlow[6],$lantossGlow[3],$lantossGlow[4],$lantossGlow[5]); $lantossGlowBrush.SurroundColors = @([System.Drawing.Color]::FromArgb(0,$lantossGlow[3],$lantossGlow[4],$lantossGlow[5]))
        $lantossGraphics.FillPath($lantossGlowBrush,$lantossGlowPath); $lantossGlowBrush.Dispose(); $lantossGlowPath.Dispose()
    }
    $lantossGraphics.ResetClip()
    # Ring strokes never drop below about one device pixel, so the 16 px icon keeps both rings.
    $lantossStroke = [Math]::Max(9.0, 1.2 * 256.0 / $lantossSize)
    $lantossOuter = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(120,237,238,242),$lantossStroke); $lantossGraphics.DrawEllipse($lantossOuter,40,40,176,176)
    $lantossInner = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(190,237,238,242),$lantossStroke); $lantossGraphics.DrawEllipse($lantossInner,72,72,112,112)
    $lantossAmber = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(245,181,74)); $lantossGraphics.FillEllipse($lantossAmber,100,100,56,56)
    $lantossMint = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(110,231,197)); $lantossGraphics.FillEllipse($lantossMint,166,62,36,36)
    $lantossOuter.Dispose(); $lantossInner.Dispose(); $lantossAmber.Dispose(); $lantossMint.Dispose(); $lantossFill.Dispose()
    $lantossPng = New-Object System.IO.MemoryStream
    $lantossBitmap.Save($lantossPng,[System.Drawing.Imaging.ImageFormat]::Png)
    $lantossIconImages += [pscustomobject]@{Size=$lantossSize;Bytes=$lantossPng.ToArray()}
    $lantossPng.Dispose(); $lantossBox.Dispose(); $lantossGraphics.Dispose(); $lantossBitmap.Dispose()
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
