param(
    [ValidateSet('Core','Test','TestLarge','Build','Publish','Format','CheckFormat')][string]$Mode = 'Build',
    [string]$DotnetPath = 'dotnet',
    [string]$BuildRoot = '',
    [string]$LargeSource = ''
)
$ErrorActionPreference = 'Stop'
$lantossSourceRoot = $PSScriptRoot
$lantossReleaseRoot = Split-Path -Parent $lantossSourceRoot
if (-not $BuildRoot) { $BuildRoot = Join-Path $lantossReleaseRoot 'work' }
$lantossBuildRoot = [System.IO.Path]::GetFullPath($BuildRoot)
New-Item -ItemType Directory -Force -Path $lantossBuildRoot | Out-Null
$lantossConfig = Join-Path $lantossSourceRoot 'NuGet.Config'
$lantossArtifacts = Join-Path $lantossBuildRoot 'artifacts'
$lantossOldEnvironment = @{}
$lantossBuildEnvironment = @{
    DOTNET_CLI_HOME = (Join-Path $lantossBuildRoot 'dotnet-home')
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
    DOTNET_NOLOGO = '1'
    NUGET_PACKAGES = (Join-Path $lantossBuildRoot 'packages')
    NUGET_HTTP_CACHE_PATH = (Join-Path $lantossBuildRoot 'nuget-cache')
    APPDATA = (Join-Path $lantossBuildRoot 'build-appdata')
}
foreach ($lantossKey in $lantossBuildEnvironment.Keys) {
    $lantossOldEnvironment[$lantossKey] = [Environment]::GetEnvironmentVariable($lantossKey, 'Process')
    [Environment]::SetEnvironmentVariable($lantossKey, $lantossBuildEnvironment[$lantossKey], 'Process')
}
function Invoke-LantossDotnet {
    param([string[]]$LantossArguments)
    & $DotnetPath @LantossArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE." }
}
try {
    $lantossSharedArgs = @('--artifacts-path', $lantossArtifacts, "-p:RestoreConfigFile=$lantossConfig", '--nologo', '-c', 'Release')
    if ($Mode -in @('Format','CheckFormat')) {
        $lantossFormatArgs = @('format', 'whitespace', $lantossSourceRoot, '--folder')
        if ($Mode -eq 'CheckFormat') { $lantossFormatArgs += '--verify-no-changes' }
        Invoke-LantossDotnet $lantossFormatArgs
    }
    elseif ($Mode -eq 'Core') { Invoke-LantossDotnet (@('build', (Join-Path $lantossSourceRoot 'Core\Lantoss.Core.csproj')) + $lantossSharedArgs) }
    elseif ($Mode -in @('Test','TestLarge')) {
        Invoke-LantossDotnet (@('build', (Join-Path $lantossSourceRoot 'tests\Lantoss.Tests.csproj')) + $lantossSharedArgs)
        $lantossTestRoot = Join-Path $lantossBuildRoot ('verification-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        $lantossTestArgs = @((Join-Path $lantossArtifacts 'bin\Lantoss.Tests\release\Lantoss.Tests.dll'), $lantossTestRoot)
        if ($Mode -eq 'TestLarge') { $lantossTestArgs += '--large' }
        if ($LargeSource) { $lantossTestArgs += @('--large-source', [System.IO.Path]::GetFullPath($LargeSource)) }
        Invoke-LantossDotnet $lantossTestArgs
    }
    elseif ($Mode -eq 'Publish') {
        Invoke-LantossDotnet (@('publish', (Join-Path $lantossSourceRoot 'UI\Lantoss.Windows.csproj'), '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true', '-p:DebugType=None', '-o', (Join-Path $lantossReleaseRoot 'app')) + $lantossSharedArgs)
    }
    else { Invoke-LantossDotnet (@('build', (Join-Path $lantossSourceRoot 'UI\Lantoss.Windows.csproj')) + $lantossSharedArgs) }
}
finally {
    foreach ($lantossKey in $lantossOldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($lantossKey, $lantossOldEnvironment[$lantossKey], 'Process') }
}
