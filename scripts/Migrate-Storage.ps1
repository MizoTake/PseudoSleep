param(
    [Parameter(Mandatory=$true)][string]$ConfigSource,
    [string]$OldDataDirectory = (Join-Path $env:LOCALAPPDATA 'PseudoSleep'),
    [string]$Executable = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\publish\PseudoSleep.exe')
)
$ErrorActionPreference = 'Stop'
& $Executable migrate-storage $ConfigSource $OldDataDirectory | Out-String | Write-Output
if ($LASTEXITCODE -ne 0) { throw 'Storage migration did not complete. Existing files were preserved.' }
