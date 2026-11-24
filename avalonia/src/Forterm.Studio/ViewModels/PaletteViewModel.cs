using System.Windows.Input;
using Forterm.Studio.Mvvm;

namespace Forterm.Studio.ViewModels;

/// <summary>Something the command palette can do.</summary>
public sealed record PaletteEntry(string Group, string Title, string Detail, string Keys, Action Run);

/// <summary>"命令面板": type a few letters to switch to a session, open a connection, run a command of the
/// window or a quick command. It only starts what the same click would start, questions included.</summary>
public sealed class PaletteViewModel : ObservableObject
{
    const int Most = 60;
    readonly Func<IEnumerable<PaletteEntry>> all;
    PaletteEntry[] entries = [];
    bool open;
    string text = "";
    int selected;

    public PaletteViewModel(Func<IEnumerable<PaletteEntry>> all)
    {
        this.all = all;
        CloseCommand = new RelayCommand(Close);
        RunCommand = new RelayCommand(p => Run(p as PaletteEntry ?? Current));
    }

    public ICommand CloseCommand { get; }
    public ICommand RunCommand { get; }
    public bool IsOpen { get => open; private set => Set(ref open, value); }
    public string Text { get => text; set { if (Set(ref text, value ?? "")) Filter(); } }
    public IReadOnlyList<PaletteEntry> Matches { get; private set; } = [];
    public int Selected { get => selected; set => Set(ref selected, Math.Clamp(value, 0, Math.Max(0, Matches.Count - 1))); }
    public bool IsEmpty => Matches.Count == 0;
    PaletteEntry? Current => selected < Matches.Count ? Matches[selected] : null;

    public void Open()
    {
        entries = all().ToArray();
        text = "";
        Raise(nameof(Text));
        Filter();
        IsOpen = true;
    }

    /// <summary>By the user, who goes on where they were.</summary>
    public void Close() { Dismissed = false; IsOpen = false; }
    /// <summary>By itself, because what it offers is no longer so. Whoever is typing in it must not find themselves
    /// typing somewhere else.</summary>
    public void Dismiss() { if (open) Dismissed = true; IsOpen = false; }
    public bool Dismissed { get; private set; }
    public void Move(int by) { if (Matches.Count > 0) Selected = (selected + by + Matches.Count) % Matches.Count; }

    void Run(PaletteEntry? entry)
    {
        if (entry == null) return;
        Close();
        entry.Run();
    }

    void Filter()
    {
        string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        Matches = entries.Select((entry, index) => (entry, index, score: Score(entry, words))).Where(x => x.score > 0)
            .OrderByDescending(x => x.score).ThenBy(x => x.index).Select(x => x.entry).Take(Most).ToArray();
        selected = 0;
        Raise(nameof(Matches));
        Raise(nameof(Selected));
        Raise(nameof(IsEmpty));
    }

    /// <summary>Every word has to be found; the start of a title counts most, then its middle, then letters of
    /// it in order, then the detail. 0 = no match.</summary>
    public static int Score(PaletteEntry entry, string[] words)
    {
        int total = 1;
        foreach (string word in words)
        {
            int at = entry.Title.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            int score = at == 0 ? 100 : at > 0 ? 60 : InOrder(entry.Title, word) ? 25 : entry.Detail.Contains(word, StringComparison.OrdinalIgnoreCase) ? 20 : entry.Group.Contains(word, StringComparison.OrdinalIgnoreCase) ? 10 : 0;
            if (score == 0) return 0;
            total += score;
        }
        return total;
    }

    static bool InOrder(string text, string letters)
    {
        int i = 0;
        foreach (char c in text) if (i < letters.Length && char.ToLowerInvariant(c) == char.ToLowerInvariant(letters[i])) i++;
        return i == letters.Length && letters.Length > 1;
    }
}
