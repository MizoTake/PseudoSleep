namespace PseudoSleep.Core;

public sealed class SleepController(IDisplayBackend displays, IRecoveryJournal journal, IPowerLease power, IGuardian guardian, IStreamingHost host, Func<long> clock, Action<string> log)
{
    public AppState State { get; private set; } = AppState.Normal;
    public string? LastError { get; private set; }
    public event Action? Changed;
    private long wakeAllowedAt;
    private (string Path, string Driver)? preparedTarget;

    public void Enter(AppConfig config)
    {
        if (State == AppState.PseudoSleep) return;
        var preparedBackup = ReadPreparedBackup(config);
        if (State is not (AppState.Normal or AppState.VirtualDisplayReady) || (journal.Read() != null && preparedBackup == null)) throw new InvalidOperationException("Recover the previous display configuration before entering sleep.");
        config.Validate();
        var displayConfig = config.FollowClientResolution && config.LastClientWidth != 0 ? config.WithResolution(config.LastClientWidth, config.LastClientHeight) : config;
        if (string.IsNullOrWhiteSpace(config.VirtualDisplayDevicePath)) throw new InvalidOperationException("仮想ディスプレイを設定してください。");
        if (!config.WakeDevices.Any(p => WakePolicy.IsAllowed(p, config))) throw new InvalidOperationException("復帰に使用するマウスまたはキーボードを登録してください。");
        host.EnsureReady(config);
        SetState(AppState.EnteringPseudoSleep);
        try
        {
            var backup = preparedBackup ?? displays.Capture(config);
            journal.Save(new(1, State, backup, DateTimeOffset.UtcNow));
            guardian.EnsureReady();
            if (config.PreventSystemSleep) power.Acquire();
            displays.EnableVirtual(displayConfig);
            displays.VerifyVirtual(displayConfig);
            displays.ShowVirtualOnly(displayConfig);
            displays.VerifyVirtual(displayConfig);
            journal.Save(new(1, AppState.PseudoSleep, backup, DateTimeOffset.UtcNow));
            wakeAllowedAt = clock() + config.WakeGuardMs;
            LastError = null;
            SetState(AppState.PseudoSleep);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            log($"Entry failed: {ex}");
            try { Recover(); } catch (Exception recoveryError) { log($"Recovery failed: {recoveryError}"); }
            throw;
        }
    }

    public void Wake() => Wake(null);

    public void PrepareVirtualDisplay(AppConfig config, bool restart = false)
    {
        var backup = ReadPreparedBackup(config);
        if (State is not (AppState.Normal or AppState.VirtualDisplayReady) || (journal.Read() != null && backup == null)) throw new InvalidOperationException("通常状態に戻してから仮想ディスプレイを操作してください。");
        config.Validate();
        if (string.IsNullOrWhiteSpace(config.VirtualDisplayDevicePath) || string.IsNullOrWhiteSpace(config.VirtualDisplayDriverInstanceId)) throw new InvalidOperationException("復旧対象の仮想ディスプレイとドライバーを登録してください。");
        SetState(AppState.PreparingVirtualDisplay);
        try
        {
            backup ??= displays.Capture(config);
            journal.Save(new(1, State, backup, DateTimeOffset.UtcNow));
            guardian.EnsureReady();
            if (restart) displays.Restore(backup);
            displays.ShowVirtualExtended(config);
            displays.VerifyVirtual(config);
            journal.Save(new(1, AppState.VirtualDisplayReady, backup, DateTimeOffset.UtcNow));
            preparedTarget = (config.VirtualDisplayDevicePath, config.VirtualDisplayDriverInstanceId);
            LastError = null;
            SetState(AppState.VirtualDisplayReady);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            log($"Manual virtual display preparation failed: {ex}");
            try { Recover(); } catch (Exception recoveryError) { log($"Manual virtual display recovery failed: {recoveryError}"); }
            throw;
        }
    }

    private DisplayBackup? ReadPreparedBackup(AppConfig config)
    {
        if (State != AppState.VirtualDisplayReady) return null;
        if (preparedTarget is not { } target || !string.Equals(target.Path, config.VirtualDisplayDevicePath, StringComparison.OrdinalIgnoreCase) || !string.Equals(target.Driver, config.VirtualDisplayDriverInstanceId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("通常状態に戻してから仮想ディスプレイの登録先を変更してください。");
        return journal.Read()?.Backup ?? throw new InvalidOperationException("仮想画面の復旧情報が見つかりません。");
    }

    public void Wake(AppConfig? config)
    {
        if (State == AppState.Normal && journal.Read() == null) return;
        var manualOnly = State == AppState.VirtualDisplayReady;
        SetState(AppState.Waking);
        Restore();
        if (!manualOnly && config?.DisconnectMoonlightOnWake == true) host.Disconnect(config);
    }

    public bool StartStream(int width, int height, AppConfig config)
    {
        if (!config.KeepVirtualDisplayInNormalMode && (!config.SleepOnMoonlightConnect || !config.DisconnectMoonlightOnWake)) throw new InvalidOperationException("On-demand virtual output requires physical-display sleep and disconnection on wake so a resumed stream cannot capture physical displays.");
        if (State is not (AppState.Normal or AppState.PseudoSleep or AppState.VirtualDisplayReady)) throw new InvalidOperationException("Recover the display configuration before streaming.");
        var requested = config.FollowClientResolution ? config.WithResolution(width, height) : config;
        var enteredForStream = false;
        try
        {
            if (config.SleepOnMoonlightConnect && State is AppState.Normal or AppState.VirtualDisplayReady)
            {
                if (config.FollowClientResolution) { requested.LastClientWidth = width; requested.LastClientHeight = height; }
                Enter(requested);
                enteredForStream = true;
            }
            return SetClientResolution(width, height, config);
        }
        catch { if (enteredForStream) { try { Wake(); } catch (Exception ex) { log($"Failed stream preparation recovery: {ex}"); } } throw; }
    }

    public bool SetClientResolution(int width, int height, AppConfig config)
    {
        if (State == AppState.Normal && config.KeepVirtualDisplayInNormalMode)
        {
            PrepareNormalDisplay(config.FollowClientResolution ? config.WithResolution(width, height) : config, useRememberedResolution: false);
            if (config.FollowClientResolution) log($"Client resolution applied on extended virtual display: {width}x{height}");
            return config.FollowClientResolution;
        }
        if (!config.FollowClientResolution) return false;
        var requested = config.WithResolution(width, height);
        if (State == AppState.Normal) { log("Client resolution skipped while local displays are active."); return false; }
        if (State != AppState.PseudoSleep) throw new InvalidOperationException("Client resolution can only change while PseudoSleep is active.");
        displays.ChangeVirtualResolution(requested);
        log($"Client resolution applied: {width}x{height}");
        return true;
    }

    public void PrepareNormalDisplay(AppConfig config, bool useRememberedResolution = true)
    {
        if (State != AppState.Normal || journal.Read() != null) throw new InvalidOperationException("Restore physical displays before changing the normal display layout.");
        config.Validate();
        var requested = useRememberedResolution && config.FollowClientResolution && config.LastClientWidth != 0 ? config.WithResolution(config.LastClientWidth, config.LastClientHeight) : config;
        displays.ConfigureNormalDisplay(requested);
    }

    public void Recover()
    {
        SetState(AppState.Recovery);
        Restore();
    }

    private void Restore()
    {
        try
        {
            var pending = journal.Read();
            if (pending != null) { displays.Restore(pending.Backup); journal.Clear(); }
            preparedTarget = null;
            LastError = null;
            SetState(AppState.Normal);
        }
        catch (Exception ex) { LastError = ex.Message; SetState(AppState.Error); log($"Restore failed: {ex}"); throw; }
        finally { power.Release(); }
    }

    public bool OnInput(string? path, AppConfig config)
    {
        if (State != AppState.PseudoSleep || clock() < wakeAllowedAt || !WakePolicy.IsAllowed(path, config)) return false;
        log($"Local wake: {path}");
        Wake(config);
        return true;
    }

    private void SetState(AppState state) { State = state; log($"State: {state}"); Changed?.Invoke(); }
}
