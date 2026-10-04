using System.Text.Json;
using PseudoSleep;
using PseudoSleep.Core;

internal static class NormalDisplayTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    [
        ("Normal monitor stops a running driver even when its output is inactive", () => { var config = Config(); config.VirtualDisplayDriverInstanceId = "driver"; var prepared = false; var monitor = new NormalDisplayMonitor(() => config, () => Array.Empty<DisplayInfo>(), _ => prepared = true, _ => { }, _ => true); monitor.Poll(config, AppState.Normal, false, 0); Assert(prepared); }),
        ("Normal monitor reloads saved policy and disables a stale active virtual display", () => WithConfigFile(path => { var saved = Config(); File.WriteAllText(path, JsonSerializer.Serialize(saved, Storage.Json)); var fixture = new Fixture(() => JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Storage.Json)!); var current = Config(); current.KeepVirtualDisplayInNormalMode = true; var result = fixture.Monitor.Poll(current, AppState.Normal, false, 0); Assert(!result.KeepVirtualDisplayInNormalMode && fixture.Prepared == 1 && !fixture.VirtualActive); })),
        ("Normal monitor repairs later virtual reactivation without a new stream", () => { var fixture = new Fixture(); fixture.Monitor.Poll(Config(), AppState.Normal, false, 0); fixture.VirtualActive = true; fixture.Monitor.Poll(Config(), AppState.Normal, false, 1000); Assert(fixture.Prepared == 1 && fixture.VirtualActive); fixture.Monitor.Poll(Config(), AppState.Normal, false, 2000); Assert(fixture.Prepared == 2 && !fixture.VirtualActive); }),
        ("Normal monitor never reads settings or touches displays during sleep or recovery", () => { foreach (var state in Enum.GetValues<AppState>().Where(state => state != AppState.Normal)) { var fixture = new Fixture(); var current = Config(); Assert(ReferenceEquals(current, fixture.Monitor.Poll(current, state, false, 0))); Assert(fixture.Loaded == 0 && fixture.Enumerated == 0 && fixture.Prepared == 0); } }),
        ("Normal monitor leaves a tracked stream in Normal state alone", () => { var fixture = new Fixture(); fixture.Monitor.Poll(Config(), AppState.Normal, true, 0); Assert(fixture.Loaded == 0 && fixture.Enumerated == 0 && fixture.Prepared == 0); fixture.Monitor.Poll(Config(), AppState.Normal, false, 1000); Assert(fixture.Prepared == 1); }),
        ("Normal monitor respects explicit persistent virtual display policy", () => { var fixture = new Fixture(() => new AppConfig { KeepVirtualDisplayInNormalMode = true, VirtualDisplayDevicePath = "virtual" }); var result = fixture.Monitor.Poll(Config(), AppState.Normal, false, 0); Assert(result.KeepVirtualDisplayInNormalMode && fixture.Loaded == 1 && fixture.Enumerated == 0 && fixture.Prepared == 0); }),
        ("Normal monitor does not change an already inactive virtual display", () => { var fixture = new Fixture { VirtualActive = false }; fixture.Monitor.Poll(Config(), AppState.Normal, false, 0); Assert(fixture.Enumerated == 1 && fixture.Prepared == 0); }),
        ("Normal monitor only disables the configured indirect display", () => { foreach (var display in new[] { Display("other", true, true), Display("virtual", true, false) }) { var prepared = false; var monitor = new NormalDisplayMonitor(Config, () => new[] { display }, _ => prepared = true, _ => { }); monitor.Poll(Config(), AppState.Normal, false, 0); Assert(!prepared); } }),
        ("Normal monitor compares device paths without case sensitivity", () => { var fixture = new Fixture(() => new AppConfig { VirtualDisplayDevicePath = "VIRTUAL" }); fixture.Monitor.Poll(Config(), AppState.Normal, false, 0); Assert(fixture.Prepared == 1); }),
        ("Normal monitor skips display discovery without a configured target", () => { var fixture = new Fixture(() => new AppConfig()); fixture.Monitor.Poll(Config(), AppState.Normal, false, 0); Assert(fixture.Loaded == 1 && fixture.Enumerated == 0 && fixture.Prepared == 0); }),
        ("Normal monitor keeps valid settings on invalid reload and retries after backoff", () => { var fixture = new Fixture(() => new AppConfig { Width = -1 }); var current = Config(); Assert(ReferenceEquals(current, fixture.Monitor.Poll(current, AppState.Normal, false, 0))); fixture.Monitor.Poll(current, AppState.Normal, false, 2000); Assert(fixture.Loaded == 1 && fixture.Prepared == 0 && fixture.Logs.Count == 1); fixture.Load = Config; fixture.Monitor.Poll(current, AppState.Normal, false, 5000); Assert(fixture.Loaded == 2 && fixture.Prepared == 1); }),
        ("Normal monitor retries display repair errors without repeated error logs", () => { var fixture = new Fixture { FailPrepare = true }; fixture.Monitor.Poll(Config(), AppState.Normal, false, 0); fixture.Monitor.Poll(Config(), AppState.Normal, false, 5000); Assert(fixture.Prepared == 2 && fixture.Logs.Count == 1 && fixture.VirtualActive); fixture.FailPrepare = false; fixture.Monitor.Poll(Config(), AppState.Normal, false, 10000); Assert(fixture.Prepared == 3 && !fixture.VirtualActive); }),
        ("Normal monitor cannot overwrite a pending recovery journal", () => { var controllerFixture = new global::Fixture(); var pending = new RecoveryRecord(1, AppState.Waking, global::Fixture.Backup, DateTimeOffset.UtcNow); controllerFixture.Record = pending; var monitor = new NormalDisplayMonitor(Config, () => new[] { Display("virtual", true, true) }, config => controllerFixture.Controller.PrepareNormalDisplay(config), _ => { }); monitor.Poll(Config(), AppState.Normal, false, 0); Assert(ReferenceEquals(pending, controllerFixture.Record) && controllerFixture.Calls.Count == 0); }),
    ];

    private static AppConfig Config() => new() { VirtualDisplayDevicePath = "virtual" };
    private static DisplayInfo Display(string path, bool active, bool indirect) => new(path, path, "source", active, indirect, 1920, 1080, 0, 0, 60);
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Normal display assertion failed."); }
    private static void WithConfigFile(Action<string> test) { var path = Path.GetTempFileName(); try { test(path); } finally { File.Delete(path); } }

    private sealed class Fixture
    {
        internal Func<AppConfig> Load { get; set; }
        internal NormalDisplayMonitor Monitor { get; }
        internal bool VirtualActive { get; set; } = true;
        internal bool FailPrepare { get; set; }
        internal int Loaded { get; private set; }
        internal int Enumerated { get; private set; }
        internal int Prepared { get; private set; }
        internal List<string> Logs { get; } = [];
        internal Fixture(Func<AppConfig>? load = null)
        {
            Load = load ?? Config;
            Monitor = new(() => { Loaded++; return Load(); }, () => { Enumerated++; return new[] { Display("physical", true, false), Display("virtual", VirtualActive, true) }; }, _ => { Prepared++; if (FailPrepare) throw new IOException("Display configuration is changing."); VirtualActive = false; }, Logs.Add);
        }
    }
}
