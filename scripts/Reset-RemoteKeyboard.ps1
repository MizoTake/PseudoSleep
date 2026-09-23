#Requires -RunAsAdministrator
param([string]$ConfigPath = (Join-Path $env:APPDATA 'PseudoSleep\config.json'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
Assert-InteractiveUser
$taskConfig = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
$taskSunshineConfig = $taskConfig.sunshine.configPath
$taskText = [IO.File]::ReadAllText($taskSunshineConfig)
$taskUpdated = Remove-SunshineImeKeybindings $taskText
if ($taskUpdated -cne $taskText) {
    $taskBackup = Get-BackupPath $taskSunshineConfig
    Copy-Item -LiteralPath $taskSunshineConfig -Destination $taskBackup
    Write-Utf8File $taskSunshineConfig $taskUpdated
    Write-Output ('Sunshine configuration backup: ' + $taskBackup)
}
Restart-Service -Name $taskConfig.sunshine.serviceName
Write-Output 'Removed the former Caps Lock and half/full-width IME toggle mappings. Sunshine was restarted to clear held-key state. Reconnect Moonlight. Host keyboard layout and ordinary key repeat were not changed.'
