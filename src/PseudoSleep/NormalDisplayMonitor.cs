using PseudoSleep.Core;

namespace PseudoSleep;

internal sealed class NormalDisplayMonitor(Func<AppConfig> loadConfig, Func<IReadOnlyList<DisplayInfo>> enumerate, Action<AppConfig> prepare, Action<string> log, Func<AppConfig, bool>? driverNeedsDisable = null)
{
    private long nextCheck;
    private string? lastError;

    internal AppConfig Poll(AppConfig config, AppState state, bool streaming, long now)
    {
        if (state != AppState.Normal || streaming || now < nextCheck) return config;
        nextCheck = now + 2000;
        try
        {
            // A running stream keeps its original configuration until it has finished restoring displays.
            var latest = loadConfig();
            latest.Validate();
            config = latest;
            if (!config.KeepVirtualDisplayInNormalMode && !string.IsNullOrWhiteSpace(config.VirtualDisplayDevicePath) && (driverNeedsDisable?.Invoke(config) == true || enumerate().Any(display => display.Active && display.Indirect && string.Equals(display.DevicePath, config.VirtualDisplayDevicePath, StringComparison.OrdinalIgnoreCase))))
            {
                prepare(config);
                log("Normal display monitor disabled an active virtual output outside streaming.");
            }
            lastError = null;
        }
        catch (Exception ex)
        {
            nextCheck = now + 5000;
            if (lastError != ex.Message) log($"Normal display monitor will retry: {ex.Message}");
            lastError = ex.Message;
        }
        return config;
    }
}
