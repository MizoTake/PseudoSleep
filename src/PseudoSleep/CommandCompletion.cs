using PseudoSleep.Core;

namespace PseudoSleep;

internal static class CommandCompletion
{
    internal static void RememberClientResolution(AppConfig config, int width, int height, Action<AppConfig> save, Action<string> warn)
    {
        config.LastClientWidth = width;
        config.LastClientHeight = height;
        try { save(config); }
        catch (Exception ex) { warn($"Client resolution applied, but could not remember it for the next connection: {ex.Message}"); }
    }

    internal static object ReadStatus(Func<object> read, Action<string> warn)
    {
        try { return read(); }
        catch (Exception ex) { warn($"Command completed, but display status is temporarily unavailable: {ex.Message}"); return new { statusAvailable = false, statusError = ex.Message }; }
    }
}
