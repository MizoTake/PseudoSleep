using System.ComponentModel;
using System.Runtime.InteropServices;
using PseudoSleep.Core;
using static PseudoSleep.DisplayNative;

namespace PseudoSleep;

internal sealed class DisplayManager : IDisplayBackend
{
    internal static (DisplayPath[] Paths, Mode[] Modes) Query(bool all = false)
    {
        uint flags = all ? 1u : 2u;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            Check(GetDisplayConfigBufferSizes(flags, out var count, out var modeCount), "Display buffer sizes");
            var paths = new DisplayPath[count];
            var modes = new Mode[modeCount];
            var error = QueryDisplayConfig(flags, ref count, paths, ref modeCount, modes, 0);
            if (error == 122) continue;
            Check(error, "QueryDisplayConfig");
            return (paths.Take((int)count).ToArray(), modes.Take((int)modeCount).ToArray());
        }
        throw new InvalidOperationException("Display configuration is changing; retry after it settles.");
    }

    private static TargetName Name(DisplayPath path)
    {
        var name = new TargetName { Header = new() { Type = 2, Size = (uint)Marshal.SizeOf<TargetName>(), Adapter = path.Target.Adapter, Id = path.Target.Id }, Name = "", Path = "" };
        Check(GetTargetName(ref name), "Display target name");
        return name;
    }

    internal static List<DisplayInfo> Enumerate()
    {
        var snapshot = Query(true);
        return snapshot.Paths.Where(p => p.Target.Available != 0).GroupBy(p => Name(p).Path, StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            var path = g.OrderByDescending(p => (p.Flags & 1) != 0).First();
            var name = Name(path);
            var source = new SourceName { Header = new() { Type = 1, Size = (uint)Marshal.SizeOf<SourceName>(), Adapter = path.Source.Adapter, Id = path.Source.Id }, Name = "" };
            _ = GetSourceName(ref source);
            var mode = path.Source.ModeIndex < snapshot.Modes.Length ? snapshot.Modes[path.Source.ModeIndex] : default;
            return new DisplayInfo(name.Path, name.Name, source.Name, (path.Flags & 1) != 0, IsVirtual(path), mode.Width, mode.Height, mode.X, mode.Y, path.Target.Refresh.Denominator == 0 ? 0 : (double)path.Target.Refresh.Numerator / path.Target.Refresh.Denominator);
        }).ToList();
    }

    DisplayBackup IDisplayBackend.Capture(AppConfig config)
    {
        var snapshot = Query();
        var paths = snapshot.Paths.Where(p => config.KeepVirtualDisplayInNormalMode || !Same(Name(p).Path, config.VirtualDisplayDevicePath)).ToArray();
        if (paths.Length == 0) throw new InvalidOperationException("No local display to back up. Restore physical displays before entering sleep.");
        if (!paths.Any(p => !IsVirtual(p) && p.Source.ModeIndex < snapshot.Modes.Length && snapshot.Modes[p.Source.ModeIndex].X == 0 && snapshot.Modes[p.Source.ModeIndex].Y == 0)) throw new InvalidOperationException("Make a physical display primary before entering sleep.");
        return new(Convert.ToBase64String(MemoryMarshal.AsBytes(paths.AsSpan())), Convert.ToBase64String(MemoryMarshal.AsBytes(snapshot.Modes.AsSpan())), paths.Select(p => Name(p).Path).ToList(), DateTimeOffset.UtcNow);
    }

    void IDisplayBackend.ConfigureNormalDisplay(AppConfig config)
    {
        var original = Query();
        if (!original.Paths.Any(p => !IsVirtual(p) && p.Source.ModeIndex < original.Modes.Length && original.Modes[p.Source.ModeIndex].X == 0 && original.Modes[p.Source.ModeIndex].Y == 0)) throw new InvalidOperationException("Normal streaming requires a physical primary display.");
        var localPaths = original.Paths.Where(p => !Same(Name(p).Path, config.VirtualDisplayDevicePath)).ToArray();
        var backup = Snapshot(original.Paths, original.Modes);
        try
        {
            if (!config.KeepVirtualDisplayInNormalMode)
            {
                if (localPaths.Length != original.Paths.Length) { Apply(localPaths, original.Modes, true); VerifyUnchanged(localPaths, original.Modes); Storage.Log("Normal display layout: virtual output disabled by setting."); }
                return;
            }
            var wasActive = original.Paths.Any(p => Same(Name(p).Path, config.VirtualDisplayDevicePath));
            ((IDisplayBackend)this).EnableVirtual(config);
            if (!wasActive)
            {
                var current = Query();
                var virtualPath = current.Paths.Single(p => Same(Name(p).Path, config.VirtualDisplayDevicePath));
                var source = current.Modes[virtualPath.Source.ModeIndex];
                source.X = localPaths.Max(p => RightEdge(p, original.Modes[p.Source.ModeIndex]));
                source.Y = 0;
                virtualPath.Source.ModeIndex = (uint)original.Modes.Length;
                virtualPath.Target.ModeIndex = uint.MaxValue;
                Apply([.. localPaths, virtualPath], [.. original.Modes, source], true);
            }
            ((IDisplayBackend)this).ChangeVirtualResolution(config);
            VerifyUnchanged(localPaths, original.Modes);
            Storage.Log("Normal display layout: physical displays retained; streaming stays on the extended virtual display.");
        }
        catch { ((IDisplayBackend)this).Restore(backup); throw; }
    }

    void IDisplayBackend.EnableVirtual(AppConfig config)
    {
        var all = Query(true);
        var candidates = all.Paths.Where(p => p.Target.Available != 0 && Same(Name(p).Path, config.VirtualDisplayDevicePath)).ToArray();
        if (candidates.Length == 0) throw new InvalidOperationException("Configured virtual display is unavailable.");
        if (candidates.Any(p => !IsVirtual(p))) throw new InvalidOperationException("The selected display is not an indirect virtual display.");
        if (candidates.Any(p => (p.Flags & 1) != 0)) return;
        var active = Query().Paths;
        var candidate = candidates.FirstOrDefault(p => !active.Any(a => a.Source.Adapter.Equals(p.Source.Adapter) && a.Source.Id == p.Source.Id));
        if (candidate.Target.Available == 0) throw new InvalidOperationException("No free display source for the virtual monitor.");
        ApplyTopology([.. active, candidate]);
        WaitFor(() => Enumerate().Any(d => Same(d.DevicePath, config.VirtualDisplayDevicePath) && d.Active), "Virtual display activation");
        Storage.Log("Virtual display activated while original displays remain active.");
    }

    void IDisplayBackend.VerifyVirtual(AppConfig config)
    {
        var display = Enumerate().SingleOrDefault(d => Same(d.DevicePath, config.VirtualDisplayDevicePath));
        if (display is not { Active: true, Indirect: true } || display.Width == 0 || display.Height == 0) throw new InvalidOperationException("Virtual display has no active desktop surface.");
        CaptureProbe.Verify(display.SourceName);
    }

    void IDisplayBackend.ShowVirtualOnly(AppConfig config)
    {
        var snapshot = Query();
        var path = snapshot.Paths.Single(p => Same(Name(p).Path, config.VirtualDisplayDevicePath));
        if (path.Source.ModeIndex >= snapshot.Modes.Length) throw new InvalidOperationException("Virtual source mode unavailable.");
        var source = snapshot.Modes[path.Source.ModeIndex];
        source.Width = (uint)config.Width;
        source.Height = (uint)config.Height;
        source.X = 0;
        source.Y = 0;
        path.Source.ModeIndex = 0;
        path.Target.ModeIndex = uint.MaxValue;
        path.Target.Refresh = new() { Numerator = (uint)config.RefreshRate, Denominator = 1 };
        path.Flags = 1;
        Apply([path], [source]);
        WaitFor(() => { var active = Enumerate().Where(d => d.Active).ToArray(); return active.Length == 1 && Same(active[0].DevicePath, config.VirtualDisplayDevicePath); }, "Physical display deactivation");
        var result = Enumerate().Single(d => d.Active);
        if (result.Width != config.Width || result.Height != config.Height || Math.Abs(result.RefreshRate - config.RefreshRate) > 1) throw new InvalidOperationException($"Requested display mode was not accepted: {result.Width}x{result.Height}@{result.RefreshRate:F2}");
        Storage.Log($"Virtual only: {result.Width}x{result.Height}@{result.RefreshRate:F2}");
    }

    void IDisplayBackend.ChangeVirtualResolution(AppConfig config)
    {
        var active = Enumerate().Where(d => d.Active).ToArray();
        var target = active.SingleOrDefault(d => Same(d.DevicePath, config.VirtualDisplayDevicePath));
        if (target is not { Indirect: true }) throw new InvalidOperationException("Client resize requires the configured virtual display to be active.");
        if (!SupportedModes(target.SourceName).Any(m => m.Width == config.Width && m.Height == config.Height && Math.Abs((double)m.Frequency - config.RefreshRate) <= 1)) throw new InvalidOperationException($"Virtual display driver does not advertise {config.Width}x{config.Height}@{config.RefreshRate}. Add this mode using scripts/Set-VirtualModes.ps1.");
        if (target.Width == config.Width && target.Height == config.Height && Math.Abs(target.RefreshRate - config.RefreshRate) <= 1) return;
        var original = Query();
        var paths = original.Paths.ToArray();
        var modes = original.Modes.ToArray();
        var index = Array.FindIndex(paths, p => Same(Name(p).Path, config.VirtualDisplayDevicePath));
        var sourceIndex = paths[index].Source.ModeIndex;
        if (paths.Count(p => p.Source.Adapter.Equals(paths[index].Source.Adapter) && p.Source.Id == paths[index].Source.Id) != 1) throw new InvalidOperationException("The virtual display must be extended, not cloned with a physical display.");
        modes[sourceIndex].Width = (uint)config.Width;
        modes[sourceIndex].Height = (uint)config.Height;
        paths[index].Target.ModeIndex = uint.MaxValue;
        paths[index].Target.Refresh = new() { Numerator = (uint)config.RefreshRate, Denominator = 1 };
        var retained = original.Paths.Where(p => !Same(Name(p).Path, config.VirtualDisplayDevicePath)).ToArray();
        if (retained.Length > 0) { modes[sourceIndex].X = retained.Max(p => RightEdge(p, original.Modes[p.Source.ModeIndex])); modes[sourceIndex].Y = 0; }
        try
        {
            Apply(paths, modes, retained.Length > 0);
            VerifyUnchanged(retained, original.Modes);
            var result = Enumerate().Single(d => Same(d.DevicePath, config.VirtualDisplayDevicePath));
            if (!result.Active || result.Width != config.Width || result.Height != config.Height || Math.Abs(result.RefreshRate - config.RefreshRate) > 1) throw new InvalidOperationException("Virtual display did not accept the requested mode.");
            ((IDisplayBackend)this).VerifyVirtual(config);
        }
        catch
        {
            ((IDisplayBackend)this).Restore(Snapshot(original.Paths, original.Modes));
            throw;
        }
    }

    private static DisplayBackup Snapshot(DisplayPath[] paths, Mode[] modes) => new(Convert.ToBase64String(MemoryMarshal.AsBytes(paths.AsSpan())), Convert.ToBase64String(MemoryMarshal.AsBytes(modes.AsSpan())), paths.Select(p => Name(p).Path).ToList(), DateTimeOffset.UtcNow);
    private static int RightEdge(DisplayPath path, Mode mode) => checked(mode.X + (int)(path.Target.Rotation is 2 or 4 ? mode.Height : mode.Width));

    private static void VerifyUnchanged(DisplayPath[] expectedPaths, Mode[] expectedModes)
    {
        var actual = Query();
        foreach (var path in expectedPaths)
        {
            var match = actual.Paths.SingleOrDefault(p => Same(Name(p).Path, Name(path).Path));
            if (match.Target.Available == 0 || match.Source.ModeIndex >= actual.Modes.Length) throw new InvalidOperationException("An existing display was deactivated unexpectedly.");
            var expected = expectedModes[path.Source.ModeIndex];
            var result = actual.Modes[match.Source.ModeIndex];
            if (expected.Width != result.Width || expected.Height != result.Height || expected.X != result.X || expected.Y != result.Y || path.Target.Rotation != match.Target.Rotation || Math.Abs((double)path.Target.Refresh.Numerator / path.Target.Refresh.Denominator - (double)match.Target.Refresh.Numerator / match.Target.Refresh.Denominator) > 0.02) throw new InvalidOperationException("An existing display mode or position changed unexpectedly.");
        }
    }

    internal static List<DeviceMode> SupportedModes(string sourceName)
    {
        var result = new List<DeviceMode>();
        for (uint index = 0; index < 4096; index++) { var mode = new DeviceMode { Size = (ushort)Marshal.SizeOf<DeviceMode>() }; if (!EnumDisplaySettings(sourceName, index, ref mode, 0)) break; result.Add(mode); }
        return result;
    }

    void IDisplayBackend.Restore(DisplayBackup backup)
    {
        var paths = Decode<DisplayPath>(backup.Paths);
        var modes = Decode<Mode>(backup.Modes);
        if (paths.Length == 0 || paths.Length != backup.DevicePaths.Count) throw new InvalidDataException("Invalid display backup.");
        var current = Query(true);
        var remapped = new DisplayPath[paths.Length];
        for (var i = 0; i < paths.Length; i++)
        {
            var original = paths[i];
            var match = current.Paths.Where(p => p.Target.Available != 0 && Same(Name(p).Path, backup.DevicePaths[i])).OrderByDescending(p => p.Source.Id == original.Source.Id).FirstOrDefault();
            if (match.Target.Available == 0) { EmergencyPhysicalDisplays(); throw new InvalidOperationException($"Saved display is disconnected; one available physical display was enabled: {backup.DevicePaths[i]}"); }
            var sourceIndex = original.Source.ModeIndex;
            var targetIndex = original.Target.ModeIndex;
            if (sourceIndex < modes.Length) { modes[sourceIndex].Adapter = match.Source.Adapter; modes[sourceIndex].Id = match.Source.Id; }
            if (targetIndex < modes.Length) { modes[targetIndex].Adapter = match.Target.Adapter; modes[targetIndex].Id = match.Target.Id; }
            original.Source.Adapter = match.Source.Adapter;
            original.Source.Id = match.Source.Id;
            original.Target.Adapter = match.Target.Adapter;
            original.Target.Id = match.Target.Id;
            remapped[i] = original;
        }
        Apply(remapped, modes, true);
        WaitFor(() => { var active = Enumerate().Where(d => d.Active).Select(d => d.DevicePath).ToHashSet(StringComparer.OrdinalIgnoreCase); return active.SetEquals(backup.DevicePaths); }, "Restore display topology");
        var restored = Query();
        for (var i = 0; i < paths.Length; i++)
        {
            var actualPath = restored.Paths.Single(p => Same(Name(p).Path, backup.DevicePaths[i]));
            var expected = modes[remapped[i].Source.ModeIndex];
            var actual = restored.Modes[actualPath.Source.ModeIndex];
            var expectedHz = (double)remapped[i].Target.Refresh.Numerator / remapped[i].Target.Refresh.Denominator;
            var actualHz = (double)actualPath.Target.Refresh.Numerator / actualPath.Target.Refresh.Denominator;
            if (expected.Width != actual.Width || expected.Height != actual.Height || expected.X != actual.X || expected.Y != actual.Y || actualPath.Target.Rotation != remapped[i].Target.Rotation || Math.Abs(expectedHz - actualHz) > 0.02) throw new InvalidOperationException($"Display is active but the saved mode or position was not restored: {backup.DevicePaths[i]}");
        }
        _ = PostMessage(0xffff, 0x112, 0xf170, -1);
        Storage.Log("Saved display topology restored; original primary and positions requested.");
    }

    internal static void EmergencyPhysicalDisplays()
    {
        var physical = Query(true).Paths.Where(p => p.Target.Available != 0 && !IsVirtual(p)).GroupBy(p => Name(p).Path).Select(g => g.First()).ToArray();
        if (physical.Length == 0) throw new InvalidOperationException("No physical display available for emergency recovery.");
        ApplyTopology([physical[0]]);
        Storage.Log("Emergency fallback enabled a physical display; exact saved topology still requires recovery.");
    }

    private static T[] Decode<T>(string value) where T : unmanaged
    {
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length % Marshal.SizeOf<T>() != 0) throw new InvalidDataException("Invalid native display backup size.");
        return MemoryMarshal.Cast<byte, T>(bytes.AsSpan()).ToArray();
    }

    private static void Apply(DisplayPath[] paths, Mode[] modes, bool persist = false)
    {
        Check(SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, 0x40 | 0x20 | 0x400), "Validate display configuration");
        Check(SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, 0x80 | 0x20 | 0x400 | (persist ? 0x200u : 0)), "Apply display configuration");
    }

    private static void ApplyTopology(DisplayPath[] paths)
    {
        for (var i = 0; i < paths.Length; i++) { paths[i].Source.ModeIndex = uint.MaxValue; paths[i].Target.ModeIndex = uint.MaxValue; paths[i].Flags = 1; }
        Check(SetDisplayConfig((uint)paths.Length, paths, 0, null, 0x80 | 0x10 | 0x2000), "Activate display topology");
    }

    private static void WaitFor(Func<bool> predicate, string operation)
    {
        for (var attempt = 0; attempt < 30; attempt++) { if (predicate()) return; Thread.Sleep(100); }
        throw new TimeoutException(operation);
    }

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool IsVirtual(DisplayPath path)
    {
        if (path.Target.Technology == 17) return true;
        var adapter = new AdapterName { Header = new() { Type = 4, Size = (uint)Marshal.SizeOf<AdapterName>(), Adapter = path.Target.Adapter }, Path = "" };
        if (GetAdapterName(ref adapter) != 0 || !adapter.Path.StartsWith("\\\\?\\", StringComparison.Ordinal)) return false;
        var parts = adapter.Path[4..].Split('#');
        if (parts.Length < 4 || !parts[0].Equals("ROOT", StringComparison.OrdinalIgnoreCase)) return false;
        using var device = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Enum\\" + string.Join('\\', parts.Take(3)));
        return device?.GetValue("UpperFilters") is string[] filters && filters.Contains("IndirectKmd", StringComparer.OrdinalIgnoreCase);
    }
    private static void Check(int code, string operation) { if (code != 0) throw new Win32Exception(code, $"{operation}: {new Win32Exception(code).Message} ({code})"); }
}
