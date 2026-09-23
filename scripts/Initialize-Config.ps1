param([string]$Executable = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\publish\PseudoSleep.exe'), [string]$VirtualDevicePath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
$taskConfigPath = Join-Path $env:APPDATA 'PseudoSleep\config.json'
& $Executable diagnose | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'PseudoSleep diagnostics failed.' }
$taskDisplays = (& $Executable displays | Out-String | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0) { throw 'Display enumeration failed.' }
$taskCandidates = @($taskDisplays | Where-Object { $_.indirect -and (!$VirtualDevicePath -or $_.devicePath -eq $VirtualDevicePath) })
if ($taskCandidates.Count -ne 1) { throw 'Pass -VirtualDevicePath from the displays command to select one virtual monitor.' }
$taskVirtual = $taskCandidates[0]
$taskConfig = Get-Content -LiteralPath $taskConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
$taskLogPath = Join-Path (Split-Path $taskConfig.sunshine.configPath -Parent) 'sunshine.log'
$taskLog = if (Test-Path -LiteralPath $taskLogPath) { Get-Content -LiteralPath $taskLogPath -Raw -Encoding UTF8 } else { '' }
$taskMatches = [regex]::Matches($taskLog, '(?s)Currently available display devices:\s*(\[.*?\r?\n\])')
$taskSunshineDisplay = @()
if ($taskMatches.Count -gt 0) { $taskLogDisplays = $taskMatches[$taskMatches.Count - 1].Groups[1].Value | ConvertFrom-Json; $taskSunshineDisplay = @($taskLogDisplays | Where-Object { $_.display_name -and $_.display_name -eq $taskVirtual.sourceName }) }
if ($taskConfig.keepVirtualDisplayInNormalMode -and ($taskSunshineDisplay.Count -ne 1 -or !$taskSunshineDisplay[0].device_id)) { throw 'The latest Sunshine display list does not uniquely identify the persistent virtual display. Restart Sunshine while that display is active.' }
$taskConfig.virtualDisplayDevicePath = $taskVirtual.devicePath
if ($taskSunshineDisplay.Count -eq 1 -and $taskSunshineDisplay[0].device_id) { $taskConfig.virtualDisplayDeviceId = $taskSunshineDisplay[0].device_id }
$taskInputs = (& $Executable devices | Out-String | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0) { throw 'Input enumeration failed.' }
$taskAllowed = @()
foreach ($taskInput in $taskInputs) {
    if (!$taskInput.path.StartsWith('\\?\HID#')) { continue }
    $taskInstance = ($taskInput.path.Substring(4).Split('#')[0..2] -join '\')
    for ($taskDepth = 0; $taskDepth -lt 8; $taskDepth++) {
        if ($taskInstance.StartsWith('USB\')) { $taskAllowed += $taskInput.path; break }
        $taskParent = (Get-PnpDeviceProperty -InstanceId $taskInstance -KeyName DEVPKEY_Device_Parent -ErrorAction SilentlyContinue).Data
        if (!$taskParent) { break }
        $taskInstance = $taskParent
    }
}
if ($taskConfig.wakeDevices.Count -eq 0) { $taskConfig.wakeDevices = $taskAllowed }
Copy-Item -LiteralPath $taskConfigPath -Destination (Get-BackupPath $taskConfigPath)
Write-Utf8File $taskConfigPath (ConvertTo-Json $taskConfig -Depth 100)
if ($taskConfig.wakeDevices.Count -eq 0) { Write-Warning 'No USB wake devices were found. Register physical input devices in Settings before entering pseudo-sleep.' }
Write-Output "Configured display $($taskConfig.virtualDisplayDeviceId), wake devices: $($taskConfig.wakeDevices.Count)"
