param(
    [Parameter(Mandatory)][string]$CompilerPath,
    [Parameter(Mandatory)][string]$SdkToolsPath,
    [Parameter(Mandatory)][string]$SigningCertificatePath,
    [string]$SigningPassword = '',
    [string]$BuildRoot = '',
    [switch]$RunTests
)
$ErrorActionPreference = 'Stop'
$release = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $BuildRoot) { $BuildRoot = Join-Path $release 'work/explorer' }
$build = [IO.Path]::GetFullPath($BuildRoot)
$app = Join-Path $release 'app'
New-Item -ItemType Directory -Force $build,$app | Out-Null
function Invoke-Checked([string]$tool, [string[]]$arguments) {
    & $tool @arguments
    if ($LASTEXITCODE) { throw ('Tool failed: '+$tool) }
}
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
    [IO.Path]::GetFullPath($SigningCertificatePath),$SigningPassword,[Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
try {
    if ($certificate.Subject -ne 'CN=Lantoss Development' -or -not $certificate.HasPrivateKey) {
        throw 'Use a private code-signing certificate with subject CN=Lantoss Development. For a production publisher, update the manifest and registration subject together.'
    }
    # Only the public key is embedded in the app/source. Keep the PFX outside release/source archives.
    [IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'Publisher.cer'),$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
} finally { $certificate.Dispose() }
$windres = Join-Path (Split-Path -Parent $CompilerPath) 'windres.exe'
$resource = Join-Path $build 'explorer-resource.o'
Invoke-Checked $windres @((Join-Path $PSScriptRoot 'LantossExplorer.rc'),'-O','coff','-o',$resource)
$common = @('-O2','-std=c++17','-Wall','-Wextra','-Werror','-static-libgcc','-static-libstdc++','-lole32','-lshell32','-lshlwapi','-luuid','-ladvapi32')
$dll = Join-Path $app 'LantossExplorer.dll'
Invoke-Checked $CompilerPath (@((Join-Path $PSScriptRoot 'LantossExplorer.cpp'),(Join-Path $PSScriptRoot 'LantossExplorer.def'),$resource,'-shared') + $common + @('-o',$dll))
$sign = Join-Path $SdkToolsPath 'signtool.exe'
$signing = @('sign','/fd','SHA256','/f',[IO.Path]::GetFullPath($SigningCertificatePath))
if ($SigningPassword) { $signing += @('/p',$SigningPassword) }
Invoke-Checked $sign ($signing + @($dll))

# Identity-only package: binaries and logos resolve from the MSI's external install directory.
$sparse = Join-Path $build 'sparse'
New-Item -ItemType Directory -Force $sparse | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'AppxManifest.xml') (Join-Path $sparse 'AppxManifest.xml') -Force
$msix = Join-Path $app 'Lantoss.Explorer.msix'
Invoke-Checked (Join-Path $SdkToolsPath 'makeappx.exe') @('pack','/d',$sparse,'/p',$msix,'/nv','/o')
Invoke-Checked $sign ($signing + @($msix))
Copy-Item (Join-Path $PSScriptRoot 'RegisterPackage.ps1') (Join-Path $app 'RegisterPackage.ps1') -Force
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $app 'Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
$icon = [Drawing.Icon]::new((Join-Path $PSScriptRoot '../UI/Lantoss.ico'),256,256)
try {
    foreach ($entry in @(@('Logo.png',150),@('SmallLogo.png',44))) {
        $bitmap = [Drawing.Bitmap]::new($entry[1],$entry[1])
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $image = $icon.ToBitmap()
            try { $graphics.DrawImage($image,0,0,$entry[1],$entry[1]) } finally { $image.Dispose() }
            $bitmap.Save((Join-Path $assets $entry[0]),[Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $icon.Dispose() }
if ($RunTests) {
    $tests = Join-Path $build 'ExplorerTests.exe'
    Invoke-Checked $CompilerPath (@((Join-Path $PSScriptRoot 'ExplorerTests.cpp'),'-municode') + $common + @('-o',$tests))
    $state = Join-Path $build 'test-state'
    New-Item -ItemType Directory -Force $state | Out-Null
    Invoke-Checked $tests @($state,$dll)
    Write-Output ('Managed interoperability fixture: '+(Join-Path $state '11111111-1111-1111-1111-111111111111.req'))
}
