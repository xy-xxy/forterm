using System.Text.RegularExpressions;
using System.Windows.Input;
using AvaloniaEdit.Document;
using Forterm.Studio.Mvvm;

namespace Forterm.Studio.ViewModels;

/// <summary>Find, replace and "go to line" of one editor. Patterns run without backtracking and within a time, so
/// neither the pattern nor the file can hold up the window. A line end counts as one character, however the file
/// writes it. Replacing is one step to undo.</summary>
public sealed class EditorFindViewModel : ObservableObject
{
    public const int MostMatches = 10_000, LongestQuery = 1000;
    static readonly TimeSpan Longest = TimeSpan.FromMilliseconds(300), LongestReplacing = TimeSpan.FromSeconds(2);
    const string TooSlow = "查找太慢，已停止；请写得更具体";
    readonly EditorTabViewModel editor;
    string query = "", replacement = "", error = "", line = "";
    bool open, replacing, matchCase, wholeWord, regex, goingToLine;
    /// <summary>With "whole words" a pattern of the user's is tried between word ends as well: "port|ports" finds
    /// "ports" only so, "=\\d+" only without.</summary>
    Regex? pattern, wordPattern;
    IReadOnlyList<TextSegment> matches = [];
    /// <summary>Keeps the matches at their places while the text changes, until it is searched again.</summary>
    TextSegmentCollection<TextSegment>? placed;
    int current = -1;
    bool stale;

    public EditorFindViewModel(EditorTabViewModel editor)
    {
        this.editor = editor;
        NextCommand = new RelayCommand(() => Step(1));
        PreviousCommand = new RelayCommand(() => Step(-1));
        ReplaceCommand = new RelayCommand(ReplaceOne);
        ReplaceAllCommand = new RelayCommand(ReplaceAll);
        ToggleReplaceCommand = new RelayCommand(() => IsReplacing = !IsReplacing);
        CloseCommand = new RelayCommand(Close);
        GoCommand = new RelayCommand(Go);
        editor.Document.Changed += (_, _) => stale = true;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(EditorTabViewModel.CanEdit)) return;
            Raise(nameof(CanReplace));
            if (!CanReplace) IsReplacing = false;
        };
    }

    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand ReplaceCommand { get; }
    public ICommand ReplaceAllCommand { get; }
    public ICommand ToggleReplaceCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand GoCommand { get; }

    /// <summary>Where the caret is, and how to go somewhere; set by the view that shows the editor.</summary>
    public Func<(int Offset, int Length)>? Selection { get; set; }
    public Action<int, int>? Select { get; set; }
    /// <summary>The matches, or the one that is current, are others now.</summary>
    public event Action? Updated;
    public event Action? FocusWanted;
    public event Action? Closed;

    public bool IsOpen { get => open; private set => Set(ref open, value); }
    public bool IsReplacing { get => replacing; set => Set(ref replacing, value && CanReplace); }
    public bool IsGoingToLine { get => goingToLine; private set => Set(ref goingToLine, value); }
    public bool CanReplace => editor.CanEdit;
    public string Query { get => query; set { if (Set(ref query, (value ?? "") is { Length: <= LongestQuery } q ? q : (value ?? "")[..LongestQuery])) Search(); } }
    public string Replacement { get => replacement; set => Set(ref replacement, value ?? ""); }
    public bool MatchCase { get => matchCase; set { if (Set(ref matchCase, value)) Search(); } }
    public bool WholeWord { get => wholeWord; set { if (Set(ref wholeWord, value)) Search(); } }
    public bool UseRegex { get => regex; set { if (Set(ref regex, value)) Search(); } }
    public string Line { get => line; set => Set(ref line, value ?? ""); }
    /// <summary>As last searched; one whose text is gone has no length any more.</summary>
    public IReadOnlyList<TextSegment> Matches => matches;
    public int Current => current;
    /// <summary>"3 / 12", "没有找到", or why the pattern cannot be used.</summary>
    public string Status => error.Length > 0 ? error : query.Length == 0 ? "" : matches.Count == 0 ? "没有找到" : $"{(current >= 0 ? current + 1 : "–")} / {matches.Count}{(matches.Count == MostMatches ? "+" : "")}";
    public bool IsBad => error.Length > 0 || query.Length > 0 && matches.Count == 0;

    public void Open(bool replace)
    {
        IsGoingToLine = false;
        IsOpen = true;
        if (replace) IsReplacing = true;
        // What is selected is what one looks for, if it is a word or two.
        if (Selection?.Invoke() is { Length: > 0 and <= 200 } s && editor.Document.GetText(s.Offset, s.Length) is var text && !text.Contains('\n')) { if (text != query) Query = text; else Search(); }
        else Search();
        FocusWanted?.Invoke();
    }

    public void OpenLine()
    {
        IsOpen = false;
        Line = "";
        IsGoingToLine = true;
        FocusWanted?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen && !IsGoingToLine) return;
        IsOpen = IsGoingToLine = false;
        Keep([]);
        current = -1;
        stale = true;
        Updated?.Invoke();
        Closed?.Invoke();
    }

    /// <summary>"120" or "120:8"; "-1" counts from the end. The nearest line there is.</summary>
    void Go()
    {
        string[] parts = line.Trim().Replace('：', ':').Split(':', ',');
        if (!int.TryParse(parts[0].Trim(), out int number) || number == 0) return;
        var document = editor.Document;
        number = Math.Clamp(number < 0 ? document.LineCount + 1 + number : number, 1, document.LineCount);
        var target = document.GetLineByNumber(number);
        int column = parts.Length > 1 && int.TryParse(parts[1].Trim(), out int c) ? Math.Clamp(c, 1, target.Length + 1) : 1;
        IsGoingToLine = false;
        Select?.Invoke(target.Offset + column - 1, 0);
        Closed?.Invoke();
    }

    // ---------- searching ----------

    void Search()
    {
        error = "";
        pattern = wordPattern = null;
        if (query.Length > 0)
        {
            var options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | RegexOptions.Multiline | (matchCase ? 0 : RegexOptions.IgnoreCase);
            // Patterns run in time linear in the text where they can. Looking around and back-references need the engine
            // that tries one way after another; it is stopped by the same time limit, between matches and inside one.
            Regex Make(string p)
            {
                try { return new Regex(p, options, Longest); }
                catch (NotSupportedException) { return new Regex(p, options & ~RegexOptions.NonBacktracking, Longest); }
            }
            try
            {
                string text = regex ? query : Regex.Escape(query);
                pattern = Make(text);
                // By itself first: "a)(b" is no pattern, although it would become one in brackets.
                if (wholeWord && regex) wordPattern = Make($@"\b(?:{text})\b");
                else if (wholeWord) pattern = Make((Word(query, 0) ? @"\b" : "") + text + (Word(query, query.Length - 1) ? @"\b" : ""));
            }
            catch (ArgumentException) { error = "正则写法有误"; }
        }
        stale = true;
        Refresh();
        // The first one from where the user is.
        if (matches.Count > 0 && Selection?.Invoke() is { } at)
        {
            current = IndexFrom(at.Offset);
            Show();
        }
        Report();
    }

    /// <summary>The text with every line end as "\n" — what "$", "." and "\n" mean in a pattern — and back.</summary>
    sealed class Lines
    {
        readonly List<int> ends = new(); // where a "\r\n" became a "\n"
        public string Text { get; }

        public Lines(string text)
        {
            if (!text.Contains("\r\n", StringComparison.Ordinal)) { Text = text; return; }
            var b = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') { ends.Add(b.Length); continue; }
                b.Append(text[i]);
            }
            Text = b.ToString();
        }

        /// <summary>The place in the file of a place in <see cref="Text"/>.</summary>
        public int Original(int at)
        {
            int low = 0, high = ends.Count;
            while (low < high) { int middle = (low + high) / 2; if (ends[middle] < at) low = middle + 1; else high = middle; }
            return at + low;
        }

        public int Place(int original)
        {
            int low = 0, high = Text.Length;
            while (low < high) { int middle = (low + high) / 2; if (Original(middle) < original) low = middle + 1; else high = middle; }
            return low;
        }
    }

    /// <summary>A character outside the basic plane is two here; a match never takes half of one.</summary>
    static (int Start, int End) Whole(string text, int start, int end)
    {
        if (start > 0 && char.IsLowSurrogate(text[start]) && char.IsHighSurrogate(text[start - 1])) start--;
        if (end < text.Length && char.IsLowSurrogate(text[end]) && char.IsHighSurrogate(text[end - 1])) end++;
        return (start, end);
    }

    /// <summary>Not when the word goes on before or behind it.</summary>
    static bool IsWhole(string text, int start, int end) => !(Word(text, start - 1) && Word(text, start) || Word(text, end - 1) && Word(text, end));

    /// <summary>What is found, in order: places only, which is quick.</summary>
    IEnumerable<(int Start, int End)> Ranges(string text, System.Diagnostics.Stopwatch clock, TimeSpan longest)
    {
        if (wordPattern == null)
        {
            var found = new List<(int, int)>();
            foreach (var m in pattern!.EnumerateMatches(text))
            {
                if (clock.Elapsed > longest) throw new RegexMatchTimeoutException();
                if (m.Length > 0) found.Add((m.Index, m.Index + m.Length));
                if (found.Count > 2 * MostMatches) break;
            }
            return found;
        }
        return Found(text, clock, longest).Select(m => (m.Index, m.Index + m.Length));
    }

    /// <summary>What is found, in order, with its groups.</summary>
    IEnumerable<Match> Found(string text, System.Diagnostics.Stopwatch clock, TimeSpan longest)
    {
        IEnumerable<Match> All(Regex regex)
        {
            for (var m = regex.Match(text); m.Success; m = m.NextMatch())
            {
                if (clock.Elapsed > longest) throw new RegexMatchTimeoutException();
                if (m.Length > 0) yield return m;
            }
        }
        if (wordPattern == null) return All(pattern!);
        return All(pattern!).Where(m => IsWhole(text, m.Index, m.Index + m.Length)).Concat(All(wordPattern)).OrderBy(m => m.Index).ThenByDescending(m => m.Length);
    }

    void Refresh()
    {
        if (!stale) return;
        stale = false;
        var found = new List<TextSegment>();
        if (pattern != null && open)
        {
            if (error == TooSlow) error = "";
            var lines = new Lines(editor.Document.Text);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int last = 0;
            try
            {
                foreach (var m in Ranges(lines.Text, clock, Longest))
                {
                    var (start, end) = Whole(lines.Text, m.Start, m.End);
                    if (start < last) continue;
                    last = end;
                    int from = lines.Original(start);
                    found.Add(new TextSegment { StartOffset = from, Length = lines.Original(end) - from });
                    if (found.Count == MostMatches) break;
                }
            }
            catch (RegexMatchTimeoutException) { error = TooSlow; found.Clear(); }
        }
        Keep(found);
        if (current >= matches.Count) current = matches.Count - 1;
    }

    void Keep(List<TextSegment> found)
    {
        placed?.Disconnect(editor.Document);
        placed = null;
        if (found.Count > 0)
        {
            placed = new TextSegmentCollection<TextSegment>(editor.Document);
            foreach (var segment in found) placed.Add(segment);
        }
        matches = found;
    }

    static bool Word(string text, int at) => at >= 0 && at < text.Length && (char.IsLetterOrDigit(text[at]) || text[at] == '_');

    /// <summary>The first match at or after a place, around the end.</summary>
    int IndexFrom(int offset)
    {
        int low = 0, high = matches.Count;
        while (low < high) { int middle = (low + high) / 2; if (matches[middle].StartOffset < offset) low = middle + 1; else high = middle; }
        return matches.Count == 0 ? -1 : low % matches.Count;
    }

    void Step(int by)
    {
        bool moved = stale;
        Refresh();
        if (matches.Count == 0) { Report(); return; }
        var at = Selection?.Invoke() ?? (0, 0);
        // From the selection when the text or the caret is elsewhere than at the current match.
        bool here = !moved && current >= 0 && current < matches.Count && matches[current].StartOffset == at.Item1 && matches[current].Length == at.Item2;
        int next = here ? current + by : by > 0 ? IndexFrom(at.Item1) : IndexFrom(at.Item1) - 1;
        current = (next + matches.Count) % matches.Count;
        Show();
        Report();
    }

    void Show() { if (current >= 0 && current < matches.Count) Select?.Invoke(matches[current].StartOffset, matches[current].Length); }

    void Report() { Raise(nameof(Status)); Raise(nameof(IsBad)); Updated?.Invoke(); }

    /// <summary>The text has changed: the count follows.</summary>
    public void FollowText()
    {
        if (!IsOpen || !stale) return;
        Refresh();
        Report();
    }

    // ---------- replacing ----------

    /// <summary>In a regular expression, $1, ${name}, $&amp; and $0 stand for what was found, exactly as it is in the
    /// file, and $$ for a "$"; otherwise the text is as it is. A line end in it becomes the file's own.</summary>
    static string Replaced(Match match, string replacement, bool regex, Lines lines, string all, string newLine)
    {
        string Literal(string text) => newLine == "\n" ? text : text.Replace("\n", newLine);
        replacement = replacement.Replace("\r\n", "\n");
        if (!regex) return Literal(replacement);
        string Found(Group g) { if (!g.Success) return ""; int from = lines.Original(g.Index); return all[from..lines.Original(g.Index + g.Length)]; }
        var made = new System.Text.StringBuilder();
        for (int i = 0; i < replacement.Length; i++)
        {
            char c = replacement[i];
            if (c != '$' || i + 1 >= replacement.Length) { made.Append(Literal(c.ToString())); continue; }
            char next = replacement[i + 1];
            if (next == '$') { made.Append('$'); i++; continue; }
            if (next == '&') { made.Append(Found(match)); i++; continue; }
            int end = i + 1;
            string name = "";
            if (next == '{' && replacement.IndexOf('}', i) is > 0 and var close) { name = replacement[(i + 2)..close]; end = close + 1; }
            else { while (end < replacement.Length && end < i + 3 && char.IsAsciiDigit(replacement[end])) end++; name = replacement[(i + 1)..end]; }
            // "$10" is the tenth group, or the first and a "0" when there is no tenth.
            if (next != '{' && name.Length == 2 && !match.Groups.ContainsKey(name) && match.Groups.ContainsKey(name[..1])) { name = name[..1]; end--; }
            if (name.Length == 0 || !match.Groups.ContainsKey(name)) { made.Append('$'); continue; }
            made.Append(Found(match.Groups[name]));
            i = end - 1;
        }
        return made.ToString();
    }
    static string NewLineOf(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    static bool IsBroken(string text)
    {
        for (int i = 0; i < text.Length; i++)
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            else if (char.IsSurrogate(text[i])) return true;
        return false;
    }

    void ReplaceOne()
    {
        if (!CanReplace) return;
        Refresh();
        var at = Selection?.Invoke() ?? (0, 0);
        if (pattern == null || current < 0 || current >= matches.Count || matches[current].StartOffset != at.Item1 || matches[current].Length != at.Item2) { Step(1); return; }
        var target = matches[current];
        string text;
        try
        {
            string all = editor.Document.Text;
            var lines = new Lines(all);
            int place = lines.Place(target.StartOffset), until = lines.Place(target.StartOffset + target.Length);
            var match = new[] { pattern, wordPattern }.OfType<Regex>().Select(p => p.Match(lines.Text, place)).FirstOrDefault(m => m.Success && Whole(lines.Text, m.Index, m.Index + m.Length) == (place, until));
            if (match == null) { Step(1); return; }
            text = Replaced(match, replacement, regex, lines, all, NewLineOf(all));
        }
        catch (RegexMatchTimeoutException) { error = TooSlow; Report(); return; }
        if (IsBroken(text)) { editor.Report("这样替换会拆开一个字符（例如表情符号），没有替换。"); return; }
        int start = target.StartOffset;
        editor.Document.Replace(start, target.Length, text);
        Select?.Invoke(start + text.Length, 0);
        Step(1);
    }

    /// <summary>All of them, however many: the text between the first and the last is put in place at once.</summary>
    void ReplaceAll()
    {
        if (!CanReplace || pattern == null) return;
        var document = editor.Document;
        string all = document.Text, newLine = NewLineOf(all);
        var lines = new Lines(all);
        var made = new System.Text.StringBuilder();
        int count = 0, first = -1, last = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            foreach (var m in Found(lines.Text, clock, LongestReplacing))
            {
                var (start, end) = Whole(lines.Text, m.Index, m.Index + m.Length);
                if (start < last) continue;
                int from = lines.Original(start), to = lines.Original(end);
                if (first < 0) first = from; else made.Append(document.GetText(lines.Original(last), from - lines.Original(last)));
                made.Append(Replaced(m, replacement, regex, lines, all, newLine));
                last = end;
                count++;
                if (made.Length > 4 * Core.RemoteText.MaxBytes) break;
            }
        }
        catch (RegexMatchTimeoutException) { error = TooSlow; Report(); return; }
        if (count == 0) { stale = true; Refresh(); Report(); return; }
        string text = made.ToString();
        int replaced = lines.Original(last) - first;
        var utf8 = System.Text.Encoding.UTF8;
        if (utf8.GetByteCount(all) - utf8.GetByteCount(all.AsSpan(first, replaced)) + utf8.GetByteCount(text) > Core.RemoteText.MaxBytes) { editor.Report("替换后会超过 1 MiB 的上限，没有替换。"); return; }
        if (IsBroken(text)) { editor.Report("这样替换会拆开一个字符（例如表情符号），没有替换。"); return; }
        var place = document.GetLocation(Math.Clamp(Selection?.Invoke().Offset ?? 0, 0, document.TextLength));
        Keep([]);
        document.Replace(first, replaced, text);
        // Near where the user was.
        var line = document.GetLineByNumber(Math.Clamp(place.Line, 1, document.LineCount));
        Select?.Invoke(line.Offset + Math.Clamp(place.Column - 1, 0, line.Length), 0);
        stale = true;
        Refresh();
        current = -1;
        Report();
        editor.Report($"已替换 {count} 处，可撤销；尚未保存。");
    }
}
