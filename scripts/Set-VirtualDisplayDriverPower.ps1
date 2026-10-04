#Requires -RunAsAdministrator
param([Parameter(Mandatory=$true)][ValidateSet('OnDemand','AlwaysAvailable')][string]$Mode)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
. (Join-Path $PSScriptRoot 'DriverPower.Common.ps1')
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskResultPath = Join-Path $taskRoot 'artifacts\driver-power-setup-result.json'
Start-Transcript -Path (Join-Path $taskRoot 'artifacts\driver-power-setup.log') -Append | Out-Null
trap { Write-Output ($_ | Out-String); Write-Utf8File $taskResultPath (ConvertTo-Json @{success=$false; error=$_.Exception.Message; backup=$taskBackupDirectory}); Stop-Transcript | Out-Null; exit 1 }
Assert-InteractiveUser
$taskConfigPath = Join-Path $env:APPDATA 'PseudoSleep\config.json'
$taskExe = Join-Path $env:LOCALAPPDATA 'Programs\PseudoSleep\PseudoSleep.exe'
$taskConfig = [IO.File]::ReadAllText($taskConfigPath) | ConvertFrom-Json
$taskStatus = & $taskExe status | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or !$taskStatus.success -or !$taskStatus.data.resident -or $taskStatus.data.state -ne 'Normal' -or $taskStatus.data.streamMonitored -or $taskStatus.data.recoveryPending -or !$taskStatus.data.PSObject.Properties['virtualDisplayDriverInstanceId']) { throw 'Install the current build and run the tray in Normal state with no stream or pending recovery first.' }
$taskDrivers = @(Get-PnpDevice -Class Display -PresentOnly | Where-Object { (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_HardwareIds -ErrorAction SilentlyContinue).Data -contains 'Root\MttVDD' })
if ($taskDrivers.Count -ne 1) { throw 'Cannot uniquely identify Virtual Display Driver.' }
$taskId = $taskDrivers[0].InstanceId
$taskMonitorParts = $taskConfig.virtualDisplayDevicePath.Substring(4).Split('#')
if ($taskMonitorParts.Count -lt 3) { throw 'The selected virtual display has an invalid device path.' }
$taskParent = (Get-PnpDeviceProperty -InstanceId ($taskMonitorParts[0..2] -join '\') -KeyName DEVPKEY_Device_Parent).Data
if ($taskParent -ne $taskId) { throw 'The selected display does not belong to the verified Virtual Display Driver.' }
$taskDisplays = & $taskExe displays | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or !@($taskDisplays | Where-Object { $_.active -and !$_.indirect -and $_.x -eq 0 -and $_.y -eq 0 }).Count) { throw 'A physical primary display must be active before changing driver power policy.' }
$taskBackupDirectory = Join-Path $taskRoot ('artifacts\driver-power-backup-' + (Get-Date -Format 'yyyyMMddHHmmssfff'))
New-Item -ItemType Directory -Path $taskBackupDirectory | Out-Null
Copy-Item -LiteralPath $taskConfigPath -Destination (Join-Path $taskBackupDirectory 'config.json')
& $taskExe backup (Join-Path $taskBackupDirectory 'displays.json') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Display backup failed.' }
$taskBeforeProblem = (Get-PnpDeviceProperty -InstanceId $taskId -KeyName DEVPKEY_Device_ProblemCode).Data
if ($taskBeforeProblem -notin @(0, 22)) { throw 'The driver must be running normally or explicitly disabled before setup.' }
$taskService = New-Object -ComObject Schedule.Service
$taskService.Connect()
$taskFolder = $taskService.GetFolder('\')
$taskSavedTasks = @{}
foreach ($taskEnabled in @($true, $false)) {
    $taskName = Get-DriverPowerTaskName $taskId $taskEnabled
    $taskExisting = $null
    try { $taskExisting = $taskFolder.GetTask($taskName) } catch { if ($_.Exception.HResult -ne -2147024894) { throw } }
    if ($taskExisting) {
        if ($taskExisting.Definition.RegistrationInfo.Source -ne 'PseudoSleep.VirtualDisplayDriverPower') { throw "Unrelated task already exists: $taskName" }
        $taskSavedTasks[$taskName] = @{ Xml=$taskExisting.Xml; Acl=$taskExisting.GetSecurityDescriptor(7) }
        Write-Utf8File (Join-Path $taskBackupDirectory ($taskName + '.xml')) $taskExisting.Xml
        Write-Utf8File (Join-Path $taskBackupDirectory ($taskName + '.sddl')) $taskSavedTasks[$taskName].Acl
    } else { $taskSavedTasks[$taskName] = $null }
}
& $taskExe exit | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot stop the tray with displays restored.' }
Wait-AppExit $taskExe
try {
    if ($Mode -eq 'OnDemand') {
        $taskSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $taskAcl = 'O:BAG:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;' + $taskSid + ')'
        foreach ($taskEnabled in @($true, $false)) {
            $taskDefinition = New-DriverPowerTaskDefinition $taskService $taskId $taskEnabled
            $null = $taskFolder.RegisterTaskDefinition((Get-DriverPowerTaskName $taskId $taskEnabled), $taskDefinition, 6, 'SYSTEM', $null, 5, $taskAcl)
        }
        $taskConfig | Add-Member -NotePropertyName virtualDisplayDriverInstanceId -NotePropertyValue $taskId -Force
        $taskConfig.keepVirtualDisplayInNormalMode = $false
        $taskConfig.sleepOnMoonlightConnect = $true
        $taskConfig.disconnectMoonlightOnWake = $true
        Set-DriverPowerState $taskId $false
    } else {
        Set-DriverPowerState $taskId $true
    }
    $taskExpectedProblem = if ($Mode -eq 'OnDemand') { 22 } else { 0 }
    $taskDeadline = (Get-Date).AddSeconds(10)
    do { $taskProblem = (Get-PnpDeviceProperty -InstanceId $taskId -KeyName DEVPKEY_Device_ProblemCode).Data; if ($taskProblem -eq $taskExpectedProblem) { break }; Start-Sleep -Milliseconds 100 } while ((Get-Date) -lt $taskDeadline)
    if ($taskProblem -ne $taskExpectedProblem) { throw 'Driver power state did not settle.' }
    if ($Mode -eq 'AlwaysAvailable') {
        $taskConfig | Add-Member -NotePropertyName virtualDisplayDriverInstanceId -NotePropertyValue '' -Force
        foreach ($taskName in $taskSavedTasks.Keys) { if ($taskSavedTasks[$taskName]) { $taskFolder.DeleteTask($taskName, 0) } }
    }
    Write-Utf8File $taskConfigPath (ConvertTo-Json $taskConfig -Depth 8)
    $taskResult = [ordered]@{success=$true; mode=$Mode; instanceId=$taskId; problemCode=$taskProblem; backup=$taskBackupDirectory; at=(Get-Date).ToString('o')}
    Write-Utf8File $taskResultPath (ConvertTo-Json $taskResult)
    $taskResult | ConvertTo-Json
    Write-Output 'Start PseudoSleep from the normal user session. No Windows restart was performed.'
} catch {
    $taskFailure = $_
    foreach ($taskName in $taskSavedTasks.Keys) {
        try { if ($taskSavedTasks[$taskName]) { $null = $taskFolder.RegisterTask($taskName, $taskSavedTasks[$taskName].Xml, 6, 'SYSTEM', $null, 5, $taskSavedTasks[$taskName].Acl) } else { $taskFolder.DeleteTask($taskName, 0) } } catch { Write-Warning $_ }
    }
    Copy-Item -LiteralPath (Join-Path $taskBackupDirectory 'config.json') -Destination $taskConfigPath -Force
    Set-DriverPowerState $taskId ($taskBeforeProblem -ne 22)
    & $taskExe restore-backup (Join-Path $taskBackupDirectory 'displays.json') | Out-Null
    Write-Utf8File $taskResultPath (ConvertTo-Json @{success=$false; error=$taskFailure.Exception.Message; backup=$taskBackupDirectory})
    throw $taskFailure
}
Stop-Transcript | Out-Null
