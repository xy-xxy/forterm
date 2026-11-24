using System.Text;
using Porta.Pty;

namespace Forterm.Studio.Services;

/// <summary>"终端显示编码": the server speaks e.g. GBK, the terminal engine speaks UTF-8.
/// Output is decoded from the server encoding and re-encoded as UTF-8; keyboard input goes the
/// other way. Both directions are stateful, so multi-byte characters split across reads survive.</summary>
public sealed class TranscodingPtyConnection(IPtyConnection inner, Encoding server) : IPtyConnection
{
    readonly Stream reader = new DecodingStream(inner.ReaderStream, server);
    readonly Stream writer = new EncodingStream(inner.WriterStream, server);

    public Stream ReaderStream => reader;
    public Stream WriterStream => writer;
    public bool SupportsCancellableRead => inner.SupportsCancellableRead;
    public int Pid => inner.Pid;
    public int ExitCode => inner.ExitCode;
    public event EventHandler<PtyExitedEventArgs>? ProcessExited { add => inner.ProcessExited += value; remove => inner.ProcessExited -= value; }
    public void Resize(int cols, int rows) => inner.Resize(cols, rows);
    public bool WaitForExit(int milliseconds) => inner.WaitForExit(milliseconds);
    public void Kill() => inner.Kill();
    public void Dispose() => inner.Dispose();

    sealed class DecodingStream(Stream source, Encoding from) : Stream
    {
        readonly Decoder decoder = from.GetDecoder();
        readonly Encoder utf8 = new UTF8Encoding(false).GetEncoder();
        readonly byte[] raw = new byte[16384];
        readonly char[] chars = new char[16384 + 16];
        byte[] pending = Array.Empty<byte>();
        int offset;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            while (true)
            {
                if (offset < pending.Length)
                {
                    int n = Math.Min(buffer.Length, pending.Length - offset);
                    pending.AsMemory(offset, n).CopyTo(buffer);
                    offset += n;
                    return n;
                }
                int read = await source.ReadAsync(raw, ct);
                if (read == 0) return 0;
                int count = decoder.GetChars(raw, 0, read, chars, 0, flush: false);
                pending = new byte[utf8.GetByteCount(chars, 0, count, flush: false)];
                utf8.GetBytes(chars, 0, count, pending, 0, flush: false);
                offset = 0;
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int index, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(index, count), ct).AsTask();
        public override int Read(byte[] buffer, int index, int count) => ReadAsync(buffer.AsMemory(index, count)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    sealed class EncodingStream(Stream target, Encoding to) : Stream
    {
        readonly Decoder utf8 = new UTF8Encoding(false).GetDecoder();
        readonly Encoder encoder = to.GetEncoder();
        readonly object gate = new();

        byte[] Convert(ReadOnlySpan<byte> input)
        {
            lock (gate)
            {
                var chars = new char[utf8.GetCharCount(input, flush: false)];
                int count = utf8.GetChars(input, chars, flush: false);
                var output = new byte[encoder.GetByteCount(chars.AsSpan(0, count), flush: false)];
                encoder.GetBytes(chars.AsSpan(0, count), output, flush: false);
                return output;
            }
        }

        public override void Write(byte[] buffer, int index, int count)
        {
            var bytes = Convert(buffer.AsSpan(index, count));
            if (bytes.Length > 0) target.Write(bytes, 0, bytes.Length);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            var bytes = Convert(buffer.Span);
            if (bytes.Length > 0) await target.WriteAsync(bytes, ct);
        }

        public override Task WriteAsync(byte[] buffer, int index, int count, CancellationToken ct) => WriteAsync(buffer.AsMemory(index, count), ct).AsTask();
        public override void Flush() => target.Flush();
        public override Task FlushAsync(CancellationToken ct) => target.FlushAsync(ct);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
    }
}
