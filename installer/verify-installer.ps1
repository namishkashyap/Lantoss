param([Parameter(Mandatory=$true)][string]$Path)
$ErrorActionPreference = 'Stop'
$lantossMsi = [IO.Path]::GetFullPath($Path)
$lantossInstaller = New-Object -ComObject WindowsInstaller.Installer
$lantossDatabase = $lantossInstaller.OpenDatabase($lantossMsi,0)
function Read-LantossTable([string]$query,[int]$columns) {
    $view = $lantossDatabase.OpenView($query)
    [void]$view.Execute()
    $rows = @()
    while ($record = $view.Fetch()) {
        $row = @()
        for ($index=1;$index -le $columns;$index++) { $row += $record.StringData($index) }
        $rows += ,$row
    }
    [void]$view.Close()
    return ,$rows
}
$props = Read-LantossTable 'SELECT `Property`,`Value` FROM `Property`' 2
$properties = @{}
foreach ($row in $props) { $properties[$row[0]]=$row[1] }
if ($properties['ProductVersion'] -ne '2.0.0' -or $properties['ProductName'] -ne 'Lantoss' -or $properties['ALLUSERS']) { throw 'Not the intended per-user package.' }
$features = Read-LantossTable 'SELECT `Feature`,`Level` FROM `Feature`' 2
foreach ($expected in @(@('Core','1'),@('Explorer','1'),@('Startup','2'),@('Desktop','2'))) {
    if (-not ($features | Where-Object { $_[0] -eq $expected[0] -and $_[1] -eq $expected[1] })) { throw ('Feature default mismatch: '+$expected[0]) }
}
$registry = Read-LantossTable 'SELECT `Root`,`Key`,`Name`,`Value` FROM `Registry`' 4
if ($registry | Where-Object { $_[0] -ne '1' }) { throw 'Registration must be current-user only.' }
if (-not ($registry | Where-Object { $_[1] -eq 'Software\Microsoft\Windows\CurrentVersion\Run' -and $_[2] -eq 'Lantoss' -and $_[3] -eq '"[#LantossExe]" --background' })) { throw 'Startup command mismatch.' }
if ($registry | Where-Object { $_[2] -eq 'MUIVerb' }) { throw 'Installer must not add legacy-only menu verbs.' }
$modern = Read-LantossTable 'SELECT `Action`,`Target` FROM `CustomAction`' 2
if (-not ($modern | Where-Object { $_[0] -eq 'RegisterExplorerPackage' -and $_[1] -eq '--install-integration' })) { throw 'Modern package registration is missing.' }
$shortcuts = Read-LantossTable 'SELECT `Name`,`Target`,`Arguments` FROM `Shortcut`' 3
if (-not ($shortcuts | Where-Object { $_[2] -eq '--background' })) { throw 'Missing sharing-server shortcut.' }
$sequence = Read-LantossTable 'SELECT `Action`,`Condition`,`Sequence` FROM `InstallExecuteSequence`' 3
$cleanup = $sequence | Where-Object { $_[0] -eq 'RemoveUserIntegration' }
$remove = $sequence | Where-Object { $_[0] -eq 'RemoveFiles' }
if (-not $cleanup -or [int]$cleanup[2] -ge [int]$remove[2] -or $cleanup[1] -notmatch 'REMOVE = "ALL"') { throw 'Uninstall cleanup sequencing mismatch.' }
$events = Read-LantossTable 'SELECT `Dialog_`,`Control_`,`Event`,`Argument`,`Ordering` FROM `ControlEvent`' 5
if (-not ($events | Where-Object { $_[0] -eq 'WelcomeDlg' -and $_[1] -eq 'Next' -and $_[3] -eq 'CustomizeDlg' -and $_[4] -eq '5' })) { throw 'Installer feature-selection navigation missing.' }
$files = Read-LantossTable 'SELECT `File`,`FileSize` FROM `File`' 2
if (-not ($files | Where-Object { $_[0] -eq 'LantossExe' -and [long]$_[1] -gt 10000000 })) { throw 'Missing standalone app payload.' }
[pscustomobject]@{Product=$properties['ProductName'];Version=$properties['ProductVersion'];CurrentUserOnly=$true;Features=$features.Count;Files=$files.Count;Shortcuts=$shortcuts.Count;RegistryRows=$registry.Count;UninstallCleanupBeforeRemoveFiles=$true;Passed=$true} | ConvertTo-Json
