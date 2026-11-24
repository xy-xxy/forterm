using System.Windows.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Forterm.Studio.Controls;
using Forterm.Studio.Mvvm;
using XTerm.Search;

namespace Forterm.Studio.ViewModels;

/// <summary>"查找" in the terminal shown in the centre column: searches the scrollback, highlights every
/// match and steps through them, newest first. Nothing is sent to the program.</summary>
public sealed class TerminalFindViewModel : ObservableObject
{
    readonly Func<StudioTerminalControl?> target;
    StudioTerminalControl? control;
    bool open, caseSensitive;
    string text = "", summary = "";

    public TerminalFindViewModel(Func<StudioTerminalControl?> target)
    {
        this.target = target;
        NextCommand = new RelayCommand(() => Step(next: true));
        PreviousCommand = new RelayCommand(() => Step(next: false));
        CloseCommand = new RelayCommand(Close);
    }

    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand CloseCommand { get; }

    public bool IsOpen { get => open; private set => Set(ref open, value); }
    public string Text { get => text; set { if (Set(ref text, value ?? "")) Search(); } }
    public bool CaseSensitive { get => caseSensitive; set { if (Set(ref caseSensitive, value)) Search(); } }
    public string Summary { get => summary; private set => Set(ref summary, value); }

    /// <summary>The view puts the focus in the find box.</summary>
    public event Action? Opened;

    public void Open()
    {
        if (target() is not { } current) return;
        IsOpen = true;
        Attach(current);
        Opened?.Invoke();
    }

    public void Close()
    {
        if (!open) return;
        IsOpen = false;
        var last = control;
        Attach(null);
        last?.FocusTerminal();
    }

    /// <summary>Another tab is shown: follow it, or close when it is not a terminal.</summary>
    public void Retarget()
    {
        if (!open) return;
        if (target() is { } current) Attach(current); else Close();
    }

    void Attach(StudioTerminalControl? next)
    {
        if (!ReferenceEquals(control, next))
        {
            if (control is { } old)
            {
                old.ClearSearch();
                old.Loaded -= OnLoaded;
            }
            control = next;
            if (next != null) next.Loaded += OnLoaded;
        }
        if (next != null) Search();
    }

    /// <summary>The terminal drops its search while another tab is shown; it starts over when it is back.</summary>
    void OnLoaded(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(() => { if (open) Search(); });

    void Search()
    {
        if (control is not { } c || !open) return;
        if (text.Length == 0) { c.ClearSearch(); Summary = ""; return; }
        // The newest match first: that is what is on the screen.
        if (c.FindInBuffer(text, Options) > 0) c.FindPrevious();
        Describe(c);
    }

    SearchOptions Options => new() { CaseSensitive = caseSensitive, KeepLast = true };

    void Step(bool next)
    {
        if (control is not { } c || !open || text.Length == 0) return;
        // Includes output that arrived since the last step, and steps on from the same match.
        if (c.RefreshSearch() == 0) c.FindInBuffer(text, Options);
        if (next) c.FindNext(); else c.FindPrevious();
        Describe(c);
    }

    void Describe(StudioTerminalControl c)
    {
        int count = c.SearchMatchCount, at = c.SearchCurrentMatch;
        Summary = count == 0 ? "无匹配" : $"{at + 1}/{count}{(c.SearchTruncated ? "+" : "")}";
    }
}
