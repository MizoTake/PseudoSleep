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
    private readonly SunshineConnectionMonitor connectionMonitor = new();
    private readonly AudioController audio;
    private bool? moonlightConnected;
    private string? audioError;
    private readonly NormalDisplayMonitor normalDisplayMonitor;
    private readonly Icon normalIcon = MakeIcon(false);
    private readonly Icon sleepingIcon = MakeIcon(true);
    private SettingsForm? settings;
    private AppConfig config;
    private bool disposed;
    private bool virtualDisplayBusy;

    internal TrayContext()
    {
        config = RuntimeConfiguration.Load();
        _ = dispatcher.Handle;
        input = new InputWindow();
        imeBridge = new RemoteImeBridge(dispatcher);
        controller = new(new DisplayManager(), new RecoveryJournal(), power, guardian, new SunshineHost(), () => Environment.TickCount64, Storage.Log);
        audio = new(new AudioBackend(), new AudioRecoveryJournal(), guardian);
        normalDisplayMonitor = new(Storage.LoadConfig, DisplayManager.Enumerate, value => controller.PrepareNormalDisplay(value), Storage.Log, value => !string.IsNullOrEmpty(value.VirtualDisplayDriverInstanceId) && VirtualDisplayDriver.ReadEnabled(value.VirtualDisplayDriverInstanceId) != false);
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
        controller.Changed += () => { if (controller.State != AppState.PseudoSleep) imeBridge.SetActive(false); UpdateAudio(); UpdateIcon(); };
        input.Input += path =>
        {
            settings?.ObserveInput(path);
            try { if (controller.OnInput(path, config)) { restoreTimer.Stop(); PrepareNormalDisplay(); } } catch (Exception ex) { NotifyError(ex.Message); }
        };
        input.ForceWake += () => Execute(new("wake", Force: true));
        restoreTimer.Tick += (_, _) => { restoreTimer.Stop(); Execute(new("wake")); };
        sessionTimer.Tick += (_, _) => { if (virtualDisplayBusy) return; CheckStreamingSession(); config = normalDisplayMonitor.Poll(config, controller.State, sessionMonitor.Armed, Environment.TickCount64); UpdateAudio(); };
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
            if (virtualDisplayBusy && command.Name is not ("status" or "settings")) throw new InvalidOperationException("仮想ディスプレイを操作中です。完了してから再試行してください。");
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
                case "config-status": return new(true, new { loadedHash = Storage.LoadedConfigHash, onDisk = Storage.LoadConfig(), onDiskHash = Storage.LoadedConfigHash, Storage.ConfigPath, process = Environment.ProcessId });
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
            UpdateAudio();
            return new(true, CommandCompletion.ReadStatus(Status, ReportWarning));
        }
        catch (Exception ex) { if (command.Name == "stream-start") imeBridge.SetActive(false); Storage.Log($"Command {command.Name}: {ex}"); NotifyError(ex.Message); return new(false, null, ex.Message); }
    }

    private object Status()
    {
        var displays = DisplayManager.Enumerate();
        return new { state = controller.State.ToString(), resident = true, physicalDisplays = displays.Count(d => d.Active && !d.Indirect), virtualDisplay = displays.Any(d => d.Active && string.Equals(d.DevicePath, config.VirtualDisplayDevicePath, StringComparison.OrdinalIgnoreCase)), keepVirtualDisplayInNormalMode = config.KeepVirtualDisplayInNormalMode, virtualDisplayDriverInstanceId = config.VirtualDisplayDriverInstanceId, virtualDisplayDriverEnabled = string.IsNullOrEmpty(config.VirtualDisplayDriverInstanceId) ? (bool?)null : VirtualDisplayDriver.ReadEnabled(config.VirtualDisplayDriverInstanceId), powerRequest = power.Active, sunshine = SunshineHost.IsRunning(config.Sunshine.ServiceName), localWakeMonitor = true, wakeDeviceCount = config.WakeDevices.Count, hotkeyRegistered = input.HotkeyRegistered, remoteImeBridgeEnabled = config.EnableRemoteImeBridge, remoteImeBridgeActive = imeBridge.Active, remoteImeToggleCount = imeBridge.ToggleCount, recoveryPending = File.Exists(Storage.StatePath), configPath = Storage.ConfigPath, executable = Environment.ProcessPath, streamMonitored = sessionMonitor.Armed, silenceAudioWhenDisconnected = config.SilenceAudioWhenDisconnected, moonlightConnected, audioRecoveryPending = File.Exists(Storage.AudioStatePath), savedAudioOutputs = audio.PendingEndpoints, audioError, lastError = controller.LastError };
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

    private void UpdateAudio(bool restore = false)
    {
        try
        {
            var previousCount = audio.PendingEndpoints;
            if (restore || controller.State != AppState.PseudoSleep || !config.SilenceAudioWhenDisconnected) { moonlightConnected = null; audio.Restore(); }
            else
            {
                try { moonlightConnected = connectionMonitor.Poll(Path.Combine(Path.GetDirectoryName(config.Sunshine.ConfigPath)!, "sunshine.log"), SunshineHost.IsRunning(config.Sunshine.ServiceName)); }
                catch (Exception ex) { moonlightConnected = null; Storage.Log($"Audio connection state unavailable; restoring volume: {ex.Message}"); }
                audio.Update(controller.State, moonlightConnected);
            }
            if (previousCount != audio.PendingEndpoints) Storage.Log(audio.PendingEndpoints == 0 ? "Audio volumes restored; recovery journal cleared." : $"Audio volumes saved for {audio.PendingEndpoints} outputs; disconnected pseudo sleep is silent.");
            audioError = null;
        }
        catch (Exception ex)
        {
            var error = ex.GetBaseException().Message;
            if (audioError != error) { Storage.Log($"Audio control: {ex}"); NotifyError("音量の制御・復元を再試行します: " + error); }
            audioError = error;
        }
    }

    private void PrepareNormalDisplay() { if (!string.IsNullOrEmpty(config.VirtualDisplayDevicePath)) controller.PrepareNormalDisplay(config); }

    private void ShowSettings()
    {
        if (settings is { IsDisposed: false }) { settings.Activate(); return; }
        settings = new SettingsForm(RuntimeConfiguration.Load(), controller.State == AppState.Normal, ControlVirtualDisplayAsync, () => controller.State == AppState.Normal);
        settings.FormClosed += (_, _) => { if (controller.State == AppState.Normal) Execute(new("apply-settings")); };
        settings.Show();
    }

    private async Task<string> ControlVirtualDisplayAsync(VirtualDisplayAction action, Action<string> progress)
    {
        if (virtualDisplayBusy) throw new InvalidOperationException("仮想ディスプレイを操作中です。");
        virtualDisplayBusy = true;
        try
        {
            config = RuntimeConfiguration.Load();
            VerifyManualDisplayOperation();
            if (string.IsNullOrEmpty(config.VirtualDisplayDriverInstanceId) || string.IsNullOrEmpty(config.VirtualDisplayDevicePath)) throw new InvalidOperationException("復旧対象のVDDドライバーと仮想ディスプレイを先に登録してください。");
            await VirtualDisplayTaskSetup.EnsureAsync(config.VirtualDisplayDriverInstanceId, progress);
            // A connection may have arrived while Windows was waiting for elevation approval.
            VerifyManualDisplayOperation();
            if (controller.State == AppState.Error) controller.Recover();
            if (action == VirtualDisplayAction.Stop)
            {
                controller.Wake();
                PrepareNormalDisplay();
                return "通常状態に戻しました。仮想ディスプレイは必要なときに再び起動できます。";
            }
            controller.PrepareVirtualDisplay(config, action == VirtualDisplayAction.Restart);
            return (action == VirtualDisplayAction.Restart ? "再起動" : "起動") + "しました。物理画面を維持して仮想画面を追加しています。停止するには「停止して通常に戻す」を押してください。";
        }
        finally { virtualDisplayBusy = false; UpdateAudio(); }
    }

    private void VerifyManualDisplayOperation()
    {
        if (controller.State is not (AppState.Normal or AppState.VirtualDisplayReady or AppState.Error)) throw new InvalidOperationException("疑似スリープを解除してから仮想ディスプレイを操作してください。");
        if (!DisplayManager.Enumerate().Any(d => d.Active && !d.Indirect)) throw new InvalidOperationException("物理画面を復帰させてから操作してください。管理者確認は物理画面が表示されている状態で行います。");
        var connected = connectionMonitor.Poll(Path.Combine(Path.GetDirectoryName(config.Sunshine.ConfigPath)!, "sunshine.log"), SunshineHost.IsRunning(config.Sunshine.ServiceName));
        if (connected != false || sessionMonitor.Armed) throw new InvalidOperationException("Moonlightを切断し、接続が終了してから仮想ディスプレイを操作してください。");
    }

    private void UpdateIcon() { tray.Icon = controller.State == AppState.PseudoSleep ? sleepingIcon : controller.State == AppState.Error ? SystemIcons.Warning : normalIcon; tray.Text = "PseudoSleep — " + controller.State; }
    private void ReportWarning(string message) { Storage.Log(message); NotifyError(message); }
    private void NotifyError(string message) { try { tray.ShowBalloonTip(8000, "PseudoSleep", message, ToolTipIcon.Warning); } catch (Exception ex) { Storage.Log($"Notification unavailable: {ex.Message}"); } }
    private static void Open(string target) { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception ex) { Storage.Log(ex.Message); } }
    private void OnSessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs e) { try { controller.Wake(); PrepareNormalDisplay(); } catch (Exception ex) { Storage.Log($"Session ending: {ex}"); } finally { UpdateAudio(true); } }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding;
            stop.Cancel();
            ((IDisposable)imeBridge).Dispose();
            try { controller.Wake(config); PrepareNormalDisplay(); } catch (Exception ex) { Storage.Log($"Exit restore: {ex}"); }
            UpdateAudio(true);
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
