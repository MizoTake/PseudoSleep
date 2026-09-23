$ErrorActionPreference = 'Stop'
$taskExe = Join-Path $env:LOCALAPPDATA 'Programs\PseudoSleep\PseudoSleep.exe'
if (!(Test-Path -LiteralPath $taskExe)) { $taskExe = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\publish\PseudoSleep.exe' }
& $taskExe wake --force | Out-String
if ($LASTEXITCODE -ne 0) { throw 'Display restoration failed. Keep state.json and inspect the log.' }
