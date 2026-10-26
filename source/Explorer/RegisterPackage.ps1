param([ValidateSet('Install','Remove')][string]$Mode, [Parameter(Mandatory)][string]$AppDirectory)
$ErrorActionPreference = 'Stop'
try {
    # Paths arrive as arguments, never interpolated into executable PowerShell text.
    $root = [IO.Path]::GetFullPath($AppDirectory)
    $registered = @(Get-AppxPackage -Name 'Lantoss.ExplorerIntegration' | Where-Object { $_.Publisher -eq 'CN=Lantoss Development' })
    if ($Mode -eq 'Remove') {
        foreach ($package in $registered) { Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop }
    } else {
        # Register in-place without tearing down a working prior package on error.
        if ($registered.Count -eq 1 -and $registered[0].Version -eq '1.9.0.0') {
            Add-AppxPackage -Register (Join-Path $registered[0].InstallLocation 'AppxManifest.xml') -DisableDevelopmentMode -ExternalLocation $root -ErrorAction Stop
        } else {
            Add-AppxPackage -Path (Join-Path $root 'Lantoss.Explorer.msix') -ExternalLocation $root -ErrorAction Stop
        }
    }
    exit 0
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
