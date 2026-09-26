using PseudoSleep.KeyboardBridge;

internal static class ClientLayoutTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    [
        ("Release-capable input layouts do not need switching", () => { var f = new Fixture { Layout = 2 }; Assert(f.Guard.Update(1, true, true, 0)); Assert(f.Requests == 0 && f.Guard.State == ClientLayoutState.Ready); }),
        ("NLS input waits for verified layout change before allowing a relay", () => { var f = new Fixture(); Assert(!f.Guard.Update(1, true, true, 0)); Assert(f.Requests == 1 && f.Guard.State == ClientLayoutState.Waiting); Assert(!f.Guard.Update(1, true, true, 100)); Assert(f.Requests == 1); f.Layout = 2; Assert(f.Guard.Update(1, true, true, 200)); Assert(f.Guard.CurrentLayout == 2); }),
        ("Missing US input is actionable and never requests an invalid layout", () => { var f = new Fixture { UsLayout = 0 }; Assert(!f.Guard.Update(1, true, true, 0)); Assert(f.Requests == 0 && f.Guard.State == ClientLayoutState.MissingUsInput); }),
        ("Manual layout mode does not overwrite the selected input", () => { var f = new Fixture(); Assert(!f.Guard.Update(1, true, false, 0)); Assert(f.Requests == 0 && f.Guard.State == ClientLayoutState.NeedsUsInput); f.Layout = 2; Assert(f.Guard.Update(1, true, false, 1)); }),
        ("Disabled helper and non-Moonlight windows never change input", () => { var f = new Fixture(); Assert(!f.Guard.Update(1, false, true, 0)); Assert(!f.Guard.Update(0, true, true, 0)); Assert(f.Requests == 0 && f.Guard.State == ClientLayoutState.Inactive); }),
        ("Rejected input request is not retried on every poll", () => { var f = new Fixture { Accept = false }; Assert(!f.Guard.Update(1, true, true, 0)); Assert(f.Guard.State == ClientLayoutState.Rejected); Assert(!f.Guard.Update(1, true, true, 3000)); Assert(f.Requests == 1); }),
        ("Ignored layout requests time out without assuming success", () => { var f = new Fixture(); f.Guard.Update(1, true, true, 0); Assert(!f.Guard.Update(1, true, true, 2000)); Assert(f.Guard.State == ClientLayoutState.Rejected && f.Requests == 1); f.Layout = 2; Assert(f.Guard.Update(1, true, true, 2001)); }),
        ("Focus return permits a fresh layout request after rejection", () => { var f = new Fixture { Accept = false }; f.Guard.Update(1, true, true, 0); f.Guard.Update(0, true, true, 1); f.Accept = true; Assert(!f.Guard.Update(1, true, true, 2)); Assert(f.Requests == 2 && f.Guard.State == ClientLayoutState.Waiting); }),
        ("Unknown target layout fails closed", () => { var f = new Fixture { Layout = 0 }; Assert(!f.Guard.Update(1, true, true, 0)); Assert(f.Requests == 0 && f.Guard.State == ClientLayoutState.Unavailable); }),
        ("Layout changes cannot manufacture a release or revive an old hold", () => { var gate = new ClientKeyGate(); gate.Observe(1, 0x29, 0, true, false, 1); gate.Cancel(); Assert(!gate.Observe(1, 0x29, 0, true, false, 2)); Assert(!gate.Observe(1, 0x29, 1, true, false, 2)); gate.Observe(1, 0x29, 0, true, false, 2); Assert(gate.Observe(1, 0x29, 1, true, false, 2)); }),
        ("Raw break scan codes are normalized before matching a physical press", () => { var gate = new ClientKeyGate(); foreach (var scan in new[] { 0x29, 0x3A }) { Assert(!gate.Observe(1, scan, 0, true, false, 1)); Assert(gate.Observe(1, scan | 0x80, 1, true, false, 1)); } }),
        ("Overrun and extended raw scan codes cannot release a held key", () => { var gate = new ClientKeyGate(); gate.Observe(1, 0x29, 0, true, false, 1); Assert(!gate.Observe(1, 0xFF, 1, true, false, 1)); Assert(!gate.Observe(1, 0x129, 1, true, false, 1)); Assert(!gate.Observe(1, 0xA9, 3, true, false, 1)); })
    ];

    private static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Client layout assertion failed."); }
    private sealed class Fixture : IClientKeyboardLayout
    {
        internal long Layout = 1;
        internal long UsLayout = 2;
        internal bool Accept = true;
        internal int Requests;
        internal readonly ClientLayoutGuard Guard;
        internal Fixture() { Guard = new(this); }
        long IClientKeyboardLayout.GetLayout(long window) => Layout;
        long IClientKeyboardLayout.GetUsLayout() => UsLayout;
        bool IClientKeyboardLayout.HasReleaseKeys(long layout) => layout == 2;
        bool IClientKeyboardLayout.Request(long window, long layout) { Requests++; Assert(window == 1 && layout == 2); return Accept; }
    }
}
