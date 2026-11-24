using System.Threading.Channels;
using Porta.Pty;
using Renci.SshNet;
using Renci.SshNet.Common;
namespace Forterm.Core;

/// <summary>A terminal connection that tells whether a little input can go out just now.</summary>
public interface IImmediateInput
{
    /// <summary>The server has closed the terminal: nothing can be sent any more.</summary>
    bool IsEnded { get; }
    /// <summary>Sends all of it now, or nothing: never waits for a server that does not read. Not on the UI thread.</summary>
    bool TrySend(ReadOnlySpan<byte> bytes);
}

public sealed class SshPtyConnection : IPtyConnection, IImmediateInput
{
    readonly ShellStream shell;
    readonly ShellWriter? writer;
    readonly IDisposable lease;
    readonly ShellReader reader;
    readonly object readGate = new();
    int disposed;
    string? failure; public string? CloseReason => Volatile.Read(ref failure);
    public event Action<string>? Faulted;
    /// <summary>"env" variables the server refused when this shell opened (empty when none were sent).</summary>
    public IReadOnlyList<string> RejectedEnvironment { get; init; } = Array.Empty<string>();
    void Fail(string reason) { if (Interlocked.CompareExchange(ref failure, reason, null) == null) Faulted?.Invoke(reason); }
    /// <summary>With flow control: how much the server may send ahead of the terminal (OpenSSH's value).</summary>
    public const uint FlowWindow = 2 * 1024 * 1024;
    const int MaxBacklog = 4 * 1024 * 1024;
    public SshPtyConnection(ShellStream shell, IDisposable lease, bool flowControl = false)
    {
        this.shell = shell; this.lease = lease;
        reader = flowControl ? new ShellReader(MaxBacklog, Consumed) : new ShellReader();
        if (flowControl) writer = new ShellWriter(shell);
        shell.DataReceived += Data;
        shell.Closed += Closed;
        Drain();
    }
    void Data(object? sender, ShellDataEventArgs e)
    {
        try { Drain(); }
        catch (Exception error) when (error is IOException or ObjectDisposedException)
        { Fail("终端通道读取失败，已关闭此终端；文件及其他会话保留：" + error.Message); reader.Complete(error); ThreadPool.QueueUserWorkItem(_ => Dispose()); }
    }
    void Drain()
    {
        lock (readGate)
        {
            while (shell.DataAvailable)
            {
                byte[] data = new byte[32768]; int n = shell.Read(data, 0, data.Length);
                if (n == 0) break; if (n != data.Length) Array.Resize(ref data, n);
                if (!reader.Push(data)) { Fail("终端输出积压超过4MiB安全边界，已关闭此终端通道；文件及其他会话保留。"); reader.Complete(new IOException(CloseReason)); ThreadPool.QueueUserWorkItem(_ => Dispose()); break; }
            }
        }
    }
    void Closed(object? sender, EventArgs e) => reader.Complete();
    /// <summary>The terminal took this much: the server may send as much again.</summary>
    void Consumed(int count)
    {
        try { shell.ReleaseWindow(count); }
        catch (Exception e) when (e is SshException or IOException or ObjectDisposedException or System.Net.Sockets.SocketException) { } // the connection is ending; what was received is still shown
    }
    public Stream ReaderStream => reader;
    /// <summary>With flow control every byte is sent once, however long the server does not read; without it the shell's
    /// own buffer, which fails after the connection's timeout and then sends what it holds again.</summary>
    public Stream WriterStream => (Stream?)writer ?? shell;
    public bool IsEnded => disposed != 0 || reader.IsComplete;
    public bool TrySend(ReadOnlySpan<byte> bytes) => writer?.TrySend(bytes.ToArray()) == true;
    public bool SupportsCancellableRead => true;
    public int Pid => 0; // Remote channel has no local process ID.
    public int ExitCode => -1; // ShellStream does not expose SSH exit status.
    public event EventHandler<PtyExitedEventArgs>? ProcessExited { add { } remove { } }
    public void Resize(int cols, int rows)
    {
        if (disposed != 0 || cols < 1 || rows < 1) return;
        try { shell.ChangeWindowSize((uint)Math.Clamp(cols, 1, ushort.MaxValue), (uint)Math.Clamp(rows, 1, ushort.MaxValue), 0, 0); }
        catch (Exception e) when (e is SshException or IOException or ObjectDisposedException)
        { Fail("终端尺寸更新失败，此终端已停止；其他通道保留：" + e.Message); reader.Complete(); ThreadPool.QueueUserWorkItem(_ => { try { Dispose(); } catch { } }); }
    }
    public bool WaitForExit(int milliseconds) => reader.IsComplete;
    public void Kill() => Dispose();
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        shell.DataReceived -= Data; shell.Closed -= Closed;
        try { shell.Dispose(); } catch (Exception e) when (e is SshException or IOException or ObjectDisposedException) { Fail("终端通道已停止：" + e.Message); } finally { reader.Complete(); lease.Dispose(); }
    }
    /// <summary>Input straight into the channel, in order and one writer at a time. A server that does not read is waited
    /// for, without a limit, until the shell ends; nothing is kept that could be sent a second time.</summary>
    sealed class ShellWriter(ShellStream shell) : Stream
    {
        readonly SemaphoreSlim one = new(1, 1);
        /// <summary>As much as goes out now; -1 when the shell has ended.</summary>
        int Send(byte[] data, int from)
        {
            int at = from;
            while (at < data.Length)
            {
                int n = shell.SendAvailable(data, at, data.Length - at, whole: false);
                if (n < 0) return -1;
                if (n == 0) break;
                at += n;
            }
            return at - from;
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            if (buffer.Length == 0) return;
            byte[] data = buffer.ToArray();
            await one.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                for (int at = 0, pause = 1; at < data.Length;)
                {
                    int from = at, sent = await Task.Run(() => Send(data, from), CancellationToken.None).ConfigureAwait(false);
                    if (sent < 0) return; // as ever: what is typed into a shell that has ended goes nowhere
                    at += sent;
                    if (at >= data.Length) return;
                    pause = sent > 0 ? 1 : Math.Min(pause * 2, 64);
                    await Task.Delay(pause, ct).ConfigureAwait(false);
                }
            }
            finally { one.Release(); }
        }
        public bool TrySend(byte[] data)
        {
            if (!one.Wait(0)) return false;
            try { return data.Length > 0 && shell.SendAvailable(data, 0, data.Length, whole: true) == data.Length; }
            finally { one.Release(); }
        }
        public override Task WriteAsync(byte[] b, int o, int n, CancellationToken ct) => WriteAsync(b.AsMemory(o, n), ct).AsTask();
        public override void Write(byte[] b, int o, int n) => WriteAsync(b.AsMemory(o, n)).AsTask().GetAwaiter().GetResult();
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] b, int o, int n) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long l) => throw new NotSupportedException();
    }
    sealed class ShellReader : Stream
    {
        readonly Channel<byte[]> queue; readonly int limit; readonly Action<int>? consumed; int backlog;
        byte[]? current; int offset;
        public ShellReader() => queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(128) { SingleReader = true, SingleWriter = true });
        /// <summary>Bounded by bytes: a window's worth of small packets is many blocks. The server cannot get
        /// past the limit unless it ignores the window it was given.</summary>
        public ShellReader(int limit, Action<int> consumed) { this.limit = limit; this.consumed = consumed; queue = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true }); }
        public bool IsComplete { get; private set; }
        public bool Push(byte[] data) => (limit == 0 || Interlocked.Add(ref backlog, data.Length) <= limit) && queue.Writer.TryWrite(data);
        public void Complete(Exception? error = null) { IsComplete = true; queue.Writer.TryComplete(error); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (buffer.Length == 0) return 0;
            if (current == null)
            {
                if (!await queue.Reader.WaitToReadAsync(ct)) return 0;
                if (!queue.Reader.TryRead(out current)) return 0;
                offset = 0;
            }
            int n = Math.Min(buffer.Length, current.Length - offset); current.AsMemory(offset, n).CopyTo(buffer); offset += n;
            if (offset == current.Length) current = null;
            if (consumed != null) { Interlocked.Add(ref backlog, -n); consumed(n); }
            return n;
        }
        public override int Read(byte[] b, int o, int n) => ReadAsync(b.AsMemory(o, n)).AsTask().GetAwaiter().GetResult();
        public override Task<int> ReadAsync(byte[] b, int o, int n, CancellationToken ct) => ReadAsync(b.AsMemory(o, n), ct).AsTask();
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long l) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int n) => throw new NotSupportedException();
    }
}
