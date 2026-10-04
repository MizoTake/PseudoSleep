using PseudoSleep.Core;

internal static class VirtualDisplayControlTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    [
        ("Manual virtual start journals and arms recovery before extending displays", () => { var f = new Fixture(); f.Controller.PrepareVirtualDisplay(Config()); Assert(f.Controller.State == AppState.VirtualDisplayReady && f.Record != null && !f.Power); Assert(string.Join(",", f.Calls) == "capture,save,guardian,extended,verify,save"); }),
        ("Manual virtual start requires no Sunshine or wake device", () => { var f = new Fixture(); var c = Config(); c.WakeDevices.Clear(); f.Controller.PrepareVirtualDisplay(c); Assert(!f.Calls.Contains("host") && !f.Calls.Contains("only")); }),
        ("Repeated start keeps the original physical display backup", () => { var f = new Fixture(); f.Controller.PrepareVirtualDisplay(Config()); var original = f.Record!.Backup; f.Controller.PrepareVirtualDisplay(Config()); Assert(f.Calls.Count(c => c == "capture") == 1 && ReferenceEquals(original, f.Record!.Backup)); }),
        ("Restart restores the saved physical layout before starting virtual display again", () => { var f = new Fixture(); f.Controller.PrepareVirtualDisplay(Config()); var original = f.Record!.Backup; f.Calls.Clear(); f.Controller.PrepareVirtualDisplay(Config(), true); Assert(f.Calls.IndexOf("restore") < f.Calls.IndexOf("extended") && ReferenceEquals(original, f.Record!.Backup)); }),
        ("Manual stop restores displays and clears recovery", () => { var f = new Fixture(); f.Controller.PrepareVirtualDisplay(Config()); f.Controller.Wake(); Assert(f.Controller.State == AppState.Normal && f.Record == null && f.Calls.Contains("restore")); }),
        ("Leaving manual mode does not restart Sunshine", () => { var f = new Fixture(); var c = Config(); c.DisconnectMoonlightOnWake = true; f.Controller.PrepareVirtualDisplay(c); f.Controller.Wake(c); Assert(!f.Calls.Contains("disconnect")); }),
        ("Changing target in manual mode preserves the original backup", () => { var f = new Fixture(); f.Controller.PrepareVirtualDisplay(Config()); var backup = f.Record; var c = Config(); c.VirtualDisplayDriverInstanceId = "other"; f.Calls.Clear(); Throws(() => f.Controller.PrepareVirtualDisplay(c)); Assert(f.Calls.Count == 0 && ReferenceEquals(backup, f.Record)); }),
        ("Failed manual start restores displays", () => { var f = new Fixture { FailAt = "extended" }; Throws(() => f.Controller.PrepareVirtualDisplay(Config())); Assert(f.Controller.State == AppState.Normal && f.Record == null && f.Calls.Contains("restore")); }),
        ("Manual start cannot overwrite an unfinished recovery", () => { var f = new Fixture { Record = new(1, AppState.Error, Fixture.Backup, DateTimeOffset.UtcNow) }; Throws(() => f.Controller.PrepareVirtualDisplay(Config())); Assert(f.Calls.Count == 0); }),
        ("Manual restart during pseudo sleep is rejected", () => { var f = new Fixture(); f.Controller.Enter(Config()); f.Calls.Clear(); Throws(() => f.Controller.PrepareVirtualDisplay(Config(), true)); Assert(f.Controller.State == AppState.PseudoSleep && f.Calls.Count == 0); }),
        ("Manual start journal failure prevents display mutation", () => { var f = new Fixture { FailAt = "save" }; Throws(() => f.Controller.PrepareVirtualDisplay(Config())); Assert(!f.Calls.Contains("extended")); }),
        ("Manual start guardian failure prevents display mutation", () => { var f = new Fixture { FailAt = "guardian" }; Throws(() => f.Controller.PrepareVirtualDisplay(Config())); Assert(!f.Calls.Contains("extended")); }),
        ("Failed manual recovery retains the original backup", () => { var f = new Fixture(); f.Controller.PrepareVirtualDisplay(Config()); f.FailAt = "restore"; Throws(f.Controller.Wake); Assert(f.Record != null && f.Controller.State == AppState.Error); }),
        ("Sleep after manual preparation reuses the original physical backup", () => { var f = new Fixture(); f.Controller.PrepareVirtualDisplay(Config()); var original = f.Record!.Backup; f.Calls.Clear(); f.Controller.Enter(Config()); Assert(f.Controller.State == AppState.PseudoSleep && ReferenceEquals(original, f.Record!.Backup) && !f.Calls.Contains("capture")); }),
        ("Normal display monitor cannot switch off a manually prepared display", () => { var f = new Fixture(); f.Controller.PrepareVirtualDisplay(Config()); Throws(() => f.Controller.PrepareNormalDisplay(Config())); Assert(!f.Calls.Contains("normal-display")); }),
        ("Stream start can enter sleep after manual virtual preparation", () => { var f = new Fixture(); var c = Config(); c.SleepOnMoonlightConnect = true; c.DisconnectMoonlightOnWake = true; f.Controller.PrepareVirtualDisplay(c); f.Controller.StartStream(1920, 1080, c); Assert(f.Controller.State == AppState.PseudoSleep && f.Calls.Contains("only")); }),
    ];

    private static AppConfig Config() => new() { VirtualDisplayDevicePath = "virtual", VirtualDisplayDriverInstanceId = "driver", WakeDevices = ["HID\\LOCAL"], SleepOnMoonlightConnect = false, DisconnectMoonlightOnWake = false };
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Virtual display control assertion failed."); }
    private static void Throws(Action action) { try { action(); } catch { return; } throw new InvalidOperationException("Expected failure."); }
}
