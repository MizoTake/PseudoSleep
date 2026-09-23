param([string]$BackupDirectory = (Join-Path $env:LOCALAPPDATA 'PseudoSleepClient\KeyboardBackups'))
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run this script in Windows PowerShell 5.1 on the Moonlight client PC.' }
# Run on the client, not the Sunshine host. Keep the existing language order, input methods, and language-bar appearance.
$taskLanguages = Get-WinUserLanguageList
$taskBar = Get-WinLanguageBarOption
if ($taskLanguages.Count -eq 0) { throw 'No existing input languages were found; no settings were changed.' }
New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
$taskBackup = Join-Path $BackupDirectory ('input-settings-' + (Get-Date -Format 'yyyyMMddHHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8) + '.clixml')
[pscustomobject]@{ Languages=$taskLanguages; LanguageBar=$taskBar; CapturedAt=(Get-Date).ToString('o') } | Export-Clixml -LiteralPath $taskBackup -Encoding UTF8 -Depth 10
$taskEnglish = $taskLanguages | Where-Object LanguageTag -EQ 'en-US' | Select-Object -First 1
$taskLanguageChanged = $false
if ($null -eq $taskEnglish) {
    $taskNewLanguages = New-WinUserLanguageList -Language 'en-US'
    $taskEnglish = $taskNewLanguages[0]
    $taskEnglish.InputMethodTips.Clear()
    $taskEnglish.InputMethodTips.Add('0409:00000409')
    $taskLanguages.Add($taskEnglish)
    $taskLanguageChanged = $true
} elseif (!$taskEnglish.InputMethodTips.Contains('0409:00000409')) {
    $taskEnglish.InputMethodTips.Add('0409:00000409')
    $taskLanguageChanged = $true
}
if ($taskLanguageChanged) { Set-WinUserLanguageList -LanguageList $taskLanguages -Force }
if (!$taskBar.IsLegacySwitchingMode) { Set-WinLanguageBarOption -UseLegacySwitchMode -UseLegacyLanguageBar:([bool]$taskBar.IsLegacyLanguageBar) }
$taskVerifiedLanguages = Get-WinUserLanguageList
$taskHasUs = @($taskVerifiedLanguages | Where-Object { $_.LanguageTag -eq 'en-US' -and $_.InputMethodTips.Contains('0409:00000409') }).Count -eq 1
if (!$taskHasUs -or !(Get-WinLanguageBarOption).IsLegacySwitchingMode) { throw ('Input settings could not be verified. Backup: ' + $taskBackup) }
Write-Output ('Backup: ' + $taskBackup)
Write-Output 'US input is available; input methods are now selected per app window. Existing Japanese input was preserved.'
Write-Output 'End the stream. In the Moonlight PC list, use Win+Space to select ENG/US, then reconnect and test Caps Lock. Select Japanese in your other apps.'
