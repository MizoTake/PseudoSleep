using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace PseudoSleep;

internal static class VirtualDisplayTaskSetup
{
    internal static async Task EnsureAsync(string instanceId, Action<string> progress)
    {
        // The read also checks device identity before asking for elevation.
        _ = VirtualDisplayDriver.ReadEnabled(instanceId);
        var check = await RunAsync(instanceId, false);
        if (check == 0) return;
        if (check != 10) throw new InvalidOperationException("仮想ディスプレイの補助タスクを確認できません。ログを確認してください。");
        progress("補助タスクを修復します。Windowsの管理者確認を承認してください。画面はそのまま保持します。");
        try
        {
            if (await RunAsync(instanceId, true) != 0) throw new InvalidOperationException("補助タスクの修復に失敗しました。同じWindowsアカウントで管理者確認を承認し、再試行してください。");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("管理者確認がキャンセルされました。画面は変更していません。起動または再起動ボタンで再試行できます。", ex);
        }
        if (await RunAsync(instanceId, false) != 0) throw new InvalidOperationException("補助タスクの修復後の確認に失敗しました。画面は変更していません。");
        progress("補助タスクを確認しました。仮想ディスプレイを準備しています…");
    }

    internal static string BuildCommand(string instanceId, string userSid, bool repair)
    {
        if (!Regex.IsMatch(instanceId, @"\AROOT\\DISPLAY\\[A-Za-z0-9_&-]+\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) throw new ArgumentException("復旧対象のVDDドライバーが設定されていません。");
        if (!Regex.IsMatch(userSid, @"\AS-1-5-21-\d+-\d+-\d+-\d+\z", RegexOptions.CultureInvariant)) throw new ArgumentException("Windowsユーザーの識別子を確認できません。");
        return ReadResource("DriverPower.Common.ps1") + "\n" + ReadResource("DriverPower.Tasks.ps1") +
            $"\ntry {{ exit (Invoke-DriverPowerTaskSetup '{instanceId}' '{userSid}' ${(repair ? "true" : "false")}) }} catch {{ [Console]::Error.WriteLine($_.Exception.Message); exit 1 }}";
    }

    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PseudoSleep." + name) ?? throw new InvalidOperationException("補助タスクの定義がEXEにありません。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static async Task<int> RunAsync(string instanceId, bool repair)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var command = BuildCommand(instanceId, identity.User!.Value, repair);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            Arguments = "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command)),
            UseShellExecute = repair,
            Verb = repair ? "runas" : "",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            RedirectStandardOutput = !repair,
            RedirectStandardError = !repair
        };
        using var process = await Task.Run(() => Process.Start(start)) ?? throw new InvalidOperationException("補助タスクの設定を開始できません。");
        var output = repair ? Task.FromResult("") : process.StandardOutput.ReadToEndAsync();
        var error = repair ? Task.FromResult("") : process.StandardError.ReadToEndAsync();
        // Do not time out an elevated transaction while it may be applying or rolling back task definitions.
        await process.WaitForExitAsync();
        Storage.Log($"VDD task {(repair ? "repair" : "check")}: exit={process.ExitCode}; {await output} {await error}");
        return process.ExitCode;
    }
}
