namespace PseudoSleep.Core;

public enum AppState { Normal, EnteringPseudoSleep, PseudoSleep, Waking, Recovery, Error }

public sealed class AppConfig
{
    public string VirtualDisplayDevicePath { get; set; } = "";
    public string VirtualDisplayDeviceId { get; set; } = "";
    public int WakeGuardMs { get; set; } = 3000;
    public List<string> WakeDevices { get; set; } = [];
    public List<string> IgnoredDevices { get; set; } = ["*Sunshine*", "*Virtual*", "*RDP*"];
    public bool RestoreOnStartup { get; set; } = true;
    public bool PreventSystemSleep { get; set; } = true;
    public int Width { get; set; } = 2560;
    public int Height { get; set; } = 1600;
    public int RefreshRate { get; set; } = 120;
    public bool FollowClientResolution { get; set; } = true;
    public bool KeepVirtualDisplayInNormalMode { get; set; }
    public bool SleepOnMoonlightConnect { get; set; } = true;
    public bool DisconnectMoonlightOnWake { get; set; } = true;
    public int LastClientWidth { get; set; }
    public int LastClientHeight { get; set; }
    public SunshineConfig Sunshine { get; set; } = new();

    public void Validate()
    {
        if (WakeGuardMs is < 0 or > 10000) throw new ArgumentException("WakeGuardMs must be 0..10000.");
        if (Width is < 640 or > 16384 || Height is < 480 or > 16384 || RefreshRate is < 24 or > 1000) throw new ArgumentException("Invalid display mode.");
        if (WakeDevices is null || IgnoredDevices is null || Sunshine is null) throw new ArgumentException("Configuration lists and Sunshine must not be null.");
        if (WakeDevices.Any(p => string.IsNullOrWhiteSpace(p) || p.All(c => c == '*'))) throw new ArgumentException("Wake devices must identify specific devices.");
        if ((LastClientWidth != 0 || LastClientHeight != 0) && (LastClientWidth is < 640 or > 16384 || LastClientHeight is < 480 or > 16384)) throw new ArgumentException("Invalid last client resolution.");
    }

    public AppConfig WithResolution(int width, int height) { var copy = (AppConfig)MemberwiseClone(); copy.Width = width; copy.Height = height; copy.Validate(); return copy; }
}

public sealed class SunshineConfig
{
    public string ServiceName { get; set; } = "SunshineService";
    public string WebUi { get; set; } = "https://localhost:47990";
    public string ConfigPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sunshine", "config", "sunshine.conf");
}

public sealed record DisplayInfo(string DevicePath, string Name, string SourceName, bool Active, bool Indirect, uint Width, uint Height, int X, int Y, double RefreshRate);
public sealed record DisplayBackup(string Paths, string Modes, List<string> DevicePaths, DateTimeOffset SavedAt);
public sealed record RecoveryRecord(int Version, AppState State, DisplayBackup Backup, DateTimeOffset SavedAt);

public interface IDisplayBackend
{
    DisplayBackup Capture(AppConfig config);
    void EnableVirtual(AppConfig config);
    void VerifyVirtual(AppConfig config);
    void ShowVirtualOnly(AppConfig config);
    void ChangeVirtualResolution(AppConfig config);
    void ConfigureNormalDisplay(AppConfig config);
    void Restore(DisplayBackup backup);
}

public interface IRecoveryJournal
{
    RecoveryRecord? Read();
    void Save(RecoveryRecord record);
    void Clear();
}

public interface IPowerLease { void Acquire(); void Release(); }
public interface IGuardian { void EnsureReady(); }
public interface IStreamingHost { void EnsureReady(AppConfig config); void Disconnect(AppConfig config); }

public static class WakePolicy
{
    public static bool IsAllowed(string? devicePath, AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(devicePath)) return false;
        return !config.IgnoredDevices.Any(p => Matches(devicePath, p)) && config.WakeDevices.Any(p => Matches(devicePath, p));
    }

    private static bool Matches(string path, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var expression = "\\A" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "\\z";
        return System.Text.RegularExpressions.Regex.IsMatch(path, expression, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
}
