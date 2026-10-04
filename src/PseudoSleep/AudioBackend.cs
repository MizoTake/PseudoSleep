using System.Runtime.InteropServices;
using PseudoSleep.Core;

namespace PseudoSleep;

internal sealed class AudioBackend : IAudioBackend
{
    private const int NotFound = unchecked((int)0x80070490);

    IReadOnlyList<AudioVolumeSnapshot> IAudioBackend.GetDefaultVolumes()
    {
        var enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
        try
        {
            var result = new List<AudioVolumeSnapshot>();
            // Console and multimedia defaults may differ. Preserve their endpoint identities independently.
            foreach (var role in new[] { 0, 1 })
            {
                IMMDevice device;
                try { enumerator.GetDefaultAudioEndpoint(0, role, out device); }
                catch (COMException ex) when (ex.HResult == NotFound) { continue; }
                try
                {
                    device.GetId(out var id);
                    if (result.Any(value => value.EndpointId == id)) continue;
                    var volume = Activate(device);
                    try { volume.GetMasterVolumeLevelScalar(out var scalar); result.Add(new(id, scalar)); }
                    finally { Marshal.ReleaseComObject(volume); }
                }
                finally { Marshal.ReleaseComObject(device); }
            }
            return result;
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    void IAudioBackend.SetVolume(string endpointId, float scalar)
    {
        AudioController.Validate([new(endpointId, scalar)]);
        var enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
        try
        {
            enumerator.GetDevice(endpointId, out var device);
            try
            {
                device.GetState(out var state);
                if (state != 1) throw new IOException("Audio output is unavailable: " + endpointId);
                var volume = Activate(device);
                try { var context = Guid.Empty; volume.SetMasterVolumeLevelScalar(scalar, ref context); }
                finally { Marshal.ReleaseComObject(volume); }
            }
            finally { Marshal.ReleaseComObject(device); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    private static IAudioEndpointVolume Activate(IMMDevice device) { var id = typeof(IAudioEndpointVolume).GUID; device.Activate(ref id, 23, 0, out var instance); return (IAudioEndpointVolume)instance; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class DeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, uint stateMask, out nint devices);
        void GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid id, uint context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        void OpenPropertyStore(uint access, out nint properties);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out uint state);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(nint callback);
        void UnregisterControlChangeNotify(nint callback);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float decibels, ref Guid context);
        void SetMasterVolumeLevelScalar(float scalar, ref Guid context);
        void GetMasterVolumeLevel(out float decibels);
        void GetMasterVolumeLevelScalar(out float scalar);
    }
}
