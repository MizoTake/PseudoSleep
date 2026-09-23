using System.ComponentModel;
using System.Runtime.InteropServices;
using PseudoSleep.KeyboardBridge;

namespace PseudoSleep;

internal sealed class RemoteImeBridge : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct KeyEvent { internal uint Key; internal uint Scan; internal uint Flags; internal uint Time; internal nuint Extra; }
    private delegate nint HookProc(int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookExW(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    private readonly Control dispatcher;
    private readonly HostKeyGate gate = new();
    private readonly HookProc callback;
    private readonly Action emitToggle;
    private nint hook;
    private int generation;
    internal bool Active => hook != 0;
    internal int ToggleCount { get; private set; }

    internal RemoteImeBridge(Control dispatcher, Action? emitToggle = null) { this.dispatcher = dispatcher; this.emitToggle = emitToggle ?? (() => KeyboardNative.Pulse(0xF4)); callback = OnKey; }

    internal void SetActive(bool active)
    {
        if (active == Active) return;
        generation++;
        gate.Reset();
        if (active)
        {
            hook = SetWindowsHookExW(13, callback, GetModuleHandleW(null), 0);
            if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Remote IME hook could not be installed.");
        }
        else { UnhookWindowsHookEx(hook); hook = 0; }
    }

    private nint OnKey(int code, nint message, nint data)
    {
        if (code >= 0 && Active && message is 0x100 or 0x101 or 0x104 or 0x105)
        {
            var key = Marshal.PtrToStructure<KeyEvent>(data);
            var action = gate.Observe((int)key.Key, message is 0x100 or 0x104, (key.Flags & 0x10) != 0, key.Extra == KeyboardNative.Tag);
            if (action == HostKeyAction.Toggle)
            {
                var version = generation;
                var foreground = KeyboardNative.GetForegroundWindow();
                // Keep the low-level hook short; perform the paired IME input on the message loop.
                dispatcher.BeginInvoke(() =>
                {
                    if (!Active || generation != version || foreground == 0 || KeyboardNative.GetForegroundWindow() != foreground || KeyboardNative.ModifiersDown()) return;
                    try { emitToggle(); ToggleCount++; }
                    catch (Exception ex) { Storage.Log($"Remote IME pulse failed: {ex.Message}"); }
                });
            }
            if (action != HostKeyAction.Pass) return 1;
        }
        return CallNextHookEx(hook, code, message, data);
    }

    void IDisposable.Dispose() { SetActive(false); }
}
