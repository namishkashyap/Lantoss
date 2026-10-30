using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lantoss.Core;

/// <summary>Current-user-only IPC. Explorer never opens a second writer on the metadata store.</summary>
public sealed class DesktopInstance : IDisposable
{
    public const int MaxMessageBytes = 98304;
    public static string Key(string dataRoot) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataRoot).ToUpperInvariant())))[..20];
    // The Windows session ID keeps another signed-in user from pre-creating this name and blocking the hand-off.
    public static string PipeName(string dataRoot) => "Lantoss-" + System.Diagnostics.Process.GetCurrentProcess().SessionId + "-" + Key(dataRoot);
    private readonly CancellationTokenSource stopped = new();
    private readonly string name;
    private readonly Action<LaunchRequest> receive;
    private readonly Task worker;
    public DesktopInstance(string dataRoot, Action<LaunchRequest> receive)
    {
        name = PipeName(dataRoot);
        this.receive = receive;
        worker = ListenAsync();
    }
    public static byte[] Encode(LaunchRequest request)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(request.Checked());
        if (body.Length > MaxMessageBytes)
            throw new InvalidDataException("Too many long file paths. Add files from inside Lantoss instead.");
        return body;
    }
    private async Task ListenAsync()
    {
        int failures = 0;
        while (!stopped.IsCancellationRequested)
        {
            try
            {
                await using NamedPipeServerStream pipe = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stopped.Token);
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                byte[] header = new byte[4];
                await pipe.ReadExactlyAsync(header, timeout.Token);
                int length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length is < 1 or > MaxMessageBytes)
                    continue;
                byte[] body = new byte[length];
                await pipe.ReadExactlyAsync(body, timeout.Token);
                LaunchRequest request = (JsonSerializer.Deserialize<LaunchRequest>(body) ?? throw new InvalidDataException()).Checked();
                receive(request);
                await pipe.WriteAsync(new byte[] { 1 }, timeout.Token);
                await pipe.FlushAsync(timeout.Token);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or JsonException or InvalidDataException or UnauthorizedAccessException)
            {
                // A squatted or failing pipe backs off instead of spinning.
                failures = error is UnauthorizedAccessException or IOException ? Math.Min(failures + 1, 50) : 0;
                if (!stopped.IsCancellationRequested)
                    await Task.Delay(100 * Math.Max(1, failures));
                continue;
            }
            failures = 0;
        }
    }
    public static async Task ForwardAsync(string dataRoot, LaunchRequest request, CancellationToken token = default)
    {
        byte[] body = Encode(request);
        await using NamedPipeClientStream pipe = new(".", PipeName(dataRoot), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        await pipe.ConnectAsync(timeout.Token);
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await pipe.WriteAsync(header, timeout.Token);
        await pipe.WriteAsync(body, timeout.Token);
        await pipe.FlushAsync(timeout.Token);
        byte[] ack = new byte[1];
        await pipe.ReadExactlyAsync(ack, timeout.Token);
        if (ack[0] != 1)
            throw new IOException("The running Lantoss could not accept the request.");
    }
    public void Dispose()
    {
        stopped.Cancel();
        // Do not block the UI dispatcher; the worker only enqueues and never waits on WPF.
        _ = worker.ContinueWith(_ => stopped.Dispose(), TaskScheduler.Default);
    }
}
