using PseudoSleep.Core;

internal static class SunshineDisplayPolicyTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    [
        ("Sunshine omitted display policy uses disabled default", () => SunshineDisplayPolicy.Verify(new(), new Dictionary<string, string>())),
        ("Sunshine explicit disabled policy is accepted", () => SunshineDisplayPolicy.Verify(new(), new Dictionary<string, string> { ["dd_configuration_option"] = "disabled" })),
        ("Sunshine empty default value is accepted", () => SunshineDisplayPolicy.Verify(new(), new Dictionary<string, string> { ["dd_configuration_option"] = "", ["output_name"] = "" })),
        ("Sunshine active and unknown display policies are still rejected", () => { foreach (var value in new[] { "verify_only", "ensure_active", "ensure_primary", "ensure_only_display", "unknown" }) Throws(() => SunshineDisplayPolicy.Verify(new(), new Dictionary<string, string> { ["dd_configuration_option"] = value })); }),
        ("Sunshine fixed output conflicts with on-demand display", () => Throws(() => SunshineDisplayPolicy.Verify(new(), new Dictionary<string, string> { ["output_name"] = "fixed" }))),
        ("Persistent display still requires the matching output ID", () => { var config = new AppConfig { KeepVirtualDisplayInNormalMode = true, VirtualDisplayDeviceId = "fixed" }; Throws(() => SunshineDisplayPolicy.Verify(config, new Dictionary<string, string>())); SunshineDisplayPolicy.Verify(config, new Dictionary<string, string> { ["output_name"] = "fixed" }); }),
    ];
    private static void Throws(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidOperationException("Expected policy rejection."); }
}
