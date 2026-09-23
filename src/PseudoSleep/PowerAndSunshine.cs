using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PseudoSleep.Core;

namespace PseudoSleep;

internal sealed class PowerLease : IPowerLease, IDisposable
{
    private nint handle;
    internal bool Active => handle != 0;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Reason { public uint Version; public uint Flags; [MarshalAs(UnmanagedType.LPWStr)] public string Text; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint PowerCreateRequest(ref Reason reason);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PowerSetRequest(nint request, int type);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PowerClearRequest(nint request, int type);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
    void IPowerLease.Acquire()
    {
        if (handle != 0) return;
        var reason = new Reason { Version = 0, Flags = 1, Text = "PseudoSleep: keep Sunshine available while physical displays are inactive." };
        var request = PowerCreateRequest(ref reason);
        if (request == -1 || request == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!PowerSetRequest(request, 1)) { var error = Marshal.GetLastWin32Error(); CloseHandle(request); throw new Win32Exception(error); }
        handle = request;
        Storage.Log("PowerRequestSystemRequired acquired.");
    }
    void IPowerLease.Release() => Release();
    void IDisposable.Dispose() => Release();
    private void Release() { if (handle == 0) return; PowerClearRequest(handle, 1); CloseHandle(handle); handle = 0; Storage.Log("Power request released."); }
}

internal sealed class SunshineHost : IStreamingHost
{
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus { public uint Type; public uint State; public uint Controls; public uint Win32Exit; public uint ServiceExit; public uint CheckPoint; public uint WaitHint; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint OpenService(nint manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool QueryServiceStatus(nint service, out ServiceStatus status);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(nint handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool ControlService(nint service, uint control, out ServiceStatus status);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool StartService(nint service, uint count, nint args);
    internal static bool IsRunning(string name)
    {
        var manager = OpenSCManager(null, null, 1);
        if (manager == 0) return false;
        try { var service = OpenService(manager, name, 4); if (service == 0) return false; try { return QueryServiceStatus(service, out var status) && status.State == 4; } finally { CloseServiceHandle(service); } }
        finally { CloseServiceHandle(manager); }
    }

    void IStreamingHost.EnsureReady(AppConfig config)
    {
        if (!IsRunning(config.Sunshine.ServiceName)) throw new InvalidOperationException($"Sunshineサービスが起動していません: {config.Sunshine.ServiceName}");
        var values = ReadConfiguration(config.Sunshine.ConfigPath);
        values.TryGetValue("output_name", out var output);
        if (config.KeepVirtualDisplayInNormalMode)
        {
            if (string.IsNullOrWhiteSpace(config.VirtualDisplayDeviceId) || !string.Equals(output, config.VirtualDisplayDeviceId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Sunshine output_name does not match the configured virtual display ID.");
        }
        else if (!string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("配信中だけ仮想画面を使うには Configure-Sunshine.ps1 を再実行し、Sunshineの出力を自動選択にしてください。");
        if (!config.KeepVirtualDisplayInNormalMode && !config.DisconnectMoonlightOnWake) throw new InvalidOperationException("配信中だけ仮想画面を使う構成では、物理復帰時の切断を有効にしてください。");
        if (!values.TryGetValue("dd_configuration_option", out var policy) || policy != "disabled") throw new InvalidOperationException("Sunshine dd_configuration_option must be disabled so it cannot restore a stale virtual-only topology after local wake.");
        if (config.DisconnectMoonlightOnWake) { var service = OpenRestartService(config); CloseServiceHandle(service); }
        Storage.Log("Sunshine service and display ownership configuration verified.");
    }

    void IStreamingHost.Disconnect(AppConfig config) => RequestDisconnect(config);

    internal static void RequestDisconnect(AppConfig config)
    {
        var service = OpenRestartService(config);
        CloseServiceHandle(service);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("restart-sunshine");
        start.ArgumentList.Add(config.Sunshine.ServiceName);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the Sunshine disconnect helper.");
        Storage.Log($"Disconnecting Moonlight via Sunshine service restart; helper PID {process.Id}.");
    }

    private static nint OpenRestartService(AppConfig config)
    {
        var manager = OpenSCManager(null, null, 1);
        if (manager == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try { var service = OpenService(manager, config.Sunshine.ServiceName, 0x34); if (service == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Run Configure-Sunshine.ps1 -AllowServiceRestart as administrator once to enable automatic disconnection."); return service; }
        finally { CloseServiceHandle(manager); }
    }

    internal static void RestartForDisconnect(string serviceName)
    {
        using var mutex = new Mutex(false, "Local\\" + Ipc.Prefix + ".sunshine-restart");
        bool owns;
        try { owns = mutex.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
        if (!owns) return;
        try
        {
            var config = new AppConfig { Sunshine = new SunshineConfig { ServiceName = serviceName } };
            var service = OpenRestartService(config);
            try
            {
                if (!QueryServiceStatus(service, out var status)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (status.State != 1 && status.State != 3 && !ControlService(service, 1, out status)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var deadline = Environment.TickCount64 + 25000;
                while (status.State != 1) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Sunshine service did not stop for disconnection."); Thread.Sleep(100); if (!QueryServiceStatus(service, out status)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
                if (!StartService(service, 0, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                Storage.Log("Sunshine service restarted after local wake; previous Moonlight sessions ended.");
            }
            finally { CloseServiceHandle(service); }
        }
        finally { mutex.ReleaseMutex(); }
    }

    internal static Dictionary<string, string> ReadConfiguration(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(path)) { var text = line.Trim(); if (text.StartsWith('#') || !text.Contains('=')) continue; var parts = text.Split('=', 2); values[parts[0].Trim()] = parts[1].Trim(); }
        return values;
    }
}
