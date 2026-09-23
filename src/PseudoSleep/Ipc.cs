using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace PseudoSleep;

internal sealed record Command(string Name, int TestSeconds = 0, int Width = 0, int Height = 0, bool Force = false);
internal sealed record Response(bool Success, object? Data, string? Error = null);

internal static class Ipc
{
    internal static readonly string Prefix = "PseudoSleep." + WindowsIdentity.GetCurrent().User!.Value + "." + Process.GetCurrentProcess().SessionId;
    internal static readonly string MutexName = "Local\\" + Prefix + ".main";
    internal static async Task<Response> Send(Command command, int connectTimeout = 2000)
    {
        using var pipe = new NamedPipeClientStream(".", Prefix, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(connectTimeout);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(command));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("IPC connection closed.");
        return JsonSerializer.Deserialize<Response>(line) ?? throw new IOException("Invalid IPC response.");
    }

    internal static async Task Serve(Func<Command, Task<Response>> execute, CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(Prefix, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancel);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(TimeSpan.FromSeconds(40));
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                var buffer = new char[1024];
                var length = 0;
                while (length < buffer.Length) { var read = await reader.ReadAsync(buffer.AsMemory(length, 1), timeout.Token); if (read == 0 || buffer[length] == '\n') break; length++; }
                if (length == buffer.Length) throw new InvalidDataException("IPC command too long.");
                var command = JsonSerializer.Deserialize<Command>(new string(buffer, 0, length)) ?? throw new InvalidDataException("Empty command.");
                var result = await execute(command).WaitAsync(timeout.Token);
                await writer.WriteLineAsync(JsonSerializer.Serialize(result));
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { break; }
            catch (Exception ex) { Storage.Log($"IPC: {ex.Message}"); }
        }
    }
}
