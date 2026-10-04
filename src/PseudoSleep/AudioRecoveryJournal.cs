using System.Text;
using System.Text.Json;
using PseudoSleep.Core;

namespace PseudoSleep;

internal sealed class AudioRecoveryJournal(string? filePath = null) : IAudioJournal
{
    private readonly string path = filePath ?? Storage.AudioStatePath;
    private sealed record Record(int Version, List<AudioVolumeSnapshot> Outputs);

    IReadOnlyList<AudioVolumeSnapshot> IAudioJournal.Read()
    {
        if (!File.Exists(path)) return [];
        var record = JsonSerializer.Deserialize<Record>(File.ReadAllText(path, Encoding.UTF8), Storage.Json) ?? throw new InvalidDataException("Invalid audio-state.json; preserve it for recovery.");
        if (record.Version != 1) throw new InvalidDataException("Unsupported audio recovery record.");
        AudioController.Validate(record.Outputs);
        return record.Outputs;
    }

    void IAudioJournal.Write(IReadOnlyList<AudioVolumeSnapshot> snapshots)
    {
        AudioController.Validate(snapshots);
        if (snapshots.Count == 0) File.Delete(path);
        else Storage.Write(path, new Record(1, snapshots.ToList()));
    }
}
