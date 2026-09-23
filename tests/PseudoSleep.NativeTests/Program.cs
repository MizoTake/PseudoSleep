using System.Runtime.InteropServices;
using PseudoSleep;

internal static class Program
{
    [StructLayout(LayoutKind.Sequential)] private struct Key { internal ushort Vk; internal ushort Scan; internal uint Flags; internal uint Time; internal nuint Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct Payload { [FieldOffset(0)] internal Key Key; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { internal uint Type; internal Payload Data; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);

    [STAThread] private static int Main()
    {
        // Explicit local smoke only. Never emit an IME toggle or printable key into the user's foreground app.
        Application.EnableVisualStyles();
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        var toggles = 0;
        using var observer = new LeakObserver();
        using var bridge = new RemoteImeBridge(dispatcher, () => toggles++);
        bridge.SetActive(true);
        for (var i = 0; i < 10; i++) { Send(0x7C, false); Send(0x7C, true); Pump(); }
        Assert(toggles == 10, "Ten native injected F13 pairs produce ten actions");
        for (var i = 0; i < 40; i++) Send(0x7C, false);
        Pump(); Assert(toggles == 10, "Repeated native keydown produces no action");
        Send(0x7C, true); Pump(); Assert(toggles == 11, "Release after native repeats produces one action");
        foreach (ushort key in new ushort[] { 0x7D, 0x7E }) { for (var i = 0; i < 40; i++) Send(key, false); Send(key, true); }
        Pump(); Assert(toggles == 11, "Inert legacy JIS signals never toggle");
        Send(0x7C, false); Pump(); bridge.SetActive(false); bridge.SetActive(true); Send(0x7C, true); Pump(); Assert(toggles == 11, "Reset clears pending native keydown");
        // Use the same scan-code path Sunshine uses, under the current Windows keyboard layout.
        Send(0x7C, false, true); Send(0x7C, true, true); Pump(); Assert(toggles == 12, "Sunshine-style scan-code F13 reaches the relay");
        foreach (ushort key in new ushort[] { 0x7D, 0x7E }) { Send(key, false, true); Send(key, true, true); }
        Pump(); Assert(observer.Leaked == 0 && toggles == 12, "Reserved signals are consumed before other keyboard hooks");
        bridge.SetActive(false);
        Assert(!bridge.Active, "Hook unregistered");
        Console.WriteLine("PASS 8 native hook checks; IME action was a recording stub, no remote client tested.");
        return 0;
    }

    private static void Send(ushort key, bool up, bool scan = false)
    {
        var input = new Input { Type = 1, Data = new Payload { Key = new Key { Vk = scan ? (ushort)0 : key, Scan = scan ? (ushort)(100 + key - 0x7C) : (ushort)0, Flags = (up ? 2u : 0u) | (scan ? 8u : 0u) } } };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1) throw new InvalidOperationException("Native input insertion failed.");
    }
    private static void Pump() { var until = Environment.TickCount64 + 40; do { Application.DoEvents(); Thread.Sleep(1); } while (Environment.TickCount64 < until); }
    private static void Assert(bool result, string message) { if (!result) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }

    private sealed class LeakObserver : IDisposable
    {
        private delegate nint Callback(int code, nint message, nint data);
        [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookExW(int id, Callback callback, nint module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
        [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
        private readonly Callback callback;
        private readonly nint hook;
        internal int Leaked;
        internal LeakObserver() { callback = Observe; hook = SetWindowsHookExW(13, callback, GetModuleHandleW(null), 0); if (hook == 0) throw new InvalidOperationException("Observer hook unavailable."); }
        private nint Observe(int code, nint message, nint data) { if (code >= 0 && Marshal.ReadInt32(data) is >= 0x7C and <= 0x7E) { Leaked++; return 1; } return CallNextHookEx(hook, code, message, data); }
        void IDisposable.Dispose() { UnhookWindowsHookEx(hook); }
    }
}
