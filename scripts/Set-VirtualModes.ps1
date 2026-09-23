#Requires -RunAsAdministrator
param([Parameter(Mandatory=$true)][ValidateRange(640,16384)][int]$Width, [Parameter(Mandatory=$true)][ValidateRange(480,16384)][int]$Height, [ValidateRange(24,1000)][int]$RefreshRate = 120, [string]$DriverSettingsPath = 'C:\VirtualDisplayDriver\vdd_settings.xml')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
Assert-InteractiveUser
$taskExe = Join-Path $env:LOCALAPPDATA 'Programs\PseudoSleep\PseudoSleep.exe'
$taskStatus = & $taskExe status | Out-String | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or !$taskStatus.success -or $taskStatus.data.state -ne 'Normal') { throw 'Restore the normal display configuration before adding a driver mode.' }
$taskDrivers = @(Get-PnpDevice -Class Display -PresentOnly | Where-Object { (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_HardwareIds -ErrorAction SilentlyContinue).Data -contains 'Root\MttVDD' })
if ($taskDrivers.Count -ne 1) { throw 'Cannot uniquely identify Virtual Display Driver.' }
$taskSettings = $DriverSettingsPath
$taskXml = New-Object System.Xml.XmlDocument
$taskXml.PreserveWhitespace = $true
$taskXml.Load($taskSettings)
$taskBackup = Join-Path (Split-Path $PSScriptRoot -Parent) ('artifacts\display-before-mode-' + (Get-Date -Format 'yyyyMMddHHmmss') + '.json')
& $taskExe backup $taskBackup | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Display backup failed.' }
& $taskExe exit | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot stop tray app.' }
Wait-AppExit $taskExe
Copy-Item -LiteralPath $taskSettings -Destination (Get-BackupPath $taskSettings)
$taskExisting = @($taskXml.vdd_settings.resolutions.resolution | Where-Object { [int]$_.width -eq $Width -and [int]$_.height -eq $Height })
if ($taskExisting.Count -eq 0) {
    $taskResolution = $taskXml.CreateElement('resolution')
    foreach ($taskPair in @(@('width', $Width), @('height', $Height), @('refresh_rate', $RefreshRate))) { $taskNode = $taskXml.CreateElement($taskPair[0]); $taskNode.InnerText = [string]$taskPair[1]; $taskResolution.AppendChild($taskNode) | Out-Null }
    $taskXml.vdd_settings.resolutions.AppendChild($taskResolution) | Out-Null
} elseif (@($taskExisting[0].refresh_rate) -notcontains [string]$RefreshRate) {
    $taskNode = $taskXml.CreateElement('refresh_rate'); $taskNode.InnerText = [string]$RefreshRate; $taskExisting[0].AppendChild($taskNode) | Out-Null
}
try {
    Write-Utf8File $taskSettings $taskXml.OuterXml
    pnputil.exe /restart-device $taskDrivers[0].InstanceId
    if ($LASTEXITCODE -ne 0) { throw "Driver restart returned $LASTEXITCODE" }
    Start-Sleep -Seconds 2
} finally {
    & $taskExe restore-backup $taskBackup | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Restore failed. Saved topology: $taskBackup" }
}
Write-Output "Added $Width x $Height @ $RefreshRate. Start PseudoSleep from the normal user session."
