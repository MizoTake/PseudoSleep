param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
    & (Join-Path $PSScriptRoot 'Test-Setup.ps1')
    dotnet build src/PseudoSleep -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet run --project tests/PseudoSleep.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    if ($Publish) {
        dotnet publish src/PseudoSleep -c Release -r win-x64 --self-contained false -o artifacts/publish
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
        $taskHeadlessTest = Start-Process powershell.exe -WindowStyle Hidden -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $PSScriptRoot 'Test-HeadlessCli.ps1') + '"') -Wait -PassThru
        if ($taskHeadlessTest.ExitCode -ne 0) { throw 'Headless CLI test failed. See artifacts/headless-cli-test.json.' }
    }
} finally { Pop-Location }
