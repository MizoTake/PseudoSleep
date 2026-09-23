using System.Runtime.InteropServices;

namespace PseudoSleep;

// The ABI and vtable slots come from the Windows SDK dxgi.h / dxgi1_2.h.
internal static class CaptureProbe
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct OutputDescription { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; public int Left; public int Top; public int Right; public int Bottom; public int Attached; public uint Rotation; public nint Monitor; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Enumerate(nint self, uint index, out nint result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Describe(nint self, out OutputDescription description);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Duplicate(nint self, nint device, out nint duplication);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Acquire(nint self, uint timeout, nint info, out nint resource);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReleaseFrame(nint self);
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out nint factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(nint adapter, uint driverType, nint software, uint flags, nint levels, uint count, uint sdkVersion, out nint device, out uint featureLevel, out nint context);
    private static T Method<T>(nint instance, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));

    internal static void Verify(string sourceName)
    {
        var factoryId = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref factoryId, out var factory));
        try
        {
            for (uint adapterIndex = 0; ; adapterIndex++)
            {
                var error = Method<Enumerate>(factory, 12)(factory, adapterIndex, out var adapter);
                if (error == unchecked((int)0x887a0002)) break;
                Marshal.ThrowExceptionForHR(error);
                try
                {
                    for (uint outputIndex = 0; ; outputIndex++)
                    {
                        error = Method<Enumerate>(adapter, 7)(adapter, outputIndex, out var output);
                        if (error == unchecked((int)0x887a0002)) break;
                        Marshal.ThrowExceptionForHR(error);
                        try
                        {
                            Marshal.ThrowExceptionForHR(Method<Describe>(output, 7)(output, out var description));
                            if (description.Attached == 0 || !description.Name.Equals(sourceName, StringComparison.OrdinalIgnoreCase)) continue;
                            VerifyOutput(adapter, output);
                            Storage.Log($"Desktop Duplication frame acquired: {sourceName}");
                            return;
                        }
                        finally { Marshal.Release(output); }
                    }
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        throw new InvalidOperationException($"DXGI cannot see the configured virtual desktop: {sourceName}");
    }

    private static void VerifyOutput(nint adapter, nint output)
    {
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(adapter, 0, 0, 0x20, 0, 0, 7, out var device, out _, out var context));
        try
        {
            var output1Id = new Guid("00cddea8-939b-4b83-a340-a685226666cc");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(output, ref output1Id, out var output1));
            try
            {
                Marshal.ThrowExceptionForHR(Method<Duplicate>(output1, 22)(output1, device, out var duplication));
                try
                {
                    var info = Marshal.AllocHGlobal(128);
                    try
                    {
                        Marshal.ThrowExceptionForHR(Method<Acquire>(duplication, 8)(duplication, 2000, info, out var resource));
                        try { if (resource == 0) throw new InvalidOperationException("Desktop Duplication returned no frame."); }
                        finally { if (resource != 0) Marshal.Release(resource); _ = Method<ReleaseFrame>(duplication, 14)(duplication); }
                    }
                    finally { Marshal.FreeHGlobal(info); }
                }
                finally { Marshal.Release(duplication); }
            }
            finally { Marshal.Release(output1); }
        }
        finally { Marshal.Release(context); Marshal.Release(device); }
    }
}
