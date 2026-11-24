using System.Threading.Channels;
using System.Net.Sockets;
using Porta.Pty;

namespace Forterm.Core;

/// <summary>Minimal Telnet stream adapter. It owns only a TCP session and does not share SSH state.</summary>
public sealed class TelnetPtyConnection : IPtyConnection, IImmediateInput
{
    readonly TcpClient client; readonly NetworkStream stream; readonly bool flowControl; readonly Channel<byte[]> output = Channel.CreateBounded<byte[]>(128);
    int disposed; string? failure; Task? pump;
    public string? CloseReason => Volatile.Read(ref failure);
    public Stream ReaderStream => new ChannelReaderStream(output.Reader);
    public Stream WriterStream => stream;
    public bool IsEnded => disposed != 0 || pump is { IsCompleted: true };
    public bool TrySend(ReadOnlySpan<byte> bytes)
    {
        if (IsEnded || bytes.Length == 0 || !client.Client.Poll(0, SelectMode.SelectWrite)) return false;
        stream.Write(bytes); return true;
    }
    public bool SupportsCancellableRead => true;
    public int Pid => 0; public int ExitCode => -1;
    public event EventHandler<PtyExitedEventArgs>? ProcessExited;
    TelnetPtyConnection(TcpClient client, bool flowControl)
    {
        this.client = client; this.flowControl = flowControl; stream = client.GetStream(); pump = Task.Run(PumpAsync);
    }
    /// <summary>With <paramref name="flowControl"/> a server that sends faster than the terminal reads is slowed
    /// down by TCP; without it the terminal is closed when too much is waiting.</summary>
    public static async Task<TelnetPtyConnection> ConnectAsync(string host, int port, CancellationToken ct, bool flowControl = false)
    {
        var client = new TcpClient();
        try { await client.ConnectAsync(host, port, ct); return new TelnetPtyConnection(client, flowControl); }
        catch { client.Dispose(); throw; }
    }
    async Task PumpAsync()
    {
        try
        {
            byte[] buffer = new byte[32768];
            while (disposed == 0)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);
                if (n == 0) break;
                byte[] chunk = buffer[..n].ToArray();
                if (flowControl) await output.Writer.WriteAsync(chunk);
                else if (!output.Writer.TryWrite(chunk)) { Fail("Telnet终端输出积压超过4MiB安全边界，已关闭此终端。"); break; }
            }
        }
        catch (ChannelClosedException) { } // disposed while waiting for the terminal
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException) { Fail("Telnet终端连接已关闭：" + e.Message); }
        finally { output.Writer.TryComplete(); }
    }
    void Fail(string reason) { Interlocked.CompareExchange(ref failure, reason, null); }
    public bool WaitForExit(int milliseconds) => output.Reader.Completion.IsCompleted;
    public void Resize(int cols, int rows) { }
    public void Kill() => Dispose();
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { stream.Dispose(); client.Dispose(); } catch { }
        output.Writer.TryComplete();
    }
    sealed class ChannelReaderStream(ChannelReader<byte[]> reader) : Stream
    {
        byte[]? current; int offset;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (buffer.Length == 0) return 0;
            if (current == null)
            {
                if (!await reader.WaitToReadAsync(ct)) return 0;
                if (!reader.TryRead(out current)) return 0;
                offset = 0;
            }
            int n = Math.Min(buffer.Length, current.Length - offset); current.AsMemory(offset, n).CopyTo(buffer); offset += n;
            if (offset == current.Length) current = null;
            return n;
        }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
