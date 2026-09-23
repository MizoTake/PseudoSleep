$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
$taskConfigPath = Join-Path $env:APPDATA 'PseudoSleep\config.json'
if (Test-Path -LiteralPath $taskConfigPath) {
    $taskConfig = Get-Content -LiteralPath $taskConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (Test-Path -LiteralPath $taskConfig.sunshine.configPath) {
        $taskPrep = @(Get-SunshinePrep ([IO.File]::ReadAllText($taskConfig.sunshine.configPath)))
        if (@($taskPrep | Where-Object { Test-PseudoSleepPrep $_ }).Count -gt 0) { throw 'Sunshine still calls PseudoSleep. First run scripts\Remove-SunshineIntegration.ps1 in an administrator PowerShell under the same signed-in account, then run this script again. No changes were made.' }
    }
}
$taskExe = Join-Path $env:LOCALAPPDATA 'Programs\PseudoSleep\PseudoSleep.exe'
if (Test-Path -LiteralPath $taskExe) {
    & $taskExe wake --force | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Display restoration failed. Uninstall stopped; preserve state.json.' }
    & $taskExe exit | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'App exit failed.' }
    Wait-AppExit $taskExe
}
$taskStartup = Join-Path ([Environment]::GetFolderPath('Startup')) 'PseudoSleep.lnk'
if (Test-Path -LiteralPath $taskStartup) { Remove-Item -LiteralPath $taskStartup }
Write-Output 'Startup disabled and tray stopped. Settings, logs, binaries and host software are preserved.'
