param([Parameter(Mandatory=$true)][ValidateSet('Enabled','Disabled')][string]$Mode)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script as administrator using the signed-in desktop account.' }
Assert-InteractiveUser
$taskExe = Join-Path $env:LOCALAPPDATA 'Programs\PseudoSleep\PseudoSleep.exe'
$taskConfigPath = Join-Path $env:APPDATA 'PseudoSleep\config.json'
$taskStatus = & $taskExe status | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or !$taskStatus.success -or !$taskStatus.data.PSObject.Properties['remoteImeBridgeEnabled']) { throw 'Install the current PseudoSleep build and start the tray app first.' }
if ($taskStatus.data.state -ne 'Normal' -or $taskStatus.data.streamMonitored) { throw 'Disconnect Moonlight and restore physical displays before changing the IME bridge.' }
$taskAppOriginal = [IO.File]::ReadAllText($taskConfigPath)
$taskConfig = $taskAppOriginal | ConvertFrom-Json
$taskSunshinePath = $taskConfig.sunshine.configPath
$taskSunshineOriginal = [IO.File]::ReadAllText($taskSunshinePath)
$taskUpdated = Set-SunshineImeBridge $taskSunshineOriginal $Mode
$taskConfig | Add-Member -NotePropertyName enableRemoteImeBridge -NotePropertyValue ($Mode -eq 'Enabled') -Force
Copy-Item -LiteralPath $taskConfigPath -Destination (Get-BackupPath $taskConfigPath)
Copy-Item -LiteralPath $taskSunshinePath -Destination (Get-BackupPath $taskSunshinePath)
try {
    Write-Utf8File $taskSunshinePath $taskUpdated
    Write-Utf8File $taskConfigPath (ConvertTo-Json $taskConfig -Depth 100)
    $taskApplied = & $taskExe apply-settings | Out-String | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or !$taskApplied.success) { throw 'PseudoSleep could not reload the IME setting.' }
    Restart-Service -Name $taskConfig.sunshine.serviceName
    (Get-Service -Name $taskConfig.sunshine.serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
} catch {
    Write-Utf8File $taskSunshinePath $taskSunshineOriginal
    Write-Utf8File $taskConfigPath $taskAppOriginal
    & $taskExe apply-settings | Out-Null
    Restart-Service -Name $taskConfig.sunshine.serviceName
    throw
}
Write-Output "IME bridge: $Mode. Existing key-repeat settings were preserved. F13-F15 are reserved for the bridge while enabled."
