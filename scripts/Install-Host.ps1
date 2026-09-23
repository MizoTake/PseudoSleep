#Requires -RunAsAdministrator
param([string]$Workspace = (Split-Path $PSScriptRoot -Parent), [string]$GpuName = '', [ValidateSet('nvenc','quicksync','amdvce','software')][string]$Encoder)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
Assert-InteractiveUser
$taskArtifacts = Join-Path $Workspace 'artifacts'
New-Item -ItemType Directory -Path $taskArtifacts -Force | Out-Null
Start-Transcript -Path (Join-Path $taskArtifacts 'host-install.log') -Append
try {
    $taskMsi = Join-Path $taskArtifacts 'downloads\Sunshine-Windows-AMD64-installer.msi'
    $taskAssets = Get-HostAssets
    foreach ($taskAsset in $taskAssets) { Assert-FileHash (Join-Path $taskArtifacts ('downloads\' + $taskAsset.Name)) $taskAsset.Hash }
    $taskVdd = Join-Path $taskArtifacts ('vdd-verified-' + [Guid]::NewGuid().ToString('N'))
    Expand-Archive -LiteralPath (Join-Path $taskArtifacts 'downloads\VDD.Control.25.7.23.zip') -DestinationPath $taskVdd
    $taskRebootRequired = $false
    foreach ($taskFile in @($taskMsi, (Join-Path $taskVdd 'Dependencies\devcon.exe'), (Join-Path $taskVdd 'SignedDrivers\x86\VDD\mttvdd.cat'), (Join-Path $taskVdd 'SignedDrivers\x86\VDD\MttVDD.dll'))) {
        if ((Get-AuthenticodeSignature -LiteralPath $taskFile).Status -ne 'Valid') { throw "Invalid Authenticode signature: $taskFile" }
    }
    if (!(Test-Path -LiteralPath (Join-Path $taskArtifacts 'preinstall-display-backup.json'))) { throw 'Capture the original display topology before installation.' }
    if (!(Get-Service -Name SunshineService -ErrorAction SilentlyContinue)) {
        $taskInstall = Start-Process msiexec.exe -ArgumentList @('/i', ('"' + $taskMsi + '"'), '/qn', '/norestart', '/L*v', ('"' + (Join-Path $taskArtifacts 'sunshine-msi.log') + '"')) -WindowStyle Hidden -Wait -PassThru
        if ($taskInstall.ExitCode -notin @(0, 3010)) { throw "Sunshine MSI exit code: $($taskInstall.ExitCode)" }
        if ($taskInstall.ExitCode -eq 3010) { $taskRebootRequired = $true }
    }
    $taskDriver = @(Get-PnpDevice -Class Display -PresentOnly -ErrorAction SilentlyContinue | Where-Object { (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_HardwareIds -ErrorAction SilentlyContinue).Data -contains 'Root\MttVDD' })
    if ($taskDriver.Count -gt 1) { throw 'Multiple Virtual Display Drivers are present; select and repair the intended installation manually.' }
    if (!$taskDriver) {
        $taskDriverDirectory = 'C:\VirtualDisplayDriver'
        New-Item -ItemType Directory -Path $taskDriverDirectory -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $taskVdd 'SignedDrivers\x86\VDD\MttVDD.inf'), (Join-Path $taskVdd 'SignedDrivers\x86\VDD\MttVDD.dll'), (Join-Path $taskVdd 'SignedDrivers\x86\VDD\mttvdd.cat') -Destination $taskDriverDirectory
        $taskSettings = Join-Path $taskDriverDirectory 'vdd_settings.xml'
        $taskSettingsSource = if (Test-Path -LiteralPath $taskSettings) { Copy-Item -LiteralPath $taskSettings -Destination (Get-BackupPath $taskSettings); $taskSettings } else { Join-Path $taskVdd 'Dependencies\vdd_settings.xml' }
        $taskXml = New-Object System.Xml.XmlDocument
        $taskXml.PreserveWhitespace = $true
        $taskXml.Load($taskSettingsSource)
        if ($GpuName) { $taskXml.vdd_settings.gpu.friendlyname = $GpuName }
        $taskResolution = @($taskXml.vdd_settings.resolutions.resolution | Where-Object { $_.width -eq '2560' -and $_.height -eq '1600' })
        if ($taskResolution.Count -eq 0) {
            $taskResolutionNode = $taskXml.CreateElement('resolution')
            foreach ($taskValue in @(@('width','2560'), @('height','1600'), @('refresh_rate','120'))) { $taskNode = $taskXml.CreateElement($taskValue[0]); $taskNode.InnerText = $taskValue[1]; $taskResolutionNode.AppendChild($taskNode) | Out-Null }
            $taskXml.vdd_settings.resolutions.AppendChild($taskResolutionNode) | Out-Null
        } elseif (@($taskResolution[0].refresh_rate) -notcontains '120') { $taskNode = $taskXml.CreateElement('refresh_rate'); $taskNode.InnerText = '120'; $taskResolution[0].AppendChild($taskNode) | Out-Null }
        Write-Utf8File $taskSettings $taskXml.OuterXml
        & (Join-Path $taskVdd 'Dependencies\devcon.exe') install (Join-Path $taskDriverDirectory 'MttVDD.inf') 'Root\MttVDD'
        if ($LASTEXITCODE -notin @(0, 1)) { throw "VDD installation failed: $LASTEXITCODE" }
        if ($LASTEXITCODE -eq 1) { $taskRebootRequired = $true }
    } elseif ($GpuName) {
        throw 'A VDD installation already exists. -GpuName applies only to a new driver installation; edit its backed-up configuration separately.'
    }
    $taskSunshineConfig = Join-Path $env:ProgramFiles 'Sunshine\config\sunshine.conf'
    if (Test-Path -LiteralPath $taskSunshineConfig) { Copy-Item -LiteralPath $taskSunshineConfig -Destination (Get-BackupPath $taskSunshineConfig); $taskText = [IO.File]::ReadAllText($taskSunshineConfig) } else { $taskText = '' }
    $taskSettingsMap = [ordered]@{ upnp='disabled'; origin_web_ui_allowed='pc'; dd_configuration_option='disabled'; dd_resolution_option='disabled'; dd_refresh_rate_option='disabled'; dd_hdr_option='disabled' }
    if ($Encoder) { $taskSettingsMap['encoder'] = $Encoder }
    Write-Utf8File $taskSunshineConfig (Set-SunshineSettings $taskText $taskSettingsMap)
    Set-Service -Name SunshineService -StartupType Automatic
    Restart-Service -Name SunshineService
    Get-Service SunshineService | Format-List Name,Status,StartType
    Get-PnpDevice -Class Display -PresentOnly | Format-Table FriendlyName,Status,InstanceId
    $taskPresentDriver = @(Get-PnpDevice -Class Display -PresentOnly | Where-Object { (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_HardwareIds -ErrorAction SilentlyContinue).Data -contains 'Root\MttVDD' })
    if (!$taskRebootRequired -and ($taskPresentDriver.Count -ne 1 -or $taskPresentDriver[0].Status -ne 'OK')) { throw 'VDD did not become ready. Inspect Device Manager before continuing.' }
    [IO.File]::WriteAllText((Join-Path $taskArtifacts 'host-install-result.json'), (ConvertTo-Json @{success=$true; rebootRequired=$taskRebootRequired} -Compress), [Text.UTF8Encoding]::new($false))
    if ($taskRebootRequired) { Write-Warning 'Installation requires a Windows restart. Restart before initializing PseudoSleep or testing streaming.' }
} catch {
    [IO.File]::WriteAllText((Join-Path $taskArtifacts 'host-install-result.json'), ('{"success":false,"error":' + (ConvertTo-Json $_.Exception.Message -Compress) + '}'), [Text.UTF8Encoding]::new($false))
    throw
} finally { Stop-Transcript }
