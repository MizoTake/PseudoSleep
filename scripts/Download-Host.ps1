$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
$taskDownloads = Join-Path $taskRoot 'artifacts\downloads'
New-Item -ItemType Directory -Path $taskDownloads -Force | Out-Null
$taskAssets = Get-HostAssets
foreach ($taskAsset in $taskAssets) {
    $taskPath = Join-Path $taskDownloads $taskAsset.Name
    if (!(Test-Path -LiteralPath $taskPath)) {
        $taskPartial = $taskPath + '.' + [Guid]::NewGuid().ToString('N') + '.partial'
        try { Invoke-WebRequest -Uri $taskAsset.Url -OutFile $taskPartial -UseBasicParsing; Assert-FileHash $taskPartial $taskAsset.Hash; Move-Item -LiteralPath $taskPartial -Destination $taskPath } finally { if (Test-Path -LiteralPath $taskPartial) { Remove-Item -LiteralPath $taskPartial } }
    }
    Assert-FileHash $taskPath $taskAsset.Hash
    Write-Output "Verified SHA256: $($taskAsset.Name)"
}
