using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PseudoSleep;

internal sealed record InputDevice(string Path, string Kind);

internal sealed class InputWindow : NativeWindow, IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Registration { public ushort Page; public ushort Usage; public uint Flags; public nint Window; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceList { public nint Handle; public uint Type; }
    [StructLayout(LayoutKind.Sequential)] private struct RawHeader { public uint Type; public uint Size; public nint Device; public nuint WParam; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(Registration[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputDeviceList([Out] DeviceList[]? devices, ref uint count, uint size);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetRawInputDeviceInfo(nint device, uint command, StringBuilder? data, ref uint size);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
    internal event Action<string>? Input;
    internal event Action? ForceWake;
    internal bool HotkeyRegistered { get; }
    private readonly Dictionary<nint, string> names = [];

    internal InputWindow()
    {
        CreateHandle(new CreateParams { Caption = "PseudoSleep.Input", Parent = -3 });
        Registration[] devices = [new() { Page = 1, Usage = 2, Flags = 0x100 | 0x2000, Window = Handle }, new() { Page = 1, Usage = 6, Flags = 0x100 | 0x2000, Window = Handle }];
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<Registration>())) throw new Win32Exception(Marshal.GetLastWin32Error(), "Raw Input registration failed.");
        HotkeyRegistered = RegisterHotKey(Handle, 1, 1 | 2 | 4 | 0x4000, 0x7b);
        if (!HotkeyRegistered) Storage.Log("Emergency hotkey already in use; CLI recovery remains available.");
    }

    internal static List<InputDevice> Enumerate()
    {
        uint count = 0;
        var size = (uint)Marshal.SizeOf<DeviceList>();
        if (GetRawInputDeviceList(null, ref count, size) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
        for (var retry = 0; retry < 5; retry++)
        {
            var devices = new DeviceList[count];
            var result = GetRawInputDeviceList(devices, ref count, size);
            if (result == uint.MaxValue) { if (Marshal.GetLastWin32Error() == 122) continue; throw new Win32Exception(Marshal.GetLastWin32Error()); }
            return devices.Take((int)result).Where(d => d.Type < 2).Select(d => new InputDevice(DeviceName(d.Handle), d.Type == 0 ? "Mouse" : "Keyboard")).Where(d => d.Path.Length > 0).ToList();
        }
        throw new InvalidOperationException("Input device list is changing.");
    }

    private static string DeviceName(nint device)
    {
        uint size = 0;
        if (GetRawInputDeviceInfo(device, 0x20000007, null, ref size) == uint.MaxValue || size == 0) return "";
        var text = new StringBuilder((int)size);
        return GetRawInputDeviceInfo(device, 0x20000007, text, ref size) == uint.MaxValue ? "" : text.ToString();
    }

    protected override void WndProc(ref Message message)
    {
        try
        {
            if (message.Msg == 0x0312) ForceWake?.Invoke();
            if (message.Msg == 0x00fe) names.Clear();
            if (message.Msg == 0x00ff)
            {
                uint size = 0;
                var headerSize = (uint)Marshal.SizeOf<RawHeader>();
                if (GetRawInputData(message.LParam, 0x10000003, 0, ref size, headerSize) != uint.MaxValue && size >= headerSize && size <= 65536)
                {
                    var memory = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        if (GetRawInputData(message.LParam, 0x10000003, memory, ref size, headerSize) == size && size >= headerSize)
                        {
                            var header = Marshal.PtrToStructure<RawHeader>(memory);
                            if (header.Device != 0 && header.Type < 2 && header.Size <= size && header.Size >= headerSize + (header.Type == 0 ? 24u : 16u))
                            {
                                if (!names.TryGetValue(header.Device, out var name)) { name = DeviceName(header.Device); names[header.Device] = name; }
                                var body = memory + (int)headerSize;
                                var meaningful = header.Type == 1 ? (Marshal.ReadInt16(body, 2) & 1) == 0 : Marshal.ReadInt16(body, 4) != 0 || Marshal.ReadInt32(body, 12) != 0 || Marshal.ReadInt32(body, 16) != 0;
                                if (meaningful && name.Length > 0) Input?.Invoke(name);
                            }
                        }
                    }
                    finally { Marshal.FreeHGlobal(memory); }
                }
            }
        }
        catch (Exception ex) { Storage.Log($"Input callback: {ex}"); }
        base.WndProc(ref message);
    }

    void IDisposable.Dispose() { UnregisterHotKey(Handle, 1); DestroyHandle(); }
}
