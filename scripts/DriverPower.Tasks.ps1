function Test-DriverPowerTaskAccess($Task, [string]$UserSid) {
    $taskSecurity = [Security.AccessControl.RawSecurityDescriptor]::new($Task.GetSecurityDescriptor(7))
    if ($taskSecurity.Owner.Value -notin @('S-1-5-18', 'S-1-5-32-544') -or $null -eq $taskSecurity.DiscretionaryAcl) { return $false }
    $taskUserCanRun = $false
    foreach ($taskAce in $taskSecurity.DiscretionaryAcl) {
        if ($taskAce.AceType -ne [Security.AccessControl.AceType]::AccessAllowed) { return $false }
        if ($taskAce.SecurityIdentifier.Value -in @('S-1-5-18', 'S-1-5-32-544')) { continue }
        if ($taskAce.SecurityIdentifier.Value -ne $UserSid -or ($taskAce.AccessMask -band 0x500D0156) -ne 0) { return $false }
        if (($taskAce.AccessMask -band 0x1200A9) -eq 0x1200A9) { $taskUserCanRun = $true }
    }
    return $taskUserCanRun
}

function Test-DriverPowerTaskCurrent($Task, $Expected, [string]$UserSid) {
    if (!$Task) { return $false }
    $taskActual = $Task.Definition
    return $Task.Enabled -and $taskActual.RegistrationInfo.Source -eq $Expected.RegistrationInfo.Source -and
        $taskActual.Triggers.Count -eq 0 -and $taskActual.Actions.Count -eq 1 -and
        $taskActual.Actions.Item(1).Type -eq 0 -and
        $taskActual.Actions.Item(1).Path -eq $Expected.Actions.Item(1).Path -and
        $taskActual.Actions.Item(1).Arguments -ceq $Expected.Actions.Item(1).Arguments -and
        [string]::IsNullOrEmpty($taskActual.Actions.Item(1).WorkingDirectory) -and
        $taskActual.Principal.UserId -in @('SYSTEM', 'S-1-5-18') -and
        $taskActual.Principal.LogonType -eq 5 -and $taskActual.Principal.RunLevel -eq 1 -and
        $taskActual.Settings.Enabled -and $taskActual.Settings.AllowDemandStart -and
        !$taskActual.Settings.DisallowStartIfOnBatteries -and !$taskActual.Settings.StopIfGoingOnBatteries -and
        $taskActual.Settings.ExecutionTimeLimit -eq 'PT30S' -and $taskActual.Settings.MultipleInstances -eq 2 -and
        (Test-DriverPowerTaskAccess $Task $UserSid)
}

# Embedded in the EXE. Registers only fixed, demand-only actions; never changes display power here.
function Invoke-DriverPowerTaskSetup([string]$InstanceId, [string]$UserSid, [bool]$Repair) {
    $ErrorActionPreference = 'Stop'
    $null = Get-DriverPowerCommand $InstanceId $true
    if ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ne $UserSid) { throw 'Approve elevation using the same signed-in Windows account.' }
    Import-Module (Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\Modules\PnpDevice\PnpDevice.psd1') -ErrorAction Stop
    $taskHardware = (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName DEVPKEY_Device_HardwareIds -ErrorAction Stop).Data
    $taskClass = (Get-PnpDeviceProperty -InstanceId $InstanceId -KeyName DEVPKEY_Device_ClassGuid -ErrorAction Stop).Data
    if ($taskHardware -notcontains 'Root\MttVDD' -or [string]$taskClass -ne '{4d36e968-e325-11ce-bfc1-08002be10318}') { throw 'The registered Virtual Display Driver is missing or does not match.' }
    $taskService = New-Object -ComObject Schedule.Service
    $taskService.Connect()
    $taskFolder = $taskService.GetFolder('\')
    $taskPlans = @()
    foreach ($taskEnabled in @($true, $false)) {
        $taskName = Get-DriverPowerTaskName $InstanceId $taskEnabled
        $taskExisting = $null
        try { $taskExisting = $taskFolder.GetTask($taskName) } catch { if ($_.Exception.HResult -ne -2147024894) { throw } }
        if ($taskExisting -and $taskExisting.Definition.RegistrationInfo.Source -ne 'PseudoSleep.VirtualDisplayDriverPower') { throw "An unrelated task uses this name: $taskName" }
        $taskExpected = New-DriverPowerTaskDefinition $taskService $InstanceId $taskEnabled
        if (!(Test-DriverPowerTaskCurrent $taskExisting $taskExpected $UserSid)) {
            $taskPlans += @{Name=$taskName; Expected=$taskExpected; Xml=$(if ($taskExisting) { $taskExisting.Xml }); Acl=$(if ($taskExisting) { $taskExisting.GetSecurityDescriptor(7) })}
        }
        if ($taskExisting -and $taskExisting.State -in @(2, 4)) { throw "A driver task is still running: $taskName" }
    }
    if (!$taskPlans.Count) { return 0 }
    if (!$Repair) { return 10 }
    $taskIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (!([Security.Principal.WindowsPrincipal]::new($taskIdentity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator rights are required to repair driver tasks.' }
    Set-DriverPowerTasks $taskFolder $taskPlans $UserSid
    return 0
}

function Set-DriverPowerTasks($TaskFolder, $TaskPlans, [string]$UserSid) {
    $taskAcl = 'O:BAG:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;' + $UserSid + ')'
    $taskChanged = @()
    try {
        foreach ($taskPlan in $TaskPlans) {
            $taskChanged += $taskPlan
            $taskRegistered = $TaskFolder.RegisterTaskDefinition($taskPlan.Name, $taskPlan.Expected, 6, 'SYSTEM', $null, 5, $taskAcl)
            if (!(Test-DriverPowerTaskCurrent $taskRegistered $taskPlan.Expected $UserSid)) { throw "Task verification failed: $($taskPlan.Name)" }
        }
    } catch {
        $taskFailure = $_
        foreach ($taskPlan in $taskChanged) {
            try {
                if ($taskPlan.Xml) { $null = $TaskFolder.RegisterTask($taskPlan.Name, $taskPlan.Xml, 6, 'SYSTEM', $null, 5, $taskPlan.Acl) }
                else { $TaskFolder.DeleteTask($taskPlan.Name, 0) }
            } catch { Write-Warning "Task rollback failed: $_" }
        }
        throw $taskFailure
    }
}
