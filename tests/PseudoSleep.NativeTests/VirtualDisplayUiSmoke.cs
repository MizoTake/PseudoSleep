using System.Security.Principal;
using PseudoSleep;
using PseudoSleep.Core;

internal static class VirtualDisplayUiSmoke
{
    internal static int Run(string[] args)
    {
        Application.EnableVisualStyles();
        var config = Storage.LoadConfig();
        var completion = new TaskCompletionSource<string>();
        VirtualDisplayAction? requested = null;
        var canSave = false;
        using var form = new SettingsForm(config, true, (action, progress) =>
        {
            requested = action;
            progress("テスト処理中（ドライバー操作なし）");
            return completion.Task;
        }, () => canSave);
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-30000, -30000);
        form.Show();
        Pump();
        var start = Button(form, "VirtualDisplayStart");
        var restart = Button(form, "VirtualDisplayRestart");
        var stop = Button(form, "VirtualDisplayStop");
        var save = Button(form, "SaveSettings");
        start.PerformClick();
        Assert(requested == VirtualDisplayAction.Start && !restart.Enabled && !stop.Enabled && !save.Enabled, "Start dispatches once and blocks conflicting actions");
        form.Close();
        Assert(!form.IsDisposed, "Closing cannot abandon a pending operation");
        completion.SetResult("起動しました（テスト）");
        Pump();
        Assert(start.Enabled && !save.Enabled, "Manual mode allows controls but prevents settings changes");
        completion = new();
        restart.PerformClick();
        Assert(requested == VirtualDisplayAction.Restart, "Restart dispatches its own operation");
        completion.SetException(new InvalidOperationException("テスト用のキャンセル"));
        Pump();
        Assert(start.Enabled && AllControls(form).OfType<Label>().Any(c => c.Text == "テスト用のキャンセル"), "Failed operation is shown and can be retried");
        completion = new();
        stop.PerformClick();
        Assert(requested == VirtualDisplayAction.Stop, "Stop dispatches restoration");
        canSave = true;
        completion.SetResult("通常状態に戻しました（テスト）");
        Pump();
        Assert(save.Enabled && start.Enabled, "Settings become editable after restoration");
        if (args.Length > 1)
        {
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(args[1]);
        }
        form.Close();
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!.Value;
        var command = VirtualDisplayTaskSetup.BuildCommand(@"ROOT\DISPLAY\0001", sid, false);
        Assert(command.Contains("function New-DriverPowerTaskDefinition") && command.Contains("function Invoke-DriverPowerTaskSetup") && command.Contains($"Invoke-DriverPowerTaskSetup 'ROOT\\DISPLAY\\0001' '{sid}' $false"), "Task definitions and read-only invocation are embedded in the executable");
        Assert(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command)).Length + 300 < 32767, "Embedded bootstrap fits the Windows command line limit");
        foreach (var id in new[] { @"PCI\DISPLAY\0001", @"ROOT\DISPLAY\*", "ROOT\\DISPLAY\\0001';whoami" })
        {
            var rejected = false;
            try { VirtualDisplayTaskSetup.BuildCommand(id, sid, true); } catch (ArgumentException) { rejected = true; }
            Assert(rejected, "Unverified task target is rejected: " + id);
        }
        var invalidSid = false;
        try { VirtualDisplayTaskSetup.BuildCommand(@"ROOT\DISPLAY\0001", "S-1-5-18';whoami", true); } catch (ArgumentException) { invalidSid = true; }
        Assert(invalidSid, "SID cannot insert a bootstrap command");
        Console.WriteLine("PASS 13 manual VDD UI/packaging checks; callbacks were stubs, no device or scheduled task changed.");
        return 0;
    }

    private static Button Button(Control parent, string name) => (Button)parent.Controls.Find(name, true).Single();
    private static IEnumerable<Control> AllControls(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(AllControls(c)));
    private static void Pump() { for (var i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(5); } }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
}
