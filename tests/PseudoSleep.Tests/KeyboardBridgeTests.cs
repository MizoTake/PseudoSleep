using PseudoSleep.KeyboardBridge;
using PseudoSleep;

internal static class KeyboardBridgeTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    [
        ("Ten physical taps produce ten pulses", () => { var gate = new ClientKeyGate(); for (var i = 0; i < 10; i++) { Assert(!Key(gate, false)); Assert(Key(gate, true)); } }),
        ("Holding the IME key never repeats", () => { var gate = new ClientKeyGate(); for (var i = 0; i < 200; i++) Assert(!Key(gate, false)); Assert(Key(gate, true)); Assert(!Key(gate, true)); }),
        ("Missing physical release fails closed without a timer", () => { var gate = new ClientKeyGate(); for (var i = 0; i < 1000; i++) Assert(!Key(gate, false)); }),
        ("Focus loss and return during hold cancels the operation", () => { var gate = new ClientKeyGate(); Key(gate, false); gate.Cancel(); Assert(!Key(gate, false)); Assert(!Key(gate, true)); Key(gate, false); Assert(Key(gate, true)); }),
        ("Background press cannot become a toggle after focus changes", () => { var gate = new ClientKeyGate(); Key(gate, false, eligible: false); Assert(!Key(gate, true)); }),
        ("Changed foreground window cancels release", () => { var gate = new ClientKeyGate(); Key(gate, false); Assert(!Key(gate, true, context: 2)); }),
        ("Modifiers cancel an armed single-key operation", () => { var gate = new ClientKeyGate(); Key(gate, false); gate.Observe(1, 0x2A, 0, true, true, 1); Assert(!Key(gate, true)); Assert(!Key(gate, false, modifiers: true)); Assert(!Key(gate, true)); }),
        ("Physical keyboards have independent pressed state", () => { var gate = new ClientKeyGate(); Key(gate, false); Assert(!Key(gate, true, device: 2)); Assert(Key(gate, true)); }),
        ("Synthetic input and extended aliases do not generate pulses", () => { var gate = new ClientKeyGate(); Assert(!Key(gate, false, device: 0)); Assert(!Key(gate, true, device: 0)); Assert(!gate.Observe(1, 0x3A, 2, true, false, 1)); Assert(!gate.Observe(1, 0x3A, 3, true, false, 1)); }),
        ("Half-full key and Caps both accept complete cycles", () => { var gate = new ClientKeyGate(); foreach (var scan in new[] { 0x29, 0x3A }) { Assert(!gate.Observe(1, scan, 0, true, false, 1)); Assert(gate.Observe(1, scan, 1, true, false, 1)); } Assert(!gate.Observe(1, 0x1E, 0, true, false, 1)); Assert(!gate.Observe(1, 0x1E, 1, true, false, 1)); }),
        ("Removed keyboard cannot release an old press", () => { var gate = new ClientKeyGate(); Key(gate, false); gate.RemoveDevice(1); Assert(!Key(gate, true)); }),
        ("Host toggles only after a complete relay pulse", () => { var gate = new HostKeyGate(); Assert(gate.Observe(0x7C, false, true, false) == HostKeyAction.Suppress); Assert(gate.Observe(0x7C, true, true, false) == HostKeyAction.Suppress); Assert(gate.Observe(0x7C, false, true, false) == HostKeyAction.Toggle); Assert(gate.Observe(0x7C, false, true, false) == HostKeyAction.Suppress); }),
        ("Host suppresses repeated relay-down and missing release", () => { var gate = new HostKeyGate(); for (var i = 0; i < 1000; i++) Assert(gate.Observe(0x7C, true, true, false) == HostKeyAction.Suppress); Assert(gate.Observe(0x7C, false, true, false) == HostKeyAction.Toggle); }),
        ("Original JIS signals are consumed as inert reserved keys", () => { var gate = new HostKeyGate(); foreach (var key in new[] { 0x7D, 0x7E }) for (var i = 0; i < 1000; i++) Assert(gate.Observe(key, i % 2 == 0, true, false) == HostKeyAction.Suppress); }),
        ("Physical HHKB and other remote keys pass unchanged", () => { var gate = new HostKeyGate(); foreach (var key in new[] { 0x14, 0xC0, 0x41, 0x08, 0x7C, 0x7D, 0x7E }) Assert(gate.Observe(key, true, false, false) == HostKeyAction.Pass); foreach (var key in new[] { 0x41, 0x08, 0x25 }) Assert(gate.Observe(key, true, true, false) == HostKeyAction.Pass); }),
        ("Own injected events cannot recurse", () => { var gate = new HostKeyGate(); Assert(gate.Observe(0x7C, true, true, true) == HostKeyAction.Pass); Assert(gate.Observe(0x7C, false, true, false) == HostKeyAction.Suppress); }),
        ("Session reset clears pending relay action", () => { var gate = new HostKeyGate(); gate.Observe(0x7C, true, true, false); gate.Reset(); Assert(gate.Observe(0x7C, false, true, false) == HostKeyAction.Suppress); }),
        ("Host accepts only bridge-compatible Sunshine mappings", () => { RemoteImeConfiguration.Verify("# comment\nkeybindings = [20, 125, # caps\n192, 126, 124, 124, 74, 75,]\n"); foreach (var text in new[] { "", "keybindings = [20,244,192,126,124,124]", "keybindings = [20,125,192,126,124,124,74,124]", "keybindings = [20,125,192,126,124,124,20,125]", "keybindings = [20,125,192,126,124,124] trailing", "keybindings = [20,125,192,126,124,124]\nkeybindings = []", "keybindings = [20,,125,192,126,124,124]" }) { var failed = false; try { RemoteImeConfiguration.Verify(text); } catch { failed = true; } Assert(failed); } })
    ];

    private static bool Key(ClientKeyGate gate, bool up, bool eligible = true, bool modifiers = false, long context = 1, long device = 1) => gate.Observe(device, 0x3A, up ? 1 : 0, eligible, modifiers, context);
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("Keyboard bridge assertion failed."); }
}
