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
