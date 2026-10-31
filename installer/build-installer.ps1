param(
    [string]$DotnetPath = 'dotnet',
    [string]$BuildRoot = '',
    [switch]$SkipPublish,
    [string]$CompilerPath = '',
    [string]$SdkToolsPath = '',
    [string]$SigningCertificatePath = '',
    [switch]$NoTimestamp
)
$ErrorActionPreference = 'Stop'
$lantossRelease = Split-Path -Parent $PSScriptRoot
if (-not $BuildRoot) { $BuildRoot = Join-Path $lantossRelease 'work' }
$lantossBuild = [IO.Path]::GetFullPath($BuildRoot)
New-Item -ItemType Directory -Force -Path $lantossBuild | Out-Null
if (-not $SkipPublish) {
    if (-not $CompilerPath -or -not $SdkToolsPath -or -not $SigningCertificatePath) { throw 'Pass CompilerPath (g++.exe), SdkToolsPath (x64 Windows SDK tools) and SigningCertificatePath, or build Explorer and the app first and use SkipPublish.' }
    & (Join-Path $lantossRelease 'source\Explorer\build-explorer.ps1') -CompilerPath $CompilerPath -SdkToolsPath $SdkToolsPath -SigningCertificatePath $SigningCertificatePath -NoTimestamp:$NoTimestamp -BuildRoot (Join-Path $lantossBuild 'explorer')
    & (Join-Path $lantossRelease 'source\build.ps1') -Mode Publish -DotnetPath $DotnetPath -BuildRoot $lantossBuild
    if ($LASTEXITCODE) { throw 'App publication failed.' }
}
if ((Get-Item (Join-Path $lantossRelease 'app\Lantoss.exe')).VersionInfo.FileVersion -ne '2.1.1.0') { throw 'Publish hopo 2.1.1 first.' }
foreach ($lantossPayload in @('LantossExplorer.dll','Lantoss.Explorer.msix','RegisterPackage.ps1','Assets\Logo.png','Assets\SmallLogo.png')) {
    if (-not (Test-Path -LiteralPath (Join-Path $lantossRelease ('app\'+$lantossPayload)))) { throw ('Build the Explorer integration first: '+$lantossPayload) }
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
$lantossMsi = Join-Path $lantossRelease 'hopo-Setup-2.1.1-x64.msi'
& $DotnetPath $lantossWix build (Join-Path $PSScriptRoot 'Lantoss.wxs') -arch x64 -ext $lantossUI -d ('ReleaseDir='+$lantossRelease) -out $lantossMsi -intermediatefolder (Join-Path $lantossBuild 'installer-objects') -pdbtype none -wx
if ($LASTEXITCODE) { throw 'MSI compilation or validation failed.' }
Get-FileHash -LiteralPath $lantossMsi -Algorithm SHA256
