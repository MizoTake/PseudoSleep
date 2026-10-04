using PseudoSleep;
using PseudoSleep.Core;

internal static class AudioTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    [
        ("Audio is zero only while sleeping without a connected client", () => { var f = new AudioFixture(); f.Controller.Update(AppState.Normal, false); Assert(f.Calls.Count == 0); f.Controller.Update(AppState.PseudoSleep, true); Assert(f.Calls.Count == 0); f.Controller.Update(AppState.PseudoSleep, false); Assert(f.Volumes["speaker"] == 0 && f.Saved.Single().Volume == .42f); Assert(string.Join(",", f.Calls) == "save,guardian,set:speaker:0"); }),
        ("Repeated suppression preserves the original volume", () => { var f = new AudioFixture(); f.Controller.Update(AppState.PseudoSleep, false); f.Volumes["speaker"] = .9f; f.Controller.Update(AppState.PseudoSleep, false); f.Controller.Update(AppState.PseudoSleep, true); Assert(f.Volumes["speaker"] == .42f && f.Saved.Count == 0); }),
        ("Wake, uncertain connection and disabled setting all restore volume", () => { foreach (var condition in new[] { (AppState.Waking, (bool?)false, true), (AppState.PseudoSleep, (bool?)null, true), (AppState.PseudoSleep, (bool?)false, false), (AppState.Error, (bool?)false, true) }) { var f = new AudioFixture(); f.Controller.Update(AppState.PseudoSleep, false); f.Controller.Update(condition.Item1, condition.Item2, condition.Item3); Assert(f.Volumes["speaker"] == .42f && f.Saved.Count == 0); } }),
        ("A restarted audio controller recovers saved endpoint volumes", () => { var f = new AudioFixture(); f.Controller.Update(AppState.PseudoSleep, false); new AudioController(f, f, f).Restore(); Assert(f.Volumes["speaker"] == .42f && f.Saved.Count == 0); }),
        ("Default output changes preserve separate original volumes", () => { var f = new AudioFixture(); f.Controller.Update(AppState.PseudoSleep, false); f.Defaults = ["headphones"]; f.Controller.Update(AppState.PseudoSleep, false); Assert(f.Volumes["headphones"] == 0 && f.Saved.Count == 2); f.Controller.Restore(); Assert(f.Volumes["speaker"] == .42f && f.Volumes["headphones"] == .7f); }),
        ("Unavailable output retains only its recovery entry for retry", () => { var f = new AudioFixture { Defaults = ["speaker", "headphones"] }; f.Controller.Update(AppState.PseudoSleep, false); f.FailAt = "set:speaker:0.42"; Throws(f.Controller.Restore); Assert(f.Saved.Single().EndpointId == "speaker" && f.Volumes["headphones"] == .7f); f.FailAt = ""; f.Controller.Restore(); Assert(f.Saved.Count == 0 && f.Volumes["speaker"] == .42f); }),
        ("Audio journal failure prevents zeroing any volume", () => { var f = new AudioFixture { FailAt = "save" }; Throws(() => f.Controller.Update(AppState.PseudoSleep, false)); Assert(f.Volumes["speaker"] == .42f && !f.Calls.Any(c => c.StartsWith("set:"))); }),
        ("Audio guardian failure prevents volume mutation", () => { var f = new AudioFixture { FailAt = "guardian" }; Throws(() => f.Controller.Update(AppState.PseudoSleep, false)); Assert(f.Volumes["speaker"] == .42f && f.Saved.Count == 1); }),
        ("Already zero volume is restored as zero", () => { var f = new AudioFixture(); f.Volumes["speaker"] = 0; f.Controller.Update(AppState.PseudoSleep, false); f.Controller.Restore(); Assert(f.Volumes["speaker"] == 0 && f.Saved.Count == 0); }),
        ("Invalid saved volume is rejected before audio changes", () => { foreach (var volume in new[] { float.NaN, float.PositiveInfinity, -1f, 2f }) { var f = new AudioFixture { Saved = [new("speaker", volume)] }; Throws(f.Controller.Restore); Assert(!f.Calls.Any(c => c.StartsWith("set:"))); } }),
        ("Failed audio write retains a durable original for later recovery", () => { var f = new AudioFixture { FailAt = "set:speaker:0" }; Throws(() => f.Controller.Update(AppState.PseudoSleep, false)); Assert(f.Saved.Single().Volume == .42f); f.FailAt = ""; f.Controller.Restore(); Assert(f.Saved.Count == 0); }),
        ("A failed recovery journal clear remains retryable", () => { var f = new AudioFixture(); f.Controller.Update(AppState.PseudoSleep, false); f.FailAt = "save"; Throws(f.Controller.Restore); Assert(f.Saved.Count == 1 && f.Volumes["speaker"] == .42f); f.FailAt = ""; f.Controller.Restore(); Assert(f.Saved.Count == 0); }),
        ("No output device makes no changes", () => { var f = new AudioFixture { Defaults = [] }; f.Controller.Update(AppState.PseudoSleep, false); Assert(f.Calls.Count == 0); }),
        ("Audio feature defaults on for existing configuration files", () => Assert(System.Text.Json.JsonSerializer.Deserialize<AppConfig>("{}")!.SilenceAudioWhenDisconnected)),
        ("Audio recovery journal survives reload and removes completed backups", () => { var path = Path.GetTempFileName(); File.Delete(path); try { IAudioJournal journal = new AudioRecoveryJournal(path); journal.Write([new("speaker", .42f), new("headphones", .7f)]); IAudioJournal reloaded = new AudioRecoveryJournal(path); Assert(reloaded.Read().SequenceEqual([new AudioVolumeSnapshot("speaker", .42f), new AudioVolumeSnapshot("headphones", .7f)])); reloaded.Write([]); Assert(!File.Exists(path)); } finally { File.Delete(path); } }),
        ("Corrupted audio journal is preserved for recovery", () => { var path = Path.GetTempFileName(); try { File.WriteAllText(path, "{\"version\":1,\"outputs\":null}"); IAudioJournal journal = new AudioRecoveryJournal(path); Throws(() => journal.Read()); Assert(File.Exists(path)); } finally { File.Delete(path); } }),
        ("Duplicate saved endpoints are rejected before audio changes", () => { var f = new AudioFixture { Saved = [new("speaker", .42f), new("speaker", .7f)] }; Throws(f.Controller.Restore); Assert(f.Calls.Count == 0); }),
        ("Passive connection monitor recognizes a client connected before sleep", () => WithLog((path, monitor) => { Append(path, "Sunshine version: test"); Append(path, "CLIENT CONNECTED"); Assert(monitor.Poll(path, true) == true); Append(path, "CLIENT DISCONNECTED"); Assert(monitor.Poll(path, true) == false); })),
        ("Passive connection monitor counts multiple clients", () => WithLog((path, monitor) => { Append(path, "Sunshine version: test"); Append(path, "CLIENT CONNECTED"); Append(path, "CLIENT CONNECTED"); Assert(monitor.Poll(path, true) == true); Append(path, "CLIENT DISCONNECTED"); Assert(monitor.Poll(path, true) == true); Append(path, "CLIENT DISCONNECTED"); Assert(monitor.Poll(path, true) == false); })),
        ("Missing connection history is unknown rather than disconnected", () => WithLog((path, monitor) => { Assert(monitor.Poll(path, true) == null); Append(path, "CLIENT DISCONNECTED"); Assert(monitor.Poll(path, true) == null); Assert(monitor.Poll(path, false) == false); })),
        ("Partial connection line keeps the previous connection state", () => WithLog((path, monitor) => { Append(path, "Sunshine version: test"); File.AppendAllText(path, "[time]: Info: CLIENT CONNE"); Assert(monitor.Poll(path, true) == false); File.AppendAllText(path, "CTED\n"); Assert(monitor.Poll(path, true) == true); })),
        ("Sunshine restart discards old connected clients", () => WithLog((path, monitor) => { Append(path, "Sunshine version: test"); Append(path, "CLIENT CONNECTED"); Assert(monitor.Poll(path, true) == true); File.WriteAllText(path, "[time]: Info: Sunshine version: next\n"); Assert(monitor.Poll(path, true) == false); })),
        ("Unrelated connection text is ignored", () => WithLog((path, monitor) => { Append(path, "Sunshine version: test"); File.AppendAllText(path, "[time]: Warning: CLIENT CONNECTED\n"); Assert(monitor.Poll(path, true) == false); })),
        ("Unreadable Sunshine log cannot leave a cached disconnected state", () => WithLog((path, monitor) => { Append(path, "Sunshine version: test"); Assert(monitor.Poll(path, true) == false); File.Delete(path); Assert(monitor.Poll(path, true) == null); })),
    ];

    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Audio assertion failed."); }
    private static void Throws(Action action) { try { action(); } catch { return; } throw new InvalidOperationException("Expected audio failure."); }
    private static void Append(string path, string value) => File.AppendAllText(path, "[time]: Info: " + value + "\n");
    private static void WithLog(Action<string, SunshineConnectionMonitor> test) { var path = Path.GetTempFileName(); try { test(path, new()); } finally { File.Delete(path); } }

    private sealed class AudioFixture : IAudioBackend, IAudioJournal, IGuardian
    {
        internal List<string> Calls { get; } = [];
        internal Dictionary<string, float> Volumes { get; } = new() { ["speaker"] = .42f, ["headphones"] = .7f };
        internal string[] Defaults { get; set; } = ["speaker"];
        internal List<AudioVolumeSnapshot> Saved { get; set; } = [];
        internal string FailAt { get; set; } = "";
        internal AudioController Controller { get; }
        internal AudioFixture() => Controller = new(this, this, this);
        private void Call(string value) { Calls.Add(value); if (FailAt == value) throw new IOException("Injected audio failure: " + value); }
        IReadOnlyList<AudioVolumeSnapshot> IAudioBackend.GetDefaultVolumes() => Defaults.Select(id => new AudioVolumeSnapshot(id, Volumes[id])).ToArray();
        void IAudioBackend.SetVolume(string id, float volume) { Call("set:" + id + ":" + volume.ToString(System.Globalization.CultureInfo.InvariantCulture)); Volumes[id] = volume; }
        IReadOnlyList<AudioVolumeSnapshot> IAudioJournal.Read() => Saved.ToArray();
        void IAudioJournal.Write(IReadOnlyList<AudioVolumeSnapshot> snapshots) { Call("save"); Saved = snapshots.ToList(); }
        void IGuardian.EnsureReady() => Call("guardian");
    }
}
