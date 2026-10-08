using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PseudoSleep;

internal static class FileLocation
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);

    internal static string Resolve(SafeFileHandle file)
    {
        if (!OperatingSystem.IsWindows()) return "";
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(file, buffer, (uint)buffer.Capacity, 0);
        if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (length >= buffer.Capacity) throw new IOException("設定の実際の保存先パスが長すぎます。");
        return buffer.ToString();
    }
}
