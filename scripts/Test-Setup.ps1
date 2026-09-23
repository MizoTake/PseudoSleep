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
$taskIme = Set-SunshineImeKey "encoder = nvenc`r`n" 'JisIme'
Assert-Test ($taskIme.Contains('0xC0, 0xF4') -and $taskIme.Contains('encoder = nvenc')) 'Remote IME mapping was not added.'
Assert-Test ($taskIme -ceq (Set-SunshineImeKey $taskIme 'JisIme')) 'Repeated IME mapping changed the configuration.'
$taskStandard = Set-SunshineImeKey $taskIme 'Standard'
Assert-Test ($taskStandard.Contains('0xC0, 0xC0') -and !$taskStandard.Contains('0xF4')) 'Standard keyboard mapping was not restored.'
$taskCaps = Set-SunshineImeKey $taskIme 'JisIme' 'CapsLock'
Assert-Test ($taskCaps.Contains('0xC0, 0xF4') -and $taskCaps.Contains('0x14, 0xF4')) 'Caps Lock mapping did not preserve the existing half/full-width mapping.'
Assert-Test ($taskCaps -ceq (Set-SunshineImeKey $taskCaps 'JisIme' 'CapsLock')) 'Repeated Caps Lock mapping changed the configuration.'
$taskCapsStandard = Set-SunshineImeKey $taskCaps 'Standard' 'CapsLock'
Assert-Test ($taskCapsStandard.Contains('0x14, 0x14') -and $taskCapsStandard.Contains('0xC0, 0xF4')) 'Restoring Caps Lock changed the half/full-width mapping.'
$taskCapsOnly = Set-SunshineImeKey '' 'JisIme' 'CapsLock'
Assert-Test ($taskCapsOnly.Contains('0x14, 0xF4') -and !$taskCapsOnly.Contains('0xC0')) 'Caps Lock-only setup unexpectedly remapped another key.'
$taskMultiline = "# user mapping`r`nkeybindings = [`r`n    74, 75, # keep [this] comment`r`n    192, 25`r`n] # keep trailing comment`r`nencoder = nvenc`r`n"
$taskExpected = $taskMultiline.Replace('192, 25', '192, 0xF4')
Assert-Test ((Set-SunshineImeKey $taskMultiline 'JisIme') -ceq $taskExpected) 'Existing mappings, comments, or newlines changed.'
$taskEmpty = Set-SunshineImeKey "keybindings = []`n" 'JisIme'
Assert-Test ($taskEmpty.Contains('0xC0, 0xF4') -and !$taskEmpty.Contains("`r")) 'Empty keybinding list or LF newline failed.'
$taskOtherKeys = "keybindings = [`r`n74, 75 # keep this mapping`r`n]`r`n"
$taskAppended = Set-SunshineImeKey $taskOtherKeys 'JisIme'
Assert-Test ($taskAppended.Contains("74, 75 # keep this mapping`r`n") -and $taskAppended -ceq (Set-SunshineImeKey $taskAppended 'JisIme')) 'Appending to an existing commented list failed.'
$taskTrailingComma = Set-SunshineImeKey 'keybindings = [74, 75,]' 'JisIme'
Assert-Test ($taskTrailingComma -ceq (Set-SunshineImeKey $taskTrailingComma 'JisIme')) 'Trailing comma produced an invalid list.'
foreach ($taskInvalid in @('keybindings = [192]', 'keybindings = [192, 25, 192, 26]', 'keybindings = [192, garbage]', 'keybindings = [192, 256]', 'keybindings = [192, 25', "keybindings = []`nkeybindings = []", 'keybindings = [] unexpected')) {
    $taskRejected = $false
    try { Set-SunshineImeKey $taskInvalid 'JisIme' | Out-Null } catch { $taskRejected = $true }
    Assert-Test $taskRejected ('Malformed or ambiguous keybindings accepted: ' + $taskInvalid)
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
