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

function Set-SunshineImeKey([string]$Text, [ValidateSet('JisIme','Standard')][string]$Mode, [ValidateSet('HalfWidthFullWidth','CapsLock')][string]$Key = 'HalfWidthFullWidth') {
    # VK_DBE_DBCSCHAR (0xF4) is the Japanese IME toggle key. Unlike 0xF3, it has no conflicting US scan code in the supported Sunshine version.
    $taskSourceKey = if ($Key -eq 'CapsLock') { 0x14 } else { 0xC0 }
    $taskSourceText = '0x{0:X2}' -f $taskSourceKey
    $taskTarget = if ($Mode -eq 'JisIme') { '0xF4' } else { $taskSourceText }
    $taskHeaders = [regex]::Matches($Text, '(?m)^[\t ]*keybindings[\t ]*=')
    if ($taskHeaders.Count -eq 0) { return Set-SunshineSettings $Text ([ordered]@{keybindings=('[0x10, 0xA0, 0x11, 0xA2, 0x12, 0xA4, ' + $taskSourceText + ', ' + $taskTarget + ']')}) }
    if ($taskHeaders.Count -ne 1) { throw 'Multiple keybindings settings found; correct the configuration first.' }
    $taskStart = $taskHeaders[0].Index + $taskHeaders[0].Length
    $taskValue = [regex]::Match($Text.Substring($taskStart), '\A[\t ]*\[(?:[^\x5D#]|#[^\r\n]*)*\][\t ]*(?:#[^\r\n]*)?(?=\r?\n|\z)')
    if (!$taskValue.Success) { throw 'keybindings must be a complete list of virtual-key pairs.' }
    $taskMasked = [regex]::Replace($taskValue.Value, '#[^\r\n]*', [Text.RegularExpressions.MatchEvaluator]{ param($taskMatch) ' ' * $taskMatch.Length })
    $taskNumber = '(?:0[xX][0-9a-fA-F]+|[0-9]+)'
    if ($taskMasked -notmatch ('\A\s*\[\s*(?:' + $taskNumber + '(?:\s*,\s*' + $taskNumber + ')*\s*,?)?\s*\]\s*\z')) { throw 'keybindings contains an invalid virtual-key value.' }
    $taskTokens = [regex]::Matches($taskMasked, $taskNumber)
    if ($taskTokens.Count % 2 -ne 0) { throw 'keybindings must contain pairs of source and destination keys.' }
    $taskNumbers = @($taskTokens | ForEach-Object { $taskDigits = $_.Value; $taskKey = if ($taskDigits.StartsWith('0x', [StringComparison]::OrdinalIgnoreCase)) { [Convert]::ToInt32($taskDigits.Substring(2), 16) } else { [Convert]::ToInt32($taskDigits, 10) }; if ($taskKey -lt 0 -or $taskKey -gt 255) { throw 'Virtual-key codes must be between 0 and 255.' }; $taskKey })
    $taskSeen = @{}
    $taskTargetToken = $null
    for ($taskIndex = 0; $taskIndex -lt $taskNumbers.Count; $taskIndex += 2) {
        $taskSource = $taskNumbers[$taskIndex]
        if ($taskSeen.ContainsKey($taskSource)) { throw 'Duplicate source keys found in keybindings; correct the configuration first.' }
        $taskSeen[$taskSource] = $true
        if ($taskSource -eq $taskSourceKey) { $taskTargetToken = $taskTokens[$taskIndex + 1] }
    }
    if ($null -ne $taskTargetToken) { return $Text.Remove($taskStart + $taskTargetToken.Index, $taskTargetToken.Length).Insert($taskStart + $taskTargetToken.Index, $taskTarget) }
    $taskClosing = $taskMasked.LastIndexOf(']')
    $taskSeparator = if ($taskNumbers.Count -gt 0 -and !$taskMasked.Substring(0, $taskClosing).TrimEnd().EndsWith(',')) { ', ' } else { ' ' }
    return $Text.Insert($taskStart + $taskClosing, $taskSeparator + $taskSourceText + ', ' + $taskTarget + ' ')
}
