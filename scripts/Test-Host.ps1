param([switch]$CrashRecovery)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskExe = Join-Path $env:LOCALAPPDATA 'Programs\PseudoSleep\PseudoSleep.exe'
function Invoke-App([string[]]$Arguments) {
    $taskResult = & $taskExe @Arguments | Out-String
    if ($LASTEXITCODE -ne 0) { throw $taskResult }
    $taskParsed = $taskResult | ConvertFrom-Json
    foreach ($taskItem in @($taskParsed)) { Write-Output $taskItem }
}
function Get-ActiveDisplayKey {
    $taskDisplays = Invoke-App @('displays')
    return @($taskDisplays | Where-Object active | Sort-Object devicePath | ForEach-Object { '{0}|{1}x{2}|{3},{4}|{5}' -f $_.devicePath,$_.width,$_.height,$_.x,$_.y,$_.refreshRate })
}
$taskInitial = Invoke-App @('status')
if (!$taskInitial.data.resident -or $taskInitial.data.state -ne 'Normal') { throw 'Run this test with the resident tray in Normal state.' }
$taskBefore = Get-ActiveDisplayKey
$taskEntry = Invoke-App @('sleep', '--test-seconds=30')
if ($taskEntry.data.state -ne 'PseudoSleep' -or $taskEntry.data.physicalDisplays -ne 0 -or !$taskEntry.data.virtualDisplay -or !$taskEntry.data.powerRequest) { throw 'Invalid PseudoSleep status.' }
$taskVirtual = @(Invoke-App @('displays') | Where-Object active)
if ($taskVirtual.Count -ne 1 -or !$taskVirtual[0].indirect) { throw 'Expected one virtual display only.' }
$taskRecoveryStart = Get-Date
if ($CrashRecovery) {
    $taskResidents = @(Get-CimInstance Win32_Process -Filter "Name='PseudoSleep.exe'" | Where-Object { $_.ExecutablePath -eq $taskExe -and $_.CommandLine -notmatch '\bguardian\b' })
    if ($taskResidents.Count -ne 1) { throw 'Cannot uniquely identify the resident process; refusing termination.' }
    Stop-Process -Id $taskResidents[0].ProcessId -Force
    $taskPending = Join-Path $env:LOCALAPPDATA 'PseudoSleep\state.json'
    $taskDeadline = (Get-Date).AddSeconds(30)
    while ((Test-Path -LiteralPath $taskPending) -and (Get-Date) -lt $taskDeadline) { Start-Sleep -Milliseconds 250 }
    if (Test-Path -LiteralPath $taskPending) { throw 'Guardian did not complete recovery; journal retained.' }
    Start-Process -FilePath $taskExe -WindowStyle Hidden
    Start-Sleep -Seconds 1
} else { $null = Invoke-App @('wake', '--force') }
$taskDuration = ((Get-Date) - $taskRecoveryStart).TotalSeconds
$taskAfter = Get-ActiveDisplayKey
if (($taskBefore -join "`n") -ne ($taskAfter -join "`n")) { throw 'Restored display modes or positions differ from the original topology.' }
$taskFinal = Invoke-App @('status')
if ($taskFinal.data.state -ne 'Normal' -or $taskFinal.data.virtualDisplay -ne $taskInitial.data.virtualDisplay -or $taskFinal.data.powerRequest -or $taskFinal.data.recoveryPending) { throw 'Invalid Normal status after restoration.' }
$taskEvidence = [ordered]@{ at=(Get-Date).ToString('o'); kind=$(if ($CrashRecovery) { 'crash-recovery' } else { 'sleep-wake' }); success=$true; recoverySeconds=$taskDuration; before=$taskBefore; sleeping=$taskEntry.data; after=$taskAfter; final=$taskFinal.data }
$taskOutput = Join-Path $taskRoot ('artifacts\host-test-' + $taskEvidence.kind + '.json')
[IO.File]::WriteAllText($taskOutput, (ConvertTo-Json $taskEvidence -Depth 8), [Text.UTF8Encoding]::new($false))
$taskEvidence | ConvertTo-Json -Depth 8
