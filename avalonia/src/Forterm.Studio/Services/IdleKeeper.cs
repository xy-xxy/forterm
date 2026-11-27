using Forterm.Core;
using Porta.Pty;

namespace Forterm.Studio.Services;

/// <summary>A choice of what "空闲保活" sends.</summary>
public sealed record IdleInput(string Key, string Title, string Text, string Note);

/// <summary>What "空闲保活" would do now: what it sends (null = nothing), and why it holds back although it is time.</summary>
public sealed record IdleDue(string? Text, string Held = "");

/// <summary>"空闲保活" of one terminal: knows when something was last sent to the server from it, and what to send
/// when that is too long ago. Bastions and servers that end idle sessions count what they receive, so everything
/// that is really sent counts here, and a key that is not sent (a shortcut of the window) does not.</summary>
public sealed class IdleKeeper
{
    public static readonly IdleInput[] Inputs =
    [
        new("space", "空格 + 退格（推荐）", " \x7f", "命令行里看不出变化。正在等待单个按键的程序（“按任意键继续”、more、git log 的分页）会收到一个空格。"),
        new("null", "空字符", "\0", "大多数程序忽略它；正在读取输入的程序（如 cat > 文件）会收到一个空字符。"),
        new("enter", "回车", "\r", "命令行会多出一行；bash 的 TMOUT 只认回车。只在光标停在命令提示符后面（以 $、#、%、❯ 结尾，oh-my-zsh 的 ➜，Windows 和 fish 的 >；右侧另有提示也可以）、没有输入到一半的内容时发送，以免替你回答 [Y/n] 之类的问题；识别不出提示符时不发送。"),
    ];
    public static readonly int[] Minutes = [1, 2, 3, 4, 5, 8, 10, 15, 20, 30], Hours = [1, 2, 4, 8, 12, 24];

    readonly TimeProvider time;
    readonly Lock gate = new();
    long sent, typed;
    bool unfinished, unasked;
    int count;
    DateTimeOffset? last;

    public IdleKeeper(TimeProvider time)
    {
        this.time = time;
        sent = typed = time.GetTimestamp();
    }

    /// <summary>How often it has sent something, and when last.</summary>
    public int Count { get { lock (gate) return count; } }
    public DateTimeOffset? Last { get { lock (gate) return last; } }
    /// <summary>The last thing typed into the terminal was not the user's, but sent by itself: nor is what the server
    /// answers, such as another prompt.</summary>
    public bool IsUnasked { get { lock (gate) return unasked; } }

    /// <summary>This went to the server. <paramref name="byUser"/>: not what the settings of a connection have
    /// written (initial script, keep-alive command).</summary>
    public void Sent(ReadOnlySpan<byte> bytes, bool byUser = true)
    {
        if (bytes.Length == 0) return;
        var kind = KindOf(bytes);
        lock (gate)
        {
            sent = time.GetTimestamp();
            if (kind == Kind.Reply) return;
            unasked = !byUser;
            if (byUser) typed = sent;
            if (kind == Kind.Mouse) return;
            // Enter and Ctrl+C leave an empty line. Ctrl+D and Ctrl+U may leave some of it, and begin none. Anything
            // else may have begun one.
            unfinished = bytes[^1] switch { (byte)'\r' or (byte)'\n' or 3 => false, 4 or 21 => unfinished, _ => true };
        }
    }

    /// <summary>All of it has gone out: a server that does not read can take its time.</summary>
    public void Done() { lock (gate) sent = time.GetTimestamp(); }

    enum Kind { Typed, Mouse, Reply }

    /// <summary>What a terminal answers by itself (device attributes, cursor, focus and window reports, colours) is not
    /// the user. The mouse is, but types nothing. Everything else is typed, also cursor and function keys: they can call
    /// up an earlier command.</summary>
    static Kind KindOf(ReadOnlySpan<byte> b)
    {
        if (b.Length < 3 || b[0] != 0x1b) return Kind.Typed;
        if (b[1] is (byte)']' or (byte)'P' or (byte)'_') return b[^1] == 7 || b[^2] == 0x1b && b[^1] == (byte)'\\' ? Kind.Reply : Kind.Typed;
        if (b[1] != (byte)'[') return Kind.Typed;
        if (b[2] == (byte)'M') return b.Length == 6 ? Kind.Mouse : Kind.Typed;
        // One sequence and nothing behind it.
        foreach (byte x in b[2..^1]) if (x is < 0x20 or > 0x3f) return Kind.Typed;
        bool plain = char.IsAsciiDigit((char)b[2]);
        return (b[^1], b[2]) switch
        {
            ((byte)'M' or (byte)'m', (byte)'<') => Kind.Mouse,
            ((byte)'I' or (byte)'O', _) when b.Length == 3 => Kind.Reply, // focus
            ((byte)'c' or (byte)'y' or (byte)'u' or (byte)'n' or (byte)'R' or (byte)'S', (byte)'?') => Kind.Reply,
            ((byte)'c', (byte)'>' or (byte)'=') => Kind.Reply,
            ((byte)'n' or (byte)'t' or (byte)'x' or (byte)'y' or (byte)'{', _) when plain => Kind.Reply,
            // A cursor report; "1;2R" is also how Shift+F3 is written, which begins no line.
            ((byte)'R', _) when plain => Kind.Reply,
            _ => Kind.Typed,
        };
    }

    /// <summary>What has to be sent now. Not into a full-screen program (unless wished), not after so long without the
    /// user that it is not a break any more, and Enter only at an empty prompt: it would answer a question.</summary>
    public IdleDue Due(StudioPreferences p, bool fullScreen, bool atPrompt)
    {
        lock (gate)
        {
            if (time.GetElapsedTime(sent) < TimeSpan.FromMinutes(p.IdleMinutes) || IsOver(p)) return new(null);
            if (fullScreen && !p.IdleFullScreen) return new(null, "有全屏程序");
            var input = Inputs.FirstOrDefault(i => i.Key == p.IdleInput) ?? Inputs[0];
            if (input.Key != "enter") return new(input.Text);
            return unfinished ? new(null, "有输入到一半的内容，不发送回车") : atPrompt ? new(input.Text) : new(null, "光标不在命令提示符后面，不发送回车");
        }
    }

    /// <summary>The user has been away for longer than the session is to be kept.</summary>
    public bool IsOver(StudioPreferences p) => time.GetElapsedTime(Volatile.Read(ref typed)) >= TimeSpan.FromHours(p.IdleHours);

    /// <summary>Something is about to be sent by itself: what the server answers is nobody's doing. False = it already
    /// was.</summary>
    public bool Begin() { lock (gate) { bool mine = !unasked; unasked = true; return mine; } }

    /// <summary>It has gone out: that counts for the server, not for the user. Or it has not; then it is as before.</summary>
    public void End(bool gone, bool mine)
    {
        lock (gate)
        {
            if (!gone) { if (mine) unasked = false; return; }
            sent = time.GetTimestamp();
            count++;
            last = time.GetLocalNow();
        }
    }
}

/// <summary>Everything that is sent from one terminal goes through here, one at a time: what is below (the buffer of
/// an SSH shell) is for one writer only, and two at once send the same bytes twice. Tells an <see cref="IdleKeeper"/>
/// what was sent; changes nothing.</summary>
public sealed class IdleWatch : IPtyConnection
{
    readonly IPtyConnection inner;
    readonly IImmediateInput? direct;
    readonly SemaphoreSlim one = new(1, 1);
    readonly Watched writer;
    volatile IdleKeeper keeper;

    /// <param name="direct">The connection itself, below any conversion, when it can say whether input goes out at once.</param>
    public IdleWatch(IPtyConnection inner, IdleKeeper keeper, IImmediateInput? direct = null)
    {
        this.inner = inner;
        this.direct = direct;
        this.keeper = keeper;
        writer = new Watched(this);
    }

    /// <summary>The shell has ended; nothing more can be sent.</summary>
    public bool IsEnded => direct?.IsEnded ?? inner.WaitForExit(0);

    internal void Use(IdleKeeper other) => keeper = other;
    public Stream ReaderStream => inner.ReaderStream;
    /// <summary>For the terminal: what the user types, and what the terminal answers.</summary>
    public Stream WriterStream => writer;
    public bool SupportsCancellableRead => inner.SupportsCancellableRead;
    public int Pid => inner.Pid;
    public int ExitCode => inner.ExitCode;
    public event EventHandler<PtyExitedEventArgs>? ProcessExited { add => inner.ProcessExited += value; remove => inner.ProcessExited -= value; }
    public void Resize(int cols, int rows) => inner.Resize(cols, rows);
    public bool WaitForExit(int milliseconds) => inner.WaitForExit(milliseconds);
    public void Kill() => inner.Kill();
    public void Dispose() => inner.Dispose();

    /// <summary>Sends a whole text, with nothing of anybody else's in between.</summary>
    public async Task SendAsync(ReadOnlyMemory<byte> bytes, bool byUser, CancellationToken ct)
    {
        await one.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            keeper.Sent(bytes.Span, byUser);
            await inner.WriterStream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await inner.WriterStream.FlushAsync(ct).ConfigureAwait(false);
            keeper.Done();
        }
        finally { one.Release(); }
    }

    /// <summary>How much of a paste goes out at a time, in UTF-8 bytes: a piece that has begun is sent whole, so a stop
    /// takes effect between pieces — never inside a character, in whatever encoding the server speaks.</summary>
    internal const int PastePiece = 1024;

    /// <summary>The number of characters from <paramref name="at"/> that make the next piece: whole characters (a pair
    /// of surrogates is one), at most <see cref="PastePiece"/> bytes of UTF-8, and at least one character.</summary>
    internal static int PieceLength(string text, int at)
    {
        int i = at, bytes = 0;
        while (i < text.Length)
        {
            char c = text[i];
            bool pair = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
            int size = pair ? 4 : c < 0x80 ? 1 : c < 0x800 ? 2 : 3; // a lone surrogate goes out as U+FFFD, 3 bytes
            if (i > at && bytes + size > PastePiece) break;
            bytes += size;
            i += pair ? 2 : 1;
        }
        return i - at;
    }

    /// <summary>A paste: <paramref name="head"/> (the keys that delete a keyboard selection it replaces), then the text,
    /// bracketed when the program asked for it — in one piece, with nothing of anybody else's in between. Cancelling stops
    /// what has not begun to go out; the piece that has begun is finished (when the server takes it), and a bracket that
    /// was opened is closed all the same, or the shell would take whatever is typed next as part of the paste.
    /// False = stopped before the end of the text.</summary>
    public async Task<bool> SendPasteAsync(string head, string text, bool bracketed, CancellationToken cancel, CancellationToken lifetime)
    {
        await one.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            // Whole or not at all: half of the opening sequence would be left for the next key to complete.
            await PieceAsync(head + (bracketed ? "\u001b[200~" : ""), lifetime).ConfigureAwait(false);
            bool stopped = false;
            for (int at = 0; at < text.Length;)
            {
                if (cancel.IsCancellationRequested) { stopped = true; break; }
                int length = PieceLength(text, at);
                // Not cancelled half way: what the server took of it would end inside a character.
                await PieceAsync(text.Substring(at, length), lifetime).ConfigureAwait(false);
                at += length;
            }
            if (bracketed) await PieceAsync("\u001b[201~", lifetime).ConfigureAwait(false);
            return !stopped;
        }
        finally
        {
            keeper.Done();
            one.Release();
        }
    }

    async Task PieceAsync(string piece, CancellationToken ct)
    {
        if (piece.Length == 0) return;
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(piece);
        keeper.Sent(bytes);
        await inner.WriterStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await inner.WriterStream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Sends what is due by itself — now or not at all: what waited for a slow server would come out later,
    /// when the screen asks something else. Not while something else is under way either. False when nothing was
    /// sent. What is sent is plain ASCII, which all the encodings of a terminal here have in common.</summary>
    public async Task<bool> BeatAsync(Func<string?> due, CancellationToken ct)
    {
        if (!one.Wait(0, CancellationToken.None)) return false;
        var to = keeper;
        bool begun = false, mine = false, gone = false;
        try
        {
            if (IsEnded || due() is not { Length: > 0 } text) return false;
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(text);
            mine = to.Begin();
            begun = true;
            if (direct != null) gone = await Task.Run(() => direct.TrySend(bytes), ct).ConfigureAwait(false);
            else
            {
                await inner.WriterStream.WriteAsync(bytes, ct).ConfigureAwait(false);
                await inner.WriterStream.FlushAsync(ct).ConfigureAwait(false);
                gone = true;
            }
            // Into a shell that has ended meanwhile it went nowhere.
            return gone = gone && !IsEnded;
        }
        finally
        {
            if (begun) to.End(gone, mine);
            one.Release();
        }
    }

    sealed class Watched(IdleWatch watch) : Stream
    {
        Stream Target => watch.inner.WriterStream;

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            await watch.one.WaitAsync(ct).ConfigureAwait(false);
            try { watch.keeper.Sent(buffer.Span); await Target.WriteAsync(buffer, ct).ConfigureAwait(false); }
            finally { watch.one.Release(); }
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override async Task FlushAsync(CancellationToken ct)
        {
            await watch.one.WaitAsync(ct).ConfigureAwait(false);
            try { await Target.FlushAsync(ct).ConfigureAwait(false); watch.keeper.Done(); }
            finally { watch.one.Release(); }
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            watch.one.Wait();
            try { watch.keeper.Sent(buffer); Target.Write(buffer); }
            finally { watch.one.Release(); }
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Flush()
        {
            watch.one.Wait();
            try { Target.Flush(); watch.keeper.Done(); }
            finally { watch.one.Release(); }
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
