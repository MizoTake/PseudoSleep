using System.Globalization;
using System.Text.RegularExpressions;

namespace PseudoSleep;

internal static class RemoteImeConfiguration
{
    internal static void Verify(string text)
    {
        var clean = Regex.Replace(text, "#[^\\r\\n]*", "");
        if (Regex.Matches(clean, @"(?m)^\s*keybindings\s*=").Count != 1) throw new InvalidDataException("IME bridge needs one Sunshine keybindings list. Run Set-RemoteImeBridge.ps1 -Mode Enabled.");
        var list = Regex.Match(clean, @"(?m)^\s*keybindings\s*=\s*\[([^\]]*)\][\t ]*(?=\r?$)");
        if (!list.Success || !Regex.IsMatch(list.Groups[1].Value, @"\A\s*(?:(?:0[xX][0-9a-fA-F]+|[0-9]+)\s*,\s*)*(?:0[xX][0-9a-fA-F]+|[0-9]+)?\s*\z")) throw new InvalidDataException("Invalid Sunshine keybindings list.");
        var values = Regex.Matches(list.Groups[1].Value, @"0[xX][0-9a-fA-F]+|[0-9]+").Select(m => m.Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? int.Parse(m.Value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : int.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
        if (values.Length % 2 != 0 || values.Any(v => v is < 0 or > 255)) throw new InvalidDataException("Invalid Sunshine keybinding pairs.");
        var map = new Dictionary<int, int>();
        for (var i = 0; i < values.Length; i += 2) if (!map.TryAdd(values[i], values[i + 1])) throw new InvalidDataException("Duplicate Sunshine keybindings.");
        Dictionary<int, int> expected = new() { [0x14] = 0x7D, [0xC0] = 0x7E, [0x7C] = 0x7C };
        if (expected.Any(pair => !map.TryGetValue(pair.Key, out var value) || value != pair.Value) || map.Any(pair => pair.Value is >= 0x7C and <= 0x7E && pair.Key != pair.Value && (!expected.TryGetValue(pair.Key, out var value) || value != pair.Value))) throw new InvalidDataException("Sunshine IME bridge mappings changed. Run Set-RemoteImeBridge.ps1 -Mode Enabled before reconnecting.");
    }
}
