$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
$taskTestCount = 0
function Assert-Test([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message }; $script:taskTestCount++ }
foreach ($taskScript in Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1') {
    $taskTokens = $null; $taskErrors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile($taskScript.FullName, [ref]$taskTokens, [ref]$taskErrors)
    Assert-Test ($taskErrors.Count -eq 0) ("PowerShell syntax: " + $taskScript.Name + ': ' + ($taskErrors -join ', '))
}
$taskLiteral = '"C:\Users\fixture$&\PseudoSleep.exe" stream-start'
$taskEntry = [pscustomobject]@{ do=$taskLiteral; undo=''; elevated=$false }
$taskJson = ConvertTo-Json -InputObject @($taskEntry) -Compress
$taskUpdated = Set-SunshineSettings "# preserved`r`nglobal_prep_cmd = []`r`nencoder = software`r`n" ([ordered]@{global_prep_cmd=$taskJson; output_name=''})
$taskRoundTrip = @(Get-SunshinePrep $taskUpdated)
Assert-Test ($taskRoundTrip.Count -eq 1 -and $taskRoundTrip[0].do -ceq $taskLiteral) 'Literal dollar signs changed during config replacement.'
Assert-Test ($taskUpdated.Contains("# preserved`r`n") -and $taskUpdated.Contains('encoder = software')) 'Unrelated config changed.'
Assert-Test ($taskUpdated -ceq (Set-SunshineSettings $taskUpdated ([ordered]@{global_prep_cmd=$taskJson; output_name=''}))) 'Repeated settings update is not stable.'
$taskOther = [pscustomobject]@{ do='echo PseudoSleep.exe stream-start'; undo='echo done'; elevated=$false }
$taskPrepText = 'global_prep_cmd = ' + (ConvertTo-Json -InputObject @($taskOther, $taskEntry) -Compress)
$taskRemaining = @(Get-OtherSunshinePrep $taskPrepText)
Assert-Test ($taskRemaining.Count -eq 1 -and $taskRemaining[0].do -eq $taskOther.do -and $taskRemaining[0].undo -eq $taskOther.undo) 'Unrelated startup command was removed.'
$taskRejected = $false
try { Get-SunshinePrep 'global_prep_cmd = {"do":"existing"}' | Out-Null } catch { $taskRejected = $true }
Assert-Test $taskRejected 'Malformed prep array was accepted.'
$taskTemporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$taskIme = "encoder = nvenc`r`nkeybindings = [0x10, 0xA0, 0xC0, 0xF4, 0x14, 0xF4]`r`nkey_repeat_delay = 500`r`n"
$taskRepaired = Remove-SunshineImeKeybindings $taskIme
Assert-Test ($taskRepaired -ceq $taskIme.Replace('0xC0, 0xF4', '0xC0, 0xC0').Replace('0x14, 0xF4', '0x14, 0x14')) 'Both repeatable IME toggles must be removed without changing other settings or normal key repeat.'
Assert-Test ($taskRepaired -ceq (Remove-SunshineImeKeybindings $taskRepaired)) 'Repeating the repair changed the configuration.'
foreach ($taskUnchanged in @('', "encoder = nvenc`n", "# keybindings = [0xC0, 0xF4]`n", "keybindings = []`n", 'keybindings = [74, 75,]', 'keybindings = [0xC0, 0x41, 0x14, 0x42, 0x70, 0xF4]', 'keybindings = [0xC0, 0xF3, 0x14, 0xF3]')) {
    Assert-Test ((Remove-SunshineImeKeybindings $taskUnchanged) -ceq $taskUnchanged) 'Repair added mappings or changed an unrelated/custom mapping.'
}
foreach ($taskPair in @(@('0xC0', '0x14'), @('0x14', '0xC0'))) {
    $taskSingle = 'keybindings = [' + $taskPair[0] + ', 0xF4, ' + $taskPair[1] + ', 0x41]'
    Assert-Test ((Remove-SunshineImeKeybindings $taskSingle) -ceq $taskSingle.Replace($taskPair[0] + ', 0xF4', $taskPair[0] + ', ' + $taskPair[0])) 'Repair missed one known IME mapping or changed another custom key.'
}
$taskMultiline = "# user mapping`r`nkeybindings = [`r`n    74, 75, # keep [this] comment`r`n    192, 244, # half/full`r`n    20, 244 # caps`r`n] # keep trailing comment`r`nencoder = nvenc`r`n"
$taskExpected = $taskMultiline.Replace('192, 244', '192, 0xC0').Replace('20, 244', '20, 0x14')
Assert-Test ((Remove-SunshineImeKeybindings $taskMultiline) -ceq $taskExpected) 'Decimal mappings, comments, or CRLF newlines were not preserved.'
$taskLf = "keybindings = [0x14, 0xf4, # 0xC0, 0xF4 in a comment`n0xc0, 0Xf4,]`n"
Assert-Test ((Remove-SunshineImeKeybindings $taskLf) -ceq $taskLf.Replace('0x14, 0xf4', '0x14, 0x14').Replace('0xc0, 0Xf4', '0xc0, 0xC0')) 'Mixed-case hex, trailing comma, comment, or LF newline failed.'
foreach ($taskInvalid in @('keybindings = [192]', 'keybindings = [192, 244, 192, 26]', 'keybindings = [192, garbage]', 'keybindings = [192, 256]', 'keybindings = [192, 244', "keybindings = []`nkeybindings = []", 'keybindings = [] unexpected', 'keybindings = [0xC0, 0xF4, 20, 244, 20, 20]')) {
    $taskRejected = $false
    try { Remove-SunshineImeKeybindings $taskInvalid | Out-Null } catch { $taskRejected = $true }
    Assert-Test $taskRejected ('Malformed or ambiguous keybindings accepted: ' + $taskInvalid)
}
$taskRepeatOriginal = "keybindings = [0xC0, 0xC0]`r`nkey_repeat_delay = 650`r`nkey_repeat_frequency = 24.9`r`nencoder = nvenc`r`n"
$taskRepeatDisabled = Set-SunshineKeyRepeat $taskRepeatOriginal 'Disabled'
Assert-Test ($taskRepeatDisabled -ceq $taskRepeatOriginal.Replace('key_repeat_delay = 650', 'key_repeat_delay = 0')) 'Disabling remote repeat changed mappings, repeat frequency, or other settings.'
Assert-Test ($taskRepeatDisabled -ceq (Set-SunshineKeyRepeat $taskRepeatDisabled 'Disabled')) 'Repeated key-repeat disable changed the configuration.'
Assert-Test ((Set-SunshineKeyRepeat $taskRepeatDisabled 'Enabled' 650) -ceq $taskRepeatOriginal) 'Custom repeat delay could not be restored.'
Assert-Test ((Set-SunshineKeyRepeat $taskRepeatDisabled 'Enabled') -ceq $taskRepeatOriginal.Replace('650', '500')) 'Default repeat delay is not 500ms.'
Assert-Test ((Set-SunshineKeyRepeat "encoder = nvenc`n" 'Disabled') -ceq "encoder = nvenc`nkey_repeat_delay = 0`n") 'Missing repeat setting was not added with LF newlines.'
$taskRejected = $false
try { Set-SunshineKeyRepeat "key_repeat_delay = 500`nkey_repeat_delay = 600`n" 'Disabled' | Out-Null } catch { $taskRejected = $true }
Assert-Test $taskRejected 'Duplicate repeat settings were accepted.'
foreach ($taskInvalidDelay in @(-1, 0, 60001)) {
    $taskRejected = $false
    try { Set-SunshineKeyRepeat '' 'Enabled' $taskInvalidDelay | Out-Null } catch { $taskRejected = $true }
    Assert-Test $taskRejected 'Invalid enabled repeat delay was accepted.'
}
$taskTestDirectory = [IO.Path]::GetFullPath((Join-Path $taskTemporaryRoot ('PseudoSleep.SetupTests.' + [Guid]::NewGuid().ToString('N'))))
if (!$taskTestDirectory.StartsWith($taskTemporaryRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid test directory.' }
New-Item -ItemType Directory -Path $taskTestDirectory | Out-Null
try {
    foreach ($taskBom in @($false, $true)) {
        $taskFile = Join-Path $taskTestDirectory ('encoding-' + $taskBom + '.txt')
        [IO.File]::WriteAllText($taskFile, 'before', [Text.UTF8Encoding]::new($taskBom))
        Write-Utf8File $taskFile "after`r`n"
        $taskBytes = [IO.File]::ReadAllBytes($taskFile)
        $taskActualBom = $taskBytes[0] -eq 239 -and $taskBytes[1] -eq 187 -and $taskBytes[2] -eq 191
        Assert-Test ($taskActualBom -eq $taskBom -and [IO.File]::ReadAllText($taskFile) -ceq "after`r`n") 'UTF-8 BOM or newline changed.'
    }
    $taskHashFile = Join-Path $taskTestDirectory 'hash.txt'
    Write-Utf8File $taskHashFile 'verified input'
    Assert-FileHash $taskHashFile (Get-FileHash -LiteralPath $taskHashFile -Algorithm SHA256).Hash
    $taskRejected = $false
    try { Assert-FileHash $taskHashFile ('0' * 64) } catch { $taskRejected = $true }
    Assert-Test $taskRejected 'Incorrect asset hash was accepted.'
    Assert-Test ((Get-BackupPath $taskHashFile) -ne (Get-BackupPath $taskHashFile)) 'Backup names collide.'
} finally {
    $taskResolved = (Resolve-Path -LiteralPath $taskTestDirectory).Path
    if (!$taskResolved.StartsWith($taskTemporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($taskResolved) -notlike 'PseudoSleep.SetupTests.*') { throw 'Refusing unexpected cleanup path.' }
    Remove-Item -LiteralPath $taskResolved -Recurse -Force
}
Write-Output "PASS $taskTestCount setup/syntax checks (no host changes)."
