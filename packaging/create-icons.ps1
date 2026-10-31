<#
.SYNOPSIS
Renders the Lantoss logo as PNG files named <Prefix>-<size>.png.
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
    # Logo drawing copied from source/UI/create-icon.ps1 (256 x 256 design space).
    $lantossBox = New-Object System.Drawing.Drawing2D.GraphicsPath
    $lantossBox.AddArc(4,4,120,120,180,90); $lantossBox.AddArc(132,4,120,120,270,90); $lantossBox.AddArc(132,132,120,120,0,90); $lantossBox.AddArc(4,132,120,120,90,90); $lantossBox.CloseFigure()
    $lantossBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush([System.Drawing.Point]::new(32,224),[System.Drawing.Point]::new(220,20),[System.Drawing.Color]::FromArgb(8,116,93),[System.Drawing.Color]::FromArgb(145,229,180))
    # Deviation from the original: its default Tile wrap repeats the gradient into the top-right and bottom-left corners.
    $lantossBrush.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipXY
    $lantossGraphics.FillPath($lantossBrush,$lantossBox)
    $lantossPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White,9)
    $lantossPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $lantossPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $lantossWave = New-Object System.Drawing.Drawing2D.GraphicsPath
    $lantossWave.AddBezier(40,141,89,58,146,211,216,104); $lantossGraphics.DrawPath($lantossPen,$lantossWave)
    $lantossWave.Reset(); $lantossWave.AddBezier(40,170,94,89,149,240,216,134); $lantossGraphics.DrawPath($lantossPen,$lantossWave)
    $lantossFile = Join-Path $OutputDir ('{0}-{1}.png' -f $Prefix, $lantossSize)
    $lantossBitmap.Save($lantossFile, [System.Drawing.Imaging.ImageFormat]::Png)
    $lantossWave.Dispose(); $lantossPen.Dispose(); $lantossBrush.Dispose(); $lantossBox.Dispose(); $lantossGraphics.Dispose(); $lantossBitmap.Dispose()
    $lantossFile
}
