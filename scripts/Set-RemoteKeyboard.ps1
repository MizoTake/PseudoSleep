#Requires -RunAsAdministrator
param([ValidateSet('JisIme','Standard')][string]$Mode = 'JisIme', [ValidateSet('HalfWidthFullWidth','CapsLock')][string]$Key = 'HalfWidthFullWidth', [string]$ConfigPath = (Join-Path $env:APPDATA 'PseudoSleep\config.json'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
Assert-InteractiveUser
$taskConfig = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
$taskSunshineConfig = $taskConfig.sunshine.configPath
$taskText = [IO.File]::ReadAllText($taskSunshineConfig)
$taskUpdated = Set-SunshineImeKey $taskText $Mode $Key
if ($taskUpdated -cne $taskText) {
    $taskBackup = Get-BackupPath $taskSunshineConfig
    Copy-Item -LiteralPath $taskSunshineConfig -Destination $taskBackup
    Write-Utf8File $taskSunshineConfig $taskUpdated
    Write-Output ('Sunshine configuration backup: ' + $taskBackup)
}
Restart-Service -Name $taskConfig.sunshine.serviceName
Write-Output ('Remote keyboard mapping applied: ' + $Key + ' = ' + $Mode + '. Reconnect Moonlight to test IME on/off. Host keyboard layout was not changed.')
