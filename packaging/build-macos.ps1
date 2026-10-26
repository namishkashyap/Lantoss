<#
.SYNOPSIS
Builds dist/Lantoss-<version>-macOS-AppleSilicon.zip and dist/Lantoss-<version>-macOS-Intel.zip on Windows.
.DESCRIPTION
For each runtime: publishes a self-contained single-file Lantoss (Contents/MacOS then holds only Mach-O code, which
code signing requires), assembles Lantoss.app (Contents/Resources/Lantoss.icns, Contents/Info.plist, Contents/PkgInfo),
puts install.sh and a double-clickable "Install Lantoss.command" next to it, zips them with Unix modes, and checks the
zip with verify.cjs and tar.exe. The app is not sealed here; the installer clears quarantine and signs it ad hoc on
the user's Mac. The version comes from <Version> in source/Desktop/Lantoss.Desktop.csproj.
.PARAMETER MinimumMacOS
LSMinimumSystemVersion. 14.0 is the oldest macOS that .NET 10 supported at release. The .NET 10 runtime
binaries are built for macOS 12.0, and verify.cjs fails if any bundled binary needs a newer macOS than this value.
.PARAMETER SkipPublish
Reuses work/pack/<rid>/publish from an earlier run.
.PARAMETER RefreshIcons
Re-renders source/Desktop/Assets/lantoss-*.png even when they exist.
.EXAMPLE
.\packaging\build-macos.ps1
.\packaging\build-macos.ps1 -Rid osx-arm64 -SkipPublish
#>
param(
    [ValidateSet('osx-arm64', 'osx-x64')][string[]]$Rid = @('osx-arm64', 'osx-x64'),
    [string]$MinimumMacOS = '14.0',
    [switch]$SkipPublish,
    [switch]$RefreshIcons
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

function Test-LantossPlist {
    param([Parameter(Mandatory = $true)][string]$Path)
    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Ignore
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create($Path, $settings)
    try { $document = New-Object System.Xml.XmlDocument; $document.Load($reader) } finally { $reader.Dispose() }
    if ($document.DocumentElement.Name -ne 'plist' -or $document.DocumentElement.FirstChild.Name -ne 'dict') { throw "$Path is not a property list" }
    $keys = @($document.SelectNodes('/plist/dict/key') | ForEach-Object { $_.InnerText })
    foreach ($key in @('CFBundleExecutable', 'CFBundleIdentifier', 'CFBundleIconFile', 'LSMinimumSystemVersion', 'NSHighResolutionCapable', 'NSLocalNetworkUsageDescription', 'NSBonjourServices')) {
        if ($keys -notcontains $key) { throw "Info.plist lacks $key" }
    }
    Write-Host "Info.plist is well-formed XML (System.Xml) with $($keys.Count) keys"
}

Assert-LantossTools
$version = Get-LantossVersion
$bundleVersion = ($version -split '[-+]')[0]
Initialize-LantossIcons -Force:$RefreshIcons
New-Item -ItemType Directory -Force -Path $LantossDist | Out-Null

# The macOS icon keeps Apple's grid margin (an 824 px body in 1024 px); Linux and the window icon use the full-bleed PNGs.
$macIcons = Join-Path $LantossWork 'icons-macos'
Reset-LantossDirectory $macIcons
& (Join-Path $PSScriptRoot 'create-icons.ps1') -OutputDir $macIcons -Sizes 16, 32, 64, 128, 256, 512, 1024 -ContentScale 0.8306 | Out-Null
$icns = Join-Path $LantossWork 'Lantoss.icns'
Invoke-LantossNative node (@($LantossPackJs, 'icns', $icns) + @(Get-ChildItem -Path $macIcons -Filter '*.png' | ForEach-Object { $_.FullName }))

$outputs = @()
foreach ($runtime in $Rid) {
    $isArm = $runtime -eq 'osx-arm64'
    $label = if ($isArm) { 'AppleSilicon' } else { 'Intel' }
    $arch = if ($isArm) { 'arm64' } else { 'x86_64' }
    $publish = Publish-Lantoss -Rid $runtime -Skip:$SkipPublish -Extra @('-p:PublishSingleFile=true', '-p:DebugType=none')

    $stage = Join-Path $LantossWork "$runtime\macos"
    Reset-LantossDirectory $stage
    $plist = Join-Path $stage 'Info.plist'
    Expand-LantossTemplate -Template (Join-Path $PSScriptRoot 'macos\Info.plist') -Destination $plist -Values @{ VERSION = $version; BUNDLE_VERSION = $bundleVersion; MIN_MACOS = $MinimumMacOS }
    Test-LantossPlist $plist
    $install = Join-Path $stage 'install.sh'
    Expand-LantossTemplate -Template (Join-Path $PSScriptRoot 'macos\install.sh') -Destination $install -Values @{ ARCH = $arch }
    $pkgInfo = Join-Path $stage 'PkgInfo'
    [System.IO.File]::WriteAllText($pkgInfo, 'APPL????', [System.Text.Encoding]::ASCII)

    $name = "Lantoss-$version-macOS-$label"
    $zip = Join-Path $LantossDist "$name.zip"
    $contents = "$name/Lantoss.app/Contents"
    Invoke-LantossNative node @($LantossPackJs, 'zip', $zip,
        '--add', "$contents/MacOS=$publish",
        '--add', "$contents/Info.plist=$plist",
        '--add', "$contents/PkgInfo=$pkgInfo",
        '--add', "$contents/Resources/Lantoss.icns=$icns",
        '--add', "$name/install.sh=$install",
        '--add', "$name/Install Lantoss.command=$install")
    Invoke-LantossNative node @($LantossVerifyJs, $zip, '--arch', $arch, '--version', $version)
    Test-LantossArchiveWithTar $zip
    $outputs += $zip
}
Write-LantossSummary $outputs
