param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
if (!$SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') -Publish }
foreach ($taskFile in @('PseudoSleep.exe','PseudoSleep.dll','PseudoSleep.Core.dll','PseudoSleep.deps.json','PseudoSleep.runtimeconfig.json')) { if (!(Test-Path -LiteralPath (Join-Path $taskRoot ('artifacts\publish\' + $taskFile)))) { throw "Missing publish output: $taskFile. Run Build.ps1 -Publish first." } }
$taskDestination = Join-Path $env:LOCALAPPDATA 'Programs\PseudoSleep'
New-Item -ItemType Directory -Path $taskDestination -Force | Out-Null
$taskExe = Join-Path $taskDestination 'PseudoSleep.exe'
if (Test-Path -LiteralPath $taskExe) {
    & $taskExe exit | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Existing app did not restore displays; installation stopped.' }
    Wait-AppExit $taskExe
}
Copy-Item -Path (Join-Path $taskRoot 'artifacts\publish\*') -Destination $taskDestination -Force -Recurse
$taskShell = New-Object -ComObject WScript.Shell
$taskStartup = [Environment]::GetFolderPath('Startup')
$taskShortcut = $taskShell.CreateShortcut((Join-Path $taskStartup 'PseudoSleep.lnk'))
$taskShortcut.TargetPath = $taskExe
$taskShortcut.WorkingDirectory = $taskDestination
$taskShortcut.Save()
$taskMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'PseudoSleep'
New-Item -ItemType Directory -Path $taskMenu -Force | Out-Null
foreach ($taskEntry in @(@('PseudoSleep',''), @('Restore displays','wake --force'), @('Settings','settings'))) {
    $taskShortcut = $taskShell.CreateShortcut((Join-Path $taskMenu ($taskEntry[0] + '.lnk')))
    $taskShortcut.TargetPath = $taskExe
    $taskShortcut.Arguments = $taskEntry[1]
    $taskShortcut.WorkingDirectory = $taskDestination
    $taskShortcut.Save()
}
Start-Process -FilePath $taskExe -WindowStyle Hidden
Write-Output $taskExe
