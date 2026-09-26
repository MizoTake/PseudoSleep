$ErrorActionPreference = 'Stop'
# In-memory replacements ensure these tests never change Windows language settings.
$global:clientKeyboardTestChecks = 0
function Assert-ClientTest([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message }; $global:clientKeyboardTestChecks++ }
function New-TestLanguage([string]$Tag, [string[]]$Tips) { $taskTips = [Collections.Generic.List[string]]::new(); foreach ($taskTip in $Tips) { $taskTips.Add($taskTip) }; [pscustomobject]@{LanguageTag=$Tag; InputMethodTips=$taskTips} }
function Get-WinUserLanguageList { return ,$global:clientKeyboardLanguagesFixture }
function Get-WinLanguageBarOption { return $global:clientKeyboardBarFixture }
function New-WinUserLanguageList([string]$Language) { $taskNewList = [Collections.Generic.List[object]]::new(); $taskNewList.Add((New-TestLanguage $Language @('0409:00000409'))); return ,$taskNewList }
function Set-WinUserLanguageList($LanguageList, [switch]$Force) { $global:clientKeyboardLanguageWrites++; $global:clientKeyboardLanguagesFixture = $LanguageList }
function Set-WinLanguageBarOption([switch]$UseLegacySwitchMode, [switch]$UseLegacyLanguageBar) { $global:clientKeyboardBarWrites++; $global:clientKeyboardBarFixture = [pscustomobject]@{IsLegacySwitchingMode=[bool]$UseLegacySwitchMode;IsLegacyLanguageBar=[bool]$UseLegacyLanguageBar} }
$taskTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$taskTestRoot = Join-Path $taskTempRoot ('PseudoSleep.ClientKeyboardTests.' + [Guid]::NewGuid().ToString('N'))
try {
    $global:clientKeyboardLanguagesFixture = [Collections.Generic.List[object]]::new()
    $global:clientKeyboardLanguagesFixture.Add((New-TestLanguage 'ja' @('0411:fixture-ime')))
    $global:clientKeyboardBarFixture = [pscustomobject]@{IsLegacySwitchingMode=$false;IsLegacyLanguageBar=$true}
    $global:clientKeyboardLanguageWrites = 0; $global:clientKeyboardBarWrites = 0
    & (Join-Path $PSScriptRoot 'Set-MoonlightClientKeyboard.ps1') -BackupDirectory $taskTestRoot | Out-Null
    Assert-ClientTest ($global:clientKeyboardLanguagesFixture.Count -eq 2 -and $global:clientKeyboardLanguagesFixture[0].LanguageTag -eq 'ja' -and $global:clientKeyboardLanguagesFixture[0].InputMethodTips[0] -eq '0411:fixture-ime') 'Japanese input or language priority changed.'
    Assert-ClientTest ($global:clientKeyboardLanguagesFixture[1].InputMethodTips.Contains('0409:00000409')) 'US input was not added.'
    Assert-ClientTest ($global:clientKeyboardBarFixture.IsLegacySwitchingMode -and $global:clientKeyboardBarFixture.IsLegacyLanguageBar) 'Per-app input or existing language-bar appearance was not preserved.'
    $taskBackupFiles = @(Get-ChildItem -LiteralPath $taskTestRoot -Filter '*.clixml')
    $taskSaved = Import-Clixml -LiteralPath $taskBackupFiles[0].FullName
    Assert-ClientTest ($taskSaved.Languages.Count -eq 1 -and $taskSaved.Languages[0].InputMethodTips[0] -eq '0411:fixture-ime' -and !$taskSaved.LanguageBar.IsLegacySwitchingMode) 'Backup does not contain settings from before the change.'
    & (Join-Path $PSScriptRoot 'Set-MoonlightClientKeyboard.ps1') -BackupDirectory $taskTestRoot | Out-Null
    Assert-ClientTest ($global:clientKeyboardLanguageWrites -eq 1 -and $global:clientKeyboardBarWrites -eq 1) 'Repeated setup rewrites existing settings.'
    $global:clientKeyboardLanguagesFixture[1].InputMethodTips.Clear()
    $global:clientKeyboardLanguagesFixture[1].InputMethodTips.Add('0409:00020409')
    $global:clientKeyboardBarFixture = [pscustomobject]@{IsLegacySwitchingMode=$false;IsLegacyLanguageBar=$false}
    & (Join-Path $PSScriptRoot 'Set-MoonlightClientKeyboard.ps1') -BackupDirectory $taskTestRoot | Out-Null
    Assert-ClientTest ($global:clientKeyboardLanguagesFixture[1].InputMethodTips.Count -eq 2 -and $global:clientKeyboardLanguagesFixture[1].InputMethodTips[0] -eq '0409:00020409') 'Existing alternative US layout was removed.'
    Assert-ClientTest (!$global:clientKeyboardBarFixture.IsLegacyLanguageBar) 'Modern language-bar appearance changed.'
    $global:clientKeyboardLanguagesFixture.Clear()
    $taskRejected = $false
    try { & (Join-Path $PSScriptRoot 'Set-MoonlightClientKeyboard.ps1') -BackupDirectory $taskTestRoot | Out-Null } catch { $taskRejected = $true }
    Assert-ClientTest $taskRejected 'An empty language list was accepted.'
    # Exercise the packaged launcher with mocked processes; never launch or change host settings.
    $global:clientKeyboardLanguagesFixture.Add((New-TestLanguage 'ja' @('0411:fixture-ime')))
    $global:clientKeyboardBarFixture = [pscustomobject]@{IsLegacySwitchingMode=$false;IsLegacyLanguageBar=$true}
    $global:clientKeyboardLaunches = @()
    function Get-Process { param([string]$Name) }
    function Start-Process { param([string]$FilePath, [string]$WorkingDirectory) $global:clientKeyboardLaunches += [pscustomobject]@{FilePath=$FilePath;WorkingDirectory=$WorkingDirectory;Ready=$global:clientKeyboardBarFixture.IsLegacySwitchingMode -and $global:clientKeyboardLanguagesFixture[1].InputMethodTips.Contains('0409:00000409')} }
    $taskLaunchDirectory = Join-Path $taskTestRoot 'Client Package With Spaces'
    New-Item -ItemType Directory -Path $taskLaunchDirectory | Out-Null
    foreach ($taskScript in @('Start-MoonlightImeClient.ps1', 'Set-MoonlightClientKeyboard.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $taskScript) -Destination $taskLaunchDirectory }
    # Override only the fixture copy's backup default so the launch test stays inside its temporary workspace.
    $taskSetupCopy = Join-Path $taskLaunchDirectory 'Set-MoonlightClientKeyboard.ps1'
    $taskSetupText = [IO.File]::ReadAllText($taskSetupCopy).Replace("(Join-Path `$env:LOCALAPPDATA 'PseudoSleepClient\KeyboardBackups')", "(Join-Path `$PSScriptRoot 'Backups')")
    [IO.File]::WriteAllText($taskSetupCopy, $taskSetupText, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $taskLaunchDirectory 'MoonlightImeClient.exe'), 'fixture only')
    & (Join-Path $taskLaunchDirectory 'Start-MoonlightImeClient.ps1') | Out-Null
    Assert-ClientTest ($global:clientKeyboardLaunches.Count -eq 1 -and $global:clientKeyboardLaunches[0].Ready) 'Launcher did not prepare input before starting the client.'
    Assert-ClientTest ($global:clientKeyboardLaunches[0].FilePath -eq (Join-Path $taskLaunchDirectory 'MoonlightImeClient.exe') -and $global:clientKeyboardLaunches[0].WorkingDirectory -eq $taskLaunchDirectory) 'Launcher did not preserve the extracted package path.'
} finally {
    if (Test-Path -LiteralPath $taskTestRoot) {
        $taskResolved = (Resolve-Path -LiteralPath $taskTestRoot).Path
        if (!$taskResolved.StartsWith($taskTempRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($taskResolved) -notlike 'PseudoSleep.ClientKeyboardTests.*') { throw 'Refusing unexpected cleanup path.' }
        Remove-Item -LiteralPath $taskResolved -Recurse -Force
    }
}
Write-Output "PASS $global:clientKeyboardTestChecks client keyboard setup checks (mocked Windows settings; no host changes)."
