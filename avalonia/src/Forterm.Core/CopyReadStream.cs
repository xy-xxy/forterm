namespace Forterm.Core;

/// <summary>Owns a protocol read lane until EOF/completion acknowledgement is verified.</summary>
internal sealed class CopyReadStream(Stream inner, long expectedLength, Func<bool, Task> close) : Stream
{
    long count; bool eof; int disposed;
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        int n = await inner.ReadAsync(buffer, ct).ConfigureAwait(false);
        if (n == 0) eof = true;
        else { count += n; if (count > expectedLength) throw new IOException("流式复制源超过已核对长度"); }
        return n;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { await close(eof && count == expectedLength).ConfigureAwait(false); }
        finally { GC.SuppressFinalize(this); }
    }
    protected override void Dispose(bool disposing) { if (disposing) DisposeAsync().AsTask().GetAwaiter().GetResult(); base.Dispose(disposing); }
    public override bool CanRead => true; public override bool CanWrite => false; public override bool CanSeek => false;
    public override long Length => expectedLength; public override long Position { get => count; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
