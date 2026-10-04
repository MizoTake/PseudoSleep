using System.Text;

namespace PseudoSleep;

// Unlike the stream preparation monitor, this also replays clients already connected before manual sleep.
internal sealed class SunshineConnectionMonitor
{
    private string? path;
    private long offset;
    private DateTime creation;
    private byte[] prefix = [];
    private string partial = "";
    private int clients;
    private bool known;

    internal bool? Poll(string logPath, bool running)
    {
        if (!running) { Reset(); return false; }
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var created = File.GetCreationTimeUtc(logPath);
            var observedPrefix = new byte[Math.Min(prefix.Length, (int)Math.Min(stream.Length, 256))];
            stream.ReadExactly(observedPrefix);
            if (path != logPath || created != creation || stream.Length < offset || !observedPrefix.SequenceEqual(prefix))
            {
                Reset(); path = logPath; creation = created;
                prefix = new byte[(int)Math.Min(stream.Length, 256)];
                stream.Position = 0; stream.ReadExactly(prefix);
            }
            stream.Position = offset;
            var remaining = stream.Length - offset;
            if (remaining > (offset == 0 ? 67108864 : 1048576)) throw new IOException("Sunshine log is too large to establish audio connection state safely.");
            var bytes = new byte[(int)remaining];
            stream.ReadExactly(bytes); offset += bytes.Length;
            if (prefix.Length == 0) prefix = bytes.Take(256).ToArray();
            var lines = (partial + Encoding.UTF8.GetString(bytes)).Split('\n');
            partial = lines[^1];
            if (partial.Length > 65536) throw new IOException("Sunshine log line is too long.");
            foreach (var line in lines.SkipLast(1))
            {
                var text = line.TrimEnd('\r');
                if (!text.StartsWith('[')) continue;
                if (text.Contains("]: Info: Sunshine version: ", StringComparison.Ordinal)) { clients = 0; known = true; }
                else if (text.EndsWith("]: Info: CLIENT CONNECTED", StringComparison.Ordinal)) clients++;
                else if (text.EndsWith("]: Info: CLIENT DISCONNECTED", StringComparison.Ordinal)) { if (clients > 0) clients--; else known = false; }
            }
            return clients > 0 ? true : known ? false : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { Reset(); return null; }
    }

    private void Reset() { path = null; offset = 0; prefix = []; partial = ""; clients = 0; known = false; }
}
