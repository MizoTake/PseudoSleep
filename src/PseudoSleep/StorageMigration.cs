using System.Text;
using System.Text.Json;
using PseudoSleep.Core;

namespace PseudoSleep;

internal static class StorageMigration
{
    internal static void Import(string source, string oldDataDirectory, string destination)
    {
        foreach (var name in new[] { "state.json", "audio-state.json" })
            if (File.Exists(Path.Combine(oldDataDirectory, name))) throw new InvalidOperationException("旧版で画面・音量を復元してから設定を移行してください。復旧情報は変更していません。");
        var bytes = File.ReadAllBytes(source);
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
        var config = JsonSerializer.Deserialize<AppConfig>(reader.ReadToEnd(), Storage.Json) ?? throw new InvalidDataException("移行元の設定が空です。");
        config.Validate();
        if (File.Exists(destination))
        {
            if (bytes.SequenceEqual(File.ReadAllBytes(destination))) return;
            throw new InvalidOperationException("共通保存先には別の設定があります。既存の設定は上書きしません。");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { file.Write(bytes); file.Flush(true); }
            File.Move(temporary, destination, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
