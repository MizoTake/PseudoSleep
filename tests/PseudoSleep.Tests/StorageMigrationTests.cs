using System.Text.Json;
using PseudoSleep;
using PseudoSleep.Core;

internal static class StorageMigrationTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    [
        ("Host settings and recovery files stay outside redirected AppData", () => { var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pseudosleep"); Assert(Storage.ConfigDirectory == expected && Storage.DataDirectory == expected); }),
        ("Migration preserves the selected driver and audio preferences byte for byte", () => WithFiles((source, data, target) => { var before = File.ReadAllBytes(source); StorageMigration.Import(source, data, target); Assert(before.SequenceEqual(File.ReadAllBytes(target))); var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(target), Storage.Json)!; Assert(config.VirtualDisplayDriverInstanceId == "ROOT\\DISPLAY\\TEST" && !config.KeepVirtualDisplayInNormalMode && config.SilenceAudioWhenDisconnected); })),
        ("Migration refuses to replace existing settings with a different copy", () => WithFiles((source, data, target) => { File.WriteAllText(target, "existing settings"); Throws(() => StorageMigration.Import(source, data, target)); Assert(File.ReadAllText(target) == "existing settings"); })),
        ("Retrying the same migration preserves the already imported file", () => WithFiles((source, data, target) => { StorageMigration.Import(source, data, target); var stamp = File.GetLastWriteTimeUtc(target); StorageMigration.Import(source, data, target); Assert(File.GetLastWriteTimeUtc(target) == stamp); })),
        ("Unfinished display or audio restoration prevents migration", () => WithFiles((source, data, target) => { foreach (var name in new[] { "state.json", "audio-state.json" }) { File.WriteAllText(Path.Combine(data, name), "pending"); Throws(() => StorageMigration.Import(source, data, target)); Assert(!File.Exists(target)); File.Delete(Path.Combine(data, name)); } })),
        ("Invalid settings cannot replace the shared configuration", () => WithFiles((source, data, target) => { File.WriteAllText(source, "{broken"); Throws(() => StorageMigration.Import(source, data, target)); Assert(!File.Exists(target)); })),
        ("Migration leaves the original settings and recovery directory intact", () => WithFiles((source, data, target) => { File.WriteAllText(Path.Combine(data, "display-backup.json"), "previous backup"); StorageMigration.Import(source, data, target); Assert(File.Exists(source) && File.ReadAllText(Path.Combine(data, "display-backup.json")) == "previous backup"); })),
    ];

    private static void WithFiles(Action<string, string, string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "PseudoSleep.Migration." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var data = Directory.CreateDirectory(Path.Combine(root, "old-data")).FullName;
        var source = Path.Combine(root, "old-config.json");
        File.WriteAllText(source, JsonSerializer.Serialize(new AppConfig { VirtualDisplayDevicePath = "virtual", VirtualDisplayDriverInstanceId = @"ROOT\DISPLAY\TEST", WakeDevices = ["HID\\physical"] }, Storage.Json));
        try { action(source, data, Path.Combine(root, "config.json")); }
        finally { Directory.Delete(root, true); }
    }

    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Storage migration expectation failed."); }
    private static void Throws(Action action) { try { action(); } catch { return; } throw new InvalidOperationException("Expected migration rejection."); }
}
