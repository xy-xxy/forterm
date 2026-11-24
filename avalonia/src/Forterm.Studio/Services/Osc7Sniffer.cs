using System.Text;
using Porta.Pty;

namespace Forterm.Studio.Services;

/// <summary>Passes terminal output through unchanged while watching for OSC 7
/// (<c>ESC ] 7 ; file://host/path BEL</c> or <c>… ESC \</c>), the standard "current directory"
/// report. Used by "pwd 自动跳转" to keep the file tree on the shell's directory.</summary>
public sealed class Osc7Sniffer(IPtyConnection inner) : IPtyConnection
{
    readonly Stream reader = new SniffStream(inner.ReaderStream);

    public event Action<string>? DirectoryChanged
    {
        add => ((SniffStream)reader).DirectoryChanged += value;
        remove => ((SniffStream)reader).DirectoryChanged -= value;
    }

    public Stream ReaderStream => reader;
    public Stream WriterStream => inner.WriterStream;
    public bool SupportsCancellableRead => inner.SupportsCancellableRead;
    public int Pid => inner.Pid;
    public int ExitCode => inner.ExitCode;
    public event EventHandler<PtyExitedEventArgs>? ProcessExited { add => inner.ProcessExited += value; remove => inner.ProcessExited -= value; }
    public void Resize(int cols, int rows) => inner.Resize(cols, rows);
    public bool WaitForExit(int milliseconds) => inner.WaitForExit(milliseconds);
    public void Kill() => inner.Kill();
    public void Dispose() => inner.Dispose();

    /// <summary>Shell hook written once into a saved connection's shells when pwd follow is on.
    /// bash: PROMPT_COMMAND; zsh: precmd_functions; other shells: no effect. The leading space keeps
    /// it out of bash history where HISTCONTROL ignores space-prefixed lines.</summary>
    public const string ShellHook =
        " __ft_osc7(){ printf '\\033]7;file://%s%s\\007' \"${HOSTNAME:-localhost}\" \"$PWD\"; }; " +
        "if [ -n \"${ZSH_VERSION:-}\" ]; then eval 'precmd_functions+=(__ft_osc7)'; " +
        "elif [ -n \"${BASH_VERSION:-}\" ]; then PROMPT_COMMAND=\"__ft_osc7${PROMPT_COMMAND:+;$PROMPT_COMMAND}\"; fi\r";

    /// <summary>"file://host/some%20dir" → "/some dir"; anything else → null.</summary>
    public static string? PathFrom(string payload)
    {
        if (!payload.StartsWith("file://", StringComparison.Ordinal)) return null;
        int slash = payload.IndexOf('/', 7);
        if (slash < 0) return null;
        string path = payload[slash..];
        try { path = Uri.UnescapeDataString(path); } catch (UriFormatException) { }
        return path.Length is > 0 and <= 4096 && path.StartsWith('/') && !path.Any(char.IsControl) ? path : null;
    }

    sealed class SniffStream(Stream source) : Stream
    {
        // 0 = text, 1 = saw ESC, 2 = inside an OSC payload, 3 = ESC inside the payload
        int state;
        readonly List<byte> payload = new();
        bool overflow;
        public event Action<string>? DirectoryChanged;

        void Scan(ReadOnlySpan<byte> data)
        {
            foreach (byte b in data)
            {
                switch (state)
                {
                    case 0:
                        if (b == 0x1b) state = 1;
                        break;
                    case 1:
                        if (b == (byte)']') { payload.Clear(); overflow = false; state = 2; }
                        else if (b != 0x1b) state = 0;
                        break;
                    case 2:
                        if (b == 0x07) { Finish(); state = 0; }
                        else if (b == 0x1b) state = 3;
                        else if (payload.Count < 4100) payload.Add(b);
                        else overflow = true;
                        break;
                    case 3:
                        if (b == (byte)'\\') { Finish(); state = 0; }
                        else if (b == (byte)']') { payload.Clear(); overflow = false; state = 2; }
                        else state = b == 0x1b ? 1 : 0;
                        break;
                }
            }
        }

        void Finish()
        {
            if (!overflow && payload.Count > 2 && payload[0] == (byte)'7' && payload[1] == (byte)';'
                && PathFrom(Encoding.UTF8.GetString(payload.ToArray(), 2, payload.Count - 2)) is { } path)
                DirectoryChanged?.Invoke(path);
            payload.Clear();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            int n = await source.ReadAsync(buffer, ct);
            if (n > 0) Scan(buffer.Span[..n]);
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override int Read(byte[] buffer, int offset, int count) { int n = source.Read(buffer, offset, count); if (n > 0) Scan(buffer.AsSpan(offset, n)); return n; }
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
}
