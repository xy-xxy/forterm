using System.Collections.Concurrent;
using System.Text;
using Forterm.Studio.Controls;
using Iciclecreek.Terminal;

namespace Forterm.Studio.Services;

/// <summary>"会话记录": writes one terminal's output as plain text to a file the user chose, from the line the
/// cursor is on when they start it until they stop it or the terminal closes. Only output is recorded, never
/// keystrokes. The file is created exclusively (never overwritten) and is readable by the user only.
/// Text is made and written on a thread of its own. When that falls behind, the terminal's reader waits for
/// it (the server is then slowed down); when the disk makes no progress at all, the recording ends and the
/// terminal goes on.</summary>
public sealed class SessionRecorder : IDisposable
{
    public const long MaxBytes = 256L * 1024 * 1024;
    /// <summary>Output waiting to be written; more than this waits.</summary>
    const int MaxPending = 8 * 1024 * 1024;
    /// <summary>How long the reader waits for a disk that writes nothing.</summary>
    static readonly TimeSpan DiskWait = TimeSpan.FromSeconds(3);
    /// <summary>Room below the limit for the line that says why the recording ended.</summary>
    const int NoteRoom = 256;
    /// <summary>How long stopping waits for the rest to be written; a hung disk must not freeze the window.</summary>
    static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(1);

    readonly object gate = new();
    readonly BlockingCollection<(byte[] Bytes, int Columns, long Before)> queue = new();
    readonly StudioTerminalControl control;
    readonly XTerm.Terminal terminal;
    readonly Stream file;
    readonly TerminalTranscript transcript;
    readonly long limit;
    readonly int maxPending;
    readonly TimeSpan diskWait;
    readonly Action<string>? stopped;
    readonly Task worker;
    readonly TerminalCursorLine? start;
    readonly int startColumns;
    long written, taken;
    int pending;
    bool closed;
    string? overflow;

    internal SessionRecorder(StudioTerminalControl control, Stream file, string path, string header, Action<string>? stopped, long limit, int maxPending, TimeSpan diskWait)
    {
        this.diskWait = diskWait;
        this.control = control;
        this.file = file;
        this.limit = limit;
        this.maxPending = maxPending;
        this.stopped = stopped;
        terminal = control.Terminal;
        Path = path;
        transcript = new TerminalTranscript(Line);
        Line(header);
        file.Flush();
        // Listening first, then the screen as it is: what is on it already is told apart by Before.
        control.OutputReceived += OnOutput;
        start = control.ReadCursorLine();
        startColumns = terminal.Cols;
        worker = Task.Factory.StartNew(Work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public string Path { get; }

    /// <summary><paramref name="stopped"/> is told why when recording ends by itself (disk error, size limit
    /// or a disk that cannot keep up), on any thread.</summary>
    public static SessionRecorder Start(StudioTerminalControl control, string path, string header, Action<string>? stopped = null, long limit = MaxBytes)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read, BufferSize = 64 * 1024 };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        FileStream file;
        try { file = new FileStream(path, options); }
        catch (IOException) when (File.Exists(path)) { throw new IOException("文件已存在，不覆盖；请换一个文件名"); }
        try { return new SessionRecorder(control, file, path, header, stopped, limit, MaxPending, DiskWait); }
        catch { file.Dispose(); throw; }
    }

    void Line(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text + Environment.NewLine);
        if (written + bytes.Length > limit - NoteRoom) throw new IOException($"记录文件达到上限（{limit / 1024 / 1024} MiB）");
        file.Write(bytes);
        written += bytes.Length;
    }

    /// <summary>On the terminal's reader: copies and returns, unless too much is waiting.</summary>
    void OnOutput(object? sender, OutputReceivedEventArgs e)
    {
        lock (gate)
        {
            while (!closed && pending > 0 && pending + e.Bytes.Length > maxPending)
            {
                long before = taken;
                if (Monitor.Wait(gate, diskWait) || taken != before || closed) continue;
                overflow = "磁盘没有响应";
                CloseInput();
            }
            if (closed) return;
            pending += e.Bytes.Length;
            queue.Add((e.Bytes.ToArray(), terminal.Cols, control.OutputWritten));
        }
    }

    /// <summary>Takes no more output; what is queued is still written. Under <see cref="gate"/>.</summary>
    void CloseInput()
    {
        if (closed) return;
        closed = true;
        control.OutputReceived -= OnOutput;
        queue.CompleteAdding();
        Monitor.PulseAll(gate); // a reader that is waiting need not
    }

    void Work()
    {
        string? failure = null;
        try
        {
            if (start != null) transcript.Begin(start, startColumns);
            foreach (var (bytes, columns, before) in queue.GetConsumingEnumerable())
            {
                if (start == null || before >= start.Written) transcript.Write(bytes, columns);
                if (queue.Count == 0) file.Flush(); // caught up: the file shows what the terminal does
                lock (gate)
                {
                    pending -= bytes.Length;
                    taken += bytes.Length;
                    Monitor.PulseAll(gate);
                }
            }
            transcript.Finish();
        }
        catch (Exception e) { failure = e.Message; } // whatever it was, only the recording ends
        lock (gate) CloseInput();
        failure ??= overflow;
        try
        {
            // The log must not look complete when it is not.
            if (failure != null) file.Write(Encoding.UTF8.GetBytes($"# 记录在此停止：{(failure.Length > 60 ? failure[..60] : failure)}{Environment.NewLine}"));
        }
        catch (Exception) { } // nothing can be written: the status line says it
        try { file.Dispose(); }
        catch (Exception e) { failure ??= e.Message; }
        if (failure != null) stopped?.Invoke(failure);
    }

    /// <summary>Takes no more output, without waiting for the rest to be written.</summary>
    public void End() { lock (gate) CloseInput(); }

    /// <summary>Ends the recording; false when the rest is still being written.</summary>
    public bool Stop()
    {
        End();
        return worker.Wait(CloseWait);
    }

    public void Dispose() => Stop();
}
