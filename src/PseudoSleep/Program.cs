using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PseudoSleep.Core;

namespace PseudoSleep;

internal static class Program
{
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetConsoleOutputCP();

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "guardian") return Guardian.Run(int.Parse(args[1]), args[2]);
            if (args.Length > 0 && args[0] != "tray")
            {
                _ = AttachConsole(uint.MaxValue);
                // A service-launched WinExe may have redirected output but no console code page.
                if (GetConsoleOutputCP() != 0) Console.OutputEncoding = new UTF8Encoding(false);
                else Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), 1024, true) { AutoFlush = true });
                return RunCommand(args).GetAwaiter().GetResult();
            }
            using var mutex = new Mutex(false, Ipc.MutexName);
            bool owns;
            try { owns = mutex.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
            if (!owns) return 0;
            try
            {
                ApplicationConfiguration.Initialize();
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                Storage.Log("App start.");
                // Restore a pending transaction before reading an editable configuration file.
                if (File.Exists(Storage.StatePath) && Guardian.RecoverStandalone() != 0) Storage.Log("Startup recovery incomplete; entering sleep will remain blocked.");
                using var context = new TrayContext();
                Application.Run(context);
                return 0;
            }
            finally { mutex.ReleaseMutex(); }
        }
        catch (Exception ex)
        {
            Storage.Log($"Fatal: {ex}");
            if (args.Length > 0) Console.WriteLine(JsonSerializer.Serialize(new Response(false, null, ex.Message), Storage.Json));
            else MessageBox.Show(ex.Message + "\n\n" + Storage.LogDirectory, "PseudoSleep", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static async Task<int> RunCommand(string[] args)
    {
        object? result;
        switch (args[0].ToLowerInvariant())
        {
            case "displays": result = DisplayManager.Enumerate(); break;
            case "devices": result = InputWindow.Enumerate(); break;
            case "restart-sunshine":
                if (args.Length != 2) throw new ArgumentException("restart-sunshine <service-name>");
                SunshineHost.RestartForDisconnect(args[1]); result = new { restarted = args[1] }; break;
            case "probe": if (args.Length != 2) throw new ArgumentException("probe <display-source-name>"); CaptureProbe.Verify(args[1]); result = new { capture = "DesktopDuplication", frameAcquired = true, display = args[1] }; break;
            case "ui-smoke":
                if (args.Length != 2) throw new ArgumentException("ui-smoke <output.png>");
                ApplicationConfiguration.Initialize();
                using (var form = new SettingsForm(Storage.LoadConfig(), true))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-30000, -30000);
                    form.Show();
                    Application.DoEvents();
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(args[1]);
                    form.Close();
                }
                result = new { rendered = Path.GetFullPath(args[1]) };
                break;
            case "diagnose": result = new { displays = DisplayManager.Enumerate(), devices = InputWindow.Enumerate(), nativeSizes = new { path = Marshal.SizeOf<DisplayNative.DisplayPath>(), mode = Marshal.SizeOf<DisplayNative.Mode>(), targetName = Marshal.SizeOf<DisplayNative.TargetName>() }, configPath = Storage.ConfigPath, recoveryPending = File.Exists(Storage.StatePath), sunshine = SunshineHost.IsRunning(Storage.LoadConfig().Sunshine.ServiceName) }; break;
            case "backup":
                if (args.Length != 2) throw new ArgumentException("backup <file.json>");
                Storage.Write(Path.GetFullPath(args[1]), ((IDisplayBackend)new DisplayManager()).Capture(new AppConfig()));
                result = new { saved = Path.GetFullPath(args[1]) };
                break;
            case "restore-backup":
                if (args.Length != 2) throw new ArgumentException("restore-backup <file.json>");
                using (var restoreMutex = new Mutex(false, Ipc.MutexName))
                {
                    bool acquired;
                    try { acquired = restoreMutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new InvalidOperationException("Exit the tray app before restoring a setup backup.");
                    try { var backup = JsonSerializer.Deserialize<DisplayBackup>(File.ReadAllText(args[1], Encoding.UTF8), Storage.Json) ?? throw new InvalidDataException("Invalid backup"); ((IDisplayBackend)new DisplayManager()).Restore(backup); }
                    finally { restoreMutex.ReleaseMutex(); }
                }
                result = new { restored = true };
                break;
            case "sleep": case "wake": case "toggle": case "status": case "settings": case "exit": case "client-mode": case "stream-start": case "stream-stop": case "apply-settings":
                var testSeconds = args.Skip(1).FirstOrDefault(a => a.StartsWith("--test-seconds=", StringComparison.Ordinal));
                var command = new Command(args[0].ToLowerInvariant(), testSeconds == null ? 0 : int.Parse(testSeconds.Split('=')[1]), Force: args.Contains("--force"));
                if (command.Name is "client-mode" or "stream-start")
                {
                    var width = args.Length >= 3 ? args[1] : Environment.GetEnvironmentVariable("SUNSHINE_CLIENT_WIDTH");
                    var height = args.Length >= 3 ? args[2] : Environment.GetEnvironmentVariable("SUNSHINE_CLIENT_HEIGHT");
                    if (!int.TryParse(width, out var clientWidth) || !int.TryParse(height, out var clientHeight)) throw new ArgumentException("client-mode requires SUNSHINE_CLIENT_WIDTH/HEIGHT or explicit width height arguments.");
                    command = command with { Width = clientWidth, Height = clientHeight };
                }
                Response response;
                try { response = await Ipc.Send(command); }
                catch (TimeoutException)
                {
                    using var mutex = new Mutex(false, Ipc.MutexName);
                    bool owns;
                    try { owns = mutex.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
                    if (!owns) throw new TimeoutException("Tray IPC is not responding. If the process is terminated, its guardian restores the display backup.");
                    try
                    {
                        if (command.Name == "wake") { var code = Guardian.RecoverStandalone(command.Force); response = new(code == 0, new { state = code == 0 ? "Normal" : "Error", resident = false }); }
                        else if (command.Name is "status" or "exit" or "stream-stop") response = new(true, new { state = File.Exists(Storage.StatePath) ? "RecoveryPending" : "Normal", resident = false });
                        else response = new(false, null, "Start PseudoSleep.exe first, then run this command.");
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                Console.WriteLine(JsonSerializer.Serialize(response, Storage.Json));
                return response.Success ? 0 : 1;
            default: result = new { usage = "PseudoSleep.exe [tray|sleep [--test-seconds=15]|wake --force|toggle|status|settings|exit|displays|devices|diagnose|client-mode [width height]|backup <file>|restore-backup <file>]" }; break;
        }
        Console.WriteLine(JsonSerializer.Serialize(result, Storage.Json));
        return 0;
    }
}
