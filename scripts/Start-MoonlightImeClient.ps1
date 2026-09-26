$ErrorActionPreference = 'Stop'
try {
    $taskClient = Join-Path $PSScriptRoot 'MoonlightImeClient.exe'
    if (!(Test-Path -LiteralPath $taskClient)) { throw 'Extract the complete client ZIP before starting this launcher.' }
    if (Get-Process -Name MoonlightImeClient -ErrorAction SilentlyContinue) { throw 'Exit the running Moonlight IME helper from its tray menu, then run this launcher again.' }
    & (Join-Path $PSScriptRoot 'Set-MoonlightClientKeyboard.ps1')
    Start-Process -FilePath $taskClient -WorkingDirectory $PSScriptRoot
} catch {
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Moonlight IME setup') | Out-Null
    exit 1
}
