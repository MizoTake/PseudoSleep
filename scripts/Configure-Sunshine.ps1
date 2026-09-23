#Requires -RunAsAdministrator
param([string]$ConfigPath = (Join-Path $env:APPDATA 'PseudoSleep\config.json'), [string]$VirtualAudioSink = '', [switch]$AllowServiceRestart, [ValidateSet('nvenc','quicksync','amdvce','software')][string]$Encoder)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
Assert-InteractiveUser
$taskConfig = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($taskConfig.keepVirtualDisplayInNormalMode -and $taskConfig.virtualDisplayDeviceId -notmatch '^\{[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\}$') { throw 'A Sunshine display GUID is required for a persistent virtual output.' }
if (!$taskConfig.keepVirtualDisplayInNormalMode -and (!$taskConfig.sleepOnMoonlightConnect -or !$taskConfig.disconnectMoonlightOnWake)) { throw 'On-demand virtual output requires sleepOnMoonlightConnect=true and disconnectMoonlightOnWake=true.' }
$taskSunshineConfig = $taskConfig.sunshine.configPath
$taskText = [IO.File]::ReadAllText($taskSunshineConfig)
$taskText = Remove-SunshineImeKeybindings $taskText
$taskExe = Join-Path $env:LOCALAPPDATA 'Programs\PseudoSleep\PseudoSleep.exe'
if (!(Test-Path -LiteralPath $taskExe)) { throw 'Install PseudoSleep before configuring the client-resolution hook.' }
$taskPrep = @(Get-OtherSunshinePrep $taskText)
$taskPrep += [pscustomobject]@{ do=('"' + $taskExe + '" stream-start'); undo=('"' + $taskExe + '" stream-stop'); elevated=$false }
$taskOutput = if ($taskConfig.keepVirtualDisplayInNormalMode) { $taskConfig.virtualDisplayDeviceId } else { '' }
$taskSettingsMap = [ordered]@{ output_name=$taskOutput; upnp='disabled'; origin_web_ui_allowed='pc'; dd_configuration_option='disabled'; dd_resolution_option='disabled'; dd_refresh_rate_option='disabled'; dd_hdr_option='disabled'; min_log_level='2'; global_prep_cmd=(ConvertTo-Json -InputObject @($taskPrep) -Depth 100 -Compress) }
if ($Encoder) { $taskSettingsMap['encoder'] = $Encoder }
if ($VirtualAudioSink) { if ($VirtualAudioSink -match '[\r\n]') { throw 'Audio sink must be one line.' }; $taskSettingsMap['virtual_sink'] = $VirtualAudioSink; $taskSettingsMap['stream_audio'] = 'enabled' }
$taskText = Set-SunshineSettings $taskText $taskSettingsMap
Copy-Item -LiteralPath $taskSunshineConfig -Destination (Get-BackupPath $taskSunshineConfig)
Write-Utf8File $taskSunshineConfig $taskText
if ($AllowServiceRestart) {
    $taskSddl = (& sc.exe sdshow $taskConfig.sunshine.serviceName | Where-Object { $_ -match '^[ODGS]:' }) -join ''
    if ($LASTEXITCODE -ne 0 -or !$taskSddl) { throw 'Cannot read Sunshine service permissions.' }
    [IO.File]::WriteAllText((Get-BackupPath ($taskSunshineConfig + '.service-acl')), $taskSddl, [Text.UTF8Encoding]::new($false))
    $taskSecurity = [Security.AccessControl.RawSecurityDescriptor]::new($taskSddl)
    $taskSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $taskHasRule = @($taskSecurity.DiscretionaryAcl | Where-Object { $_ -is [Security.AccessControl.CommonAce] -and $_.SecurityIdentifier -eq $taskSid -and $_.AceQualifier -eq [Security.AccessControl.AceQualifier]::AccessAllowed -and ($_.AccessMask -band 0x34) -eq 0x34 }).Count -gt 0
    if (!$taskHasRule) { $taskAce = [Security.AccessControl.CommonAce]::new([Security.AccessControl.AceFlags]::None, [Security.AccessControl.AceQualifier]::AccessAllowed, 0x34, $taskSid, $false, $null); $taskSecurity.DiscretionaryAcl.InsertAce($taskSecurity.DiscretionaryAcl.Count, $taskAce); & sc.exe sdset $taskConfig.sunshine.serviceName ($taskSecurity.GetSddlForm([Security.AccessControl.AccessControlSections]::All)); if ($LASTEXITCODE -ne 0) { throw 'Cannot grant Sunshine query/start/stop permissions.' } }
}
Restart-Service -Name $taskConfig.sunshine.serviceName
