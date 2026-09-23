#Requires -RunAsAdministrator
param([Parameter(Mandatory=$true)][ValidateSet('Disabled','Enabled')][string]$Mode, [ValidateRange(1,60000)][int]$DelayMilliseconds = 500, [string]$ConfigPath = (Join-Path $env:APPDATA 'PseudoSleep\config.json'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
Assert-InteractiveUser
$taskConfig = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
$taskSunshineConfig = $taskConfig.sunshine.configPath
$taskText = [IO.File]::ReadAllText($taskSunshineConfig)
$taskUpdated = Set-SunshineKeyRepeat $taskText $Mode $DelayMilliseconds
if ($taskUpdated -cne $taskText) {
    $taskBackup = Get-BackupPath $taskSunshineConfig
    Copy-Item -LiteralPath $taskSunshineConfig -Destination $taskBackup
    Write-Utf8File $taskSunshineConfig $taskUpdated
    Write-Output ('Sunshine configuration backup: ' + $taskBackup)
}
Restart-Service -Name $taskConfig.sunshine.serviceName
if ($Mode -eq 'Disabled') { Write-Output 'Sunshine-generated repeat is disabled for ALL remote keys, including letters, arrows, and Backspace. This contains missing-release repeats; it does not repair the client input events. Physical keyboard repeat is unchanged.' }
else { Write-Output ('Sunshine-generated repeat is enabled with a delay of ' + $DelayMilliseconds + 'ms. Missing client key releases can cause repeated input again.') }
Write-Output 'Sunshine was restarted. Reconnect Moonlight.'
