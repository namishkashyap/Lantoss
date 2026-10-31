<#
.SYNOPSIS
Builds dist/hopo-<version>-linux-<x64|arm64>.tar.gz and dist/hopo_<version>_<amd64|arm64>.deb on Windows.
.DESCRIPTION
For each runtime: publishes a self-contained Lantoss, then writes
  - a tarball holding lantoss-<version>/ with app/, hopo.desktop, hopo.png, install.sh and uninstall.sh;
  - a .deb with /opt/hopo, a /usr/bin/hopo symlink, /usr/share/applications/hopo.desktop, hicolor icons,
    and postinst/postrm scripts that refresh the desktop and icon caches.
Both are checked with verify.cjs and tar.exe. The version comes from <Version> in source/Desktop/Lantoss.Desktop.csproj.
.PARAMETER Maintainer
The Debian Maintainer field, "Name <email>".
.PARAMETER SkipPublish
Reuses work/pack/<rid>/publish from an earlier run.
.PARAMETER RefreshIcons
Re-renders source/Desktop/Assets/lantoss-*.png even when they exist.
.EXAMPLE
.\packaging\build-linux.ps1
.\packaging\build-linux.ps1 -Rid linux-x64 -SkipPublish
#>
param(
    [ValidateSet('linux-x64', 'linux-arm64')][string[]]$Rid = @('linux-x64', 'linux-arm64'),
    [string]$Maintainer = 'namishkashyap <36044596+namishkashyap@users.noreply.github.com>',
    [switch]$SkipPublish,
    [switch]$RefreshIcons
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

Assert-LantossTools
$version = Get-LantossVersion
$debVersion = $version -replace '-', '~'
Initialize-LantossIcons -Force:$RefreshIcons
New-Item -ItemType Directory -Force -Path $LantossDist | Out-Null
$hicolorSizes = @(16, 32, 48, 64, 128, 256, 512)

$outputs = @()
foreach ($runtime in $Rid) {
    $isArm = $runtime -eq 'linux-arm64'
    $tarArch = if ($isArm) { 'arm64' } else { 'x64' }
    $debArch = if ($isArm) { 'arm64' } else { 'amd64' }
    $machine = if ($isArm) { 'aarch64' } else { 'x86_64' }
    $publish = Publish-Lantoss -Rid $runtime -Skip:$SkipPublish

    $stage = Join-Path $LantossWork "$runtime\linux"
    Reset-LantossDirectory $stage
    $desktop = Join-Path $stage 'hopo.desktop'
    Expand-LantossTemplate -Template (Join-Path $PSScriptRoot 'linux\hopo.desktop') -Destination $desktop
    $install = Join-Path $stage 'install.sh'
    Expand-LantossTemplate -Template (Join-Path $PSScriptRoot 'linux\install.sh') -Destination $install -Values @{ ARCH = $machine }
    $uninstall = Join-Path $stage 'uninstall.sh'
    Expand-LantossTemplate -Template (Join-Path $PSScriptRoot 'linux\uninstall.sh') -Destination $uninstall

    $top = "hopo-$version"
    $tarball = Join-Path $LantossDist "hopo-$version-linux-$tarArch.tar.gz"
    Invoke-LantossNative node @($LantossPackJs, 'targz', $tarball,
        '--add', "$top/app=$publish",
        '--add', "$top/hopo.desktop=$desktop",
        '--add', "$top/hopo.png=$(Join-Path $LantossAssets 'lantoss-256.png')",
        '--add', "$top/install.sh=$install",
        '--add', "$top/uninstall.sh=$uninstall")
    Invoke-LantossNative node @($LantossVerifyJs, $tarball, '--arch', $tarArch, '--version', $version)
    Test-LantossArchiveWithTar $tarball
    $outputs += $tarball

    $control = Join-Path $stage 'deb-control'
    Expand-LantossTemplate -Template (Join-Path $PSScriptRoot 'linux\deb\control') -Destination (Join-Path $control 'control') -Values @{ VERSION = $debVersion; ARCH = $debArch; MAINTAINER = $Maintainer }
    foreach ($script in @('postinst', 'postrm')) {
        Expand-LantossTemplate -Template (Join-Path $PSScriptRoot "linux\deb\$script") -Destination (Join-Path $control $script)
    }
    $deb = Join-Path $LantossDist "hopo_${debVersion}_$debArch.deb"
    $debArgs = @($LantossPackJs, 'deb', $deb, '--control', $control,
        '--add', "opt/hopo=$publish",
        '--add', "usr/share/applications/hopo.desktop=$desktop",
        '--symlink', 'usr/bin/hopo=/opt/hopo/hopo')
    foreach ($size in $hicolorSizes) {
        $debArgs += @('--add', "usr/share/icons/hicolor/${size}x${size}/apps/hopo.png=$(Join-Path $LantossAssets "lantoss-$size.png")")
    }
    Invoke-LantossNative node $debArgs
    Invoke-LantossNative node @($LantossVerifyJs, $deb, '--arch', $tarArch, '--version', $version)
    Test-LantossArchiveWithTar $deb
    $outputs += $deb
}
Write-LantossSummary $outputs
