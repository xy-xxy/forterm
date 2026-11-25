using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Forterm.Core;

/// <summary>Current-user, framed, bounded IPC. Raw args/temporary credentials
/// only cross in-memory pipes; no endpoint or command line is persisted.</summary>
public sealed class InstanceBroker : IDisposable
{
    readonly Mutex mutex; readonly string pipe; readonly CancellationTokenSource lifetime = new(); readonly Task listener;
    readonly Func<string[], bool> accept; readonly int maxBytes = 1024 * 1024;
    InstanceBroker(Mutex mutex, string pipe, Func<string[], bool> accept, bool queue) { this.mutex = mutex; this.pipe = pipe; this.accept = accept; listener = queue ? ListenAlways() : Listen(); }
    public static string DefaultName => ForDataDirectory(Environment.GetEnvironmentVariable("FORTERM_AVALONIA_DATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    public static string ForDataDirectory(string directory) => "forterm-avalonia-v1-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory) + Environment.UserName)))[..24];
    /// <param name="acrossSessions">macOS and Linux: .NET scopes a plain mutex name to the POSIX session, so a
    /// launch from Finder and one from a terminal (or from two terminals) would each be the first. With this they
    /// share one instance per user. Windows keeps its name per logon session either way.</param>
    /// <param name="queue">Launches that come at the same moment are all taken. Without it the pipe is gone for a moment
    /// after each, and on macOS and Linux a launch that was waiting then fails.</param>
    public static async Task<(InstanceBroker? Primary, bool Forwarded)> StartAsync(string name, string[] args, Func<string[], bool> accept, CancellationToken ct, bool acrossSessions = false, bool queue = false)
    {
        // The primary owns the mutex on the caller's main thread. StartAsync never
        // awaits on that branch, so release remains on the same STA at normal exit.
        name = "ft-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..16];
        bool primary;
        var mutex = acrossSessions && !OperatingSystem.IsWindows()
            ? new Mutex(true, name + "-owner", new NamedWaitHandleOptions { CurrentSessionOnly = false, CurrentUserOnly = true }, out primary)
            : new Mutex(true, name + "-owner", out primary);
        if (primary) return (new InstanceBroker(mutex, name, accept, queue), false);
        mutex.Dispose();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(args); if (bytes.Length > 1024 * 1024) throw new IOException("启动消息过长");
        try
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeout.Token); byte[] header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
            await client.WriteAsync(header, timeout.Token); await client.WriteAsync(bytes, timeout.Token); await client.FlushAsync(timeout.Token); byte[] ack = new byte[1]; await client.ReadExactlyAsync(ack, timeout.Token); if (ack[0] != 1) throw new IOException("现有实例没有确认转交"); return (null, true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    async Task Listen()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(lifetime.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                byte[] header = new byte[4]; await server.ReadExactlyAsync(header, timeout.Token); int n = BinaryPrimitives.ReadInt32LittleEndian(header); if (n < 2 || n > maxBytes) continue;
                byte[] payload = new byte[n]; try
                {
                    await server.ReadExactlyAsync(payload, timeout.Token); var args = JsonSerializer.Deserialize<string[]>(payload);
                    if (args == null || args.Length > 128 || args.Any(a => a == null || a.Length > 32768)) continue;
                    bool accepted = accept(args); await server.WriteAsync(new byte[] { accepted ? (byte)1 : (byte)0 }, timeout.Token); await server.FlushAsync(timeout.Token);
                }
                finally { CryptographicOperations.ZeroMemory(payload); }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
            catch (IOException) { }
            catch (JsonException) { }
            catch (OperationCanceledException) { }
        }
    }
    const int AtOnce = 8;
    NamedPipeServerStream Open() => new(pipe, PipeDirection.InOut, AtOnce + 2, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    /// <summary>There is always one more pipe to connect to, also while launches are being read, so that nobody finds it
    /// gone. A launch that hangs holds up none of the others, and whatever goes wrong with one ends nothing.</summary>
    async Task ListenAlways()
    {
        using var room = new SemaphoreSlim(AtOnce); var running = new List<Task>(); var one = new object(); NamedPipeServerStream? server = null;
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                server ??= Open(); await server.WaitForConnectionAsync(lifetime.Token); await room.WaitAsync(lifetime.Token);
                var connected = server; server = null;
                try { server = Open(); } finally { running.RemoveAll(t => t.IsCompleted); running.Add(Take(connected)); }
            }
            catch (Exception) when (lifetime.IsCancellationRequested) { break; }
            catch (Exception)
            {
                server?.Dispose(); server = null;
                try { await Task.Delay(50, lifetime.Token); } catch (OperationCanceledException) { break; }
            }
        }
        server?.Dispose(); await Task.WhenAll(running);

        async Task Take(NamedPipeServerStream connected)
        {
            try
            {
                using (connected)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    byte[] header = new byte[4]; await connected.ReadExactlyAsync(header, timeout.Token); int n = BinaryPrimitives.ReadInt32LittleEndian(header); if (n < 2 || n > maxBytes) return;
                    byte[] payload = new byte[n]; Task<int>? closed = null; bool accepted;
                    try
                    {
                        await connected.ReadExactlyAsync(payload, timeout.Token); var args = JsonSerializer.Deserialize<string[]>(payload);
                        if (args == null || args.Length > 128 || args.Any(a => a == null || a.Length > 32768)) return;
                        // The entry says nothing more; it closes when it has the answer, or has given up and said so. What
                        // it has given up must not be opened after all.
                        closed = connected.ReadAsync(new byte[1].AsMemory(), timeout.Token).AsTask(); _ = closed.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                        lock (one) { if (closed.IsCompleted) return; accepted = accept(args); }
                    }
                    finally { CryptographicOperations.ZeroMemory(payload); }
                    await connected.WriteAsync(new byte[] { accepted ? (byte)1 : (byte)0 }, timeout.Token); await connected.FlushAsync(timeout.Token);
                    await closed; // closing first could take the answer away before it was read
                }
            }
            catch (Exception) { } // that launch was not taken, or its entry has gone
            finally { room.Release(); }
        }
    }
    public void Dispose() { lifetime.Cancel(); try { listener.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { } mutex.ReleaseMutex(); mutex.Dispose(); lifetime.Dispose(); }
}
