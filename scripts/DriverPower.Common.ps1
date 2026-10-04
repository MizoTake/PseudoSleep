function Get-DriverPowerTaskName([string]$InstanceId, [bool]$Enabled) {
    $taskHash = [Security.Cryptography.SHA256]::Create()
    try { $taskDigest = ([BitConverter]::ToString($taskHash.ComputeHash([Text.Encoding]::UTF8.GetBytes($InstanceId.ToUpperInvariant())))).Replace('-', '').Substring(0, 16) } finally { $taskHash.Dispose() }
    return 'PseudoSleep-VDD-' + $taskDigest + $(if ($Enabled) { '-Enable' } else { '-Disable' })
}

function Get-DriverPowerCommand([string]$InstanceId, [bool]$Enabled) {
    if ($InstanceId -notmatch '\AROOT\\DISPLAY\\[A-Za-z0-9_&-]+\z') { throw 'Unsupported display driver instance ID.' }
    $taskCommand = @'
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\Modules\PnpDevice\PnpDevice.psd1') -ErrorAction Stop
$taskId = '__INSTANCE_ID__'
$taskHardware = (Get-PnpDeviceProperty -InstanceId $taskId -KeyName DEVPKEY_Device_HardwareIds -ErrorAction Stop).Data
$taskClass = (Get-PnpDeviceProperty -InstanceId $taskId -KeyName DEVPKEY_Device_ClassGuid -ErrorAction Stop).Data
if ($taskHardware -notcontains 'Root\MttVDD' -or [string]$taskClass -ne '{4d36e968-e325-11ce-bfc1-08002be10318}') { throw 'The selected device is not a verified Virtual Display Driver.' }
__STATE_COMMAND__ -InstanceId $taskId -Confirm:$false -ErrorAction Stop
'@
    return $taskCommand.Replace('__INSTANCE_ID__', $InstanceId).Replace('__STATE_COMMAND__', $(if ($Enabled) { 'Enable-PnpDevice' } else { 'Disable-PnpDevice' }))
}

function Set-DriverPowerState([string]$InstanceId, [bool]$Enabled) {
    & ([scriptblock]::Create((Get-DriverPowerCommand $InstanceId $Enabled)))
}

function New-DriverPowerTaskDefinition($Service, [string]$InstanceId, [bool]$Enabled) {
    $taskCommand = Get-DriverPowerCommand $InstanceId $Enabled
    $taskDefinition = $Service.NewTask(0)
    $taskDefinition.RegistrationInfo.Source = 'PseudoSleep.VirtualDisplayDriverPower'
    $taskDefinition.RegistrationInfo.Description = 'Explicitly requested power control for one verified virtual display driver. No automatic triggers.'
    $taskDefinition.Principal.UserId = 'SYSTEM'
    $taskDefinition.Principal.LogonType = 5
    $taskDefinition.Principal.RunLevel = 1
    $taskDefinition.Settings.Enabled = $true
    $taskDefinition.Settings.AllowDemandStart = $true
    $taskDefinition.Settings.DisallowStartIfOnBatteries = $false
    $taskDefinition.Settings.StopIfGoingOnBatteries = $false
    $taskDefinition.Settings.ExecutionTimeLimit = 'PT30S'
    $taskDefinition.Settings.MultipleInstances = 2
    $taskAction = $taskDefinition.Actions.Create(0)
    $taskAction.Path = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
    $taskAction.Arguments = '-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand ' + [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($taskCommand))
    return $taskDefinition
}
