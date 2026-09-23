param([string]$Exe = (Join-Path $PSScriptRoot '..\artifacts\publish\PseudoSleep.exe'), [string]$ReportPath = (Join-Path $PSScriptRoot '..\artifacts\headless-cli-test.json'))
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public static class HeadlessCliNative { [DllImport("kernel32.dll")] public static extern bool FreeConsole(); }'
[HeadlessCliNative]::FreeConsole() | Out-Null
$taskResults = @()
foreach ($taskRedirect in @($false, $true)) {
    $taskStart = [Diagnostics.ProcessStartInfo]::new()
    $taskStart.FileName = [IO.Path]::GetFullPath($Exe)
    $taskStart.Arguments = 'status'
    $taskStart.UseShellExecute = $false
    $taskStart.CreateNoWindow = $true
    $taskStart.RedirectStandardOutput = $taskRedirect
    $taskStart.RedirectStandardError = $taskRedirect
    $taskProcess = [Diagnostics.Process]::Start($taskStart)
    if (!$taskProcess.WaitForExit(15000)) { $taskProcess.Kill(); throw 'Headless CLI timed out.' }
    $taskOutput = if ($taskRedirect) { $taskProcess.StandardOutput.ReadToEnd() } else { '' }
    $taskError = if ($taskRedirect) { $taskProcess.StandardError.ReadToEnd() } else { '' }
    $taskResults += [pscustomobject]@{ redirected=$taskRedirect; exitCode=$taskProcess.ExitCode; output=$taskOutput; error=$taskError }
    $taskProcess.Dispose()
}
[IO.File]::WriteAllText([IO.Path]::GetFullPath($ReportPath), (ConvertTo-Json -InputObject $taskResults -Depth 10), [Text.UTF8Encoding]::new($false))
if (@($taskResults | Where-Object exitCode -NE 0).Count) { exit 1 }
foreach ($taskResult in $taskResults | Where-Object redirected) { if (!(($taskResult.output | ConvertFrom-Json).success)) { exit 1 } }
