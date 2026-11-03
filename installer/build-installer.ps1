param(
    [string]$DotnetPath = 'dotnet',
    [string]$BuildRoot = '',
    [switch]$SkipPublish
)
$ErrorActionPreference = 'Stop'
$lantossRelease = Split-Path -Parent $PSScriptRoot
if (-not $BuildRoot) { $BuildRoot = Join-Path $lantossRelease 'work' }
$lantossBuild = [IO.Path]::GetFullPath($BuildRoot)
New-Item -ItemType Directory -Force -Path $lantossBuild | Out-Null
if (-not $SkipPublish) {
    & (Join-Path $lantossRelease 'source\build.ps1') -Mode Publish -DotnetPath $DotnetPath -BuildRoot $lantossBuild
    if ($LASTEXITCODE) { throw 'App publication failed.' }
}
# The Windows 11 package is no longer shipped (it can only start Lantoss.exe); its script stays only to remove packages left by hopo 2.3 and earlier.
Copy-Item -LiteralPath (Join-Path $lantossRelease 'source\Explorer\RegisterPackage.ps1') -Destination (Join-Path $lantossRelease 'app\RegisterPackage.ps1') -Force
foreach ($lantossExe in @('hopoUsers.exe','hopoAdmin.exe')) {
    if ((Get-Item (Join-Path $lantossRelease ('app\'+$lantossExe))).VersionInfo.FileVersion -ne '2.5.1.0') { throw ('Publish '+$lantossExe+' 2.5.1 first.') }
}

# Build-only tools are pinned and hash checked. No compiler or installer is installed on the host.
$lantossTools = Join-Path $lantossBuild 'installer-tools'
New-Item -ItemType Directory -Force -Path $lantossTools | Out-Null
$lantossPackages = @(
    @{Name='wix';SHA256='A94DD42AE1FB56B32DA180E2173CEDA4F0D10B4C8871C5EE59ECB502131A1EB6'},
    @{Name='wixtoolset.ui.wixext';SHA256='E92C4DDAC5D17F5360291AB856C676E19FD92AF14DAFFB5D5D4B8E1E9C716B47'}
)
foreach ($lantossPackage in $lantossPackages) {
    $lantossArchive = Join-Path $lantossTools ($lantossPackage.Name+'.zip')
    if (-not (Test-Path -LiteralPath $lantossArchive)) {
        Invoke-WebRequest -Uri ('https://api.nuget.org/v3-flatcontainer/'+$lantossPackage.Name+'/4.0.6/'+$lantossPackage.Name+'.4.0.6.nupkg') -OutFile $lantossArchive
    }
    if ((Get-FileHash -LiteralPath $lantossArchive -Algorithm SHA256).Hash -ne $lantossPackage.SHA256) { throw ('Installer tool hash mismatch: '+$lantossPackage.Name) }
    $lantossExpanded = Join-Path $lantossTools $lantossPackage.Name
    # Always extract afresh from the verified archive so a modified earlier extraction is never reused.
    if (Test-Path -LiteralPath $lantossExpanded) { Remove-Item -LiteralPath $lantossExpanded -Recurse -Force }
    Expand-Archive -LiteralPath $lantossArchive -DestinationPath $lantossExpanded
}
$lantossWix = Join-Path $lantossTools 'wix\tools\net6.0\any\wix.dll'
$lantossUI = Join-Path $lantossTools 'wixtoolset.ui.wixext\wixext4\WixToolset.UI.wixext.dll'
# One setup per app, so each person installs only hopo Users or hopo Admin.
foreach ($lantossApp in @('Users','Admin')) {
    $lantossMsi = Join-Path $lantossRelease ('hopo-'+$lantossApp+'-Setup-2.5.1-x64.msi')
    & $DotnetPath $lantossWix build (Join-Path $PSScriptRoot 'Lantoss.wxs') -arch x64 -ext $lantossUI -d ('ReleaseDir='+$lantossRelease) -d ('App='+$lantossApp) -out $lantossMsi -intermediatefolder (Join-Path $lantossBuild ('installer-objects-'+$lantossApp)) -pdbtype none -wx
    if ($LASTEXITCODE) { throw 'MSI compilation or validation failed.' }
    Get-FileHash -LiteralPath $lantossMsi -Algorithm SHA256
}
