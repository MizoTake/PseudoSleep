namespace PseudoSleep.Core;

public sealed record AudioVolumeSnapshot(string EndpointId, float Volume);
public interface IAudioBackend { IReadOnlyList<AudioVolumeSnapshot> GetDefaultVolumes(); void SetVolume(string endpointId, float volume); }
public interface IAudioJournal { IReadOnlyList<AudioVolumeSnapshot> Read(); void Write(IReadOnlyList<AudioVolumeSnapshot> snapshots); }

public sealed class AudioController(IAudioBackend audio, IAudioJournal journal, IGuardian guardian)
{
    public int PendingEndpoints { get; private set; }

    public void Update(AppState state, bool? connected, bool enabled = true)
    {
        if (state != AppState.PseudoSleep || connected != false || !enabled) { Restore(); return; }
        var saved = ReadSaved();
        var current = audio.GetDefaultVolumes();
        Validate(current);
        if (current.Count == 0) return;
        var added = current.Where(value => !saved.Any(previous => previous.EndpointId == value.EndpointId)).ToArray();
        if (added.Length > 0) { saved.AddRange(added); journal.Write(saved); PendingEndpoints = saved.Count; }
        // Both the durable original and an independent recovery process must exist before changing volume.
        guardian.EnsureReady();
        foreach (var value in current.Where(value => value.Volume != 0)) audio.SetVolume(value.EndpointId, 0);
    }

    public void Restore()
    {
        var saved = ReadSaved();
        if (saved.Count == 0) return;
        var pending = new List<AudioVolumeSnapshot>();
        var errors = new List<Exception>();
        foreach (var value in saved)
        {
            try { audio.SetVolume(value.EndpointId, value.Volume); }
            catch (Exception ex) { pending.Add(value); errors.Add(ex); }
        }
        if (pending.Count != saved.Count) journal.Write(pending);
        PendingEndpoints = pending.Count;
        if (errors.Count > 0) throw new AggregateException("Audio restoration is pending for unavailable outputs; saved volumes are retained.", errors);
    }

    private List<AudioVolumeSnapshot> ReadSaved() { var saved = journal.Read(); Validate(saved); PendingEndpoints = saved.Count; return saved.ToList(); }

    public static void Validate(IReadOnlyList<AudioVolumeSnapshot> snapshots)
    {
        if (snapshots == null || snapshots.Any(value => value == null || string.IsNullOrWhiteSpace(value.EndpointId) || !float.IsFinite(value.Volume) || value.Volume is < 0 or > 1) || snapshots.Select(value => value.EndpointId).Distinct(StringComparer.Ordinal).Count() != snapshots.Count) throw new InvalidDataException("Invalid audio recovery snapshot; preserve it for recovery.");
    }
}
