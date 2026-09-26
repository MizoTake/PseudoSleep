using System;
using System.Runtime.InteropServices;
using PseudoSleep.KeyboardBridge;

namespace PseudoSleep.MoonlightImeClient
{
    internal sealed class ClientKeyboardLayout : IClientKeyboardLayout
    {
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int size, IntPtr[] layouts);
        [DllImport("user32.dll")] private static extern uint MapVirtualKeyExW(uint code, uint map, IntPtr layout);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);

        long IClientKeyboardLayout.GetLayout(long window) { uint process; var thread = GetWindowThreadProcessId(new IntPtr(window), out process); return thread == 0 ? 0 : GetKeyboardLayout(thread).ToInt64(); }
        long IClientKeyboardLayout.GetUsLayout()
        {
            var layouts = new IntPtr[GetKeyboardLayoutList(0, null)];
            var count = GetKeyboardLayoutList(layouts.Length, layouts);
            for (var i = 0; i < count && i < layouts.Length; i++) if ((layouts[i].ToInt64() & 0xFFFFFFFFL) == 0x04090409) return layouts[i].ToInt64();
            return 0;
        }
        // Inspect the effective mapping, not the PC model or input-language name. Japanese input can also use a US physical layout.
        bool IClientKeyboardLayout.HasReleaseKeys(long layout) { return MapVirtualKeyExW(0x29, 3, new IntPtr(layout)) == 0xC0 && MapVirtualKeyExW(0x3A, 3, new IntPtr(layout)) == 0x14; }
        bool IClientKeyboardLayout.Request(long window, long layout) { return window != 0 && layout != 0 && KeyboardNative.GetForegroundWindow().ToInt64() == window && PostMessageW(new IntPtr(window), 0x50, IntPtr.Zero, new IntPtr(layout)); }
    }
}
