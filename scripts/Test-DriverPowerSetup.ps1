$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DriverPower.Common.ps1')
$taskService = New-Object -ComObject Schedule.Service
$taskService.Connect()
$taskId = 'ROOT\DISPLAY\0001'
foreach ($taskEnabled in @($true, $false)) {
    $taskDefinition = New-DriverPowerTaskDefinition $taskService $taskId $taskEnabled
    if ($taskDefinition.Triggers.Count -ne 0 -or $taskDefinition.Actions.Count -ne 1) { throw 'Driver task must have no automatic triggers and exactly one action.' }
    if ($taskDefinition.Principal.UserId -ne 'SYSTEM' -or $taskDefinition.Principal.LogonType -ne 5) { throw 'Driver task principal is invalid.' }
    $taskAction = $taskDefinition.Actions.Item(1)
    if ($taskAction.Path -ne (Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe') -or $taskAction.Arguments -notmatch '\A-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand ([A-Za-z0-9+/=]+)\z') { throw 'Driver task must invoke only Windows PowerShell with a fixed inline command and no profile.' }
    $taskCommand = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($Matches[1]))
    $taskVerb = if ($taskEnabled) { 'Enable-PnpDevice' } else { 'Disable-PnpDevice' }
    if (!$taskCommand.Contains("`$taskId = '$taskId'")) { throw 'Driver task does not pin the device instance ID.' }
    if (!$taskCommand.Contains($taskVerb + ' -InstanceId $taskId -Confirm:$false -ErrorAction Stop')) { throw 'Driver task does not use the PnP device state operation.' }
    if (!$taskCommand.Contains('Root\MttVDD') -or !$taskCommand.Contains('{4d36e968-e325-11ce-bfc1-08002be10318}')) { throw 'Driver task must verify hardware identity and display class before mutation.' }
    if (!$taskCommand.Contains("Import-Module (Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\Modules\PnpDevice\PnpDevice.psd1') -ErrorAction Stop") -or $taskCommand.Contains('-File ') -or $taskCommand.Contains('ExecutionPolicy')) { throw 'Driver task must use the inbox module and no user-writable script or policy override.' }
    if ((Get-DriverPowerTaskName $taskId $taskEnabled) -ne (Get-DriverPowerTaskName $taskId.ToLowerInvariant() $taskEnabled)) { throw 'Task identity is not case insensitive.' }
}
foreach ($taskInvalid in @('PCI\PHYSICAL\0001', 'ROOT\DISPLAY\*', 'ROOT\DISPLAY\0001" /enable-device other', 'ROOT\DISPLAY\..\other')) {
    $taskRejected = $false
    try { $null = New-DriverPowerTaskDefinition $taskService $taskInvalid $true } catch { $taskRejected = $true }
    if (!$taskRejected) { throw 'Invalid task device identifier was accepted.' }
}
function Import-Module { [CmdletBinding()] param($Name) }
function Get-PnpDeviceProperty {
    [CmdletBinding()] param([string]$InstanceId, [string]$KeyName)
    if ($InstanceId -ne $taskId) { throw 'Unexpected device requested.' }
    if ($KeyName -eq 'DEVPKEY_Device_HardwareIds') { return [pscustomobject]@{Data=$script:taskTestHardware} }
    if ($KeyName -eq 'DEVPKEY_Device_ClassGuid') { return [pscustomobject]@{Data=$script:taskTestClass} }
    throw 'Unexpected property requested.'
}
function Enable-PnpDevice { [CmdletBinding(SupportsShouldProcess)] param([string]$InstanceId) $script:taskRequests.Add('enable:' + $InstanceId) }
function Disable-PnpDevice { [CmdletBinding(SupportsShouldProcess)] param([string]$InstanceId) $script:taskRequests.Add('disable:' + $InstanceId) }
$taskRequests = [Collections.Generic.List[string]]::new()
$taskTestHardware = @('Root\MttVDD')
$taskTestClass = '{4d36e968-e325-11ce-bfc1-08002be10318}'
Set-DriverPowerState $taskId $true
Set-DriverPowerState $taskId $false
if (($taskRequests -join ',') -ne ('enable:' + $taskId + ',disable:' + $taskId)) { throw 'Driver commands did not invoke the expected PnP operations.' }
foreach ($taskMismatch in @('hardware','class')) {
    $taskRequests.Clear()
    $taskTestHardware = @('Root\MttVDD'); $taskTestClass = '{4d36e968-e325-11ce-bfc1-08002be10318}'
    if ($taskMismatch -eq 'hardware') { $taskTestHardware = @('PCI\PHYSICAL') } else { $taskTestClass = 'not-display' }
    $taskRejected = $false
    try { Set-DriverPowerState $taskId $true } catch { $taskRejected = $true }
    if (!$taskRejected -or $taskRequests.Count -ne 0) { throw 'Unverified device reached a PnP mutation.' }
}
Write-Output 'PASS driver power task definitions, trigger isolation, fixed inbox commands, identifier rejection and hardware/class guards (mocked device operations; no tasks registered).'
