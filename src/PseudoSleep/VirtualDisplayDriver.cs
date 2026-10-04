using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace PseudoSleep;

internal sealed class VirtualDisplayDriver(Func<string, bool?> readEnabled, Action<string, bool> request, Action wait)
{
    [SupportedOSPlatform("windows")] internal static readonly VirtualDisplayDriver System = new(ReadEnabled, Request, () => Thread.Sleep(100));

    internal void SetEnabled(string instanceId, bool enabled)
    {
        if (string.IsNullOrEmpty(instanceId)) return;
        if (readEnabled(instanceId) == enabled) return;
        request(instanceId, enabled);
        for (var attempt = 0; attempt < 100; attempt++) { if (readEnabled(instanceId) == enabled) return; wait(); }
        throw new TimeoutException($"Virtual display driver did not become {(enabled ? "enabled" : "disabled")}: {instanceId}");
    }

    internal static string TaskName(string instanceId, bool enabled) => "PseudoSleep-VDD-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instanceId.ToUpperInvariant())))[..16] + (enabled ? "-Enable" : "-Disable");

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Locate_DevNode(out uint device, string id, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint device, uint flags);

    [SupportedOSPlatform("windows")] internal static bool? ReadEnabled(string instanceId)
    {
        using var key = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Enum\\" + instanceId);
        if (key == null) throw new InvalidOperationException("登録済みのVDDドライバーが見つかりません。削除されている場合はドライバーを再インストールして登録してください。");
        if (key.GetValue("HardwareID") is not string[] ids || !ids.Contains("Root\\MttVDD", StringComparer.OrdinalIgnoreCase) || !string.Equals(key.GetValue("ClassGUID") as string, "{4d36e968-e325-11ce-bfc1-08002be10318}", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("登録先がVDDドライバーと一致しないため、操作を中止しました。");
        if (CM_Locate_DevNode(out var device, instanceId, 0) != 0 || CM_Get_DevNode_Status(out var status, out var problem, device, 0) != 0) return null;
        if (problem == 22) return false;
        return problem == 0 && (status & 8) != 0 ? true : null;
    }

    private static void Request(string instanceId, bool enabled)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/Run");
        start.ArgumentList.Add("/TN");
        start.ArgumentList.Add(TaskName(instanceId, enabled));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot run the virtual display driver task.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5000)) throw new TimeoutException("Virtual display driver task request timed out.");
        if (process.ExitCode != 0) throw new InvalidOperationException("設定画面の「起動」または「再起動」でVDDの補助タスクを修復してください。 " + output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
        Storage.Log($"Requested virtual display driver {(enabled ? "enable" : "disable")}: {instanceId}");
    }
}
