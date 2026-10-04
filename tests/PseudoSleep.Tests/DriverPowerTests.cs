using PseudoSleep;
using PseudoSleep.Core;

internal static class DriverPowerTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    [
        ("Driver policy rejects persistent virtual output before any display operation", () => { var config = new AppConfig { VirtualDisplayDevicePath = "virtual", VirtualDisplayDriverInstanceId = "driver", KeepVirtualDisplayInNormalMode = true }; Throws(config.Validate); }),
        ("Driver policy requires an explicitly selected display", () => Throws(new AppConfig { VirtualDisplayDriverInstanceId = "driver" }.Validate)),
        ("Disabled driver stays disabled during repeated normal startup", () => { var requests = 0; var driver = new VirtualDisplayDriver(_ => false, (_, _) => requests++, () => { }); driver.SetEnabled("driver", false); driver.SetEnabled("driver", false); Assert(requests == 0); }),
        ("Explicit entry enables driver and restoration disables it", () => { bool? enabled = false; var requests = new List<bool>(); var driver = new VirtualDisplayDriver(_ => enabled, (_, value) => { requests.Add(value); enabled = value; }, () => { }); driver.SetEnabled("driver", true); driver.SetEnabled("driver", true); driver.SetEnabled("driver", false); Assert(requests.SequenceEqual(new[] { true, false }) && enabled == false); }),
        ("Driver task waits for PnP completion", () => { bool? enabled = false; var waits = 0; var driver = new VirtualDisplayDriver(_ => enabled, (_, _) => enabled = null, () => { if (++waits == 3) enabled = true; }); driver.SetEnabled("driver", true); Assert(waits == 3); }),
        ("Failed driver task never reports success", () => { var driver = new VirtualDisplayDriver(_ => false, (_, _) => throw new IOException("Task denied"), () => { }); Throws(() => driver.SetEnabled("driver", true)); }),
        ("Unfinished driver change times out with bounded polling", () => { var waits = 0; var driver = new VirtualDisplayDriver(_ => null, (_, _) => { }, () => waits++); Throws(() => driver.SetEnabled("driver", false)); Assert(waits == 100); }),
        ("Unconfigured driver requires no native operations", () => { var driver = new VirtualDisplayDriver(_ => throw new Exception(), (_, _) => throw new Exception(), () => throw new Exception()); driver.SetEnabled("", false); driver.SetEnabled("", true); }),
        ("Recovery journal keeps driver identity independent of editable settings", () => { var backup = new DisplayBackup("paths", "modes", ["physical"], DateTimeOffset.UtcNow) { VirtualDisplayDriverInstanceId = "driver" }; var json = System.Text.Json.JsonSerializer.Serialize(new RecoveryRecord(1, AppState.PseudoSleep, backup, DateTimeOffset.UtcNow), Storage.Json); var restored = System.Text.Json.JsonSerializer.Deserialize<RecoveryRecord>(json, Storage.Json)!; Assert(restored.Backup.VirtualDisplayDriverInstanceId == "driver"); }),
        ("Driver task identity is case insensitive and device specific", () => { Assert(VirtualDisplayDriver.TaskName(@"ROOT\DISPLAY\0001", true) == VirtualDisplayDriver.TaskName(@"root\display\0001", true)); Assert(VirtualDisplayDriver.TaskName("one", true) != VirtualDisplayDriver.TaskName("two", true)); Assert(VirtualDisplayDriver.TaskName("one", true) != VirtualDisplayDriver.TaskName("one", false)); }),
    ];

    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Driver power assertion failed."); }
    private static void Throws(Action action) { try { action(); } catch { return; } throw new InvalidOperationException("Expected failure."); }
}
