using System.Runtime.InteropServices;
using System.Text;
using PseudoSleep.Core;

namespace PseudoSleep;

internal static class RuntimeConfiguration
{
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Locate_DevNode(out uint device, string id, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint device, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Device_ID_Size(out uint length, uint device, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_ID(uint device, StringBuilder id, uint length, uint flags);

    internal static AppConfig Load()
    {
        var config = Storage.LoadConfig();
        try
        {
            string? output = null;
            if (File.Exists(config.Sunshine.ConfigPath)) SunshineHost.ReadConfiguration(config.Sunshine.ConfigPath).TryGetValue("output_name", out output);
            var inputs = config.WakeDevices.Count == 0 && string.IsNullOrWhiteSpace(config.VirtualDisplayDevicePath) ? InputWindow.Enumerate().Where(d => IsPhysicalUsb(d.Path)).Select(d => d.Path).ToArray() : [];
            if (ConfigurationDiscovery.Complete(config, DisplayManager.Enumerate(), output, inputs))
            {
                try { Storage.Write(Storage.ConfigPath, config); } catch (Exception ex) { Storage.Log($"Detected configuration could not be saved: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Storage.Log($"Automatic device discovery unavailable: {ex.Message}"); }
        return config;
    }

    private static bool IsPhysicalUsb(string path)
    {
        if (!path.StartsWith(@"\\?\HID#", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = path[4..].Split('#');
        if (parts.Length < 3 || CM_Locate_DevNode(out var device, string.Join("\\", parts.Take(3)), 0) != 0) return false;
        for (var depth = 0; depth < 8; depth++)
        {
            if (CM_Get_Device_ID_Size(out var length, device, 0) != 0 || length > 4096) return false;
            var id = new StringBuilder((int)length + 1);
            if (CM_Get_Device_ID(device, id, length + 1, 0) != 0) return false;
            if (id.ToString().StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)) return true;
            if (CM_Get_Parent(out device, device, 0) != 0) return false;
        }
        return false;
    }
}
