function Get-HostAssets {
    @(
        @{ Name='Sunshine-Windows-AMD64-installer.msi'; Url='https://github.com/LizardByte/Sunshine/releases/download/v2026.914.233613/Sunshine-Windows-AMD64-installer.msi'; Hash='1d7fed8beecd5889dc7ff14cf9f42d6d38f37c3066c13c6c2a5f4e91847e0ccf' },
        @{ Name='VDD.Control.25.7.23.zip'; Url='https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VDD.Control.25.7.23.zip'; Hash='a701f2272e9fcf382849b24f913c6dd07597b3b1116525f2e90182f019609154' }
    )
}

function Assert-FileHash([string]$Path, [string]$ExpectedHash) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $ExpectedHash) { throw "SHA256 mismatch: $Path" }
}

function Assert-InteractiveUser {
    $taskSessionId = [Diagnostics.Process]::GetCurrentProcess().SessionId
    $taskShells = @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" | Where-Object SessionId -EQ $taskSessionId)
    $taskShellSids = @($taskShells | ForEach-Object { $taskOwner = Invoke-CimMethod -InputObject $_ -MethodName GetOwnerSid; if ($taskOwner.ReturnValue -eq 0) { $taskOwner.Sid } } | Select-Object -Unique)
    $taskCurrentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    if ($taskShellSids.Count -ne 1 -or $taskShellSids[0] -ne $taskCurrentSid) { throw 'Run from the signed-in desktop account, elevated using that same account. Alternate administrator credentials, service sessions, and sessions without Explorer are unsupported.' }
}

function Get-BackupPath([string]$Path) {
    return $Path + '.' + (Get-Date -Format 'yyyyMMddHHmmssfff') + '.' + [Guid]::NewGuid().ToString('N').Substring(0, 8) + '.bak'
}

function Write-Utf8File([string]$Path, [string]$Text) {
    $taskBom = $false
    if (Test-Path -LiteralPath $Path) { $taskBytes = [IO.File]::ReadAllBytes($Path); $taskBom = $taskBytes.Length -ge 3 -and $taskBytes[0] -eq 239 -and $taskBytes[1] -eq 187 -and $taskBytes[2] -eq 191 }
    $taskTemporary = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($taskTemporary, $Text, [Text.UTF8Encoding]::new($taskBom))
        if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($taskTemporary, $Path, [NullString]::Value) } else { [IO.File]::Move($taskTemporary, $Path) }
    } finally { if (Test-Path -LiteralPath $taskTemporary) { Remove-Item -LiteralPath $taskTemporary } }
}

function Set-SunshineSettings([string]$Text, [System.Collections.IDictionary]$Settings) {
    $taskNewLine = if ($Text.Contains("`r`n") -or !$Text) { "`r`n" } else { "`n" }
    foreach ($taskKey in $Settings.Keys) {
        $taskPattern = '(?m)^[\t ]*' + [regex]::Escape($taskKey) + '[\t ]*=[^\r\n]*'
        $taskLine = $taskKey + ' = ' + $Settings[$taskKey]
        if ([regex]::IsMatch($Text, $taskPattern)) { $Text = [regex]::Replace($Text, $taskPattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($taskMatch) $taskLine }) } else { $Text = $Text.TrimEnd([char[]]"`r`n") + $taskNewLine + $taskLine + $taskNewLine }
    }
    return $Text.TrimEnd([char[]]"`r`n") + $taskNewLine
}

function Get-SunshinePrep([string]$Text) {
    $taskMatch = [regex]::Match($Text, '(?m)^[\t ]*global_prep_cmd[\t ]*=[\t ]*(.*)$')
    if (!$taskMatch.Success) { return }
    $taskValue = $taskMatch.Groups[1].Value.Trim()
    if (!$taskValue.StartsWith('[') -or !$taskValue.EndsWith(']')) { throw 'global_prep_cmd must be a JSON array; preserve the existing configuration and correct it first.' }
    $taskParsed = $taskValue | ConvertFrom-Json
    foreach ($taskEntry in @($taskParsed)) { Write-Output $taskEntry }
}

function Test-PseudoSleepPrep($Entry) {
    return $null -ne $Entry -and $Entry.do -match '^(?:"(?:[^"\r\n]*[\\/])?PseudoSleep\.exe"|(?:[^\s"]*[\\/])?PseudoSleep\.exe)\s+(?:stream-start|client-mode)(?:\s|$)'
}

function Get-OtherSunshinePrep([string]$Text) {
    @(Get-SunshinePrep $Text) | Where-Object { !(Test-PseudoSleepPrep $_) }
}

function Wait-AppExit([string]$Executable) {
    for ($taskAttempt = 0; $taskAttempt -lt 150; $taskAttempt++) {
        $taskRunning = Get-Process PseudoSleep -ErrorAction SilentlyContinue | Where-Object Path -EQ $Executable
        if (!$taskRunning) { return }
        Start-Sleep -Milliseconds 200
    }
    throw 'Existing PseudoSleep processes have not exited; setup stopped.'
}
