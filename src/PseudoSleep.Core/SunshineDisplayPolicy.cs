namespace PseudoSleep.Core;

public static class SunshineDisplayPolicy
{
    public static void Verify(AppConfig config, IReadOnlyDictionary<string, string> values)
    {
        values.TryGetValue("output_name", out var output);
        if (config.KeepVirtualDisplayInNormalMode)
        {
            if (string.IsNullOrWhiteSpace(config.VirtualDisplayDeviceId) || !string.Equals(output, config.VirtualDisplayDeviceId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Sunshineの配信先が登録済みの仮想画面IDと一致しません。通常時にVDDを停止する設定とSunshineの出力設定を確認してください。");
        }
        else if (!string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("配信中だけ仮想画面を使うには、Sunshineの出力を自動選択にしてください。");
        if (!config.KeepVirtualDisplayInNormalMode && !config.DisconnectMoonlightOnWake) throw new InvalidOperationException("配信中だけ仮想画面を使う構成では、物理復帰時の切断を有効にしてください。");
        // Sunshine v2026.914.233613 defaults to disabled and its Web UI omits default values.
        if (values.TryGetValue("dd_configuration_option", out var policy) && !string.IsNullOrWhiteSpace(policy) && policy != "disabled") throw new InvalidOperationException("Sunshineの「デバイス設定」を「無効」にしてください。画面構成はPseudoSleepで管理します。");
    }
}
