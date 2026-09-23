using System.Diagnostics;
using PseudoSleep.Core;

namespace PseudoSleep;

internal sealed class TrayContext : ApplicationContext
{
    private readonly Form dispatcher = new() { ShowInTaskbar = false };
    private readonly NotifyIcon tray;
    private readonly InputWindow input;
    private readonly RemoteImeBridge imeBridge;
    private readonly PowerLease power = new();
    private readonly Guardian guardian = new();
    private readonly SleepController controller;
    private readonly CancellationTokenSource stop = new();
    private readonly System.Windows.Forms.Timer restoreTimer = new();
    private readonly System.Windows.Forms.Timer sessionTimer = new() { Interval = 1000 };
    private readonly SunshineSessionMonitor sessionMonitor = new();
    private readonly Icon normalIcon = MakeIcon(false);
    private readonly Icon sleepingIcon = MakeIcon(true);
    private SettingsForm? settings;
    private AppConfig config;
    private bool disposed;

    internal TrayContext()
    {
        config = RuntimeConfiguration.Load();
        _ = dispatcher.Handle;
        input = new InputWindow();
        imeBridge = new RemoteImeBridge(dispatcher);
        controller = new(new DisplayManager(), new RecoveryJournal(), power, guardian, new SunshineHost(), () => Environment.TickCount64, Storage.Log);
        var menu = new ContextMenuStrip();
        menu.Items.Add("🌙 疑似スリープ", null, (_, _) => Execute(new("sleep")));
        menu.Items.Add("☀ 通常状態に戻す", null, (_, _) => Execute(new("wake")));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("🎮 Sunshine Web UI", null, (_, _) => Open(config.Sunshine.WebUi));
        menu.Items.Add("🖥 ディスプレイ設定", null, (_, _) => Open("ms-settings:display"));
        menu.Items.Add("⚙ 設定・復帰デバイス", null, (_, _) => Execute(new("settings")));
        menu.Items.Add("📄 ログを開く", null, (_, _) => Open(Storage.LogDirectory));
        menu.Items.Add("終了（画面を復元）", null, (_, _) => Execute(new("exit")));
        tray = new NotifyIcon { Icon = normalIcon, Text = "PseudoSleep — Normal", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Execute(new("toggle")); };
        controller.Changed += () => { if (controller.State != AppState.PseudoSleep) imeBridge.SetActive(false); UpdateIcon(); };
        input.Input += path =>
        {
            settings?.ObserveInput(path);
            try { if (controller.OnInput(path, config)) { restoreTimer.Stop(); PrepareNormalDisplay(); } } catch (Exception ex) { NotifyError(ex.Message); }
        };
        input.ForceWake += () => Execute(new("wake", Force: true));
        restoreTimer.Tick += (_, _) => { restoreTimer.Stop(); Execute(new("wake")); };
        sessionTimer.Tick += (_, _) => CheckStreamingSession();
        sessionTimer.Start();
        _ = Ipc.Serve(command =>
        {
            var completion = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
            dispatcher.BeginInvoke(() => completion.SetResult(Execute(command)));
            return completion.Task;
        }, stop.Token);
        Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;
        if (File.Exists(Storage.StatePath)) { try { controller.Recover(); } catch (Exception ex) { NotifyError(ex.Message); } }
        if (controller.State == AppState.Normal && !string.IsNullOrEmpty(config.VirtualDisplayDevicePath)) { try { PrepareNormalDisplay(); } catch (Exception ex) { Storage.Log($"Normal display setup: {ex}"); NotifyError(ex.Message); } }
        if (string.IsNullOrEmpty(config.VirtualDisplayDevicePath) || config.WakeDevices.Count == 0) tray.ShowBalloonTip(5000, "PseudoSleep 初期設定", "右クリック → 設定から仮想画面と復帰デバイスを登録してください。", ToolTipIcon.Info);
    }

    private Response Execute(Command command)
    {
        try
        {
            switch (command.Name)
            {
                case "toggle": command = command with { Name = controller.State == AppState.Normal ? "sleep" : "wake" }; return Execute(command);
                case "sleep":
                    if (command.TestSeconds is < 0 or > 300) throw new ArgumentException("Test seconds must be 0..300.");
                    config = Storage.LoadConfig();
                    controller.Enter(config);
                    if (command.TestSeconds > 0) { restoreTimer.Interval = command.TestSeconds * 1000; restoreTimer.Start(); }
                    break;
                case "wake":
                    imeBridge.SetActive(false);
                    restoreTimer.Stop();
                    sessionMonitor.Reset();
                    controller.Wake(config);
                    if (command.Force && Guardian.RecoverStandalone(true) != 0) throw new InvalidOperationException("Force recovery did not complete. The last display backup is preserved.");
                    PrepareNormalDisplay();
                    break;
                case "status": return new(true, Status());
                case "client-mode": case "stream-start":
                    if (command.Name == "stream-start") imeBridge.SetActive(false);
                    config = RuntimeConfiguration.Load();
                    if (command.Name == "stream-start" && config.EnableRemoteImeBridge) RemoteImeConfiguration.Verify(File.ReadAllText(config.Sunshine.ConfigPath));
                    if (command.Name == "stream-start" && (!config.KeepVirtualDisplayInNormalMode || config.EnableRemoteImeBridge)) sessionMonitor.Arm(Path.Combine(Path.GetDirectoryName(config.Sunshine.ConfigPath)!, "sunshine.log"), Environment.TickCount64);
                    if (command.Name == "stream-start" ? controller.StartStream(command.Width, command.Height, config) : controller.SetClientResolution(command.Width, command.Height, config))
                    {
                        CommandCompletion.RememberClientResolution(config, command.Width, command.Height, value => Storage.Write(Storage.ConfigPath, value), ReportWarning);
                    }
                    if (command.Name == "stream-start") imeBridge.SetActive(config.EnableRemoteImeBridge);
                    break;
                case "stream-stop": imeBridge.SetActive(false); sessionMonitor.Reset(); restoreTimer.Stop(); controller.Wake(); PrepareNormalDisplay(); break;
                case "apply-settings": config = RuntimeConfiguration.Load(); PrepareNormalDisplay(); break;
                case "settings": ShowSettings(); break;
                case "exit": imeBridge.SetActive(false); sessionMonitor.Reset(); controller.Wake(config); PrepareNormalDisplay(); dispatcher.BeginInvoke(ExitThread); break;
                default: return new(false, null, "Unknown command.");
            }
            return new(true, CommandCompletion.ReadStatus(Status, ReportWarning));
        }
        catch (Exception ex) { if (command.Name == "stream-start") imeBridge.SetActive(false); Storage.Log($"Command {command.Name}: {ex}"); NotifyError(ex.Message); return new(false, null, ex.Message); }
    }

    private object Status()
    {
        var displays = DisplayManager.Enumerate();
        return new { state = controller.State.ToString(), resident = true, physicalDisplays = displays.Count(d => d.Active && !d.Indirect), virtualDisplay = displays.Any(d => d.Active && string.Equals(d.DevicePath, config.VirtualDisplayDevicePath, StringComparison.OrdinalIgnoreCase)), keepVirtualDisplayInNormalMode = config.KeepVirtualDisplayInNormalMode, powerRequest = power.Active, sunshine = SunshineHost.IsRunning(config.Sunshine.ServiceName), localWakeMonitor = true, wakeDeviceCount = config.WakeDevices.Count, hotkeyRegistered = input.HotkeyRegistered, remoteImeBridgeEnabled = config.EnableRemoteImeBridge, remoteImeBridgeActive = imeBridge.Active, remoteImeToggleCount = imeBridge.ToggleCount, recoveryPending = File.Exists(Storage.StatePath), configPath = Storage.ConfigPath, executable = Environment.ProcessPath, streamMonitored = sessionMonitor.Armed, lastError = controller.LastError };
    }

    private void CheckStreamingSession()
    {
        if (!sessionMonitor.Armed) return;
        if (controller.State != AppState.PseudoSleep && !config.KeepVirtualDisplayInNormalMode) { imeBridge.SetActive(false); sessionMonitor.Reset(); return; }
        try { if (SunshineHost.IsRunning(config.Sunshine.ServiceName) && !sessionMonitor.Poll(Environment.TickCount64)) return; }
        catch (Exception ex) { Storage.Log($"Cannot track Sunshine session; restoring local displays: {ex.Message}"); }
        imeBridge.SetActive(false);
        sessionMonitor.Reset();
        if (controller.State == AppState.Normal && config.KeepVirtualDisplayInNormalMode) return;
        Storage.Log("Moonlight session ended or failed to connect; restoring physical displays.");
        Execute(new("wake"));
    }

    private void PrepareNormalDisplay() { if (!string.IsNullOrEmpty(config.VirtualDisplayDevicePath)) controller.PrepareNormalDisplay(config); }

    private void ShowSettings()
    {
        if (settings is { IsDisposed: false }) { settings.Activate(); return; }
        settings = new SettingsForm(RuntimeConfiguration.Load(), controller.State == AppState.Normal);
        settings.FormClosed += (_, _) => { if (controller.State == AppState.Normal) Execute(new("apply-settings")); };
        settings.Show();
    }

    private void UpdateIcon() { tray.Icon = controller.State == AppState.PseudoSleep ? sleepingIcon : controller.State == AppState.Error ? SystemIcons.Warning : normalIcon; tray.Text = "PseudoSleep — " + controller.State; }
    private void ReportWarning(string message) { Storage.Log(message); NotifyError(message); }
    private void NotifyError(string message) { try { tray.ShowBalloonTip(8000, "PseudoSleep", message, ToolTipIcon.Warning); } catch (Exception ex) { Storage.Log($"Notification unavailable: {ex.Message}"); } }
    private static void Open(string target) { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception ex) { Storage.Log(ex.Message); } }
    private void OnSessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs e) { try { controller.Wake(); } catch (Exception ex) { Storage.Log($"Session ending: {ex}"); } }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding;
            stop.Cancel();
            ((IDisposable)imeBridge).Dispose();
            try { controller.Wake(config); } catch (Exception ex) { Storage.Log($"Exit restore: {ex}"); }
            tray.Visible = false;
            tray.Dispose();
            ((IDisposable)input).Dispose();
            ((IDisposable)power).Dispose();
            ((IDisposable)guardian).Dispose();
            restoreTimer.Dispose();
            sessionTimer.Dispose();
            settings?.Dispose();
            dispatcher.Dispose();
            normalIcon.Dispose();
            sleepingIcon.Dispose();
            stop.Dispose();
        }
        base.Dispose(disposing);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    private static Icon MakeIcon(bool sleeping)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(sleeping ? Color.FromArgb(156, 185, 255) : Color.FromArgb(255, 199, 72));
        graphics.FillEllipse(brush, 4, 4, 24, 24);
        if (sleeping) { graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy; using var clear = new SolidBrush(Color.Transparent); graphics.FillEllipse(clear, 12, 0, 24, 24); }
        var handle = bitmap.GetHicon();
        try { using var source = Icon.FromHandle(handle); return (Icon)source.Clone(); } finally { DestroyIcon(handle); }
    }
}
