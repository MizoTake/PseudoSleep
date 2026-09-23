using System.Text;

namespace PseudoSleep;

internal sealed class SunshineSessionMonitor
{
    private string? path;
    private long offset;
    private DateTime creation;
    private string partial = "";
    private int clients;
    private bool connected;
    private long deadline;
    internal bool Armed => path != null;

    internal void Arm(string logPath, long now)
    {
        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        path = logPath;
        offset = stream.Length;
        creation = File.GetCreationTimeUtc(logPath);
        partial = "";
        clients = 0;
        connected = false;
        deadline = now + 30000;
    }

    internal void Reset() { path = null; clients = 0; partial = ""; connected = false; }

    internal bool Poll(long now)
    {
        if (path == null) return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (File.GetCreationTimeUtc(path) != creation || stream.Length < offset) return true;
        stream.Position = offset;
        var remaining = stream.Length - offset;
        if (remaining > 1048576) throw new IOException("Sunshine log grew too quickly to track the current session safely.");
        var bytes = new byte[(int)remaining];
        stream.ReadExactly(bytes);
        offset += bytes.Length;
        var lines = (partial + Encoding.UTF8.GetString(bytes)).Split('\n');
        partial = lines[^1];
        if (partial.Length > 65536) throw new IOException("Sunshine log line is too long.");
        foreach (var line in lines.SkipLast(1))
        {
            var text = line.TrimEnd('\r');
            if (text.EndsWith(": Info: CLIENT CONNECTED", StringComparison.Ordinal)) { clients++; connected = true; }
            else if (text.EndsWith(": Info: CLIENT DISCONNECTED", StringComparison.Ordinal) && clients > 0) clients--;
        }
        return connected ? clients == 0 : now >= deadline;
    }
}
