using PseudoSleep;
using PseudoSleep.Core;

internal static class AudioSmoke
{
    internal static int Run()
    {
        using var mutex = new Mutex(false, Ipc.MutexName);
        bool owns;
        try { owns = mutex.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
        if (!owns) throw new InvalidOperationException("Exit the resident application before the explicit native audio smoke test.");
        try
        {
            var config = Storage.LoadConfig();
            if (new SunshineConnectionMonitor().Poll(Path.Combine(Path.GetDirectoryName(config.Sunshine.ConfigPath)!, "sunshine.log"), SunshineHost.IsRunning(config.Sunshine.ServiceName)) != false) throw new InvalidOperationException("Native audio test requires confirmed Moonlight disconnection.");
            if (File.Exists(Storage.StatePath) || File.Exists(Storage.AudioStatePath)) throw new InvalidOperationException("Recover existing display/audio transactions before testing.");
            IAudioBackend backend = new AudioBackend();
            var before = backend.GetDefaultVolumes();
            if (before.Count == 0) throw new InvalidOperationException("No default audio output available for testing.");
            using var guardian = new Guardian();
            var controller = new AudioController(backend, new AudioRecoveryJournal(), guardian);
            try
            {
                controller.Update(AppState.PseudoSleep, false);
                Check(backend.GetDefaultVolumes().All(value => value.Volume == 0), "Native default output volume becomes zero");
                Check(File.Exists(Storage.AudioStatePath), "Original native volume is durably saved");
                controller.Update(AppState.PseudoSleep, true);
                Verify(before, backend.GetDefaultVolumes());
                Check(!File.Exists(Storage.AudioStatePath), "Connection restoration clears audio recovery journal");
                controller.Update(AppState.PseudoSleep, false);
                controller.Update(AppState.Waking, false);
                Verify(before, backend.GetDefaultVolumes());
                controller.Update(AppState.PseudoSleep, false);
                Check(Guardian.RecoverStandalone() == 0, "Independent recovery restores native audio without a display journal");
                Verify(before, backend.GetDefaultVolumes());
                Check(!File.Exists(Storage.AudioStatePath), "Audio recovery is complete");
                return 0;
            }
            finally { controller.Restore(); }
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static void Verify(IReadOnlyList<AudioVolumeSnapshot> before, IReadOnlyList<AudioVolumeSnapshot> after) => Check(before.Count == after.Count && before.All(original => after.Any(current => current.EndpointId == original.EndpointId && Math.Abs(current.Volume - original.Volume) < .005f)), "Native volume is restored on the original outputs");
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
}
