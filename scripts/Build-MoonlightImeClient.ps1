param([string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\moonlight-ime-client'))
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $taskCompiler)) { $taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (!(Test-Path -LiteralPath $taskCompiler)) { throw 'Windows .NET Framework 4 compiler was not found.' }
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskClient = Join-Path $taskOutput 'MoonlightImeClient.exe'
& $taskCompiler /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ "/out:$taskClient" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll "/win32manifest:$(Join-Path $taskRoot 'src\PseudoSleep\app.manifest')" (Join-Path $taskRoot 'src\MoonlightImeClient\Program.cs') (Join-Path $taskRoot 'src\KeyboardBridge.Shared\KeyGates.cs') (Join-Path $taskRoot 'src\KeyboardBridge.Shared\KeyboardNative.cs')
if ($LASTEXITCODE -ne 0) { throw 'Moonlight IME client build failed.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\moonlight-ime-client.md') -Destination (Join-Path $taskOutput 'README.md')
Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination $taskOutput
$taskFiles = @('MoonlightImeClient.exe', 'README.md', 'LICENSE') | ForEach-Object { Join-Path $taskOutput $_ }
$taskManifest = @($taskFiles | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path $_ -Leaf) }) -join "`r`n"
[IO.File]::WriteAllText((Join-Path $taskOutput 'SHA256.txt'), $taskManifest + "`r`n", [Text.UTF8Encoding]::new($false))
Compress-Archive -LiteralPath ($taskFiles + (Join-Path $taskOutput 'SHA256.txt')) -DestinationPath ($taskOutput.TrimEnd('\') + '.zip') -Force
Write-Output $taskClient
