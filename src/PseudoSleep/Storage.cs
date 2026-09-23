using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PseudoSleep.Core;

namespace PseudoSleep;

internal static class Storage
{
    internal static readonly string ConfigDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PseudoSleep");
    internal static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PseudoSleep");
    internal static readonly string ConfigPath = Path.Combine(ConfigDirectory, "config.json");
    internal static readonly string StatePath = Path.Combine(DataDirectory, "state.json");
    internal static readonly string LogDirectory = Path.Combine(DataDirectory, "Logs");
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
    private static readonly object logLock = new();

    internal static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { JsonSerializer.Serialize(stream, value, Json); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static AppConfig LoadConfig()
    {
        if (!File.Exists(ConfigPath)) Write(ConfigPath, new AppConfig());
        var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath, Encoding.UTF8), Json) ?? throw new InvalidDataException("Empty config.json");
        config.Validate();
        return config;
    }

    internal static void Log(string message) => Log(message, LogDirectory);

    internal static void Log(string message, string directory)
    {
        try
        {
            lock (logLock)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, DateTime.Now.ToString("yyyy-MM-dd") + ".log"), $"{DateTimeOffset.Now:O} [{Environment.ProcessId}] {message}{Environment.NewLine}", new UTF8Encoding(false));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { /* Diagnostic output must not interrupt display restoration or guardian retries. */ }
    }
}

internal sealed class RecoveryJournal : IRecoveryJournal
{
    RecoveryRecord? IRecoveryJournal.Read()
    {
        if (!File.Exists(Storage.StatePath)) return null;
        var record = JsonSerializer.Deserialize<RecoveryRecord>(File.ReadAllText(Storage.StatePath, Encoding.UTF8), Storage.Json) ?? throw new InvalidDataException("Invalid state.json; preserve it for recovery.");
        if (record.Version != 1 || record.Backup == null) throw new InvalidDataException("Unsupported recovery record.");
        return record;
    }

    void IRecoveryJournal.Save(RecoveryRecord record)
    {
        Storage.Write(Path.Combine(Storage.DataDirectory, "display-backup.json"), record.Backup);
        Storage.Write(Storage.StatePath, record);
        Storage.Log("Recovery journal saved before display change.");
    }

    void IRecoveryJournal.Clear() { File.Delete(Storage.StatePath); Storage.Log("Recovery journal cleared after verified restoration."); }
}
