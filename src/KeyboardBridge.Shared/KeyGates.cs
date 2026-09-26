using System.Collections.Generic;

namespace PseudoSleep.KeyboardBridge
{
    // C# 5 compatible: the client is built with the .NET Framework supplied by Windows.
    internal sealed class ClientKeyGate
    {
        private sealed class Press { internal bool Eligible; internal long Context; }
        private readonly Dictionary<long, Dictionary<int, Press>> devices = new Dictionary<long, Dictionary<int, Press>>();

        internal static int KeyIndex(int scan, int flags)
        {
            if (scan < 0 || scan >= 0xFF || (flags & 6) != 0) return -1;
            scan &= 0x7F;
            return scan == 0x3A ? 0 : scan == 0x29 ? 1 : -1;
        }

        internal bool Observe(long device, int scan, int flags, bool eligible, bool modifiers, long context)
        {
            if (device == 0) return false;
            if (modifiers) Cancel();
            if (KeyIndex(scan, flags) < 0) return false;
            scan &= 0x7F;
            if (!devices.ContainsKey(device)) devices.Add(device, new Dictionary<int, Press>());
            var keys = devices[device];
            if ((flags & 1) == 0)
            {
                if (!keys.ContainsKey(scan)) keys.Add(scan, new Press { Eligible = eligible && !modifiers, Context = context });
                return false;
            }
            if (!keys.ContainsKey(scan)) return false;
            var press = keys[scan];
            keys.Remove(scan);
            return press.Eligible && eligible && !modifiers && press.Context == context;
        }

        // Keep held keys blocked until their real release, even after focus returns.
        internal void Cancel() { foreach (var keys in devices.Values) foreach (var press in keys.Values) press.Eligible = false; }
        internal void RemoveDevice(long device) { devices.Remove(device); }
    }

    internal enum HostKeyAction { Pass, Suppress, Toggle }

    internal sealed class HostKeyGate
    {
        private bool pending;
        internal HostKeyAction Observe(int key, bool down, bool injected, bool own)
        {
            if (!injected || own || key < 0x7C || key > 0x7E) return HostKeyAction.Pass;
            if (key != 0x7C) return HostKeyAction.Suppress;
            if (down) { pending = true; return HostKeyAction.Suppress; }
            var complete = pending;
            pending = false;
            return complete ? HostKeyAction.Toggle : HostKeyAction.Suppress;
        }
        internal void Reset() { pending = false; }
    }
}
