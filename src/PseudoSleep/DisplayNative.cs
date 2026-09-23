using System.Runtime.InteropServices;

namespace PseudoSleep;

internal static class DisplayNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rational { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)] internal struct Source { public Luid Adapter; public uint Id; public uint ModeIndex; public uint Status; }
    [StructLayout(LayoutKind.Sequential)] internal struct Target { public Luid Adapter; public uint Id; public uint ModeIndex; public uint Technology; public uint Rotation; public uint Scaling; public Rational Refresh; public uint ScanLine; public int Available; public uint Status; }
    [StructLayout(LayoutKind.Sequential)] internal struct DisplayPath { public Source Source; public Target Target; public uint Flags; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] internal struct Mode
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public Luid Adapter;
        [FieldOffset(16)] public uint Width;
        [FieldOffset(20)] public uint Height;
        [FieldOffset(24)] public uint PixelFormat;
        [FieldOffset(28)] public int X;
        [FieldOffset(32)] public int Y;
        [FieldOffset(16)] public ulong PixelRate;
        [FieldOffset(24)] public Rational HSync;
        [FieldOffset(32)] public Rational VSync;
        [FieldOffset(40)] public uint ActiveWidth;
        [FieldOffset(44)] public uint ActiveHeight;
        [FieldOffset(48)] public uint TotalWidth;
        [FieldOffset(52)] public uint TotalHeight;
        [FieldOffset(56)] public uint VideoStandard;
        [FieldOffset(60)] public uint ScanLine;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Header { public uint Type; public uint Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct TargetName { public Header Header; public uint Flags; public uint Technology; public ushort Manufacturer; public ushort Product; public uint Connector; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Path; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct SourceName { public Header Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct AdapterName { public Header Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Path; }
    [DllImport("user32.dll")] internal static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] internal static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DisplayPath[] paths, ref uint modeCount, [Out] Mode[] modes, nint topology);
    [DllImport("user32.dll")] internal static extern int SetDisplayConfig(uint pathCount, DisplayPath[]? paths, uint modeCount, Mode[]? modes, uint flags);
    [StructLayout(LayoutKind.Explicit, Size = 220)] internal struct DeviceMode { [FieldOffset(68)] public ushort Size; [FieldOffset(172)] public uint Width; [FieldOffset(176)] public uint Height; [FieldOffset(184)] public uint Frequency; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsExW")] internal static extern bool EnumDisplaySettings(string name, uint index, ref DeviceMode mode, uint flags);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] internal static extern int GetTargetName(ref TargetName name);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] internal static extern int GetSourceName(ref SourceName name);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] internal static extern int GetAdapterName(ref AdapterName name);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
}
