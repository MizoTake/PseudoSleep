using PseudoSleep;
using PseudoSleep.Core;

internal static class SessionTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    [
        ("Default keeps the virtual display inactive outside streaming", () => Assert(!new AppConfig().KeepVirtualDisplayInNormalMode)),
        ("On-demand stream rejects resume-unsafe configuration before display changes", () => { var config = new AppConfig { VirtualDisplayDevicePath = "virtual", WakeDevices = ["HID\\LOCAL"], DisconnectMoonlightOnWake = false }; var fixture = new Fixture(); var rejected = false; try { fixture.Controller.StartStream(2880, 1920, config); } catch (InvalidOperationException) { rejected = true; } Assert(rejected && fixture.Calls.Count == 0); }),
        ("Missing settings discover one virtual display and physical inputs", () => { var c = new AppConfig(); Assert(ConfigurationDiscovery.Complete(c, [Virtual("one")], null, ["HID\\USB"])); Assert(c.VirtualDisplayDevicePath == "one" && c.WakeDevices.SequenceEqual(["HID\\USB"]) && c.VirtualDisplayDeviceId == ""); }),
        ("Discovery preserves configured device and wake choices", () => { var c = new AppConfig { VirtualDisplayDevicePath = "chosen", WakeDevices = ["HID\\chosen"] }; Assert(!ConfigurationDiscovery.Complete(c, [Virtual("other")], null, ["HID\\other"])); Assert(c.VirtualDisplayDevicePath == "chosen" && c.WakeDevices.SequenceEqual(["HID\\chosen"])); }),
        ("Discovery refuses to guess between multiple virtual displays", () => { var c = new AppConfig(); Assert(!ConfigurationDiscovery.Complete(c, [Virtual("one"), Virtual("two")], null, [])); Assert(c.VirtualDisplayDevicePath == ""); }),
        ("Disconnect only ends the final connected session", () => WithLog((path, monitor) => { monitor.Arm(path, 0); Append(path, "CLIENT CONNECTED"); Append(path, "CLIENT CONNECTED"); Assert(!monitor.Poll(1000)); Append(path, "CLIENT DISCONNECTED"); Assert(!monitor.Poll(2000)); Append(path, "CLIENT DISCONNECTED"); Assert(monitor.Poll(3000)); })),
        ("Connection timeout restores a failed stream preparation", () => WithLog((path, monitor) => { monitor.Arm(path, 0); Assert(!monitor.Poll(29999)); Assert(monitor.Poll(30000)); })),
        ("Old log messages do not end a new stream", () => WithLog((path, monitor) => { Append(path, "CLIENT CONNECTED"); Append(path, "CLIENT DISCONNECTED"); monitor.Arm(path, 0); Assert(!monitor.Poll(1000)); })),
        ("Partial log lines are not treated as complete events", () => WithLog((path, monitor) => { monitor.Arm(path, 0); File.AppendAllText(path, "[time]: Info: CLIENT CONNE"); Assert(!monitor.Poll(1000)); File.AppendAllText(path, "CTED\n"); Assert(!monitor.Poll(30000)); Append(path, "CLIENT DISCONNECTED"); Assert(monitor.Poll(31000)); })),
        ("Restarted Sunshine log ends the tracked stream", () => WithLog((path, monitor) => { File.AppendAllText(path, "existing content\n"); monitor.Arm(path, 0); File.WriteAllText(path, ""); Assert(monitor.Poll(1000)); })),
        ("Unrelated text cannot spoof the structured session marker", () => WithLog((path, monitor) => { monitor.Arm(path, 0); Append(path, "CLIENT CONNECTED"); Assert(!monitor.Poll(1000)); File.AppendAllText(path, "[time]: Warning: CLIENT DISCONNECTED\n"); Assert(!monitor.Poll(30000)); monitor.Reset(); Assert(!monitor.Armed && !monitor.Poll(40000)); })),
    ];

    private static DisplayInfo Virtual(string path) => new(path, "virtual", "source", false, true, 0, 0, 0, 0, 0);
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Session assertion failed."); }
    private static void Append(string path, string value) => File.AppendAllText(path, "[time]: Info: " + value + "\n");
    private static void WithLog(Action<string, SunshineSessionMonitor> test) { var path = Path.GetTempFileName(); try { test(path, new()); } finally { File.Delete(path); } }
}
