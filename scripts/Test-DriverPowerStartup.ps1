$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
. (Join-Path $PSScriptRoot 'DriverPower.Common.ps1')
$taskExe = Join-Path $env:LOCALAPPDATA 'Programs\PseudoSleep\PseudoSleep.exe'
function Read-Status {
    $taskResponse = & $taskExe status | Out-String | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or !$taskResponse.success) { throw 'Cannot read resident status.' }
    return $taskResponse.data
}
$taskInitial = Read-Status
if (!$taskInitial.resident -or $taskInitial.state -ne 'Normal' -or !$taskInitial.virtualDisplayDriverInstanceId -or $taskInitial.virtualDisplayDriverEnabled -ne $false -or $taskInitial.streamMonitored -or $taskInitial.recoveryPending) { throw 'Run with on-demand driver power enabled and the resident in Normal state.' }
$taskService = New-Object -ComObject Schedule.Service
$taskService.Connect()
$taskEnable = $taskService.GetFolder('\').GetTask((Get-DriverPowerTaskName $taskInitial.virtualDisplayDriverInstanceId $true))
if ($taskEnable.Definition.Triggers.Count -ne 0) { throw 'Driver enable task has automatic triggers.' }
$taskLastRun = $taskEnable.LastRunTime
$taskDisplaysBefore = & $taskExe displays | Out-String | ConvertFrom-Json
$taskDisplayKeysBefore = @($taskDisplaysBefore | Where-Object active | Sort-Object devicePath | ForEach-Object { '{0}|{1}x{2}|{3},{4}|{5}' -f $_.devicePath,$_.width,$_.height,$_.x,$_.y,$_.refreshRate })
try {
    & $taskExe exit | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Exit failed.' }
    Wait-AppExit $taskExe
    # Some nodes omit DEVPKEY_Device_ProblemCode even when the PnP device reports a problem.
    $taskProblemWhileStopped = [int](Get-PnpDevice -InstanceId $taskInitial.virtualDisplayDriverInstanceId -ErrorAction Stop).ConfigManagerErrorCode
    if ($taskProblemWhileStopped -ne 22) { throw 'Driver is not disabled while the app is stopped.' }
} finally { Start-Process -FilePath $taskExe -WindowStyle Hidden }
Start-Sleep -Seconds 3
$taskFinal = Read-Status
if (!$taskFinal.resident -or $taskFinal.state -ne 'Normal' -or $taskFinal.virtualDisplay -or $taskFinal.virtualDisplayDriverEnabled -ne $false -or $taskFinal.physicalDisplays -ne $taskInitial.physicalDisplays) { throw 'Application startup changed the normal display policy.' }
$taskEnableAfter = $taskService.GetFolder('\').GetTask($taskEnable.Name)
if ($taskEnableAfter.LastRunTime -ne $taskLastRun) { throw 'Application startup ran the driver enable task.' }
$taskDisplaysAfter = & $taskExe displays | Out-String | ConvertFrom-Json
$taskDisplayKeysAfter = @($taskDisplaysAfter | Where-Object active | Sort-Object devicePath | ForEach-Object { '{0}|{1}x{2}|{3},{4}|{5}' -f $_.devicePath,$_.width,$_.height,$_.x,$_.y,$_.refreshRate })
if (($taskDisplayKeysBefore -join "`n") -ne ($taskDisplayKeysAfter -join "`n")) { throw 'Application startup changed physical display modes or positions.' }
$taskFlags = (Get-PnpDeviceProperty -InstanceId $taskInitial.virtualDisplayDriverInstanceId -KeyName DEVPKEY_Device_ConfigFlags).Data
if (($taskFlags -band 1) -eq 0) { throw 'Driver disable is not persisted for the next boot.' }
$taskEvidence = [ordered]@{success=$true; at=(Get-Date).ToString('o'); driverDisabledWhileAppStopped=$true; driverDisablePersisted=$true; driverEnableTaskWasNotRun=$true; physicalLayoutUnchanged=$true; final=$taskFinal; windowsRebootTested=$false}
Write-Utf8File (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\driver-power-startup-test.json') (ConvertTo-Json $taskEvidence -Depth 8)
$taskEvidence | ConvertTo-Json -Depth 8
