using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PseudoSleep.KeyboardBridge
{
    internal static class KeyboardNative
    {
        internal static readonly UIntPtr Tag = new UIntPtr(0x5053494D);
        [StructLayout(LayoutKind.Sequential)] private struct Keyboard { internal ushort Key; internal ushort Scan; internal uint Flags; internal uint Time; internal UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct Mouse { internal int X; internal int Y; internal uint Data; internal uint Flags; internal uint Time; internal UIntPtr Extra; }
        [StructLayout(LayoutKind.Explicit)] private struct Payload { [FieldOffset(0)] internal Keyboard Keyboard; [FieldOffset(0)] internal Mouse Mouse; }
        [StructLayout(LayoutKind.Sequential)] private struct Input { internal uint Type; internal Payload Data; }
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        internal static int InputSize { get { return Marshal.SizeOf(typeof(Input)); } }
        internal static bool ModifiersDown() { foreach (var key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }) if ((GetAsyncKeyState(key) & 0x8000) != 0) return true; return false; }

        internal static void Pulse(ushort key)
        {
            var down = new Input { Type = 1, Data = new Payload { Keyboard = new Keyboard { Key = key, Extra = Tag } } };
            var up = down;
            up.Data.Keyboard.Flags = 2;
            var count = SendInput(2, new[] { down, up }, InputSize);
            if (count == 2) return;
            var error = Marshal.GetLastWin32Error();
            // Cleanup only: never retry a toggle or synthesize another down on failure.
            if (count == 1) SendInput(1, new[] { up }, InputSize);
            throw new Win32Exception(error, "Key pulse was not fully inserted. Run Moonlight and the helper as the same normal user.");
        }
    }
}
