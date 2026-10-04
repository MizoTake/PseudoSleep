$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DriverPower.Common.ps1')
. (Join-Path $PSScriptRoot 'DriverPower.Tasks.ps1')
$taskService = New-Object -ComObject Schedule.Service
$taskService.Connect()
$taskSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$taskId = 'ROOT\DISPLAY\0001'
$taskSafeAcl = 'O:BAG:BAD:(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1200a9;;;' + $taskSid + ')(A;;FR;;;SY)'
$taskChecks = 0
function Assert-Task([bool]$Value, [string]$Message) { if (!$Value) { throw $Message }; $script:taskChecks++; Write-Output ('PASS ' + $Message) }
function New-TestTask($Definition, [string]$Acl) {
    $taskMock = [pscustomobject]@{Enabled=$true; Definition=$Definition; Security=$Acl}
    $taskMock | Add-Member ScriptMethod GetSecurityDescriptor { param($flags) return $this.Security }
    return $taskMock
}
$taskExpected = New-DriverPowerTaskDefinition $taskService $taskId $true
$taskActual = New-TestTask (New-DriverPowerTaskDefinition $taskService $taskId $true) $taskSafeAcl
Assert-Task (Test-DriverPowerTaskCurrent $taskActual $taskExpected $taskSid) 'Current demand-only definition and mapped read/execute ACL are accepted'
Assert-Task (!(Test-DriverPowerTaskCurrent $null $taskExpected $taskSid)) 'Missing tasks require repair'
foreach ($taskMutation in @('legacy-action', 'trigger', 'extra-action', 'user-write', 'unrelated-user', 'user-owner', 'battery', 'disabled')) {
    $taskActual = New-TestTask (New-DriverPowerTaskDefinition $taskService $taskId $true) $taskSafeAcl
    switch ($taskMutation) {
        'legacy-action' { $taskActual.Definition.Actions.Item(1).Path = 'pnputil.exe' }
        'trigger' { $null = $taskActual.Definition.Triggers.Create(9) }
        'extra-action' { $null = $taskActual.Definition.Actions.Create(0) }
        'user-write' { $taskActual.Security = $taskSafeAcl.Replace('0x1200a9', 'FA') }
        'unrelated-user' { $taskActual.Security += '(A;;FA;;;BU)' }
        'user-owner' { $taskActual.Security = $taskSafeAcl.Replace('O:BA', 'O:' + $taskSid) }
        'battery' { $taskActual.Definition.Settings.DisallowStartIfOnBatteries = $true }
        'disabled' { $taskActual.Enabled = $false }
    }
    Assert-Task (!(Test-DriverPowerTaskCurrent $taskActual $taskExpected $taskSid)) ('Stale or unsafe definition requires repair: ' + $taskMutation)
}

function New-TestFolder([bool]$FailSecond, [bool]$BadResult) {
    $taskFolder = [pscustomobject]@{Calls=[Collections.Generic.List[string]]::new(); Count=0; FailSecond=$FailSecond; BadResult=$BadResult}
    $taskFolder | Add-Member ScriptMethod RegisterTaskDefinition {
        param($name, $definition, $flags, $principal, $password, $logonType, $acl)
        $this.Calls.Add('register:' + $name)
        $this.Count++
        if ($this.FailSecond -and $this.Count -eq 2) { throw 'Simulated second registration failure' }
        if ($flags -ne 6 -or $principal -ne 'SYSTEM' -or $logonType -ne 5 -or $acl -ne ('O:BAG:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;' + $taskSid + ')')) { throw 'Invalid registration parameters' }
        $result = New-TestTask $definition $taskSafeAcl
        if ($this.BadResult) { $result.Enabled = $false }
        return $result
    }
    $taskFolder | Add-Member ScriptMethod RegisterTask {
        param($name, $xml, $flags, $principal, $password, $logonType, $acl)
        if ($xml -ne 'original-xml' -or $acl -ne 'original-acl') { throw 'Original definition and ACL must be restored exactly' }
        $this.Calls.Add('restore:' + $name)
    }
    $taskFolder | Add-Member ScriptMethod DeleteTask { param($name, $flags) $this.Calls.Add('delete:' + $name) }
    return $taskFolder
}
$taskPlans = @(
    @{Name='enable'; Expected=(New-DriverPowerTaskDefinition $taskService $taskId $true); Xml='original-xml'; Acl='original-acl'},
    @{Name='disable'; Expected=(New-DriverPowerTaskDefinition $taskService $taskId $false); Xml=$null; Acl=$null}
)
$taskFolder = New-TestFolder $false $false
Set-DriverPowerTasks $taskFolder $taskPlans $taskSid
Assert-Task (($taskFolder.Calls -join ',') -eq 'register:enable,register:disable') 'Repair registers and verifies both fixed task definitions'
$taskFolder = New-TestFolder $true $false
$taskRejected = $false
try { Set-DriverPowerTasks $taskFolder $taskPlans $taskSid } catch { $taskRejected = $true }
Assert-Task ($taskRejected -and ($taskFolder.Calls -join ',') -eq 'register:enable,register:disable,restore:enable,delete:disable') 'Partial failure restores the old definition and removes the new task'
$taskFolder = New-TestFolder $false $true
$taskRejected = $false
try { Set-DriverPowerTasks $taskFolder $taskPlans $taskSid } catch { $taskRejected = $true }
Assert-Task ($taskRejected -and ($taskFolder.Calls -join ',') -eq 'register:enable,restore:enable') 'Verification failure rolls back registration'
Write-Output "PASS $taskChecks driver task repair checks (mocked registration; no host task or device changed)."
