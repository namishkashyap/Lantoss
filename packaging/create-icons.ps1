<#
.SYNOPSIS
Renders the hopo logo as PNG files named <Prefix>-<size>.png.
.DESCRIPTION
The drawing is the same as source/UI/create-icon.ps1, which builds the Windows .ico. Keep the two in sync if the logo changes.
-ContentScale shrinks the logo around its centre. The macOS build uses 0.8306, which fits the 824 px icon body inside Apple's 1024 px grid.
.EXAMPLE
.\packaging\create-icons.ps1
.\packaging\create-icons.ps1 -OutputDir work\pack\icons-macos -Sizes 16,32,64,128,256,512,1024 -ContentScale 0.8306
#>
param(
    [string]$OutputDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'source\Desktop\Assets'),
    [int[]]$Sizes = @(16, 32, 48, 64, 128, 256, 512, 1024),
    [double]$ContentScale = 1.0,
    [string]$Prefix = 'lantoss'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
foreach ($lantossSize in $Sizes) {
    $lantossBitmap = New-Object System.Drawing.Bitmap($lantossSize, $lantossSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $lantossGraphics = [System.Drawing.Graphics]::FromImage($lantossBitmap)
    $lantossGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $lantossGraphics.ScaleTransform(($lantossSize / 256.0), ($lantossSize / 256.0))
    if ($ContentScale -ne 1.0) {
        $lantossGraphics.TranslateTransform(128, 128); $lantossGraphics.ScaleTransform($ContentScale, $ContentScale); $lantossGraphics.TranslateTransform(-128, -128)
    }
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
    $lantossFile = Join-Path $OutputDir ('{0}-{1}.png' -f $Prefix, $lantossSize)
    $lantossBitmap.Save($lantossFile, [System.Drawing.Imaging.ImageFormat]::Png)
    $lantossBox.Dispose(); $lantossGraphics.Dispose(); $lantossBitmap.Dispose()
    $lantossFile
}
