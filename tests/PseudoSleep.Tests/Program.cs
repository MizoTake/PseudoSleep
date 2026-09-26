using PseudoSleep.Core;

var tests = new (string Name, Action Run)[]
{
    ("Only explicitly allowed physical HID wakes; deny wins", () => { var c = Config(); Assert(WakePolicy.IsAllowed("HID\\LOCAL", c)); Assert(!WakePolicy.IsAllowed(null, c)); Assert(!WakePolicy.IsAllowed("HID\\REMOTE", c)); c.IgnoredDevices.Add("*LOCAL*"); Assert(!WakePolicy.IsAllowed("HID\\LOCAL", c)); }),
    ("Guard boundaries", () => { var f = new Fixture(); f.Controller.Enter(Config()); f.Now = 2999; Assert(!f.Controller.OnInput("HID\\LOCAL", Config())); f.Now = 3000; Assert(f.Controller.OnInput("HID\\LOCAL", Config())); Assert(f.Controller.State == AppState.Normal); }),
    ("Remote input never releases sleep", () => { var f = new Fixture(); f.Controller.Enter(Config()); f.Now = 4000; Assert(!f.Controller.OnInput("HID\\REMOTE", Config())); Assert(f.Controller.State == AppState.PseudoSleep); }),
    ("Recovery saved and guardian armed before mutation; virtual verified before physical off", () => { var f = new Fixture(); f.Controller.Enter(Config()); Assert(string.Join(",", f.Calls) == "host,capture,save,guardian,power,enable,verify,only,verify,save"); }),
    ("Activation failure restores and releases power", () => { var f = new Fixture { FailAt = "enable" }; Throws(() => f.Controller.Enter(Config())); Assert(f.Controller.State == AppState.Normal); Assert(f.Record == null && !f.Power); Assert(!f.Calls.Contains("only")); }),
    ("Restore failure preserves recovery journal", () => { var f = new Fixture(); f.Controller.Enter(Config()); f.FailAt = "restore"; Throws(f.Controller.Wake); Assert(f.Controller.State == AppState.Error && f.Record != null && !f.Power); }),
    ("Service unavailable never touches displays", () => { var f = new Fixture { FailAt = "host" }; Throws(() => f.Controller.Enter(Config())); Assert(!f.Calls.Contains("capture") && !f.Calls.Contains("enable")); }),
    ("No wake device refuses entry", () => { var f = new Fixture(); var c = Config(); c.WakeDevices.Clear(); Throws(() => f.Controller.Enter(c)); Assert(f.Calls.Count == 0); }),
    ("Duplicate sleep cannot replace original backup", () => { var f = new Fixture(); f.Controller.Enter(Config()); var record = f.Record; f.Controller.Enter(Config()); Assert(ReferenceEquals(record, f.Record)); Assert(f.Calls.Count(c => c == "capture") == 1); }),
    ("Startup recovers interrupted transition", () => { var f = new Fixture(); f.Record = new(1, AppState.EnteringPseudoSleep, Fixture.Backup, DateTimeOffset.UtcNow); f.Controller.Recover(); Assert(f.Record == null && f.Calls.Contains("restore")); }),
    ("Guardian failure prevents display changes", () => { var f = new Fixture { FailAt = "guardian" }; Throws(() => f.Controller.Enter(Config())); Assert(!f.Calls.Contains("enable")); }),
    ("Guard is validated", () => { var c = Config(); c.WakeGuardMs = 10001; Throws(c.Validate); }),
    ("Wake configuration rejects every all-wildcard pattern", () => { foreach (var pattern in new[] { "*", "**", "***" }) { var c = Config(); c.WakeDevices = [pattern]; Throws(c.Validate); } }),
    ("Wake configuration permits device-specific wildcard patterns", () => { var c = Config(); c.WakeDevices = ["HID\\LOCAL_*"]; c.Validate(); Assert(WakePolicy.IsAllowed("HID\\LOCAL_MOUSE", c)); Assert(!WakePolicy.IsAllowed("HID\\REMOTE_MOUSE", c)); }),
    ("Wake retries previous failed restoration", () => { var f = new Fixture(); f.Controller.Enter(Config()); f.FailAt = "restore"; Throws(f.Controller.Wake); f.FailAt = ""; f.Controller.Wake(); Assert(f.Record == null && f.Controller.State == AppState.Normal); }),
    ("Power disabled by configuration", () => { var f = new Fixture(); var c = Config(); c.PreventSystemSleep = false; f.Controller.Enter(c); Assert(!f.Power); f.Controller.Wake(); }),
    ("Capture failure never activates a display", () => { var f = new Fixture { FailAt = "capture" }; Throws(() => f.Controller.Enter(Config())); Assert(!f.Calls.Contains("enable") && f.Record == null); }),
    ("Capture verification failure never disables physical displays", () => { var f = new Fixture { FailAt = "verify" }; Throws(() => f.Controller.Enter(Config())); Assert(f.Calls.Contains("enable") && f.Calls.Contains("restore") && !f.Calls.Contains("only")); }),
    ("Topology switch failure restores physical displays", () => { var f = new Fixture { FailAt = "only" }; Throws(() => f.Controller.Enter(Config())); Assert(f.Calls.Contains("restore") && f.Record == null && !f.Power); }),
    ("Unfinished recovery cannot be overwritten", () => { var f = new Fixture(); var original = new RecoveryRecord(1, AppState.Waking, Fixture.Backup, DateTimeOffset.UtcNow); f.Record = original; Throws(() => f.Controller.Enter(Config())); Assert(ReferenceEquals(original, f.Record) && f.Calls.Count == 0); }),
    ("Saving recovery is mandatory before activation", () => { var f = new Fixture { FailAt = "save" }; Throws(() => f.Controller.Enter(Config())); Assert(!f.Calls.Contains("enable") && !f.Power); }),
    ("Zero guard accepts physical input immediately", () => { var f = new Fixture(); var c = Config(); c.WakeGuardMs = 0; f.Controller.Enter(c); Assert(f.Controller.OnInput("HID\\LOCAL", c)); }),
    ("Injected/null input never wakes after guard", () => { var f = new Fixture(); f.Controller.Enter(Config()); f.Now = 5000; Assert(!f.Controller.OnInput(null, Config())); Assert(!f.Controller.OnInput("", Config())); Assert(f.Controller.State == AppState.PseudoSleep); }),
    ("Device matching is exact unless wildcard explicitly configured", () => { var c = Config(); Assert(!WakePolicy.IsAllowed("HID\\LOCAL_EXTRA", c)); Assert(WakePolicy.IsAllowed("hid\\local", c)); c.WakeDevices = ["HID\\LOCAL_*"]; Assert(WakePolicy.IsAllowed("HID\\LOCAL_EXTRA", c)); }),
    ("Client resolution leaves local desktop unchanged when extended virtual display is disabled", () => { var f = new Fixture(); Assert(!f.Controller.SetClientResolution(1920, 1080, Config())); Assert(!f.Calls.Contains("client-mode")); }),
    ("Normal streaming prepares virtual display without sleep or physical deactivation", () => { var f = new Fixture(); var c = Config(); c.KeepVirtualDisplayInNormalMode = true; Assert(f.Controller.SetClientResolution(2880, 1920, c)); Assert(string.Join(",", f.Calls) == "normal-display"); Assert(f.Width == 2880 && f.Height == 1920 && f.Controller.State == AppState.Normal && f.Record == null && !f.Power); }),
    ("Preparing normal layout uses remembered client resolution", () => { var f = new Fixture(); var c = Config(); c.KeepVirtualDisplayInNormalMode = true; c.LastClientWidth = 2880; c.LastClientHeight = 1920; f.Controller.PrepareNormalDisplay(c); Assert(f.Width == 2880 && f.Height == 1920 && f.Controller.State == AppState.Normal); }),
    ("Normal layout settings cannot wake an active pseudo sleep", () => { var f = new Fixture(); var c = Config(); f.Controller.Enter(c); Throws(() => f.Controller.PrepareNormalDisplay(c)); Assert(!f.Calls.Contains("normal-display") && f.Controller.State == AppState.PseudoSleep); }),
    ("Disabling resolution follow still prepares normal virtual output", () => { var f = new Fixture(); var c = Config(); c.KeepVirtualDisplayInNormalMode = true; c.FollowClientResolution = false; Assert(!f.Controller.SetClientResolution(2880, 1920, c)); Assert(f.Calls.Contains("normal-display") && f.Width == c.Width); }),
    ("Normal display setup cannot replace pending recovery", () => { var f = new Fixture(); f.Record = new(1, AppState.Waking, Fixture.Backup, DateTimeOffset.UtcNow); Throws(() => f.Controller.PrepareNormalDisplay(Config())); Assert(f.Calls.Count == 0); }),
    ("Stream start automatically sleeps and applies client resolution", () => { var f = new Fixture(); var c = Config(); c.SleepOnMoonlightConnect = true; c.DisconnectMoonlightOnWake = true; Assert(f.Controller.StartStream(2880, 1920, c)); Assert(f.Controller.State == AppState.PseudoSleep && f.Width == 2880 && f.Height == 1920 && f.Power); Assert(f.Calls.Contains("only") && !f.Calls.Contains("disconnect")); }),
    ("Failed automatic stream preparation restores the original displays", () => { var f = new Fixture { FailAt = "client-mode" }; var c = Config(); c.SleepOnMoonlightConnect = true; c.DisconnectMoonlightOnWake = true; Throws(() => f.Controller.StartStream(2880, 1920, c)); Assert(f.Controller.State == AppState.Normal && f.Record == null && !f.Power && !f.Calls.Contains("disconnect")); }),
    ("Failed client mode in existing sleep preserves physical-off state", () => { var f = new Fixture(); var c = Config(); c.SleepOnMoonlightConnect = true; c.DisconnectMoonlightOnWake = true; f.Controller.Enter(c); f.FailAt = "client-mode"; Throws(() => f.Controller.StartStream(2880, 1920, c)); Assert(f.Controller.State == AppState.PseudoSleep && f.Record != null && f.Power); }),
    ("Local wake restores before requesting stream disconnection", () => { var f = new Fixture(); var c = Config(); c.DisconnectMoonlightOnWake = true; f.Controller.Enter(c); f.Now = 4000; Assert(f.Controller.OnInput("HID\\LOCAL", c)); Assert(f.Calls.IndexOf("restore") < f.Calls.IndexOf("disconnect") && f.Controller.State == AppState.Normal && !f.Power); }),
    ("Remote input cannot disconnect a stream", () => { var f = new Fixture(); var c = Config(); c.DisconnectMoonlightOnWake = true; f.Controller.Enter(c); f.Now = 4000; Assert(!f.Controller.OnInput("HID\\REMOTE", c)); Assert(!f.Calls.Contains("disconnect")); }),
    ("Duplicate wake does not restart Sunshine again", () => { var f = new Fixture(); var c = Config(); c.DisconnectMoonlightOnWake = true; f.Controller.Enter(c); f.Controller.Wake(c); f.Controller.Wake(c); Assert(f.Calls.Count(x => x == "disconnect") == 1); }),
    ("Failed disconnection still leaves restored displays and released power", () => { var f = new Fixture(); var c = Config(); c.DisconnectMoonlightOnWake = true; f.Controller.Enter(c); f.FailAt = "disconnect"; Throws(() => f.Controller.Wake(c)); Assert(f.Controller.State == AppState.Normal && f.Record == null && !f.Power); }),
    ("Client resize preserves original recovery snapshot", () => { var f = new Fixture(); f.Controller.Enter(Config()); var original = f.Record; Assert(f.Controller.SetClientResolution(1920, 1080, Config())); Assert(ReferenceEquals(original, f.Record)); Assert(f.Width == 1920 && f.Height == 1080 && f.Controller.State == AppState.PseudoSleep); }),
    ("Invalid client resolution fails before mutation", () => { var f = new Fixture(); f.Controller.Enter(Config()); Throws(() => f.Controller.SetClientResolution(-1, 1080, Config())); Assert(!f.Calls.Contains("client-mode")); }),
    ("Automatic client resolution can be disabled", () => { var f = new Fixture(); var c = Config(); c.FollowClientResolution = false; f.Controller.Enter(c); Assert(!f.Controller.SetClientResolution(1920, 1080, c)); Assert(!f.Calls.Contains("client-mode")); }),
    ("Remembered client resolution is used on next sleep", () => { var f = new Fixture(); var c = Config(); c.LastClientWidth = 1920; c.LastClientHeight = 1200; f.Controller.Enter(c); Assert(f.Width == 1920 && f.Height == 1200); Assert(c.Width == 2560); }),
};
tests = tests.Concat(AppBoundaryTests.Cases).Concat(SessionTests.Cases).Concat(KeyboardBridgeTests.Cases).Concat(ClientLayoutTests.Cases).ToArray();
var failures = 0;
foreach (var test in tests) { try { test.Run(); Console.WriteLine($"PASS {test.Name}"); } catch (Exception ex) { failures++; Console.WriteLine($"FAIL {test.Name}: {ex.Message}"); } }
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static AppConfig Config() => new() { VirtualDisplayDevicePath = "virtual", WakeDevices = ["HID\\LOCAL"], KeepVirtualDisplayInNormalMode = false, SleepOnMoonlightConnect = false, DisconnectMoonlightOnWake = false };
static void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
static void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected exception"); }

sealed class Fixture : IDisplayBackend, IRecoveryJournal, IPowerLease, IGuardian, IStreamingHost
{
    public static readonly DisplayBackup Backup = new("paths", "modes", ["physical"], DateTimeOffset.UtcNow);
    public List<string> Calls { get; } = [];
    public string FailAt { get; set; } = "";
    public long Now { get; set; }
    public RecoveryRecord? Record { get; set; }
    public bool Power { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public SleepController Controller { get; }
    public Fixture() => Controller = new(this, this, this, this, this, () => Now, _ => { });
    private void Call(string name) { Calls.Add(name); if (name == FailAt) throw new Exception($"Injected {name} failure"); }
    DisplayBackup IDisplayBackend.Capture(AppConfig c) { Call("capture"); return Backup; }
    void IDisplayBackend.EnableVirtual(AppConfig c) => Call("enable");
    void IDisplayBackend.VerifyVirtual(AppConfig c) => Call("verify");
    void IDisplayBackend.ShowVirtualOnly(AppConfig c) { Call("only"); Width = c.Width; Height = c.Height; }
    void IDisplayBackend.ChangeVirtualResolution(AppConfig c) { Call("client-mode"); Width = c.Width; Height = c.Height; }
    void IDisplayBackend.ConfigureNormalDisplay(AppConfig c) { Call("normal-display"); Width = c.Width; Height = c.Height; }
    void IStreamingHost.Disconnect(AppConfig c) => Call("disconnect");
    void IDisplayBackend.Restore(DisplayBackup b) => Call("restore");
    RecoveryRecord? IRecoveryJournal.Read() => Record;
    void IRecoveryJournal.Save(RecoveryRecord r) { Call("save"); Record = r; }
    void IRecoveryJournal.Clear() { Call("clear"); Record = null; }
    void IPowerLease.Acquire() { Call("power"); Power = true; }
    void IPowerLease.Release() { Call("release"); Power = false; }
    void IGuardian.EnsureReady() => Call("guardian");
    void IStreamingHost.EnsureReady(AppConfig c) => Call("host");
}
