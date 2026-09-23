using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using PseudoSleep.KeyboardBridge;

namespace PseudoSleep.MoonlightImeClient
{
    public sealed class ClientSettings
    {
        public bool Enabled = true;
        public bool Caps = true;
        public bool HalfFull = true;
        public string ProcessName = "Moonlight";
        public string WindowClass = "SDL_app";
    }

    internal static class Program
    {
        [STAThread] private static int Main(string[] args)
        {
            try
            {
                using (var mutex = new Mutex(false, "Local\\PseudoSleep.MoonlightImeClient"))
                {
                    bool acquired;
                    try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) { MessageBox.Show("IME補助は既に起動しています。通知領域のアイコンを開いてください。", "Moonlight IME補助"); return 0; }
                    try { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); using (var form = new ClientForm()) { if (args.Length == 2 && args[0] == "--smoke") { form.Smoke(args[1]); return 0; } Application.Run(form); } }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Moonlight IME補助", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
            return 0;
        }
    }

    internal sealed class ClientForm : Form
    {
        private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PseudoSleep", "MoonlightImeClient.xml");
        private readonly ClientSettings settings;
        private readonly RawKeyboard input;
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 };
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(560, 0) };
        private readonly CheckBox enabled = new CheckBox { AutoSize = true, Text = "Moonlightの配信画面で補助を有効にする" };
        private readonly CheckBox caps = new CheckBox { AutoSize = true, Text = "Caps Lock／英数の単押しで切り替える" };
        private readonly CheckBox half = new CheckBox { AutoSize = true, Text = "半角／全角の単押しで切り替える" };
        private bool closing;
        private bool disposed;

        internal ClientForm()
        {
            settings = File.Exists(SettingsPath) ? ReadSettings() : new ClientSettings();
            if (string.IsNullOrWhiteSpace(settings.ProcessName) || string.IsNullOrWhiteSpace(settings.WindowClass)) throw new InvalidDataException("Moonlightの検出設定が空です: " + SettingsPath);
            Text = "Moonlight IME補助";
            Font = new Font("Yu Gothic UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(610, 405);
            MinimumSize = new Size(630, 440);
            var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(20) };
            Controls.Add(layout);
            layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(560, 0), Text = "このアプリはMoonlightを使う操作端末で起動してください。\nキーを押して離すたびに、メインPCの日本語入力を1回切り替えます。\n長押しやShift等との同時押しでは切り替えません。", Margin = new Padding(0, 0, 0, 15) });
            enabled.Checked = settings.Enabled; caps.Checked = settings.Caps; half.Checked = settings.HalfFull;
            layout.Controls.Add(enabled); layout.Controls.Add(caps); layout.Controls.Add(half);
            input = new RawKeyboard(settings);
            foreach (var checkbox in new[] { enabled, caps, half }) checkbox.CheckedChanged += delegate { settings.Enabled = enabled.Checked; settings.Caps = caps.Checked; settings.HalfFull = half.Checked; input.Cancel(); try { SaveSettings(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "設定を保存できません"); } };
            status.Margin = new Padding(0, 15, 0, 15);
            layout.Controls.Add(status);
            var buttons = new FlowLayoutPanel { AutoSize = true };
            var copy = new Button { Text = "診断情報をコピー", AutoSize = true };
            copy.Click += delegate { try { Clipboard.SetText(input.Diagnostic()); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "コピーできません"); } };
            var hide = new Button { Text = "通知領域にしまう", AutoSize = true }; hide.Click += delegate { Hide(); };
            var exit = new Button { Text = "終了", AutoSize = true }; exit.Click += delegate { closing = true; Close(); };
            buttons.Controls.Add(copy); buttons.Controls.Add(hide); buttons.Controls.Add(exit); layout.Controls.Add(buttons);
            layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(560, 0), Text = "初回はメインPC側のIME補助設定も必要です。\n診断情報には対象2キーの回数だけを含み、入力した文章は記録しません。", Margin = new Padding(0, 12, 0, 0) });
            var menu = new ContextMenuStrip();
            menu.Items.Add("設定・診断", null, delegate { Show(); WindowState = FormWindowState.Normal; Activate(); });
            menu.Items.Add("終了", null, delegate { closing = true; Close(); });
            tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "Moonlight IME補助", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
            timer.Tick += delegate { input.CheckFocus(); status.Text = input.Status(); };
            timer.Start();
            status.Text = input.Status();
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!closing && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        }

        private static ClientSettings ReadSettings() { using (var file = File.OpenRead(SettingsPath)) return (ClientSettings)new XmlSerializer(typeof(ClientSettings)).Deserialize(file); }
        private void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            var temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false))) new XmlSerializer(typeof(ClientSettings)).Serialize(writer, settings); if (File.Exists(SettingsPath)) File.Replace(temporary, SettingsPath, null); else File.Move(temporary, SettingsPath); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal void Smoke(string path)
        {
            if (KeyboardNative.InputSize != (IntPtr.Size == 8 ? 40 : 28)) throw new InvalidOperationException("Wrong native INPUT layout.");
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Location = new Point(-30000, -30000); Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(path + ".png"); }
            File.WriteAllText(path, input.Diagnostic() + "\r\nRaw input registration: OK\r\nForeground hook: OK\r\nINPUT size: " + KeyboardNative.InputSize, new UTF8Encoding(false));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed) { disposed = true; timer.Dispose(); if (input != null) ((IDisposable)input).Dispose(); if (tray != null) { tray.Visible = false; tray.Dispose(); } }
            base.Dispose(disposing);
        }
    }

    internal sealed class RawKeyboard : NativeWindow, IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct Registration { internal ushort Page; internal ushort Usage; internal uint Flags; internal IntPtr Window; }
        [StructLayout(LayoutKind.Sequential)] private struct Header { internal uint Type; internal uint Size; internal IntPtr Device; internal UIntPtr WParam; }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(Registration[] devices, uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr window, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        private readonly ClientSettings settings;
        private readonly ClientKeyGate gate = new ClientKeyGate();
        private readonly WinEventProc focusCallback;
        private readonly IntPtr focusHook;
        private IntPtr foreground;
        private long context;
        private bool target;
        private readonly int[] down = new int[2];
        private readonly int[] up = new int[2];
        private int sent;
        private int failures;
        private string error = "";

        internal RawKeyboard(ClientSettings settings)
        {
            this.settings = settings;
            CreateHandle(new CreateParams { Caption = "PseudoSleep.MoonlightImeClient.Input", Parent = new IntPtr(-3) });
            if (!RegisterRawInputDevices(new[] { new Registration { Page = 1, Usage = 6, Flags = 0x100 | 0x2000, Window = Handle } }, 1, (uint)Marshal.SizeOf(typeof(Registration)))) throw new Win32Exception(Marshal.GetLastWin32Error(), "Raw Input registration failed.");
            focusCallback = delegate { context++; gate.Cancel(); CheckFocus(); };
            focusHook = SetWinEventHook(3, 3, IntPtr.Zero, focusCallback, 0, 0, 0);
            if (focusHook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Foreground event registration failed.");
            CheckFocus();
        }

        internal void Cancel() { gate.Cancel(); context++; }
        internal void CheckFocus()
        {
            var current = KeyboardNative.GetForegroundWindow();
            if (current == foreground) return;
            foreground = current; Cancel(); target = false;
            if (foreground == IntPtr.Zero) return;
            var name = new StringBuilder(256);
            GetClassName(foreground, name, name.Capacity);
            if (!string.Equals(name.ToString(), settings.WindowClass, StringComparison.Ordinal)) return;
            uint pid; GetWindowThreadProcessId(foreground, out pid);
            try { using (var process = Process.GetProcessById((int)pid)) target = string.Equals(process.ProcessName, settings.ProcessName, StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { } catch (InvalidOperationException) { } catch (Win32Exception) { }
        }

        internal string Status() { return (error.Length > 0 ? "送信エラー: " + error : !settings.Enabled ? "補助は停止中です。" : target ? "Moonlightの配信画面を検出しました。" : "Moonlightの配信画面が前面になるのを待っています。") + "\n英数: 押下 " + down[0] + " ／ 解放 " + up[0] + "\n半角全角: 押下 " + down[1] + " ／ 解放 " + up[1] + "\n送信した切り替え: " + sent + " 回"; }
        internal string Diagnostic() { return "Moonlight IME bridge v1\r\n" + Status() + "\r\nFailures: " + failures + "\r\nEnabled: " + settings.Enabled + ", Caps: " + settings.Caps + ", HalfFull: " + settings.HalfFull + "\r\nTarget: " + settings.ProcessName + " / " + settings.WindowClass + "\r\nOS: " + Environment.OSVersion + ", process bits: " + (IntPtr.Size * 8); }

        protected override void WndProc(ref Message message)
        {
            try
            {
                if (message.Msg == 0xFE) { gate.RemoveDevice(message.LParam.ToInt64()); Cancel(); }
                if (message.Msg == 0xFF) Read(message.LParam);
            }
            catch (Exception ex) { failures++; error = ex.Message; Cancel(); }
            base.WndProc(ref message);
        }

        private void Read(IntPtr handle)
        {
            var headerSize = (uint)Marshal.SizeOf(typeof(Header));
            uint size = 0;
            if (GetRawInputData(handle, 0x10000003, IntPtr.Zero, ref size, headerSize) == uint.MaxValue || size < headerSize + 16 || size > 65536) return;
            var memory = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(handle, 0x10000003, memory, ref size, headerSize) != size) return;
                var header = (Header)Marshal.PtrToStructure(memory, typeof(Header));
                if (header.Type != 1 || header.Device == IntPtr.Zero || header.Size < headerSize + 16 || header.Size > size) return;
                var scan = (ushort)Marshal.ReadInt16(memory, (int)headerSize);
                var flags = (ushort)Marshal.ReadInt16(memory, (int)headerSize + 2);
                var key = (ushort)Marshal.ReadInt16(memory, (int)headerSize + 6);
                if (scan == 0xFF || key == 0xFF) { Cancel(); return; }
                var isUp = (flags & 1) != 0;
                var modifierEvent = !isUp && (scan == 0x2A || scan == 0x36 || scan == 0x1D || scan == 0x38 || ((flags & 2) != 0 && (scan == 0x5B || scan == 0x5C)));
                var index = scan == 0x3A ? 0 : scan == 0x29 ? 1 : -1;
                if (index >= 0 && (flags & 6) == 0) { if (isUp) up[index]++; else down[index]++; }
                CheckFocus();
                var selected = (index == 0 && settings.Caps) || (index == 1 && settings.HalfFull);
                if (gate.Observe(header.Device.ToInt64(), scan, flags, settings.Enabled && selected && target, modifierEvent || KeyboardNative.ModifiersDown(), context))
                {
                    // Observe raw events without suppressing them; the host consumes the original JIS signals.
                    if (KeyboardNative.GetForegroundWindow() != foreground) { Cancel(); return; }
                    KeyboardNative.Pulse(0x7C); sent++; error = "";
                }
            }
            finally { Marshal.FreeHGlobal(memory); }
        }

        void IDisposable.Dispose()
        {
            gate.Cancel(); UnhookWinEvent(focusHook);
            RegisterRawInputDevices(new[] { new Registration { Page = 1, Usage = 6, Flags = 1, Window = IntPtr.Zero } }, 1, (uint)Marshal.SizeOf(typeof(Registration)));
            DestroyHandle();
        }
    }
}
