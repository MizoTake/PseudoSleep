#Requires -RunAsAdministrator
param([string]$ConfigPath = (Join-Path $env:APPDATA 'PseudoSleep\config.json'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
Assert-InteractiveUser
$taskConfig = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
$taskSunshineConfig = $taskConfig.sunshine.configPath
$taskText = [IO.File]::ReadAllText($taskSunshineConfig)
$taskPrep = @(Get-SunshinePrep $taskText)
if (@($taskPrep | Where-Object { Test-PseudoSleepPrep $_ }).Count -eq 0) { Write-Output 'No PseudoSleep hook is configured. No changes were made.'; return }
$taskRemaining = @(Get-OtherSunshinePrep $taskText)
$taskUpdated = Set-SunshineSettings $taskText ([ordered]@{ global_prep_cmd=(ConvertTo-Json -InputObject $taskRemaining -Depth 100 -Compress) })
$taskBackup = Get-BackupPath $taskSunshineConfig
Copy-Item -LiteralPath $taskSunshineConfig -Destination $taskBackup
Write-Utf8File $taskSunshineConfig $taskUpdated
Restart-Service -Name $taskConfig.sunshine.serviceName
Write-Output "Removed only PseudoSleep startup hooks. Other settings and commands, including the selected virtual output, are preserved. Backup: $taskBackup"
Write-Output 'Run Uninstall-App.ps1 from the normal user session next. Sunshine/VDD and previously granted query/start/stop permissions remain installed. To use a physical output, choose it in the Sunshine Web UI.'
