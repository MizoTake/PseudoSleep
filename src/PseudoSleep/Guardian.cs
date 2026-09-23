using System.Diagnostics;
using PseudoSleep.Core;

namespace PseudoSleep;

internal sealed class Guardian : IGuardian, IDisposable
{
    private Process? process;
    void IGuardian.EnsureReady()
    {
        if (process is { HasExited: false }) return;
        var eventName = Ipc.Prefix + ".guardian." + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("guardian");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add(eventName);
        process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start recovery guardian.");
        if (!ready.WaitOne(TimeSpan.FromSeconds(8)) || process.HasExited) throw new InvalidOperationException("Recovery guardian did not become ready.");
        Storage.Log($"Recovery guardian ready: {process.Id}");
    }

    internal static int Run(int parentId, string readyEvent)
    {
        using var parent = Process.GetProcessById(parentId);
        using var ready = EventWaitHandle.OpenExisting(readyEvent);
        ready.Set();
        parent.WaitForExit();
        using var mutex = new Mutex(false, Ipc.MutexName);
        bool owns;
        try { owns = mutex.WaitOne(TimeSpan.FromSeconds(20)); } catch (AbandonedMutexException) { owns = true; }
        if (!owns) return 0;
        try
        {
            if (!File.Exists(Storage.StatePath)) return 0;
            Storage.Log("Parent exited with recovery pending; restoring physical displays.");
            return RecoverStandalone();
        }
        finally { mutex.ReleaseMutex(); }
    }

    internal static int RecoverStandalone(bool force = false)
    {
        IRecoveryJournal journal = new RecoveryJournal();
        IDisplayBackend displays = new DisplayManager();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var pending = journal.Read();
                if (pending != null)
                {
                    displays.Restore(pending.Backup);
                    journal.Clear();
                    try { var config = Storage.LoadConfig(); if (config.DisconnectMoonlightOnWake) SunshineHost.RequestDisconnect(config); } catch (Exception ex) { Storage.Log($"Recovered displays but could not disconnect Moonlight: {ex.Message}"); }
                }
                else if (force && !DisplayManager.Enumerate().Any(d => d.Active && !d.Indirect))
                {
                    var backupPath = Path.Combine(Storage.DataDirectory, "display-backup.json");
                    if (File.Exists(backupPath)) { var backup = System.Text.Json.JsonSerializer.Deserialize<DisplayBackup>(File.ReadAllText(backupPath), Storage.Json) ?? throw new InvalidDataException("Invalid last display backup."); displays.Restore(backup); }
                    else DisplayManager.EmergencyPhysicalDisplays();
                }
                return 0;
            }
            catch (Exception ex) { Storage.Log($"Independent recovery attempt {attempt + 1}: {ex}"); Thread.Sleep(1000); }
        }
        try { DisplayManager.EmergencyPhysicalDisplays(); } catch (Exception ex) { Storage.Log($"Emergency physical display recovery: {ex}"); }
        return 1;
    }

    void IDisposable.Dispose() { process?.Dispose(); }
}
