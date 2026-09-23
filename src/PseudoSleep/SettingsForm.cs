using PseudoSleep.Core;

namespace PseudoSleep;

internal sealed class SettingsForm : Form
{
    private readonly AppConfig config;
    private readonly ComboBox display = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox deviceId = new() { Dock = DockStyle.Fill };
    private readonly CheckedListBox devices = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true, CheckOnClick = true };
    private readonly NumericUpDown guard = new() { Minimum = 0, Maximum = 10000, Increment = 500, Dock = DockStyle.Fill };
    private readonly NumericUpDown width = new() { Minimum = 640, Maximum = 16384, Dock = DockStyle.Fill };
    private readonly NumericUpDown height = new() { Minimum = 480, Maximum = 16384, Dock = DockStyle.Fill };
    private readonly NumericUpDown hz = new() { Minimum = 24, Maximum = 1000, Dock = DockStyle.Fill };
    private readonly CheckBox preventSleep = new() { Text = "疑似スリープ中はPC本体の自動スリープを防止", AutoSize = true };
    private readonly CheckBox followClient = new() { Text = "Moonlightから要求された解像度に合わせる", AutoSize = true };
    private readonly CheckBox sleepOnConnect = new() { Text = "Moonlight接続時に物理画面を消灯する", AutoSize = true };
    private readonly CheckBox disconnectOnWake = new() { Text = "物理画面の復帰時にMoonlightを切断する", AutoSize = true };
    private readonly TextBox ignored = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Label captureStatus = new() { AutoSize = true, Text = "登録済みの物理デバイスだけが復帰に使用されます。" };
    private DateTime captureStarts;
    private DateTime captureEnds;
    private readonly List<DisplayInfo> choices;

    internal SettingsForm(AppConfig config, bool editable)
    {
        this.config = config;
        Text = "PseudoSleep 設定";
        Font = new Font("Yu Gothic UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(780, 700);
        Size = new Size(900, 960);
        AutoScroll = true;
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(20), ColumnCount = 2, RowCount = 15 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout);
        choices = DisplayManager.Enumerate().Where(d => d.Indirect).ToList();
        foreach (var item in choices) display.Items.Add($"{item.Name} — {item.DevicePath}");
        display.SelectedIndex = choices.FindIndex(d => string.Equals(d.DevicePath, config.VirtualDisplayDevicePath, StringComparison.OrdinalIgnoreCase));
        deviceId.Text = config.VirtualDisplayDeviceId;
        deviceId.PlaceholderText = "通常時に仮想画面を無効にする構成ではID入力不要";
        deviceId.ReadOnly = !config.KeepVirtualDisplayInNormalMode;
        foreach (var item in InputWindow.Enumerate()) devices.Items.Add(item.Path, config.WakeDevices.Contains(item.Path, StringComparer.OrdinalIgnoreCase));
        foreach (var path in config.WakeDevices.Where(p => !devices.Items.Cast<string>().Contains(p, StringComparer.OrdinalIgnoreCase))) devices.Items.Add(path, true);
        guard.Value = config.WakeGuardMs;
        width.Value = config.Width;
        height.Value = config.Height;
        hz.Value = config.RefreshRate;
        preventSleep.Checked = config.PreventSystemSleep;
        followClient.Checked = config.FollowClientResolution;
        sleepOnConnect.Checked = config.SleepOnMoonlightConnect;
        sleepOnConnect.Enabled = config.KeepVirtualDisplayInNormalMode;
        disconnectOnWake.Checked = config.DisconnectMoonlightOnWake;
        disconnectOnWake.Enabled = config.KeepVirtualDisplayInNormalMode;
        ignored.Text = string.Join(Environment.NewLine, config.IgnoredDevices);
        AddRow(layout, 0, "仮想ディスプレイ", display, 45);
        AddRow(layout, 1, "Sunshine画面ID", deviceId, 45);
        AddRow(layout, 2, "幅 / 高さ / Hz", new FlowLayoutPanel { Dock = DockStyle.Fill, Controls = { width, height, hz } }, 45);
        width.Width = 130; height.Width = 130; hz.Width = 100;
        AddRow(layout, 3, "復帰ガード（ms）", guard, 45);
        AddRow(layout, 4, "復帰デバイス", devices, 180);
        var capture = new Button { Text = "操作して登録", AutoSize = true };
        capture.Click += (_, _) => { captureStarts = DateTime.UtcNow.AddSeconds(2); captureEnds = captureStarts.AddSeconds(15); captureStatus.Text = "2秒後から15秒間：登録するマウスを動かすかキーを押してください。"; };
        AddRow(layout, 5, "デバイス登録", capture, 40);
        layout.Controls.Add(captureStatus, 1, 6);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        AddRow(layout, 7, "除外（1行1パターン）", ignored, 90);
        layout.Controls.Add(preventSleep, 1, 8);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(followClient, 1, 9);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(sleepOnConnect, 1, 10);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(disconnectOnWake, 1, 11);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        var audio = new Label { AutoSize = true, MaximumSize = new Size(600, 0), Text = "音声はMoonlight側の「ホストPCで音声を再生」で切り替えます。\nオフ：操作端末のみ ／ オン：メインPCでも再生（再接続時に反映）" };
        AddRow(layout, 12, "配信中の音声", audio, 65);
        var status = new Label { AutoSize = true, Text = $"Sunshine: {(SunshineHost.IsRunning(config.Sunshine.ServiceName) ? "Running" : "Stopped")}  /  強制復帰: Ctrl + Alt + Shift + F12" };
        layout.Controls.Add(status, 1, 13);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        var save = new Button { Text = "保存", AutoSize = true, Enabled = editable };
        save.Click += (_, _) => Save();
        layout.Controls.Add(save, 1, 14);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        if (!editable) captureStatus.Text = "通常状態に戻してから設定を変更してください。";
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control, float size) { layout.RowStyles.Add(new RowStyle(SizeType.Absolute, size)); layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row); layout.Controls.Add(control, 1, row); }

    internal void ObserveInput(string path)
    {
        if (DateTime.UtcNow < captureStarts || DateTime.UtcNow > captureEnds) return;
        captureEnds = DateTime.MinValue;
        var index = devices.Items.IndexOf(path);
        if (index < 0) index = devices.Items.Add(path);
        devices.SetItemChecked(index, true);
        captureStatus.Text = "登録候補: " + path + "\n保存すると有効になります。";
    }

    private void Save()
    {
        try
        {
            if (display.SelectedIndex < 0) throw new InvalidOperationException("仮想ディスプレイを選択してください。");
            config.VirtualDisplayDevicePath = choices[display.SelectedIndex].DevicePath;
            config.VirtualDisplayDeviceId = deviceId.Text.Trim();
            config.WakeDevices = devices.CheckedItems.Cast<string>().ToList();
            config.IgnoredDevices = ignored.Lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
            config.WakeGuardMs = (int)guard.Value;
            config.Width = (int)width.Value;
            config.Height = (int)height.Value;
            config.RefreshRate = (int)hz.Value;
            config.PreventSystemSleep = preventSleep.Checked;
            config.FollowClientResolution = followClient.Checked;
            config.SleepOnMoonlightConnect = sleepOnConnect.Checked;
            config.DisconnectMoonlightOnWake = disconnectOnWake.Checked;
            config.Validate();
            Storage.Write(Storage.ConfigPath, config);
            Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "設定", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
