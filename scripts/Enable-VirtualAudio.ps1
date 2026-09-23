param([string]$AdapterName = 'Steam Streaming Speakers')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Setup.Common.ps1')
New-Item -ItemType Directory -Path (Join-Path $taskRoot 'artifacts') -Force | Out-Null
$taskEndpoints = @(Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render' | ForEach-Object { $taskState = Get-ItemProperty $_.PSPath; $taskProperties = Get-ItemProperty (Join-Path $_.PSPath 'Properties'); if ($taskProperties.'{b3f8fa53-0004-438e-9003-51a46e139bfc},6' -eq $AdapterName -and ($taskState.DeviceState -band 4) -eq 0) { [pscustomobject]@{ id=('{0.0.0.00000000}.' + $_.PSChildName); state=$taskState.DeviceState; adapter=$AdapterName } } })
if ($taskEndpoints.Count -ne 1) { throw "Expected one installed, present virtual audio endpoint for $AdapterName; found $($taskEndpoints.Count). No driver was installed." }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class VirtualAudioSetup {
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetVisibility(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string endpoint, int visible);
    public static void Enable(string endpoint) {
        var clsid = new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9");
        var iid = new Guid("f8679f50-850a-41cf-9c72-430f290290c8");
        IntPtr policy;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, 23, ref iid, out policy));
        try { var table = Marshal.ReadIntPtr(policy); var callback = (SetVisibility)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, 14 * IntPtr.Size), typeof(SetVisibility)); Marshal.ThrowExceptionForHR(callback(policy, endpoint, 1)); }
        finally { Marshal.Release(policy); }
    }
}
'@
$taskBackup = Get-BackupPath (Join-Path $taskRoot 'artifacts\virtual-audio-before.json')
[IO.File]::WriteAllText($taskBackup, (ConvertTo-Json -InputObject $taskEndpoints -Depth 4), [Text.UTF8Encoding]::new($false))
[VirtualAudioSetup]::Enable($taskEndpoints[0].id)
$taskInfo = & "$env:ProgramFiles\Sunshine\tools\audio-info.exe" | Out-String
if ($LASTEXITCODE -ne 0) { throw "Sunshine audio-info failed. Original endpoint state: $taskBackup" }
[IO.File]::WriteAllText((Join-Path $taskRoot 'artifacts\virtual-audio-after.txt'), $taskInfo, [Text.UTF8Encoding]::new($false))
if ($taskInfo -notmatch [regex]::Escape($taskEndpoints[0].id)) { throw 'The virtual endpoint is not available to Sunshine yet. See artifacts/virtual-audio-after.txt.' }
$taskEndpoints[0].id
