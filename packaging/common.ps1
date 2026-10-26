# Shared helpers for build-macos.ps1 and build-linux.ps1. Dot-source this file; it does nothing on its own.
$ErrorActionPreference = 'Stop'

$LantossPackaging = $PSScriptRoot
$LantossRepo = Split-Path -Parent $PSScriptRoot
$LantossProject = Join-Path $LantossRepo 'source\Desktop\Lantoss.Desktop.csproj'
$LantossNuGetConfig = Join-Path $LantossRepo 'source\NuGet.Config'
$LantossAssets = Join-Path $LantossRepo 'source\Desktop\Assets'
$LantossWork = Join-Path $LantossRepo 'work\pack'
$LantossArtifacts = Join-Path $LantossRepo 'work\pack-artifacts'
$LantossDist = Join-Path $LantossRepo 'dist'
$LantossPackJs = Join-Path $PSScriptRoot 'pack.cjs'
$LantossVerifyJs = Join-Path $PSScriptRoot 'verify.cjs'
$LantossIconSizes = @(16, 32, 48, 64, 128, 256, 512, 1024)
# rcodesign (apple-codesign, open source) seals and ad-hoc signs the macOS bundle on Windows. Build-only tool, pinned by hash.
$LantossRcodesignUrl = 'https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign/0.29.0/apple-codesign-0.29.0-x86_64-pc-windows-msvc.zip'
$LantossRcodesignSha256 = '54BB500E2DA7A8DE02FCAE0F331D1CAC6E6D7173B4281042FF9C528BA3159AAA'

function Invoke-LantossNative {
    param([Parameter(Mandatory = $true)][string]$FilePath, [string[]]$ArgumentList = @())
    # Tools such as rcodesign log progress to stderr; Windows PowerShell would treat that as an error, so only the exit code decides.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $FilePath @ArgumentList 2>&1 | ForEach-Object { "$_" } | Out-Host } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw "$FilePath $($ArgumentList[0]) failed with exit code $LASTEXITCODE" }
}

function Assert-LantossTools {
    foreach ($tool in @('dotnet', 'node')) {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool was not found on PATH" }
    }
}

function Get-LantossVersion {
    $text = [System.IO.File]::ReadAllText($LantossProject)
    if ($text -notmatch '<Version>\s*([^<\s]+)\s*</Version>') { throw "No <Version> found in $LantossProject" }
    $Matches[1]
}

# Renders source/Desktop/Assets/lantoss-<size>.png when any size is missing, or always with -Force.
function Initialize-LantossIcons {
    param([switch]$Force)
    $missing = @($LantossIconSizes | Where-Object { -not (Test-Path (Join-Path $LantossAssets "lantoss-$_.png")) })
    if ($Force -or $missing.Count -gt 0) {
        & (Join-Path $LantossPackaging 'create-icons.ps1') -OutputDir $LantossAssets -Sizes $LantossIconSizes | Out-Null
        Write-Host "Rendered icons into $LantossAssets"
    }
}

function Get-LantossRcodesign {
    $tools = Join-Path $LantossRepo 'work\pack-tools'
    $zip = Join-Path $tools 'rcodesign.zip'
    $exe = Join-Path $tools 'rcodesign\apple-codesign-0.29.0-x86_64-pc-windows-msvc\rcodesign.exe'
    New-Item -ItemType Directory -Force -Path $tools | Out-Null
    if (-not (Test-Path $zip)) { Invoke-WebRequest -Uri $LantossRcodesignUrl -OutFile $zip }
    if ((Get-FileHash -Algorithm SHA256 -Path $zip).Hash -ne $LantossRcodesignSha256) { Remove-Item -Force $zip; throw 'rcodesign download does not match the pinned SHA-256' }
    if (-not (Test-Path $exe)) { Expand-Archive -Path $zip -DestinationPath (Join-Path $tools 'rcodesign') -Force }
    $exe
}

function Reset-LantossDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (Test-Path $Path) { Remove-Item -Recurse -Force $Path }
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

# Self-contained folder publish into work/pack/<rid>/publish, isolated from other builds by work/pack-artifacts.
function Publish-Lantoss {
    param([Parameter(Mandatory = $true)][string]$Rid, [switch]$Skip, [string[]]$Extra = @())
    $publish = Join-Path $LantossWork "$Rid\publish"
    if ($Skip) {
        if (-not (Test-Path (Join-Path $publish 'Lantoss'))) { throw "No earlier publish in $publish. Run without -SkipPublish." }
        return $publish
    }
    if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    Write-Host "Publishing $Rid"
    $arguments = @('publish', $LantossProject, '-c', 'Release', '-r', $Rid, '--self-contained', 'true',
        '--artifacts-path', $LantossArtifacts, "-p:RestoreConfigFile=$LantossNuGetConfig", '-o', $publish, '--nologo') + $Extra
    Invoke-LantossNative dotnet $arguments
    if (-not (Test-Path (Join-Path $publish 'Lantoss'))) { throw "dotnet publish did not produce $publish\Lantoss" }
    $publish
}

# Copies a template with @NAME@ placeholders replaced, LF line endings and UTF-8 without BOM.
function Expand-LantossTemplate {
    param([Parameter(Mandatory = $true)][string]$Template, [Parameter(Mandatory = $true)][string]$Destination, [hashtable]$Values = @{})
    $text = [System.IO.File]::ReadAllText($Template) -replace "`r`n", "`n"
    foreach ($key in $Values.Keys) { $text = $text.Replace("@$key@", [string]$Values[$key]) }
    if ($text -match '@[A-Z_]+@') { throw "Unexpanded placeholder $($Matches[0]) in $Template" }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    [System.IO.File]::WriteAllText($Destination, $text, (New-Object System.Text.UTF8Encoding($false)))
}

# Cross-checks an archive with Windows tar.exe (bsdtar), an independent reader of zip, tar.gz and ar.
function Test-LantossArchiveWithTar {
    param([Parameter(Mandatory = $true)][string]$Archive)
    $tar = Join-Path $env:SystemRoot 'System32\tar.exe'
    if (-not (Test-Path $tar)) { return }
    $listing = @(& $tar -tf $Archive)
    if ($LASTEXITCODE -ne 0) { throw "tar.exe could not read $Archive" }
    Write-Host "tar.exe reads $($listing.Count) entries from $(Split-Path -Leaf $Archive)"
}

function Write-LantossSummary {
    param([string[]]$Files)
    Write-Host ''
    foreach ($file in $Files) {
        $item = Get-Item $file
        $hash = (Get-FileHash -Algorithm SHA256 -Path $file).Hash.ToLowerInvariant()
        Write-Host ([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, '{0}  {1} bytes ({2:0.0} MiB)  SHA-256 {3}', $item.Name, $item.Length, ($item.Length / 1MB), $hash))
    }
}
