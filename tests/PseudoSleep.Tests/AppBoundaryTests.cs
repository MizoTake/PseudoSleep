using PseudoSleep;
using PseudoSleep.Core;

internal static class AppBoundaryTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    [
        ("Completed stream keeps success when remembering resolution fails", () => { var config = new AppConfig { VirtualDisplayDevicePath = "virtual", WakeDevices = ["HID\\LOCAL"] }; var fixture = new RecoveryFixture(_ => { }, false); var warnings = new List<string>(); Assert(fixture.Controller.StartStream(2880, 1920, config)); CommandCompletion.RememberClientResolution(config, 2880, 1920, _ => throw new IOException("Settings file is locked."), warnings.Add); Assert(fixture.Controller.State == AppState.PseudoSleep && !fixture.Restored && config.LastClientWidth == 2880 && config.LastClientHeight == 1920 && warnings.Count == 1); }),
        ("Completed command survives a transient display status failure", () => { var warnings = new List<string>(); var result = CommandCompletion.ReadStatus(() => throw new InvalidOperationException("Display topology changed."), warnings.Add); Assert(result != null && warnings.Count == 1); }),
        ("Completed command preserves successful display status", () => { var expected = new object(); var warnings = new List<string>(); Assert(ReferenceEquals(expected, CommandCompletion.ReadStatus(() => expected, warnings.Add)) && warnings.Count == 0); }),
        ("Log creation failure cannot interrupt display recovery", () => WithTemporaryDirectory(directory => { var blocked = Path.Combine(directory, "blocked"); File.WriteAllText(blocked, "Existing file blocks log directory creation."); var fixture = new RecoveryFixture(message => Storage.Log(message, blocked)); fixture.Controller.Recover(); Assert(fixture.Restored && fixture.Cleared && fixture.PowerReleased && fixture.Controller.State == AppState.Normal); })),
        ("Log sharing failure remains nonfatal", () => WithTemporaryDirectory(directory => { using var locked = new FileStream(Path.Combine(directory, DateTime.Now.ToString("yyyy-MM-dd") + ".log"), FileMode.Create, FileAccess.ReadWrite, FileShare.None); Storage.Log("Must not escape the logging boundary.", directory); })),
        ("Successful logging still writes UTF-8 text", () => WithTemporaryDirectory(directory => { Storage.Log("日本語の復旧ログ", directory); var files = Directory.GetFiles(directory, "*.log"); Assert(files.Length == 1 && File.ReadAllText(files[0]).Contains("日本語の復旧ログ", StringComparison.Ordinal)); })),
    ];

    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("App boundary assertion failed."); }

    private static void WithTemporaryDirectory(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "PseudoSleep.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { test(directory); }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class RecoveryFixture : IDisplayBackend, IRecoveryJournal, IPowerLease, IGuardian, IStreamingHost
    {
        private RecoveryRecord? pending = new(1, AppState.PseudoSleep, new("paths", "modes", ["physical"], DateTimeOffset.UtcNow), DateTimeOffset.UtcNow);
        internal SleepController Controller { get; }
        internal bool Restored { get; private set; }
        internal bool Cleared { get; private set; }
        internal bool PowerReleased { get; private set; }
        internal RecoveryFixture(Action<string> log, bool recoveryPending = true) { if (!recoveryPending) pending = null; Controller = new(this, this, this, this, this, () => 0, log); }
        RecoveryRecord? IRecoveryJournal.Read() => pending;
        void IRecoveryJournal.Save(RecoveryRecord record) => pending = record;
        void IRecoveryJournal.Clear() { pending = null; Cleared = true; }
        DisplayBackup IDisplayBackend.Capture(AppConfig config) => new("paths", "modes", ["physical"], DateTimeOffset.UtcNow);
        void IDisplayBackend.EnableVirtual(AppConfig config) { }
        void IDisplayBackend.VerifyVirtual(AppConfig config) { }
        void IDisplayBackend.ShowVirtualOnly(AppConfig config) { }
        void IDisplayBackend.ChangeVirtualResolution(AppConfig config) { }
        void IDisplayBackend.ConfigureNormalDisplay(AppConfig config) { }
        void IDisplayBackend.Restore(DisplayBackup backup) => Restored = true;
        void IPowerLease.Acquire() => PowerReleased = false;
        void IPowerLease.Release() => PowerReleased = true;
        void IGuardian.EnsureReady() { }
        void IStreamingHost.EnsureReady(AppConfig config) { }
        void IStreamingHost.Disconnect(AppConfig config) { }
    }
}
