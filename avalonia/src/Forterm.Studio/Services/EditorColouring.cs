using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;

namespace Forterm.Studio.Services;

/// <summary>Colours the lines of an editor within a limit: a file — it comes from a server — must not be able to
/// hold up the window. When colouring takes too long, or a line is too long, it stops for this editor and says
/// so once. The text itself is never affected.</summary>
sealed class GuardedColorizer(IHighlightingDefinition definition, Action<string> stopped) : HighlightingColorizer(definition)
{
    readonly IHighlightingDefinition definition = definition;
    protected override IHighlighter CreateHighlighter(TextView textView, TextDocument document) => new GuardedHighlighter(document, definition, stopped);
}

sealed class GuardedHighlighter : IHighlighter
{
    public static readonly TimeSpan LongestMatch = TimeSpan.FromMilliseconds(500);
    /// <summary>One round is what is coloured between two drawings of the editor.</summary>
    internal TimeSpan LongestRound { get; init; } = TimeSpan.FromSeconds(1.5);
    internal TimeSpan SlowRound { get; init; } = TimeSpan.FromMilliseconds(300);
    const int MostSlowRounds = 3;

    readonly TextDocument document;
    readonly DocumentHighlighter inner;
    readonly Action<string> stopped;
    readonly Stopwatch spent = new();
    int reached, slow;
    bool off, disposed;

    public GuardedHighlighter(TextDocument document, IHighlightingDefinition definition, Action<string> stopped)
    {
        this.document = document;
        this.stopped = stopped;
        inner = new DocumentHighlighter(document, definition);
        document.Changed += OnChanged;
    }

    public bool IsOff => off;
    public IDocument Document => document;
    public HighlightingColor DefaultTextColor => inner.DefaultTextColor;
    public HighlightingColor GetNamedColor(string name) => inner.GetNamedColor(name);
    public event HighlightingStateChangedEventHandler HighlightingStateChanged { add => inner.HighlightingStateChanged += value; remove => inner.HighlightingStateChanged -= value; }

    /// <summary>The lines from the change on have to be looked at again.</summary>
    void OnChanged(object? sender, DocumentChangeEventArgs e) => reached = Math.Min(reached, document.GetLineByOffset(Math.Min(e.Offset, document.TextLength)).LineNumber - 1);

    public void BeginHighlighting() { spent.Reset(); inner.BeginHighlighting(); }

    public void EndHighlighting()
    {
        inner.EndHighlighting();
        slow = spent.Elapsed > SlowRound ? slow + 1 : 0;
        if (slow >= MostSlowRounds) Stop("着色太慢");
    }

    public IEnumerable<HighlightingColor> GetColorStack(int lineNumber) => UpTo(lineNumber) ? inner.GetColorStack(lineNumber) : [];
    public void UpdateHighlightingState(int lineNumber) => UpTo(lineNumber);

    public HighlightedLine HighlightLine(int lineNumber)
    {
        HighlightedLine? line = null;
        if (UpTo(lineNumber - 1)) Try(lineNumber, () => line = inner.HighlightLine(lineNumber));
        return line ?? new HighlightedLine(document, document.GetLineByNumber(lineNumber));
    }

    /// <summary>Line by line, so that a long way to the end of a file can be given up.</summary>
    bool UpTo(int lineNumber)
    {
        while (!off && reached < lineNumber)
        {
            int next = reached + 1;
            if (Try(next, () => inner.UpdateHighlightingState(next))) reached = next;
        }
        return !off;
    }

    bool Try(int lineNumber, Action work)
    {
        if (off) return false;
        if (document.GetLineByNumber(lineNumber).Length > EditorSyntax.LongestLine) { Stop(EditorSyntax.LongLineText); return false; }
        spent.Start();
        try { work(); }
        catch (RegexMatchTimeoutException) { Stop("着色太慢"); return false; }
        finally { spent.Stop(); }
        if (spent.Elapsed > LongestRound) Stop("着色太慢");
        return true;
    }

    void Stop(string reason)
    {
        if (off) return;
        off = true;
        Dispatcher.UIThread.Post(() => { if (!disposed) stopped(reason); });
    }

    public void Dispose()
    {
        disposed = true;
        document.Changed -= OnChanged;
        inner.Dispose();
    }
}
