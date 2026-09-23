namespace PseudoSleep.Core;

public static class ConfigurationDiscovery
{
    public static bool Complete(AppConfig config, IReadOnlyList<DisplayInfo> displays, string? sunshineOutput, IEnumerable<string> physicalInputs)
    {
        var changed = false;
        var virtuals = displays.Where(d => d.Indirect).ToArray();
        if (string.IsNullOrWhiteSpace(config.VirtualDisplayDevicePath) && virtuals.Length == 1) { config.VirtualDisplayDevicePath = virtuals[0].DevicePath; changed = true; }
        if (string.IsNullOrWhiteSpace(config.VirtualDisplayDeviceId) && Guid.TryParse(sunshineOutput, out _) && virtuals.Any(d => string.Equals(d.DevicePath, config.VirtualDisplayDevicePath, StringComparison.OrdinalIgnoreCase))) { config.VirtualDisplayDeviceId = sunshineOutput!; changed = true; }
        if (config.WakeDevices.Count == 0) { var inputs = physicalInputs.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); if (inputs.Count > 0) { config.WakeDevices = inputs; changed = true; } }
        return changed;
    }
}
