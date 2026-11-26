using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Studio.Controls;
using Forterm.Studio.Library;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>0.4.0-alpha.8: editor find and options, chosen shortcuts, file list, layout, thresholds, backup.</summary>
public sealed class Alpha8Tests
{
    static async Task Pump(Func<bool> until, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end && !until()) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
        Dispatcher.UIThread.RunJobs();
    }

    static void Settle(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    static void Shot(Window window, string name) { Settle(window); window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, name + ".png")); }
    static void Key_(Window window, Key key, KeyModifiers modifiers = KeyModifiers.None) { window.KeyPress(key, (RawInputModifiers)(int)modifiers, PhysicalKey.None, null); if (window.IsVisible) window.KeyRelease(key, (RawInputModifiers)(int)modifiers, PhysicalKey.None, null); }
    static KeyModifiers Main => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    static async Task<(EditorTabViewModel Tab, WorkspaceViewModel Workspace)> Edit(StudioHarness h, LoopbackFixture fixture, string name, string text)
    {
        var w = (h.Model.Selected as WorkspaceViewModel) ?? await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "回环主机") with { DisablePwdFollow = true, DisableListMonitor = true });
        string local = Path.Combine(h.Data, name);
        File.WriteAllText(local, text);
        await w.DropAsync("/", [local]);
        await Pump(() => w.FileRoots.Any(n => n.Name == name), TimeSpan.FromSeconds(15));
        return ((await w.OpenEditorAsync(w.FileRoots.First(n => n.Name == name)))!, w);
    }

    // ---------- editor: find, replace, go to line ----------

    [AvaloniaFact]
    public async Task TheEditorFindsAndReplaces()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1240, Height = 720, DataContext = h.Model };
        window.Show();
        try
        {
            var (tab, _) = await Edit(h, fixture, "find.conf", "port=80\nPort=8080\nexport PORT=1\nsupport\n端口 port\n");
            Settle(window);
            var view = window.GetVisualDescendants().OfType<EditorView>().Single();
            var editor = view.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
            view.FocusText();
            // The component's own panel (no limit of time) is not there to be opened by its keys.
            Assert.DoesNotContain(editor.TextArea.DefaultInputHandler.NestedInputHandlers, x => x.GetType().Name.Contains("Search", StringComparison.Ordinal));
            Key_(window, Key.F, Main);
            var find = tab.Find;
            Assert.True(find.IsOpen);
            await Pump(() => view.FindControl<TextBox>("FindText")!.IsFocused, TimeSpan.FromSeconds(2));
            Assert.True(view.FindControl<TextBox>("FindText")!.IsFocused);
            find.Query = "port";
            Assert.Equal(5 + 1, find.Matches.Count); // port, Port, "export", PORT, "support", port
            Assert.Equal("1 / 6", find.Status);
            Assert.Equal("port", editor.SelectedText);
            find.MatchCase = true;
            Assert.Equal(4, find.Matches.Count);
            find.WholeWord = true;
            Assert.Equal(2, find.Matches.Count); // not export, support
            find.MatchCase = false;
            Assert.Equal(4, find.Matches.Count); // port, Port, PORT, port
            Shot(window, "editor-find");
            // Enter goes on, Shift+Enter back, around the ends.
            editor.Select(0, 0);
            var box = view.FindControl<TextBox>("FindText")!;
            box.Focus();
            Key_(window, Key.Enter);
            Assert.Equal((0, "1 / 4"), (editor.SelectionStart, find.Status));
            Key_(window, Key.Enter);
            Assert.Equal((8, "2 / 4"), (editor.SelectionStart, find.Status));
            Key_(window, Key.Enter, KeyModifiers.Shift);
            Key_(window, Key.Enter, KeyModifiers.Shift);
            Assert.Equal("4 / 4", find.Status);
            // Typing in the text: the count follows.
            editor.Document.Insert(editor.Document.TextLength, "port\n");
            await Pump(() => find.Status.EndsWith("/ 5", StringComparison.Ordinal), TimeSpan.FromSeconds(2));
            Assert.EndsWith("/ 5", find.Status);
            editor.Document.UndoStack.Undo();
            // Replace one, then all; all is one step to undo.
            find.IsReplacing = true;
            find.Replacement = "$1-x";
            editor.Select(0, 0);
            find.NextCommand.Execute(null);
            find.ReplaceCommand.Execute(null);
            Assert.StartsWith("$1-x=80\nPort=8080", tab.Document.Text.ReplaceLineEndings("\n")); // plain text is plain
            Assert.Equal("Port", editor.SelectedText); // on to the next
            find.ReplaceAllCommand.Execute(null);
            Assert.Equal("$1-x=80\n$1-x=8080\nexport $1-x=1\nsupport\n端口 $1-x\n", tab.Document.Text.ReplaceLineEndings("\n"));
            Assert.Contains("已替换 3 处", tab.Status);
            Assert.True(tab.IsDirty);
            editor.Document.UndoStack.Undo();
            Assert.Equal("$1-x=80\nPort=8080\nexport PORT=1\nsupport\n端口 port\n", tab.Document.Text.ReplaceLineEndings("\n"));
            // Patterns: groups, and what cannot be used.
            find.UseRegex = true;
            find.WholeWord = false;
            find.Query = "^(\\w+)=(\\d+)$";
            Assert.Equal(1, find.Matches.Count);
            find.Replacement = "$2=$1";
            find.ReplaceAllCommand.Execute(null);
            Assert.Contains("8080=Port", tab.Document.Text);
            // Looking around and back-references work too (alpha.10).
            find.Query = "(?<==)port";
            Assert.Equal(1, find.Matches.Count);
            Assert.False(find.IsBad);
            find.Query = "(\\w)\\1";
            Assert.Equal(1, find.Matches.Count); // "pp" of "support"
            Assert.False(find.IsBad);
            find.Query = "(";
            Assert.Contains("有误", find.Status);
            find.Query = "x*"; // matches nothing everywhere
            Assert.All(find.Matches, m => Assert.True(m.Length > 0));
            find.Query = "zzz";
            Assert.Equal("没有找到", find.Status);
            Key_(window, Key.Escape);
            Assert.False(find.IsOpen);
            await Pump(() => view.IsKeyboardFocusWithin, TimeSpan.FromSeconds(1));
            // What is selected is what one looks for.
            editor.Select(editor.Document.Text.IndexOf("support", StringComparison.Ordinal), 7);
            find.UseRegex = false;
            find.Open(replace: false);
            Assert.Equal("support", find.Query);
            find.Close();

            // Go to line.
            Key_(window, Key.G, KeyModifiers.Control);
            Assert.True(find.IsGoingToLine);
            await Pump(() => view.FindControl<TextBox>("LineText")!.IsFocused, TimeSpan.FromSeconds(2));
            find.Line = "4:3";
            Key_(window, Key.Enter);
            Assert.False(find.IsGoingToLine);
            Assert.Equal((4, 3), (editor.TextArea.Caret.Line, editor.TextArea.Caret.Column));
            foreach (var (text, line) in new[] { ("999", 6), ("-1", 6), ("1", 1), ("3：2", 3) }) { find.OpenLine(); find.Line = text; find.GoCommand.Execute(null); Assert.Equal(line, editor.TextArea.Caret.Line); }
            find.OpenLine();
            find.Line = "abc";
            find.GoCommand.Execute(null);
            Assert.True(find.IsGoingToLine); // nothing to go to
            find.Close();
            Settle(window);
            Assert.Equal("行 3，列 2", view.FindControl<TextBlock>("PositionText")!.Text);
            editor.Select(0, 12);
            Settle(window);
            Assert.StartsWith("已选 12 个字符", view.FindControl<TextBlock>("SelectionText")!.Text);
            // The component's own panel, whose patterns have no limits, is not there any more.
            Assert.Empty(window.GetVisualDescendants().OfType<AvaloniaEdit.Search.SearchPanel>().Where(p => p.IsEffectivelyVisible));
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    static EditorTabViewModel Draft(string text, out Func<string> content)
    {
        var tab = new EditorTabViewModel(new FakeDocument(text), "来源", new TransferQueue(), new ScriptedInteraction(), (_, _) => { }, _ => { });
        (int Offset, int Length) at = (0, 0);
        tab.Find.Selection = () => at;
        tab.Find.Select = (o, l) => at = (o, l);
        content = () => tab.Document.Text;
        return tab;
    }

    sealed class FakeDocument(string text) : IRemoteTextDocument
    {
        sealed class FakeSource : IRemoteFileSource
        {
            public Guid SourceId { get; } = Guid.NewGuid();
            public FileResource Resource => throw new NotSupportedException();
            public bool IsAvailable => true;
            public CancellationToken Closed => CancellationToken.None;
            public WorkspaceProtocol Protocol => WorkspaceProtocol.Sftp;
            public string? CloseReason => null;
            public void Dispose() { }
        }
        public IRemoteFileSource Source { get; } = new FakeSource();
        public string Path => "/x.txt";
        public string Text => text;
        public bool HasBom => false;
        public bool Exists => true;
        public bool CanReplace => true;
        public Task<IRemoteTextDocument> SaveAsync(string text, IProgress<TransferProgress> progress, CancellationToken ct) => throw new NotSupportedException();
        public Task<IRemoteTextDocument> SaveAsNewAsync(string name, string text, IProgress<TransferProgress> progress, CancellationToken ct) => throw new NotSupportedException();
    }

    [AvaloniaFact]
    public void ReplacingKeepsLineEndsContextAndWholeCharacters()
    {
        string Run(string text, string query, string replacement, bool regex = true, bool word = false, bool matchCase = true)
        {
            var tab = Draft(text, out var content);
            var f = tab.Find;
            f.Open(replace: true);
            (f.UseRegex, f.WholeWord, f.MatchCase, f.Query, f.Replacement) = (regex, word, matchCase, query, replacement);
            f.ReplaceAllCommand.Execute(null);
            string all = content();
            tab.Document.UndoStack.Undo();
            Assert.Equal(text, content()); // one step
            return all;
        }
        // "$", "." and "\n" mean the same whatever the file writes at line ends; the file keeps its own.
        Assert.Equal("x\r\nx\r\n", Run("a=1\r\nb=2\r\n", "^.+$", "x"));
        Assert.Equal("\"a=1\"\r\n\"b=2\"\r\n", Run("a=1\r\nb=2\r\n", "^(.+)$", "\"$1\""));
        Assert.Equal("a=1;b=2;", Run("a=1\r\nb=2\r\n", "\\n", ";"));
        Assert.Equal("a=#\r\nb=#\r\n", Run("a=1\r\nb=2\r\n", "\\d$", "#"));
        Assert.Equal("a\r\nb\r\n", Run("a;b\r\n", ";", "\n", regex: false));
        Assert.Equal("x\nx\n", Run("a=1\nb=2\n", "^.+$", "x"));
        // What was found is replaced where it stands.
        Assert.Equal("a<foo>", Run("afoo", "\\B(foo)", "<$1>"));
        Assert.Equal("a[b]", Run("ab", "(a)$|(b)$", "[$1$2]"));
        Assert.Equal("[a]b [a]", Run("ab a", "a", "[$0]"));
        Assert.Equal("$0b $0", Run("ab a", "a", "$0", regex: false)); // plain text is plain
        Assert.Equal("a.c a-c", Run("abc a-c", "abc", "a.c", regex: false));
        Assert.Equal("abc x", Run("abc a.c", "a.c", "x", regex: false)); // so is what one looks for
        Assert.Equal("ports x support", Run("ports port support", "port", "x", regex: false, word: true));
        Assert.Equal("x x support", Run("ports port support", "port|ports", "x", word: true));
        Assert.Equal("a x b --xy", Run("a --x b --xy", "--x", "x", regex: false, word: true));
        Assert.Equal("a)(b", Run("a)(b", "a)(b", "x", word: true)); // no pattern
        Assert.Equal("x X", Run("x =5", "=\\d+", "X", word: true)); // begins with no word character
        Assert.Equal("a1b", Run("a1b", "\\d", "#", word: true));
        // Only what ends a line in the file is a line end; what was found stays as it is.
        Assert.Equal("\"b\":\"x\u2028y\"}", Run("{\"a\":\"x\u2028y\"}", "\\{\"a\":(.*)\\}", "\"b\":$1}"));
        Assert.Equal("[a\u0085b]", Run("a\u0085b", "^(.*)$", "[$1]"));
        Assert.Equal("[a\rb\r]", Run("a\rb\r", "^([\\s\\S]*)$", "[$1]"));
        Assert.Equal("<a\r\nb\nc>", Run("a\r\nb\nc", "^(a\\nb\\nc)$", "<$1>"));
        Assert.Equal("$1 b-a a0 $ {x}", Run("a b", "(\\w) (?<n>\\w)", "$$1 ${n}-$1 $10 $ {x}"));
        Assert.Equal("[ab]$9$", Run("ab", "ab", "[$&]$9$"));
        // All of them, also more than are marked; a second time changes nothing that was changed.
        string many = string.Concat(Enumerable.Repeat("a,b\n", 12_000));
        string once = Run(many, ",", ", ", regex: false);
        Assert.Equal(many.Replace(",", ", "), once);
        // Characters outside the basic plane stay whole, or nothing is replaced.
        Assert.Equal("xbc", Run("😀bc", "^.", "x"));
        Assert.Equal("😀bc", Run("😀bc", "^(.)", "$1-"));
        Assert.Equal("--", Run("😀😀", ".", "-"));
        // Not beyond what can be saved.
        string big = new string('a', 700_000);
        Assert.Equal(big, Run(big, "a", "bb", regex: false));
        Assert.Equal(new string('b', 700_000), Run(big, "a", "b", regex: false));
        Assert.Equal(new string('a', 400_000), Run(new string('a', 400_000), "a", "中", regex: false)); // in bytes
    }

    [AvaloniaFact]
    public void FindingMapsPlacesAndGivesUpInTime()
    {
        var tab = Draft("one\r\ntwo two\r\nthree\r\n", out _);
        var f = tab.Find;
        f.Open(replace: false);
        f.Query = "t";
        Assert.Equal([5, 9, 14], f.Matches.Select(m => m.StartOffset));
        f.UseRegex = true;
        f.Query = "o\\nt|e\\n";
        Assert.NotEmpty(f.Matches);
        Assert.All(f.Matches, m => Assert.Contains("\r\n", tab.Document.GetText(m.StartOffset, m.Length)));
        // One by one, in a file with two characters at line ends.
        var single = Draft("a=1\r\na=2\r\n", out var ones);
        single.Find.Open(replace: true);
        (single.Find.UseRegex, single.Find.Query, single.Find.Replacement) = (true, "a=(\\d)$", "b:$1");
        single.Find.ReplaceCommand.Execute(null);
        Assert.Equal("b:1\r\na=2\r\n", ones());
        single.Find.ReplaceCommand.Execute(null);
        Assert.Equal("b:1\r\nb:2\r\n", ones());
        // Places follow the text until it is searched again.
        f.UseRegex = false;
        f.Query = "three";
        Assert.Equal(14, f.Matches[0].StartOffset);
        tab.Document.Insert(0, "xxxx");
        Assert.Equal(18, f.Matches[0].StartOffset);
        // A pattern that has to look at everything again and again is given up, and says so.
        var slow = Draft(string.Concat(Enumerable.Repeat("key and more text\n", 55_000)), out _);
        slow.Find.Open(replace: true);
        slow.Find.UseRegex = true;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        slow.Find.Query = "key[\\s\\S]*?ZZZ|key";
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), clock.Elapsed.ToString());
        Assert.Contains("太慢", slow.Find.Status);
        slow.Find.Replacement = "x";
        string before = slow.Document.Text;
        clock.Restart();
        slow.Find.ReplaceAllCommand.Execute(null);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(8), clock.Elapsed.ToString());
        Assert.Equal(before, slow.Document.Text);
        slow.Find.Query = "more";
        Assert.DoesNotContain("太慢", slow.Find.Status);
        Assert.Equal(EditorFindViewModel.MostMatches, slow.Find.Matches.Count);
        // Read-only: neither one nor all.
        var read = Draft("a a a", out var content);
        read.Find.Open(replace: true);
        read.Find.Query = "a";
        read.Find.Replacement = "b";
        var raised = new List<string?>();
        read.Find.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        typeof(EditorTabViewModel).GetProperty("IsReadOnly")!.GetSetMethod(true)!.Invoke(read, [true]);
        Assert.Contains(nameof(EditorFindViewModel.CanReplace), raised);
        Assert.False(read.Find.IsReplacing);
        read.Find.ReplaceCommand.Execute(null);
        read.Find.ReplaceAllCommand.Execute(null);
        Assert.Equal("a a a", content());
    }

    [AvaloniaFact]
    public async Task ManyMatchesInOneLongLineAreDrawnInTime()
    {
        Appearance.Apply(new StudioPreferences { EditorWrap = true });
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1240, Height = 720, DataContext = h.Model };
        window.Show();
        try
        {
            var (tab, _) = await Edit(h, fixture, "long.txt", string.Concat(Enumerable.Repeat("port=80;", 7_500)) + "\nport\n");
            Settle(window);
            tab.Find.Open(replace: false);
            tab.Find.Query = "port";
            Assert.Equal(7_501, tab.Find.Matches.Count);
            Settle(window);
            // Against the same steps with the marks off: how fast the machine is does not matter.
            TimeSpan Steps() { var clock = System.Diagnostics.Stopwatch.StartNew(); for (int i = 0; i < 4; i++) { tab.Find.Select!(9 * (1000 * (i % 2) + 10 * i), 4); window.CaptureRenderedFrame(); } return clock.Elapsed; }
            Steps();
            var marked = Steps();
            Shot(window, "editor-find-long-line");
            tab.Find.CloseCommand.Execute(null);
            Settle(window);
            Assert.False(tab.Find.IsOpen);
            var plain = Steps();
            Assert.True(marked < plain * 3 + TimeSpan.FromSeconds(2), $"{marked} / {plain}");
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    /// <summary>Caret and selection are those of the text before while a change is being made.</summary>
    [AvaloniaFact]
    public async Task ShorterTextUnderASelectionIsNoProblem()
    {
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1240, Height = 720, DataContext = h.Model };
        window.Show();
        var (tab, w) = await Edit(h, fixture, "short.txt", "aaa\nbbb\nccc\nddd\n");
        Settle(window);
        var editor = window.GetVisualDescendants().OfType<EditorView>().Single().FindControl<AvaloniaEdit.TextEditor>("Editor")!;
        tab.Find.Open(replace: false);
        tab.Find.Query = "c";
        editor.Select(0, 14);
        tab.Document.Text = "x";
        Settle(window);
        tab.Document.Text = "ccc\nccc\nccc\n";
        await Pump(() => tab.Find.Matches.Count == 9, TimeSpan.FromSeconds(2));
        Assert.Equal(9, tab.Find.Matches.Count);
        editor.SelectAll();
        tab.Document.Remove(0, tab.Document.TextLength);
        Settle(window);
        tab.Document.Insert(0, "a\nb c\nc\n");
        editor.Select(2, 5);
        w.ActiveTab = w.Terminals[0]; // another tab in front, with a selection behind
        Settle(window);
        w.ActiveTab = tab;
        Settle(window);
        editor = window.GetVisualDescendants().OfType<EditorView>().Single().FindControl<AvaloniaEdit.TextEditor>("Editor")!;
        editor.Select(0, 6);
        h.Model.Close(w); // the draft is cleared
        Settle(window);
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task FindingStaysQuickAndReadOnlyFilesAreNotReplaced()
    {
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var (tab, w) = await Edit(h, fixture, "big.txt", string.Concat(Enumerable.Repeat("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaab\n", 30_000)));
        var find = tab.Find;
        find.Open(replace: false);
        find.UseRegex = true;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (string query in new[] { "(a+)+$", "(a|a)*c", "(a|aa)+$", "(.*)(.*)(.*)x", "a{1,50}a{1,50}c", "a" }) find.Query = query;
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), clock.Elapsed.ToString());
        Assert.Equal(EditorFindViewModel.MostMatches, find.Matches.Count);
        Assert.Contains("10000+", find.Status);
        find.Query = new string('a', EditorFindViewModel.LongestQuery + 50);
        Assert.Equal(EditorFindViewModel.LongestQuery, find.Query.Length);
        // The connection is gone: the draft is read-only, and stays as it is.
        find.Query = "b";
        find.Replacement = "c";
        Assert.NotNull(w);
        fixture.Dispose();
        await Pump(() => !tab.CanEdit, TimeSpan.FromSeconds(20));
        Assert.False(tab.CanEdit);
        string before = tab.Document.Text;
        find.ReplaceAllCommand.Execute(null);
        find.ReplaceCommand.Execute(null);
        find.IsReplacing = true;
        Assert.False(find.IsReplacing);
        Assert.Equal(before, tab.Document.Text);
    }

    [AvaloniaFact]
    public async Task TheEditorFollowsItsSettingsAndRemembersLanguages()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1240, Height = 720, DataContext = h.Model };
        window.Show();
        try
        {
            var (tab, w) = await Edit(h, fixture, "site.conf", "a = 1\n");
            Settle(window);
            var view = window.GetVisualDescendants().OfType<EditorView>().Single();
            var editor = view.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
            Assert.Equal((13, false, true, 4, false), (editor.FontSize, editor.WordWrap, editor.ShowLineNumbers, editor.Options.IndentationSize, editor.Options.ConvertTabsToSpaces));
            Appearance.Apply(Appearance.Current with { EditorFontSize = 17, EditorWrap = true, EditorLineNumbers = false, EditorTab = 2, EditorSpaces = true, EditorWhitespace = true });
            Assert.Equal((17, true, false, 2, true, true), (editor.FontSize, editor.WordWrap, editor.ShowLineNumbers, editor.Options.IndentationSize, editor.Options.ConvertTabsToSpaces, editor.Options.ShowTabs));
            // The wheel and the button change the same settings.
            Settle(window);
            var over = editor.TranslatePoint(new Point(80, 40), window)!.Value;
            for (int i = 0; i < 10; i++) window.MouseWheel(over, new Vector(0, 0.1), (RawInputModifiers)(int)Main);
            Assert.InRange(Appearance.Current.EditorFontSize, 17, 18);
            window.MouseWheel(over, new Vector(0, 1), (RawInputModifiers)(int)Main);
            Assert.InRange(Appearance.Current.EditorFontSize, 18, 19);
            view.FindControl<Button>("WrapButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.False(Appearance.Current.EditorWrap);
            Shot(window, "editor-options");

            // Colours off and on again.
            Assert.Equal("INI", tab.ColouredLanguage.Id);
            Appearance.Apply(Appearance.Current with { EditorColours = false });
            Assert.Null(view.Definition);
            Assert.Contains("关闭", tab.LanguageTip);
            Appearance.Apply(Appearance.Current with { EditorColours = true });
            Assert.NotNull(view.Definition);

            // A choice by hand holds for files of the same kind, until what would be recognised is chosen again.
            tab.Language = EditorSyntax.Find("YAML");
            Assert.Equal("YAML", Appearance.Current.EditorLanguages![".conf"]);
            var (other, _) = await Edit(h, fixture, "other.conf", "b: 1\n");
            Assert.Equal("YAML", other.Language!.Id);
            other.Language = EditorSyntax.Find("INI");
            Assert.Null(Appearance.Current.EditorLanguages);
            Assert.Equal("YAML", tab.Language!.Id); // tabs that are open stay as they are
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    // ---------- shortcuts ----------

    [AvaloniaFact]
    public void ShortcutsCanBeChosenWithinLimits()
    {
        string? Check(string gesture, bool mac) => Shortcuts.Check(KeyGesture.Parse(gesture), mac);
        // Everything comes back as it was written.
        foreach (var s in Shortcuts.All) Assert.True(Shortcuts.Same(s.Standard, Shortcuts.Parse(s.Standard.ToString())!), s.Standard.ToString());
        foreach (string g in new[] { "Ctrl+Shift+K", "Ctrl+Shift+F5", "Ctrl+Tab", "Ctrl+Shift+Tab", "Ctrl+Shift+OemComma" }) Assert.Null(Check(g, mac: false));
        foreach (string g in new[] { "K", "Shift+K", "F5", "Ctrl+K", "Ctrl+C", "Alt+K", "Ctrl+Alt+K", "Ctrl+Alt+Shift+K", "Ctrl+Shift+OemMinus", "Ctrl+Shift+D2", "Ctrl+Shift+Space", "Ctrl+Shift+Up", "Cmd+K", "Ctrl+Back" }) Assert.NotNull(Check(g, mac: false));
        foreach (string g in new[] { "Cmd+J", "Cmd+Shift+K", "Cmd+Alt+K", "Cmd+F5", "Ctrl+Shift+K", "Ctrl+Tab" }) Assert.Null(Check(g, mac: true));
        foreach (string g in new[] { "Cmd+Q", "Cmd+H", "Cmd+Space", "Cmd+D1", "Cmd+S", "Cmd+Z", "Alt+K", "Ctrl+K", "Ctrl+C", "F5" }) Assert.NotNull(Check(g, mac: true));
        foreach (var taken in Iciclecreek.Terminal.TerminalKeyGestures.Copy.Concat(Iciclecreek.Terminal.TerminalKeyGestures.Paste)) Assert.NotNull(Shortcuts.Check(taken));
        Assert.NotNull(Shortcuts.Check(new KeyGesture(Key.LeftShift, KeyModifiers.Control | KeyModifiers.Shift)));

        foreach (string g in new[] { "Cmd+G", "Cmd+Shift+G", "Cmd+Alt+F", "Cmd+Back", "Cmd+Left", "Cmd+Enter" }) Assert.NotNull(Check(g, mac: true)); // the editor's
        // A file may say anything.
        Assert.Null(Shortcuts.Validated(null));
        Assert.Null(Shortcuts.Validated(new Dictionary<string, string> { ["nope"] = "Ctrl+Shift+K", ["palette"] = "????", ["home"] = "Ctrl+C", ["zoomIn"] = "Ctrl+Shift+Y", ["settings"] = new string('x', 500), ["find"] = null! }));
        Assert.Equal("", Shortcuts.Validated(new Dictionary<string, string> { ["home"] = "" })!["home"]);
        var both = Shortcuts.Validated(new Dictionary<string, string> { ["palette"] = "Ctrl+Shift+Y", ["home"] = "Ctrl+Shift+Y" })!;
        Assert.Single(both);
        // Taking over another one's standard keys needs that one to be moved away.
        Assert.Null(Shortcuts.Validated(new Dictionary<string, string> { ["home"] = Shortcuts.NewTerminal.Standard.ToString() }));
        var swapped = Shortcuts.Validated(new Dictionary<string, string> { ["home"] = Shortcuts.NewTerminal.Standard.ToString(), ["newTerminal"] = "Ctrl+Shift+Y" })!;
        Assert.Equal(2, swapped.Count);
    }

    [AvaloniaFact]
    public void ShortcutsAreChosenOnTheSettingsPage()
    {
        string data = Paths.TempDirectory();
        using var h = new StudioHarness();
        var vm = new SettingsViewModel(data) { Section = "keys" };
        var settings = new SettingsWindow { DataContext = vm, Width = 900, Height = 700 };
        var window = new MainWindow { Width = 1000, Height = 600, DataContext = h.Model };
        try
        {
            window.Show();
            settings.Show();
            Settle(settings);
            var palette = vm.Keys.Single(k => k.Shortcut == Shortcuts.Palette);
            Assert.All(vm.Keys.Where(k => k.Shortcut.IsFixed), k => Assert.False(k.CanChange));
            Assert.False(vm.Press(Key.Y, KeyModifiers.Control | KeyModifiers.Shift)); // nothing is being chosen
            palette.ChangeCommand.Execute(null);
            Assert.True(palette.IsCapturing);
            Key_(settings, Key.LeftShift, KeyModifiers.Shift); // not finished
            Key_(settings, Key.C, KeyModifiers.Control); // the shell's
            Assert.True(palette.HasError && palette.IsCapturing);
            Assert.Contains("不能用", palette.Error);
            Key_(settings, Shortcuts.Home.Gesture.Key, Shortcuts.Home.Gesture.KeyModifiers); // another one's
            Assert.Contains("连接库", palette.Error);
            Shot(settings, "settings-shortcuts-choosing");
            Key_(settings, Key.Y, KeyModifiers.Control | KeyModifiers.Shift);
            Assert.False(palette.IsCapturing);
            Assert.Equal("", palette.Error);
            Assert.True(palette.IsChanged);
            Assert.Equal(Shortcuts.Show(new(Key.Y, KeyModifiers.Control | KeyModifiers.Shift)), palette.Keys);
            Assert.True(Appearance.Load(data).Shortcuts!.ContainsKey("palette"));
            // It works at once, the old one does not.
            Key_(window, Shortcuts.Palette.Standard.Key, Shortcuts.Palette.Standard.KeyModifiers);
            Assert.False(h.Model.Palette.IsOpen);
            Key_(window, Key.Y, KeyModifiers.Control | KeyModifiers.Shift);
            Assert.True(h.Model.Palette.IsOpen);
            Key_(window, Key.Escape);
            Assert.Contains(Shortcuts.Palette.Keys, (string)ToolTip.GetTip(window.FindControl<Button>("PaletteButton")!)!);
            // Off, and Esc while choosing changes nothing.
            palette.OffCommand.Execute(null);
            Assert.Equal("未设置", palette.Keys);
            Key_(window, Key.Y, KeyModifiers.Control | KeyModifiers.Shift);
            Assert.False(h.Model.Palette.IsOpen);
            palette.ChangeCommand.Execute(null);
            Key_(settings, Key.Escape);
            Assert.False(vm.IsCapturing);
            Assert.True(Shortcuts.Palette.IsOff);
            palette.ResetCommand.Execute(null);
            Assert.False(palette.IsChanged || vm.KeysChanged);
            // Back to the standard, while another one has it: said, and nobody loses anything.
            var home = vm.Keys.Single(k => k.Shortcut == Shortcuts.Home);
            palette.ChangeCommand.Execute(null);
            vm.Press(Key.Y, KeyModifiers.Control | KeyModifiers.Shift);
            home.ChangeCommand.Execute(null);
            vm.Press(Shortcuts.Palette.Standard.Key, Shortcuts.Palette.Standard.KeyModifiers);
            Assert.True(home.IsChanged);
            palette.ResetCommand.Execute(null);
            Assert.Contains("连接库", palette.Error);
            Assert.True(home.IsChanged && palette.IsChanged);
            vm.ResetKeysCommand.Execute(null);
            Assert.False(home.IsChanged || palette.IsChanged || palette.HasError);
            // Leaving the page or the window ends choosing.
            palette.ChangeCommand.Execute(null);
            vm.Section = "theme";
            Assert.False(vm.IsCapturing);
            // Choosing the standard again is no change.
            palette.ChangeCommand.Execute(null);
            vm.Press(Shortcuts.Palette.Standard.Key, Shortcuts.Palette.Standard.KeyModifiers);
            Assert.Null(Appearance.Current.Shortcuts);
        }
        finally { settings.Close(); window.Close(); Appearance.Apply(new StudioPreferences()); Directory.Delete(data, true); }
    }

    // ---------- files ----------

    static RemoteEntry E(string name, bool directory = false, long size = 0, int day = 0) => new("/" + name, name, directory, false, !directory, size) { Modified = day == 0 ? null : new DateTimeOffset(2026, 1, day, 10, 0, 0, TimeSpan.Zero) };

    [AvaloniaFact]
    public void TheListIsArrangedButPlansSeeEverything()
    {
        var all = new[] { E("b.txt", size: 5, day: 3), E(".hidden", size: 9, day: 9), E("A.log", size: 50, day: 1), E("dir", directory: true, day: 2), E(".git", directory: true), E("c.txt", size: 1), E("."), E("..") };
        string Names(StudioPreferences p) { Appearance.Apply(p); return string.Join(' ', FileNodeViewModel.Arrange(all).Select(e => e.Name)); }
        try
        {
            Assert.Equal(".git dir .hidden A.log b.txt c.txt", Names(new()));
            Assert.Equal("dir A.log b.txt c.txt", Names(new() { ShowHidden = false }));
            Assert.Equal(".git dir c.txt b.txt .hidden A.log", Names(new() { FileSort = "size" })); // directories by name
            Assert.Equal(".git dir A.log .hidden b.txt c.txt", Names(new() { FileSort = "size", FileSortDescending = true }));
            Assert.Equal("dir .git .hidden b.txt A.log c.txt", Names(new() { FileSort = "time", FileSortDescending = true })); // unknown times last
            Assert.Equal(".git dir .hidden A.log b.txt c.txt", Names(new() { FileSort = "type" }));
            Assert.Equal("dir .git c.txt b.txt A.log .hidden", Names(new() { FileSortDescending = true }));
            Appearance.Apply(new() { ShowHidden = false, FileSort = "size", FileSortDescending = true });
            Assert.Equal(".git dir .hidden A.log b.txt c.txt", string.Join(' ', FileNodeViewModel.Sort(all).Select(e => e.Name)));
            Assert.Equal("name", new StudioPreferences { FileSort = "nonsense" }.Validated().FileSort);
        }
        finally { Appearance.Apply(new StudioPreferences()); }
        // The time is no part of an entry's identity: what Desktop compares stays equal.
        Assert.Equal(E("x", size: 1, day: 1), E("x", size: 1, day: 2));
        Assert.Equal(E("x", size: 1, day: 1).GetHashCode(), E("x", size: 1).GetHashCode());
        Assert.NotEqual(E("x", size: 1), E("x", size: 2));
    }

    [AvaloniaFact]
    public async Task HiddenFilesAreOnlyNotShown()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1300, Height = 720, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "回环主机") with { DisablePwdFollow = true, DisableListMonitor = true });
            string folder = Path.Combine(h.Data, "mixed");
            Directory.CreateDirectory(Path.Combine(folder, ".inner"));
            File.WriteAllText(Path.Combine(folder, ".env"), "SECRET\n");
            File.WriteAllText(Path.Combine(folder, "seen.txt"), "seen\n");
            File.WriteAllText(Path.Combine(folder, ".inner", "deep.txt"), "deep\n");
            await w.DropAsync("/", [folder]);
            await Pump(() => w.FileRoots.Any(n => n.Name == "mixed") && !w.Transfers!.HasActive, TimeSpan.FromSeconds(20));
            var node = w.FileRoots.First(n => n.Name == "mixed");
            node.IsExpanded = true;
            await Pump(() => node.Children.Any(c => c.Name == "seen.txt"), TimeSpan.FromSeconds(10));
            Assert.Equal(3, node.Children.Count);
            var row = node.Children.First(c => c.Name == "seen.txt");
            Assert.NotNull(row.Modified);
            Assert.Equal("5 B", row.SizeText);
            Assert.Matches("^\\d\\d-\\d\\d \\d\\d:\\d\\d$", row.TimeText);
            w.SelectedNode = row;
            Shot(window, "files-details");
            int lists = fixture.Stats().GetProperty("lstats").GetInt32();
            Appearance.Apply(Appearance.Current with { ShowHidden = false });
            Assert.Equal(["seen.txt"], node.Children.Select(c => c.Name));
            Settle(window); // the tree has its say
            Assert.Equal("seen.txt", w.SelectedNode?.Name); // the selection stays
            Assert.Equal("/mixed", w.CurrentDirectory);
            Assert.Equal(lists, fixture.Stats().GetProperty("lstats").GetInt32()); // nothing was asked of the server
            // Downloading and deleting the folder still mean all of it.
            h.Interaction.Folder = Path.Combine(h.Data, "down");
            Directory.CreateDirectory(h.Interaction.Folder);
            w.SelectedNode = node;
            w.DownloadCommand.Execute(null);
            await Pump(() => File.Exists(Path.Combine(h.Interaction.Folder, "mixed", ".inner", "deep.txt")) && !w.Transfers!.HasActive, TimeSpan.FromSeconds(20));
            Assert.Equal("SECRET\n", File.ReadAllText(Path.Combine(h.Interaction.Folder, "mixed", ".env")));
            w.SelectedNode = node;
            w.DeleteCommand.Execute(null);
            await Pump(() => !w.FileRoots.Any(n => n.Name == "mixed"), TimeSpan.FromSeconds(20));
            Assert.DoesNotContain(w.FileRoots, n => n.Name == "mixed");
            Appearance.Apply(Appearance.Current with { ShowHidden = true });
            Assert.DoesNotContain(w.FileRoots, n => n.Name == "mixed");
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    /// <summary>The tree gives up its selection while rows are exchanged; the next upload must not go to "/".</summary>
    [AvaloniaFact]
    public async Task TheSelectionOutlastsSortingHidingAndRefreshing()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1300, Height = 720, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "回环主机") with { DisablePwdFollow = true, DisableListMonitor = true });
            string folder = Path.Combine(h.Data, "proj");
            Directory.CreateDirectory(Path.Combine(folder, "in", "deep"));
            Directory.CreateDirectory(Path.Combine(folder, ".conf", "sub"));
            foreach (var (name, size) in new[] { ("a.txt", 5), ("b.log", 50), ("c.txt", 1) }) File.WriteAllText(Path.Combine(folder, name), new string('x', size));
            File.WriteAllText(Path.Combine(folder, "in", "deep", "y.txt"), "y");
            File.WriteAllText(Path.Combine(folder, ".conf", "sub", "z.txt"), "z");
            await w.DropAsync("/", [folder]);
            await Pump(() => w.FileRoots.Any(n => n.Name == "proj") && !w.Transfers!.HasActive, TimeSpan.FromSeconds(20));
            async Task<FileNodeViewModel> Go(string path)
            {
                FileNodeViewModel? node = null;
                IEnumerable<FileNodeViewModel> level = w.FileRoots;
                foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    await Pump(() => level.Any(n => n.Name == part), TimeSpan.FromSeconds(8));
                    node = level.First(n => n.Name == part);
                    if (node.IsDirectory) { node.IsExpanded = true; await node.EnsureLoadedAsync(); }
                    level = node.Children;
                }
                Settle(window);
                w.SelectedNode = node;
                node!.IsSelected = true;
                Settle(window);
                return node;
            }
            void Is(string path, string directory)
            {
                for (int i = 0; i < 3; i++) Settle(window);
                Assert.Equal((path, directory), (w.SelectedNode?.Path ?? "", w.CurrentDirectory));
                var selected = window.GetVisualDescendants().OfType<TreeView>().First(t => t.Name == "FileTree").SelectedItem as FileNodeViewModel;
                Assert.Equal(path, selected?.Path);
            }
            foreach (var (path, directory) in new[] { ("/proj/a.txt", "/proj"), ("/proj/in", "/proj/in"), ("/proj/in/deep", "/proj/in/deep"), ("/proj/in/deep/y.txt", "/proj/in/deep"), ("/proj/.conf/sub", "/proj/.conf/sub"), ("/proj/.conf/sub/z.txt", "/proj/.conf/sub"), ("/proj", "/proj") })
            {
                Appearance.Apply(new StudioPreferences());
                await Go(path);
                Is(path, directory);
                foreach (var p in new[] { new StudioPreferences { FileSort = "size", FileSortDescending = true }, new StudioPreferences { FileSort = "time" }, new StudioPreferences { ShowHidden = false }, new StudioPreferences { ShowHidden = false, FileSortDescending = true }, new StudioPreferences() })
                {
                    Appearance.Apply(p);
                    Is(path, directory);
                }
                // Listed again: the same.
                await w.ReloadDirectoryAsync(directory);
                Is(path, directory);
                await w.ReloadDirectoryAsync("/");
                Is(path, directory);
            }
            // What is gone leaves the user in its directory, not in "/", and nothing selected: Delete once more is not
            // for the directory.
            var tree = window.GetVisualDescendants().OfType<TreeView>().First(t => t.Name == "FileTree");
            await Go("/proj/c.txt");
            w.DeleteCommand.Execute(null);
            await Pump(() => !w.FileRoots.First(n => n.Name == "proj").Children.Any(n => n.Name == "c.txt"), TimeSpan.FromSeconds(10));
            for (int i = 0; i < 3; i++) Settle(window);
            Assert.Null(w.SelectedNode);
            Assert.Null(tree.SelectedItem);
            Assert.False(w.HasSelection || w.SelectedIsDirectory);
            Assert.Equal("/proj", w.CurrentDirectory);
            Assert.Equal(["proj"], w.PathSegments.Select(x => x.Name));
            int asked = h.Interaction.Confirms.Count;
            tree.Focus();
            Key_(window, Key.Delete);
            Key_(window, Key.F2);
            w.DeleteCommand.Execute(null);
            await Pump(() => false, TimeSpan.FromMilliseconds(200));
            Assert.Equal(asked, h.Interaction.Confirms.Count);
            // Sorting, hiding and refreshing do not move the user from there.
            foreach (var p in new[] { new StudioPreferences { FileSort = "size" }, new StudioPreferences { ShowHidden = false }, new StudioPreferences() })
            {
                Appearance.Apply(p);
                for (int i = 0; i < 3; i++) Settle(window);
                Assert.Equal((null, "/proj"), (w.SelectedNode?.Path, w.CurrentDirectory));
            }
            w.RefreshDirectoryCommand.Execute(null);
            await w.ReloadDirectoryAsync("/");
            for (int i = 0; i < 3; i++) Settle(window);
            Assert.Equal((null, "/proj"), (w.SelectedNode?.Path, w.CurrentDirectory));
            // A hidden directory the user is in that way stays in sight; one that is gone leaves the next above.
            await Go("/proj/.conf/sub/z.txt");
            w.DeleteCommand.Execute(null);
            await Pump(() => w.SelectedNode == null, TimeSpan.FromSeconds(10));
            Appearance.Apply(new StudioPreferences { ShowHidden = false });
            for (int i = 0; i < 3; i++) Settle(window);
            Assert.Equal((null, "/proj/.conf/sub"), (w.SelectedNode?.Path, w.CurrentDirectory));
            Assert.Contains(w.FileRoots.First(n => n.Name == "proj").Children, n => n.Name == ".conf");
            Appearance.Apply(new StudioPreferences());
            await Go("/proj/in/deep/y.txt");
            w.DeleteCommand.Execute(null);
            await Pump(() => w.SelectedNode == null, TimeSpan.FromSeconds(10));
            Assert.Equal("/proj/in/deep", w.CurrentDirectory);
            var inside = w.FileRoots.First(n => n.Name == "proj").Children.First(n => n.Name == "in");
            ((SftpLease)w.Actions!.Source).Client.DeleteDirectory("/proj/in/deep"); // somebody else
            await w.ReloadDirectoryAsync("/proj/in");
            for (int i = 0; i < 3; i++) Settle(window);
            Assert.DoesNotContain(inside.Children, n => n.Name == "deep");
            Assert.Equal((null, "/proj/in"), (w.SelectedNode?.Path, w.CurrentDirectory));
            // To the top by a click beside the rows; choosing a row ends it as well.
            var seen = new List<string?>();
            w.PropertyChanged += (_, e) => seen.Add(e.PropertyName);
            w.SelectNothing();
            Assert.Equal("/", w.CurrentDirectory);
            Assert.Empty(w.PathSegments);
            Assert.Contains(nameof(w.CurrentDirectory), seen);
            File.WriteAllText(Path.Combine(h.Data, "d.txt"), "d");
            await w.DropAsync("/proj", [Path.Combine(h.Data, "d.txt")]);
            await Pump(() => !w.Transfers!.HasActive && w.FileRoots.First(n => n.Name == "proj").Children.Any(n => n.Name == "d.txt"), TimeSpan.FromSeconds(15));
            await Go("/proj/d.txt");
            w.DeleteCommand.Execute(null);
            await Pump(() => w.SelectedNode == null && w.CurrentDirectory == "/proj", TimeSpan.FromSeconds(10));
            Assert.Equal("/proj", w.CurrentDirectory);
            await Go("/proj/in");
            w.SelectedNode = null;
            for (int i = 0; i < 3; i++) Settle(window);
            Assert.Equal("/", w.CurrentDirectory);
            // Another choice meanwhile is the user's.
            await Go("/proj/a.txt");
            Appearance.Apply(new StudioPreferences { FileSort = "size" });
            var b = w.FileRoots.First(n => n.Name == "proj").Children.First(n => n.Name == "b.log");
            w.SelectedNode = b;
            Is("/proj/b.log", "/proj");
            // Choosing nothing is possible as ever, also just now.
            Appearance.Apply(new StudioPreferences { FileSort = "time" });
            w.SelectNothing();
            for (int i = 0; i < 3; i++) Settle(window);
            Assert.Equal((null, "/"), (w.SelectedNode?.Path, w.CurrentDirectory));
            await Go("/proj/a.txt");
            w.SelectedNode = null;
            Settle(window);
            Assert.Equal("/", w.CurrentDirectory);
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    static TreeView Tree(Window window) => window.GetVisualDescendants().OfType<TreeView>().First(t => t.Name == "FileTree");
    static IEnumerable<FileNodeViewModel> All(IEnumerable<FileNodeViewModel> level) => level.SelectMany(n => new[] { n }.Concat(All(n.Children)));

    /// <summary>The list, the path above it and where files go are one, also after another session was in front.</summary>
    static void Consistent(WorkspaceViewModel w, Window window, string? selected, string directory)
    {
        for (int i = 0; i < 4; i++) Settle(window);
        Assert.Equal((selected, directory), (w.SelectedNode?.Path, w.CurrentDirectory));
        Assert.Equal(directory, "/" + string.Join('/', w.PathSegments.Select(x => x.Name)));
        Assert.Equal(selected == null ? [] : [selected], All(w.FileRoots).Where(n => n.IsSelected).Select(n => n.Path));
        Assert.Equal(selected, (Tree(window).SelectedItem as FileNodeViewModel)?.Path);
    }

    static async Task<WorkspaceViewModel> WithFiles(StudioHarness h, LoopbackFixture fixture, Window window)
    {
        var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "回环主机") with { DisablePwdFollow = true, DisableListMonitor = true });
        await Pump(() => w.HasFiles && w.FileRoots.Count > 0, TimeSpan.FromSeconds(10));
        var client = ((SftpLease)w.Actions!.Source).Client;
        foreach (string d in new[] { "/proj", "/proj/in", "/proj/in/deep", "/proj/other", "/proj/empty", "/zeta" }) if (!client.Exists(d)) client.CreateDirectory(d);
        foreach (string f in new[] { "/proj/a.txt", "/proj/b.txt", "/proj/in/z.txt", "/proj/other/o1" }) client.WriteAllText(f, f);
        await w.ReloadDirectoryAsync("/");
        Settle(window);
        return w;
    }

    static async Task Cd(WorkspaceViewModel w, Window window, string directory)
    {
        w.OnTerminalDirectory(w.Terminals[0], directory);
        await Pump(() => w.CurrentDirectory == directory, TimeSpan.FromSeconds(4));
        Settle(window);
    }

    [AvaloniaTheory]
    [InlineData("session")]
    [InlineData("home")]
    [InlineData("pane")]
    [InlineData("here")]
    public async Task WhatIsInFrontChangesNothingInASession(string how)
    {
        Appearance.Apply(new StudioPreferences { CursorBlink = false });
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1300, Height = 720, DataContext = h.Model };
        window.Show();
        try
        {
            var other = await WithFiles(h, fixture, window);
            await Cd(other, window, "/zeta");
            Consistent(other, window, "/zeta", "/zeta");
            var w = await WithFiles(h, fixture, window);
            var client = ((SftpLease)w.Actions!.Source).Client;
            void Away() { if (how == "session") h.Model.Selected = other; else if (how == "home") h.Model.Selected = h.Model.Home; else if (how == "pane") w.ShowFiles = false; Settle(window); Settle(window); }
            void Back() { h.Model.Selected = w; w.ShowFiles = true; Settle(window); Settle(window); }
            await Cd(w, window, "/proj/in");
            // Away and back: nothing; and while away, files still go where they went.
            Away();
            Assert.Equal("/proj/in", w.CurrentDirectory);
            Back();
            Consistent(w, window, "/proj/in", "/proj/in");
            // The shell moves on meanwhile, through places that were selected before.
            Away();
            foreach (string d in new[] { "/proj/other", "/zeta", "/proj/in/deep", "/proj" }) await Cd(w, window, d);
            Assert.Equal("/proj", w.CurrentDirectory);
            Back();
            Consistent(w, window, "/proj", "/proj");
            Appearance.Apply(Appearance.Current with { FileSortDescending = true });
            Consistent(w, window, "/proj", "/proj");
            // Sorted while away; a row that was chosen by the user.
            w.SelectedNode = All(w.FileRoots).First(n => n.Path == "/proj/a.txt");
            Settle(window);
            Away();
            Appearance.Apply(Appearance.Current with { FileSortDescending = false });
            Back();
            Consistent(w, window, "/proj/a.txt", "/proj");
            // Deleted, and away at once; gone while away.
            await Cd(w, window, "/proj/in");
            w.SelectedNode = All(w.FileRoots).First(n => n.Path == "/proj/in/z.txt");
            Settle(window);
            w.DeleteCommand.Execute(null);
            Away();
            await Pump(() => !All(w.FileRoots).Any(n => n.Path == "/proj/in/z.txt"), TimeSpan.FromSeconds(8));
            Back();
            Consistent(w, window, null, "/proj/in");
            w.SelectedNode = All(w.FileRoots).First(n => n.Path == "/proj/other/o1");
            Settle(window);
            Away();
            client.DeleteFile("/proj/other/o1");
            await w.ReloadDirectoryAsync("/proj/other");
            Back();
            Consistent(w, window, null, "/proj/other");
            if (how == "session") { h.Model.Selected = other; Consistent(other, window, "/zeta", "/zeta"); }
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    [AvaloniaFact]
    public async Task FilesGoWhereTheListSays()
    {
        Appearance.Apply(new StudioPreferences { CursorBlink = false });
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1300, Height = 720, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await WithFiles(h, fixture, window);
            var client = ((SftpLease)w.Actions!.Source).Client;
            FileNodeViewModel Row(string path) => All(w.FileRoots).First(n => n.Path == path);
            TreeViewItem Item(FileNodeViewModel node) => Tree(window).GetVisualDescendants().OfType<TreeViewItem>().First(i => ReferenceEquals(i.DataContext, node));
            void Click(FileNodeViewModel node, RawInputModifiers modifiers = RawInputModifiers.None)
            {
                Settle(window);
                var at = Item(node).TranslatePoint(new Point(60, 12), window)!.Value;
                window.MouseDown(at, MouseButton.Left, modifiers);
                window.MouseUp(at, MouseButton.Left, modifiers);
                Settle(window);
            }
            await Cd(w, window, "/proj");
            foreach (string d in new[] { "/proj/empty", "/proj/in" }) { Row(d).IsExpanded = true; await Row(d).EnsureLoadedAsync(); }
            // The mouse, with and without Ctrl; keys.
            Click(Row("/proj/a.txt"));
            Consistent(w, window, "/proj/a.txt", "/proj");
            Click(Row("/proj/in/z.txt"));
            Consistent(w, window, "/proj/in/z.txt", "/proj/in");
            await Task.Delay(700, CancellationToken.None); // not a double click, which opens the file
            Click(Row("/proj/in/z.txt"), OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
            Consistent(w, window, "/proj/in/z.txt", "/proj/in");
            Assert.Empty(w.Editors);
            Item(Row("/proj/in/z.txt")).Focus();
            Key_(window, Key.Up);
            Consistent(w, window, "/proj/in/deep", "/proj/in/deep");
            Key_(window, Key.Down);
            Consistent(w, window, "/proj/in/z.txt", "/proj/in");
            // "空目录" stands for the directory it is in.
            var note = Row("/proj/empty").Children.Single();
            Assert.True(note.IsPlaceholder);
            Click(note);
            for (int i = 0; i < 3; i++) Settle(window);
            Assert.Equal((null, "/proj/empty"), (w.SelectedNode?.Path, w.CurrentDirectory));
            Assert.Equal(["proj", "empty"], w.PathSegments.Select(x => x.Name));
            Assert.False(w.HasSelection);
            // Listing it again fails because somebody has removed it: not "/" from there.
            Click(Row("/proj/in/z.txt"));
            client.DeleteFile("/proj/in/z.txt");
            client.DeleteDirectory("/proj/in/deep");
            client.DeleteDirectory("/proj/in");
            w.RefreshDirectoryCommand.Execute(null);
            await Pump(() => Row("/proj/in").Children.FirstOrDefault() is { IsPlaceholder: true } p && p.Name.Contains("读取失败"), TimeSpan.FromSeconds(5));
            for (int i = 0; i < 3; i++) Settle(window);
            Assert.Equal((null, "/proj/in"), (w.SelectedNode?.Path, w.CurrentDirectory));
            await w.ReloadDirectoryAsync("/proj");
            Consistent(w, window, null, "/proj");
            // A directory where the selected file was is another thing.
            Click(Row("/proj/b.txt"));
            client.DeleteFile("/proj/b.txt");
            client.CreateDirectory("/proj/b.txt");
            await w.ReloadDirectoryAsync("/proj");
            Consistent(w, window, null, "/proj");
            // The shell moves while the menu of a row is open, or is about to open: the row stays until it has closed.
            Click(Row("/proj/a.txt"));
            w.OnTerminalDirectory(w.Terminals[0], "/proj/other");
            w.SetFileMenuOpen(true);
            await Pump(() => false, TimeSpan.FromMilliseconds(300));
            Consistent(w, window, "/proj/a.txt", "/proj");
            w.SetFileMenuOpen(false);
            await Pump(() => w.CurrentDirectory == "/proj/other", TimeSpan.FromSeconds(4));
            Consistent(w, window, "/proj/other", "/proj/other");
            // What the user chooses while the shell's directory is being looked up stands.
            client.CreateDirectory("/zeta/new");
            w.OnTerminalDirectory(w.Terminals[0], "/zeta/new");
            w.SelectedNode = Row("/proj/a.txt");
            await Pump(() => false, TimeSpan.FromMilliseconds(400));
            Consistent(w, window, "/proj/a.txt", "/proj");
            // Two in a row, the first of which is still being looked up: the last one.
            foreach (string d in new[] { "/late", "/late/one", "/later", "/later/two" }) client.CreateDirectory(d);
            await w.ReloadDirectoryAsync("/");
            w.OnTerminalDirectory(w.Terminals[0], "/late/one");
            Assert.NotEqual("/late/one", w.CurrentDirectory);
            w.OnTerminalDirectory(w.Terminals[0], "/");
            await Pump(() => false, TimeSpan.FromMilliseconds(400));
            Consistent(w, window, null, "/");
            w.OnTerminalDirectory(w.Terminals[0], "/later/two");
            w.SelectNothing(); // a click beside the rows meanwhile
            await Pump(() => false, TimeSpan.FromMilliseconds(400));
            Consistent(w, window, null, "/");
            // A tree that begins further down ("/ho") is not left for a directory whose name only begins alike ("/home").
            foreach (string d in new[] { "/ho", "/ho/me", "/ho/me/x", "/home", "/home/x" }) client.CreateDirectory(d);
            var root = typeof(WorkspaceViewModel).GetField("rootPath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            root.SetValue(w, "/ho");
            await w.ReloadDirectoryAsync("/ho");
            await Cd(w, window, "/ho/me/x");
            Consistent(w, window, "/ho/me/x", "/ho/me/x");
            w.SelectNothing();
            Consistent(w, window, null, "/ho");
            w.OnTerminalDirectory(w.Terminals[0], "/home/x");
            await Pump(() => false, TimeSpan.FromMilliseconds(300));
            Consistent(w, window, null, "/ho");
            root.SetValue(w, "/");
            await w.ReloadDirectoryAsync("/");
            w.SelectNothing();
            // The mouse beside the rows.
            Row("/proj").IsExpanded = true;
            await Row("/proj").EnsureLoadedAsync();
            Click(Row("/proj/a.txt"));
            var tree = Tree(window);
            var below = tree.TranslatePoint(new Point(40, tree.Bounds.Height - 6), window)!.Value;
            window.MouseDown(below, MouseButton.Right);
            Assert.Equal((null, "/"), (w.SelectedNode?.Path, w.CurrentDirectory));
            window.MouseUp(below, MouseButton.Right);
            Key_(window, Key.Escape);
            Consistent(w, window, null, "/");
            // Also when no row is selected although the user is in a directory.
            w.SelectedNode = Row("/proj/empty").Children.Single();
            Assert.Equal("/proj/empty", w.CurrentDirectory);
            window.MouseDown(below, MouseButton.Right);
            Assert.Equal((null, "/"), (w.SelectedNode?.Path, w.CurrentDirectory));
            window.MouseUp(below, MouseButton.Right);
            Key_(window, Key.Escape);
            Consistent(w, window, null, "/");
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    /// <summary>Refreshing leaves the list where it is, and the keyboard with it.</summary>
    [AvaloniaFact]
    public async Task RefreshingLeavesThePlaceAndTheKeyboard()
    {
        Appearance.Apply(new StudioPreferences { CursorBlink = false });
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1300, Height = 620, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await WithFiles(h, fixture, window);
            var client = ((SftpLease)w.Actions!.Source).Client;
            for (int i = 0; i < 60; i++) client.WriteAllText($"/proj/f{i:00}.txt", new string('x', i + 1));
            await w.ReloadDirectoryAsync("/proj");
            await Cd(w, window, "/proj");
            var tree = Tree(window);
            var scroll = tree.GetVisualDescendants().OfType<ScrollViewer>().First();
            FileNodeViewModel Row(string path) => All(w.FileRoots).First(n => n.Path == path);
            TreeViewItem Item(string path) => (TreeViewItem)tree.TreeContainerFromItem(Row(path))!;
            string? Keys() => (TopLevel.GetTopLevel(tree)!.FocusManager!.GetFocusedElement() as Control)?.DataContext is FileNodeViewModel n ? n.Path.Length > 0 ? n.Path : n.Name : null;
            async Task Refresh() { await w.ReloadDirectoryAsync("/proj"); await Pump(() => false, TimeSpan.FromMilliseconds(60)); for (int i = 0; i < 4; i++) Settle(window); }
            void Choose(string path) { w.SelectedNode = Row(path); Settle(window); Item(path).Focus(); Settle(window); Assert.Equal(path, Keys()); }
            Choose("/proj/f05.txt");
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            Settle(window);
            double at = scroll.Offset.Y;
            Assert.True(at > 600);
            // Another file before it is gone, another has come: nothing moves but by what is gone.
            client.DeleteFile("/proj/f02.txt");
            await Refresh();
            Assert.Equal("/proj/f05.txt", Keys());
            Assert.True(Math.Abs(scroll.Offset.Y - at) < 60, $"{at} -> {scroll.Offset.Y}");
            client.WriteAllText("/proj/b-new.txt", "n");
            await Refresh();
            Assert.Equal("/proj/f05.txt", Keys());
            Assert.True(scroll.Offset.Y > 600);
            // The row itself is exchanged (it has grown), or moves: the keyboard goes along.
            client.WriteAllText("/proj/f05.txt", new string('y', 999));
            await Refresh();
            Assert.Equal(("/proj/f05.txt", "/proj/f05.txt"), (w.SelectedNode?.Path, Keys()));
            Key_(window, Key.Down);
            Consistent(w, window, "/proj/f06.txt", "/proj");
            Appearance.Apply(Appearance.Current with { FileSortDescending = true });
            await Pump(() => false, TimeSpan.FromMilliseconds(60));
            for (int i = 0; i < 4; i++) Settle(window);
            Assert.Equal("/proj/f06.txt", Keys());
            Appearance.Apply(Appearance.Current with { FileSortDescending = false });
            await Pump(() => false, TimeSpan.FromMilliseconds(60));
            for (int i = 0; i < 4; i++) Settle(window);
            // Deleted: no row is selected, and the arrow keys go on from its directory.
            Choose("/proj/f20.txt");
            w.DeleteCommand.Execute(null);
            await Pump(() => w.SelectedNode == null, TimeSpan.FromSeconds(6));
            await Pump(() => false, TimeSpan.FromMilliseconds(60));
            for (int i = 0; i < 4; i++) Settle(window);
            Assert.Equal("/proj", Keys());
            Assert.Null(w.SelectedNode);
            int asked = h.Interaction.Confirms.Count;
            Key_(window, Key.Delete);
            Assert.Equal(asked, h.Interaction.Confirms.Count);
            Key_(window, Key.Down);
            Consistent(w, window, "/proj", "/proj");
            // Through "空目录" with the keys.
            Row("/proj/empty").IsExpanded = true;
            await Row("/proj/empty").EnsureLoadedAsync();
            Choose("/proj/empty");
            Key_(window, Key.Down);
            Assert.Equal((null, "/proj/empty"), (w.SelectedNode?.Path, w.CurrentDirectory));
            Assert.Equal("空目录", Keys());
            Key_(window, Key.Down);
            Consistent(w, window, "/proj/in", "/proj/in");
            Key_(window, Key.Up);
            Key_(window, Key.Up);
            Consistent(w, window, "/proj/empty", "/proj/empty");
            // Not taken from where somebody is typing: the terminal, also when they went there only just now.
            w.Terminal!.FocusTerminal();
            Settle(window);
            Assert.True(w.Terminal.IsKeyboardFocusWithin);
            client.WriteAllText("/proj/empty/new", "n");
            await w.ReloadDirectoryAsync("/proj/empty");
            await Refresh();
            Assert.True(w.Terminal.IsKeyboardFocusWithin);
            Choose("/proj/f30.txt");
            Appearance.Apply(Appearance.Current with { FileSortDescending = true });
            w.Terminal.FocusTerminal();
            Assert.True(w.Terminal.IsKeyboardFocusWithin);
            await Pump(() => false, TimeSpan.FromMilliseconds(80));
            for (int i = 0; i < 4; i++) Settle(window);
            Assert.True(w.Terminal.IsKeyboardFocusWithin);
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    [AvaloniaFact]
    public async Task AStartDirectoryThatIsNotThereIsSaidAndNotShown()
    {
        Appearance.Apply(new StudioPreferences { CursorBlink = false });
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1300, Height = 720, DataContext = h.Model };
        window.Show();
        try
        {
            foreach (var (start, directory) in new[] { ("/nonexistent/app", "/"), ("/a/missing", "/a"), ("/a", "/a") })
            {
                var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "起点") with { DisablePwdFollow = true, DisableListMonitor = true, DefaultPath = start });
                await Pump(() => w.HasFiles && w.FileRoots.Count > 0 && w.CurrentDirectory == directory, TimeSpan.FromSeconds(10));
                await Pump(() => false, TimeSpan.FromMilliseconds(200));
                Consistent(w, window, directory == "/" ? null : directory, directory);
                Assert.Equal(start != directory, w.FileNotice.Contains(start));
            }
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    [AvaloniaFact]
    public async Task HiddenDirectoriesTheUserIsInAreShown()
    {
        Appearance.Apply(new StudioPreferences { ShowHidden = false });
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        try
        {
            var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "回环主机") with { DisablePwdFollow = true, DisableListMonitor = true });
            string folder = Path.Combine(h.Data, "proj");
            Directory.CreateDirectory(Path.Combine(folder, ".conf", "sub"));
            Directory.CreateDirectory(Path.Combine(folder, ".other"));
            File.WriteAllText(Path.Combine(folder, ".conf", "sub", "x.txt"), "x\n");
            await w.DropAsync("/", [folder]);
            await Pump(() => w.FileRoots.Any(n => n.Name == "proj") && !w.Transfers!.HasActive, TimeSpan.FromSeconds(20));
            // The shell went there: the tree goes along.
            w.OnTerminalDirectory(w.Terminals[0], "/proj/.conf/sub");
            await Pump(() => w.CurrentDirectory == "/proj/.conf/sub", TimeSpan.FromSeconds(10));
            Assert.Equal("/proj/.conf/sub", w.CurrentDirectory);
            var proj = w.FileRoots.First(n => n.Name == "proj");
            Assert.Equal([".conf"], proj.Children.Select(c => c.Name)); // the other one stays hidden
            // Hidden while one is in it: it stays in sight, the rest does not.
            Appearance.Apply(Appearance.Current with { ShowHidden = true });
            Assert.Equal([".conf", ".other"], proj.Children.Select(c => c.Name));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("/proj/.conf/sub", w.CurrentDirectory);
            Appearance.Apply(Appearance.Current with { ShowHidden = false });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("/proj/.conf/sub", w.CurrentDirectory);
            Assert.Equal([".conf"], proj.Children.Select(c => c.Name));
            // Listed again, still there; and the time of a directory is that of the new listing.
            await w.ReloadDirectoryAsync("/proj");
            Assert.Equal([".conf"], proj.Children.Select(c => c.Name));
            var entries = new[] { E("d", directory: true, day: 1) };
            var rows = new System.Collections.ObjectModel.ObservableCollection<FileNodeViewModel>();
            FileNodeViewModel Make(RemoteEntry e) => new(e, (_, _, _) => Task.FromResult<IReadOnlyList<RemoteEntry>>([]), CancellationToken.None);
            FileNodeViewModel.Merge(rows, entries, Make);
            var first = rows[0];
            FileNodeViewModel.Merge(rows, [E("d", directory: true, day: 5)], Make);
            Assert.Same(first, rows[0]);
            Assert.Equal(5, rows[0].Modified!.Value.Day);
            // A file of another size is another row: what was asked about the one is not answered for the other.
            FileNodeViewModel.Merge(rows, [E("d", directory: true, day: 5), E("f", size: 5)], Make);
            var file = rows[1];
            FileNodeViewModel.Merge(rows, [E("d", directory: true, day: 5), E("f", size: 5)], Make);
            Assert.Same(file, rows[1]);
            FileNodeViewModel.Merge(rows, [E("d", directory: true, day: 5), E("f", size: 6)], Make);
            Assert.NotSame(file, rows[1]);
            Assert.Equal(6, rows[1].Size);
        }
        finally { Appearance.Apply(new StudioPreferences()); }
    }

    static int Listeners() => ((Delegate?)typeof(Appearance).GetField("Changed", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null))?.GetInvocationList().Length ?? 0;

    [AvaloniaFact]
    public async Task ClosedSessionsAndWindowsStopListening()
    {
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        Assert.NotNull(h.Model);
        int before = Listeners();
        var window = new MainWindow { Width = 1240, Height = 720, DataContext = h.Model };
        window.Show();
        var (_, w) = await Edit(h, fixture, "a.conf", "a = 1\n");
        Settle(window);
        Assert.True(Listeners() > before);
        h.Model.Close(w);
        Settle(window);
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, Listeners());
    }

    [AvaloniaFact]
    public async Task DownloadsGoWhereTheSettingsSay()
    {
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        string folder = Path.Combine(h.Data, "收件");
        Directory.CreateDirectory(folder);
        Appearance.Apply(new StudioPreferences { DownloadFolder = folder, NotifyTransfers = false });
        try
        {
            var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "回环主机"));
            Assert.Equal("下载到“收件”", w.DownloadHereHeader);
            var raised = new List<string?>();
            w.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            Appearance.Apply(Appearance.Current with { DownloadFolder = "" });
            Assert.Contains(nameof(WorkspaceViewModel.DownloadHereHeader), raised); // the menu follows
            Assert.Equal("下载到桌面", w.DownloadHereHeader);
            Appearance.Apply(Appearance.Current with { DownloadFolder = folder });
            string local = Path.Combine(h.Data, "a.txt");
            File.WriteAllText(local, "hello\n");
            await w.DropAsync("/", [local]);
            await Pump(() => w.FileRoots.Any(n => n.Name == "a.txt"), TimeSpan.FromSeconds(15));
            w.SelectedNode = w.FileRoots.First(n => n.Name == "a.txt");
            w.DownloadToDesktopCommand.Execute(null);
            await Pump(() => File.Exists(Path.Combine(folder, "a.txt")) && !w.Transfers!.HasActive, TimeSpan.FromSeconds(15));
            Assert.Equal("hello\n", File.ReadAllText(Path.Combine(folder, "a.txt")));
            await Pump(() => false, TimeSpan.FromMilliseconds(400));
            Assert.DoesNotContain(h.Interaction.Toasts, t => t.Text.Contains("已完成")); // not wanted
            Directory.Delete(folder, true);
            w.DownloadToDesktopCommand.Execute(null);
            await Pump(() => w.FileNotice.Contains("找不到默认下载目录"), TimeSpan.FromSeconds(3));
            Assert.Contains("找不到默认下载目录", w.FileNotice);
            Assert.Equal("", new StudioPreferences { DownloadFolder = "relative/path" }.Validated().DownloadFolder);
        }
        finally { Appearance.Apply(new StudioPreferences()); }
    }

    // ---------- layout, thresholds ----------

    [AvaloniaFact]
    public async Task NewSessionsShowTheColumnsThatWereChosen()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        Appearance.Apply(new StudioPreferences { OpenFiles = false, OpenMonitor = false });
        try
        {
            var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "一"));
            Assert.False(w.ShowFiles || w.ShowMonitor);
            Assert.True(w.ShowTerminal);
            int execs = fixture.Stats().GetProperty("execs").GetInt32();
            await Pump(() => false, TimeSpan.FromSeconds(1));
            Assert.Equal(0, execs); // a monitor that is not shown reads nothing
            w.ShowFiles = true;
            Assert.True(w.ShowFiles);
            // A file session is its files, whatever the settings say.
            var files = (await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xftp", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"])))!;
            Assert.True(files.ShowFiles);
            Appearance.Apply(new StudioPreferences());
            var second = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "二"));
            Assert.True(second.ShowFiles && second.ShowMonitor);
        }
        finally { Appearance.Apply(new StudioPreferences()); }
    }

    [AvaloniaFact]
    public void ThresholdsCanBeChosen()
    {
        string data = Paths.TempDirectory();
        var vm = new SettingsViewModel(data) { Section = "monitor" };
        try
        {
            Assert.Equal(MonitorLevel.Normal, MonitorFindings.Disk(80));
            vm.DiskWarning = 70;
            Assert.Equal(MonitorLevel.Warning, MonitorFindings.Disk(80));
            vm.DiskDanger = 75;
            Assert.Equal(MonitorLevel.Danger, MonitorFindings.Disk(80));
            vm.DiskWarning = 90; // beyond the serious one: that goes along
            Assert.Equal((90, 90), (vm.DiskWarning, vm.DiskDanger));
            vm.DiskDanger = 40;
            Assert.Equal((40, 40), (vm.DiskWarning, vm.DiskDanger));
            vm.MemoryWarning = 30;
            Assert.Equal(MonitorLevel.Warning, MonitorFindings.Memory(new MemoryReading(100, 80, 0, 20, 20, 0, 0)));
            vm.MemoryDanger = 40;
            Assert.Equal((40, 40), (vm.MemoryWarning, vm.MemoryDanger));
            vm.MemoryWarning = 10;
            Assert.Equal((10, 10), (vm.MemoryWarning, vm.MemoryDanger));
            vm.CpuDanger = 50;
            Assert.Equal(MonitorLevel.Danger, MonitorFindings.Cpu(60));
            vm.LoadWarning = 0.5;
            Assert.Equal(MonitorLevel.Warning, MonitorFindings.Load(5, 8));
            Assert.True(vm.LimitsChanged);
            Assert.Equal(40, Appearance.Load(data).Thresholds.DiskDanger);
            var window = new SettingsWindow { DataContext = vm, Width = 900, Height = 640 };
            window.Show();
            Shot(window, "settings-thresholds");
            window.Close();
            vm.ResetThresholdsCommand.Execute(null);
            Assert.False(vm.LimitsChanged);
            var odd = new MonitorThresholds { DiskWarning = double.NaN, CpuWarning = 500, CpuDanger = -5, MemoryWarning = -3, MemoryDanger = 80, LoadWarning = 0, LoadDanger = double.PositiveInfinity }.Validated();
            Assert.Equal((85, 95, 100, 100, 0, 0, 2, 4), (odd.DiskWarning, odd.DiskDanger, odd.CpuWarning, odd.CpuDanger, odd.MemoryWarning, odd.MemoryDanger, odd.LoadWarning, odd.LoadDanger));
        }
        finally { vm.Dispose(); Appearance.Apply(new StudioPreferences()); Directory.Delete(data, true); }
    }

    [AvaloniaFact]
    public void TheNewPagesAreShownAndFound()
    {
        string data = Paths.TempDirectory();
        var vm = new SettingsViewModel(data);
        var window = new SettingsWindow { DataContext = vm, Width = 900, Height = 700 };
        window.Show();
        try
        {
            foreach (string section in new[] { "editor", "files", "layout", "monitor", "keys", "backup" }) { vm.Section = section; Shot(window, "settings-" + section); }
            foreach (var (text, section) in new[] { ("隐藏", "files"), ("换行", "editor"), ("栏宽", "layout"), ("阈值", "monitor"), ("还原", "backup"), ("热键", "keys") })
            {
                vm.SearchText = text;
                Assert.False(vm.NoMatches, text);
                Assert.Equal(section, SettingsViewModel.Entries.First(e => vm.Shown[e.Key]).Section);
                vm.SearchText = "";
            }
            // Every card of the list is really there.
            Assert.Equal(SettingsViewModel.Entries.Length, SettingsViewModel.Entries.Select(e => e.Key).Distinct().Count());
            vm.Section = "editor";
            vm.EditorFontSize = 20;
            vm.EditorTab = 8;
            vm.EditorFont = SettingsViewModel.SameAsTerminal;
            vm.FileSort = "大小";
            vm.ShowHiddenFiles = false;
            vm.OpenMonitor = false;
            vm.NotifyTransfers = false;
            var saved = Appearance.Load(data);
            Assert.Equal((20, 8, "", "size", false, false, false), (saved.EditorFontSize, saved.EditorTab, saved.EditorFont, saved.FileSort, saved.ShowHidden, saved.OpenMonitor, saved.NotifyTransfers));
            Appearance.Apply(Appearance.Current with { EditorLanguages = new Dictionary<string, string> { [".conf"] = "Nginx", [".x"] = "nonsense" } });
            Assert.Contains(".conf → Nginx", vm.LanguagesText);
            Assert.DoesNotContain(".x", vm.LanguagesText);
            vm.ForgetLanguagesCommand.Execute(null);
            Assert.False(vm.HasLanguages);
        }
        finally { window.Close(); vm.Dispose(); Appearance.Apply(new StudioPreferences()); Directory.Delete(data, true); }
    }

    // ---------- backup ----------

    sealed class Source : IDisposable
    {
        public string Data { get; } = Paths.TempDirectory();
        public LibraryStore Library { get; }
        public QuickCommandStore Quick { get; }
        public Source() { Library = new LibraryStore(Data); Quick = new QuickCommandStore(Data); }
        public void Dispose() { try { Directory.Delete(Data, true); } catch (IOException) { } }
    }

    static Source Filled()
    {
        var s = new Source();
        var group = s.Library.AddGroup("生产", "");
        var inner = s.Library.AddGroup("华东", group.Id);
        var jump = new StudioProfile { Name = "跳板", Host = "198.51.100.1", User = "ops", HasPassword = true, GroupId = group.Id };
        var web = new StudioProfile
        {
            Name = "web", Host = "198.51.100.2", User = "root", GroupId = inner.Id, JumpChain = [jump.Id], InitialScript = "sudo -i", KeepAliveCommand = "true", KeepAliveCommandMs = 60000,
            SessionEnvironment = [new EnvironmentEntry("TOKEN", "s3cret")], HasPassword = true, UseCount = 7, LastUsed = DateTimeOffset.UtcNow,
        };
        s.Library.Upsert(jump);
        s.Library.Upsert(web);
        s.Library.UpsertTunnel(new TunnelDefinition { Name = "库", ProfileId = web.Id, LocalPort = 13306, RemotePort = 3306 });
        s.Quick.Upsert(new QuickCommand { Name = "磁盘", Text = "df -h" });
        s.Quick.Upsert(new QuickCommand { Name = "日志", Text = "tail -f x" });
        return s;
    }

    static readonly RestoreChoice Everything = new(true, true, true, false);

    [AvaloniaFact]
    public void ABackupHoldsNoSecretsAndRestoresWhatIsMissing()
    {
        using var a = Filled();
        using var b = new Source();
        string file = Path.Combine(a.Data, "backup.json");
        var preferences = new StudioPreferences { Theme = "深蓝", TerminalFontSize = 18, LocalShell = "/tmp/evil", DownloadFolder = Path.GetTempPath(), ConfirmPaste = false };
        StudioBackup.Write(file, StudioBackup.Create(preferences, a.Quick, a.Library, DateTimeOffset.Now));
        string text = File.ReadAllText(file);
        Assert.DoesNotContain("s3cret", text);
        Assert.DoesNotContain("TOKEN", text);
        Assert.DoesNotContain("\"hasPassword\": true", text);
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));

        // Something is there already: the same jump host, the same quick command, the group, a tunnel on the port.
        Appearance.Apply(new StudioPreferences { LocalShell = "/bin/mine" });
        var group = b.Library.AddGroup("生产", "");
        var there = new StudioProfile { Name = "跳板", Host = "198.51.100.1", User = "ops", Note = "我的", HasPassword = true, GroupId = group.Id };
        b.Library.Upsert(there);
        b.Library.UpsertTunnel(new TunnelDefinition { Name = "占用", ProfileId = there.Id, LocalPort = 13306, RemotePort = 80 });
        b.Quick.Upsert(new QuickCommand { Name = "磁盘", Text = "df -h" });
        Appearance.Save(b.Data, Appearance.Current);
        try
        {
            var backup = StudioBackup.Read(file);
            Assert.Equal((true, 2, 2, 1), (backup.HasSettings, backup.QuickCommandCount, backup.ConnectionCount, backup.TunnelCount));
            var preview = backup.Preview(Everything, b.Quick, b.Library);
            Assert.Equal(new RestoreSummary(true, 1, 1, 0, 1, 1, 1, 0, 1, 2), preview);
            Assert.Single(b.Library.Profiles); // a preview changes nothing
            var done = backup.Restore(Everything, b.Quick, b.Library, b.Data, DateTime.Now);
            Assert.Equal(preview, done);
            var web = b.Library.Profiles.Single(p => p.Name == "web");
            Assert.Equal("生产 / 华东", b.Library.GroupPath(web.GroupId));
            Assert.Equal(2, b.Library.Groups.Count); // not doubled
            Assert.Equal([there.Id], web.JumpChain); // the one that was there
            Assert.Equal("我的", b.Library.Find(there.Id)!.Note);
            Assert.True(b.Library.Find(there.Id)!.HasPassword);
            Assert.Equal(("", "", 0, false, 0), (web.InitialScript, web.KeepAliveCommand, web.KeepAliveCommandMs, web.HasPassword, web.UseCount));
            Assert.Empty(web.SessionEnvironment);
            Assert.Equal(["磁盘", "日志"], b.Quick.Commands.Select(c => c.Name));
            Assert.Equal(("深蓝", 18, "/bin/mine", "", true), (Appearance.Current.Theme, Appearance.Current.TerminalFontSize, Appearance.Current.LocalShell, Appearance.Current.DownloadFolder, Appearance.Current.ConfirmPaste));
            Assert.Equal("深蓝", Appearance.Load(b.Data).Theme);
            Assert.Equal("/bin/mine", System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Directory.GetFiles(b.Data, "preferences.before-restore-*.json").Single())).RootElement.GetProperty("LocalShell").GetString());
            // Once more: nothing is left to add.
            Assert.Equal(new RestoreSummary(false, 0, 2, 0, 0, 0, 2, 0, 1, 0), backup.Restore(Everything with { Settings = false }, b.Quick, b.Library, b.Data, DateTime.Now));
            // One's own backup, in an empty place: all of it.
            using var c = new Source();
            var trusted = backup.Restore(new RestoreChoice(true, true, true, true), c.Quick, c.Library, c.Data, DateTime.Now);
            Assert.Equal(new RestoreSummary(true, 2, 0, 0, 2, 2, 0, 1, 0, 0), trusted);
            Assert.Equal("sudo -i", c.Library.Profiles.Single(p => p.Name == "web").InitialScript);
            Assert.Equal(("/tmp/evil", false), (Appearance.Current.LocalShell, Appearance.Current.ConfirmPaste));
            Assert.Equal(c.Library.Profiles.Single(p => p.Name == "web").Id, c.Library.Tunnels.Single().ProfileId);
            // What is not ticked is not touched.
            using var d = new Source();
            Assert.Equal(new RestoreSummary(false, 0, 0, 0, 2, 2, 0, 1, 0, 1), backup.Restore(new RestoreChoice(false, false, true, false), d.Quick, d.Library, d.Data, DateTime.Now));
            Assert.Empty(d.Quick.Commands);
        }
        finally { Appearance.Apply(new StudioPreferences()); }
    }

    [AvaloniaFact]
    public void BackupsThatAreNotBackupsAreRefused()
    {
        using var a = Filled();
        string file = Path.Combine(a.Data, "x.json");
        string good = System.Text.Encoding.UTF8.GetString(StudioBackup.Create(new StudioPreferences(), a.Quick, a.Library, DateTimeOffset.Now));
        string Refused(string text) { File.WriteAllText(file, text); return Assert.ThrowsAny<Exception>(() => StudioBackup.Read(file)).Message; }
        foreach (string text in new[] { "", "{}", "[]", "null", "not json", "{\"format\":1}", "{\"format\":\"forterm-studio-connections\",\"version\":1}", "{\"format\":\"forterm-studio-backup\",\"version\":9}" }) Assert.NotEqual("", Refused(text));
        Assert.Contains("秘密字段", Refused(good.Replace("\"name\": \"web\"", "\"password\": \"x\", \"name\": \"web\"")));
        Assert.Contains("类型", Refused(good.Replace("\"port\": 22", "\"port\": \"22\"")));
        Refused(good.Replace("\"host\": \"198.51.100.2\"", "\"host\": \"a b/c\""));
        Refused(good.Replace("\"localPort\": 13306", "\"localPort\": 0"));
        Refused(good.Replace("\"text\": \"df -h\"", "\"text\": \"df\\u001b[2J\""));
        Refused(good.Replace("\"name\": \"web\"", "\"name\": null"));
        // Groups in a circle, a chain through a stranger.
        string id = a.Library.Groups[0].Id;
        Refused(good.Replace("\"parentId\": \"\"", $"\"parentId\": \"{a.Library.Groups[1].Id}\""));
        Refused(good.Replace(a.Library.Profiles[0].Id, "0123456789abcdef", StringComparison.Ordinal).Replace("\"id\": \"0123456789abcdef\"", "\"id\": \"fedcba9876543210\""));
        File.WriteAllText(file, good);
        Assert.Equal(2, StudioBackup.Read(file).ConnectionCount);
        // A file called "token" that was given a language is no secret.
        Appearance.Apply(new StudioPreferences { EditorLanguages = new Dictionary<string, string> { ["token"] = "YAML", [".seed"] = "Log" } });
        StudioBackup.Write(file, StudioBackup.Create(Appearance.Current, a.Quick, a.Library, DateTimeOffset.Now));
        Assert.True(StudioBackup.Read(file).HasSettings);
        Appearance.Apply(new StudioPreferences());
        // Environment variables put into a file by hand are not taken over, not even from one's own.
        File.WriteAllText(file, good.Replace("\"sessionEnvironment\": []", "\"sessionEnvironment\": [{\"name\":\"LD_PRELOAD\",\"value\":\"/tmp/x.so\"}]"));
        using (var target = new Source())
        {
            StudioBackup.Read(file).Restore(new RestoreChoice(false, false, true, true), target.Quick, target.Library, null, DateTime.Now);
            Assert.All(target.Library.Profiles, p => Assert.Empty(p.SessionEnvironment));
        }
        // Full: what does not fit is left out, and said; empty groups come along.
        using (var full = new Source())
        {
            full.Quick.AddRange(Enumerable.Range(0, QuickCommandStore.MaxCommands - 1).Select(i => new QuickCommand { Name = "c" + i, Text = "t" + i }).ToArray());
            a.Library.AddGroup("空分组", "");
            StudioBackup.Write(file, StudioBackup.Create(new StudioPreferences(), a.Quick, a.Library, DateTimeOffset.Now));
            var summary = StudioBackup.Read(file).Restore(new RestoreChoice(false, true, true, false), full.Quick, full.Library, null, DateTime.Now);
            Assert.Equal((1, 1), (summary.QuickCommands, summary.QuickLeft));
            Assert.Equal(QuickCommandStore.MaxCommands, full.Quick.Commands.Count);
            Assert.Contains(full.Library.Groups, g => g.Name == "空分组");
        }
        // Guards are not lowered by somebody's file.
        StudioBackup.Write(file, StudioBackup.Create(new StudioPreferences { RightClickMenu = false, CopyOnSelect = true, ConfirmPaste = false, Thresholds = new() { DiskWarning = 100, DiskDanger = 100 }, Shortcuts = new Dictionary<string, string> { ["palette"] = "" } }, a.Quick, a.Library, DateTimeOffset.Now));
        Assert.Equal(1, StudioBackup.Read(file).Restore(new RestoreChoice(true, false, false, false), a.Quick, a.Library, null, DateTime.Now).Stripped);
        Assert.Equal((true, false, true, 85, false), (Appearance.Current.RightClickMenu, Appearance.Current.CopyOnSelect, Appearance.Current.ConfirmPaste, Appearance.Current.Thresholds.DiskWarning, Shortcuts.Palette.IsOff));
        Appearance.Apply(new StudioPreferences());
        // Settings that make no sense become the defaults.
        var odd = System.Text.Json.Nodes.JsonNode.Parse(good)!;
        (odd["preferences"]!["uiScale"], odd["preferences"]!["theme"], odd["preferences"]!["shortcuts"]) = (99, "无", new System.Text.Json.Nodes.JsonObject { ["palette"] = null });
        File.WriteAllText(file, odd.ToJsonString());
        StudioBackup.Read(file).Restore(new RestoreChoice(true, false, false, true), a.Quick, a.Library, null, DateTime.Now);
        Assert.Equal((1.5, "暗夜"), (Appearance.Current.UiScale, Appearance.Current.Theme));
        Assert.Null(Appearance.Current.Shortcuts);
        Appearance.Apply(new StudioPreferences());
    }

    [AvaloniaFact]
    public async Task BackupAndRestoreOnTheSettingsPage()
    {
        using var a = Filled();
        using var h = new StudioHarness();
        var quick = new QuickCommandStore(h.Data);
        Appearance.Apply(new StudioPreferences { Theme = "墨黑" });
        var vm = new SettingsViewModel(h.Data, quick, h.Store, h.Interaction) { Section = "backup" };
        var window = new SettingsWindow { DataContext = vm, Width = 900, Height = 700 };
        window.Show();
        try
        {
            h.Interaction.SaveFile = Path.Combine(h.Data, "mine.json");
            vm.BackupCommand.Execute(null);
            await Pump(() => vm.HasBackupStatus, TimeSpan.FromSeconds(3));
            Assert.Contains("已备份到", vm.BackupStatus);
            Assert.True(StudioBackup.Read(h.Interaction.SaveFile).HasSettings);

            string file = Path.Combine(a.Data, "theirs.json");
            StudioBackup.Write(file, StudioBackup.Create(new StudioPreferences { Theme = "深蓝" }, a.Quick, a.Library, DateTimeOffset.Now));
            h.Interaction.OpenFile = file;
            vm.OpenBackupCommand.Execute(null);
            await Pump(() => vm.HasOpened, TimeSpan.FromSeconds(3));
            Assert.Contains("2 条快速命令，2 个连接，1 个隧道", vm.OpenedText);
            Assert.Contains("替换全部设置", vm.PlanText);
            Assert.Contains("不恢复", vm.PlanText);
            Assert.False(vm.RestoreTrusted); // never by itself
            Shot(window, "settings-restore");
            vm.RestoreSettings = false;
            Assert.DoesNotContain("设置", vm.PlanText);
            h.Interaction.ConfirmAnswer = false;
            vm.RestoreCommand.Execute(null);
            await Pump(() => h.Interaction.Confirms.Count == 1, TimeSpan.FromSeconds(2));
            Assert.Contains("密码和口令不在备份里", h.Interaction.Confirms[0]);
            Assert.Empty(h.Store.Profiles);
            h.Interaction.ConfirmAnswer = true;
            vm.RestoreCommand.Execute(null);
            await Pump(() => h.Store.Profiles.Count == 2, TimeSpan.FromSeconds(3));
            Assert.Equal(2, h.Store.Profiles.Count);
            Assert.Equal(2, quick.Commands.Count);
            Assert.Equal("墨黑", Appearance.Current.Theme);
            Assert.False(vm.HasOpened);
            Assert.Contains("已添加 2 条快速命令", vm.BackupStatus);
            // What cannot be read says so, and leaves nothing open.
            File.WriteAllText(file, "{}");
            vm.OpenBackupCommand.Execute(null);
            await Pump(() => vm.BackupStatus.Contains("没有读取"), TimeSpan.FromSeconds(3));
            Assert.Contains("不是 Forterm Studio 的备份文件", vm.BackupStatus);
            Assert.False(vm.HasOpened);
            // Without a library (another caller) the page only explains.
            using var bare = new SettingsViewModel(h.Data);
            Assert.False(bare.CanBackup);
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    // ---------- "空闲保活" ----------

    sealed class FakeTime : TimeProvider
    {
        long now = 1;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Volatile.Read(ref now);
        public void Pass(double minutes) => Interlocked.Add(ref now, (long)(minutes * 60_000));
    }

    [Fact]
    public void WhatIsSentByItselfDependsOnWhatWasSent()
    {
        var time = new FakeTime();
        var p = new StudioPreferences { IdleMinutes = 4, IdleHours = 2 };
        var keeper = new IdleKeeper(time);
        string? Due(StudioPreferences q, bool fullScreen = false, bool atPrompt = false) => keeper.Due(q, fullScreen, atPrompt).Text;
        string Held(StudioPreferences q, bool fullScreen = false, bool atPrompt = false) => keeper.Due(q, fullScreen, atPrompt).Held;
        Assert.Null(Due(p));
        time.Pass(3.9);
        Assert.Null(Due(p));
        Assert.Equal("", Held(p, fullScreen: true)); // not time yet: nothing is held back
        time.Pass(0.2);
        Assert.Equal(" \x7f", Due(p));
        Assert.Null(Due(p, fullScreen: true)); // not into vim
        Assert.Contains("全屏", Held(p, fullScreen: true));
        Assert.Equal(" \x7f", Due(p with { IdleFullScreen = true }, fullScreen: true));
        Assert.Equal("\0", Due(p with { IdleInput = "null" }));
        Assert.Equal(" \x7f", Due(p with { IdleInput = "nonsense" }));
        bool mine = keeper.Begin();
        keeper.End(gone: false, mine); // the server did not take it: still due, and as if nothing had been
        Assert.NotNull(Due(p));
        Assert.Equal((0, false), (keeper.Count, keeper.IsUnasked));
        mine = keeper.Begin();
        Assert.Equal(0, keeper.Count); // it has not gone out yet
        keeper.End(gone: true, mine);
        Assert.Equal((1, true), (keeper.Count, keeper.IsUnasked));
        Assert.Null(Due(p)); // counts for the server
        Assert.False(keeper.Begin()); // it already was
        keeper.End(gone: false, mine: false);
        Assert.True(keeper.IsUnasked);
        time.Pass(4);
        Assert.NotNull(Due(p));
        // What the user sends starts it over; what took long to go out, when it has.
        keeper.Sent("x"u8);
        Assert.Null(Due(p));
        keeper.Sent([]);
        time.Pass(4);
        Assert.NotNull(Due(p));
        keeper.Done();
        Assert.Null(Due(p));
        // Enter: not behind something half typed, and only at a prompt.
        var enter = p with { IdleInput = "enter" };
        time.Pass(5);
        Assert.Null(Due(enter, atPrompt: true));
        Assert.Contains("一半", Held(enter, atPrompt: true));
        foreach (var (bytes, empty) in new (byte[], bool)[]
        {
            ("ls\r"u8.ToArray(), true), ("ls"u8.ToArray(), false), ([3], true), ("a"u8.ToArray(), false),
            // Ctrl+D deletes a character and Ctrl+U what is before the cursor: the rest of the line stays.
            ([4], false), ([1], false), ([21], false), ("\r"u8.ToArray(), true), ([4], true), ([21], true),
            ("\x1b[A"u8.ToArray(), false), ("\r"u8.ToArray(), true), ("\x1bOA"u8.ToArray(), false), ("\n"u8.ToArray(), true), ("\x1b[3~"u8.ToArray(), false), ("\r"u8.ToArray(), true),
            // Keys, however they look.
            ("\x1b[Z"u8.ToArray(), false), ("\r"u8.ToArray(), true), ("\x1b[1;2P"u8.ToArray(), false), ("\r"u8.ToArray(), true), ("\x1b[97;5u"u8.ToArray(), false), ("\r"u8.ToArray(), true),
            ("\x1b[200~ls\n\x1b[201~"u8.ToArray(), false), ("\r"u8.ToArray(), true), ("\x1b]"u8.ToArray(), false), ("\r"u8.ToArray(), true), ("\x1bb"u8.ToArray(), false), ("\r"u8.ToArray(), true),
            ("\x1b[65;30;97;1;0;1_"u8.ToArray(), false), ("\r"u8.ToArray(), true), ("\x1b[Ix"u8.ToArray(), false), ("\r"u8.ToArray(), true), ("\x1b[24;80Rcat /mnt"u8.ToArray(), false), ("\r"u8.ToArray(), true),
            // Answers of the terminal, and the mouse.
            ("\x1b[?62;22c"u8.ToArray(), true), ("\x1b[>0;1;0c"u8.ToArray(), true), ("\x1b[I"u8.ToArray(), true), ("\x1b[O"u8.ToArray(), true), ("\x1b[24;80R"u8.ToArray(), true), ("\x1b[0n"u8.ToArray(), true), ("\x1b[?2026;2$y"u8.ToArray(), true), ("\x1b[4;2$y"u8.ToArray(), true),
            ("\x1b[8;24;80t"u8.ToArray(), true), ("\x1b[?0;1S"u8.ToArray(), true), ("\x1b[100*{"u8.ToArray(), true), ("\x1b[?1u"u8.ToArray(), true), ("\x1b[<0;1;1M"u8.ToArray(), true), ("\x1b[<0;1;1m"u8.ToArray(), true), ("\x1b[M !!"u8.ToArray(), true),
            ("\x1b]11;rgb:0/0/0\x1b\\"u8.ToArray(), true), ("\x1b]11;rgb:0/0/0\a"u8.ToArray(), true), ("\x1bP>|x\x1b\\"u8.ToArray(), true),
        })
        {
            keeper.Sent(bytes);
            time.Pass(5);
            Assert.True((Due(enter, atPrompt: true) == "\r") == empty, BitConverter.ToString(bytes));
            Assert.Null(Due(enter)); // elsewhere than at a prompt it may answer a question
        }
        Assert.Contains("提示符", Held(enter));
        // After the longest time without the user it stops, until there is one again.
        keeper.Sent("\r"u8);
        for (int i = 0; i < 29; i++) { time.Pass(4); Assert.NotNull(Due(p)); keeper.End(true, keeper.Begin()); }
        time.Pass(5);
        Assert.True(keeper.IsOver(p));
        Assert.Null(Due(p));
        Assert.Equal("", Held(p, fullScreen: true));
        keeper.Sent("\r"u8);
        Assert.False(keeper.IsOver(p));
        time.Pass(4);
        Assert.NotNull(Due(p));
        Assert.Equal(30, keeper.Count);
        // Only the user counts for that: not what a program asks the terminal, nor what the connection's settings write.
        foreach (var by in new Action[] { () => keeper.Sent("\x1b[24;80R"u8), () => keeper.Sent("\x1b[I"u8), () => keeper.Sent("uptime\r"u8, byUser: false) })
        {
            keeper.Sent("\r"u8);
            for (int i = 0; i < 9; i++) { time.Pass(15); by(); }
            Assert.True(keeper.IsOver(p));
        }
        keeper.Sent("\x1b[<0;1;1M"u8); // a click is
        Assert.False(keeper.IsOver(p));
        // Whose doing it is, if the server answers now.
        Assert.False(keeper.IsUnasked);
        keeper.Begin();
        Assert.True(keeper.IsUnasked);
        keeper.Sent("\x1b[24;80R"u8);
        Assert.True(keeper.IsUnasked);
        keeper.Sent("\r"u8);
        Assert.False(keeper.IsUnasked);
        keeper.Sent("uptime\r"u8, byUser: false);
        Assert.True(keeper.IsUnasked);
        Assert.Equal((1, 24, "space"), new StudioPreferences { IdleMinutes = -5, IdleHours = 999, IdleInput = "x" }.Validated() is var v ? (v.IdleMinutes, v.IdleHours, v.IdleInput) : default);
        Assert.False(new StudioPreferences().IdleKeepAlive); // never by itself
    }

    [Theory]
    [InlineData("root@web:~# ", true)]
    [InlineData("$ ", true)]
    [InlineData("# ", true)]
    [InlineData("% ", true)]
    [InlineData("[deploy@web-1 ~]$", true)]
    [InlineData("[deploy@web-1 my dir]$ ", true)]
    [InlineData("bash-5.1$ ", true)]
    [InlineData("web% ", true)]
    [InlineData("Router#", true)]
    [InlineData("Switch(config)#", true)]
    [InlineData("(venv) deploy@web:~/app$ ", true)]
    [InlineData("deploy@web:/srv/my app$ ", true)]
    [InlineData("root@web /etc # ", true)]
    [InlineData("web ~ # ", true)]
    [InlineData("  host$ ", true)]
    [InlineData("postgres=# ", true)]
    [InlineData("host:/opt/a_$ ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("root@web:~# ls", false)]
    [InlineData("Do you want to continue? [Y/n] ", false)]
    [InlineData("Is this ok [y/N]: ", false)]
    [InlineData("[sudo] password for deploy: ", false)]
    [InlineData("> ", false)]
    [InlineData("quote> ", false)]
    [InlineData("❯ ", true)]
    [InlineData("? Pick one ❯ ", false)]
    [InlineData("<a b>", false)]
    [InlineData("<y/n>", false)]
    [InlineData("<HUAWEI>", false)]
    [InlineData("--More--", false)]
    [InlineData("  45%", false)]
    [InlineData("file.iso  45 %", false)]
    [InlineData("45 %", false)]
    [InlineData("##########", false)]
    [InlineData("[#####", false)]
    [InlineData("$$", false)]
    [InlineData("%%", false)]
    [InlineData("(Reading database ... ", false)]
    [InlineData("Login: ", false)]
    [InlineData("[continue? yes@no]# ", false)]
    [InlineData("mail sent to a@b now# ", false)]
    [InlineData("postgres-# ", false)]
    [InlineData("postgres(# ", false)]
    [InlineData("postgres'# ", false)]
    [InlineData("postgres\"# ", false)]
    [InlineData("postgres$# ", false)]
    [InlineData("postgres*# ", false)]
    [InlineData("Select item # ", false)]
    [InlineData("Amount in US$ ", false)]
    [InlineData("Really delete user@host:/srv? # ", false)]
    [InlineData("Enter a number for root@web # or #", false)]
    [InlineData("price: 5 $", false)]
    [InlineData("do it now $ ", false)]
    [InlineData("Overwrite root@web:~/x and go on #", false)]
    public void OnlyAPromptLooksLikeOne(string line, bool prompt) => Assert.Equal(prompt, StudioTerminalControl.LooksLikePrompt(line));

    /// <summary>A server that takes nothing until it is told to, and notices two writers at once.</summary>
    sealed class SlowPty : Porta.Pty.IPtyConnection
    {
        public readonly System.Text.StringBuilder Got = new();
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Held, Overlapped, Ended, Broken;
        int writers;
        sealed class Out(SlowPty pty) : MemoryStream
        {
            async Task Take(string? text)
            {
                if (Interlocked.Increment(ref pty.writers) > 1) pty.Overlapped = true;
                try
                {
                    if (pty.Broken) throw new IOException("broken");
                    if (pty.Held) await pty.Release.Task.ConfigureAwait(false);
                    if (text != null) lock (pty.Got) pty.Got.Append(text);
                }
                finally { Interlocked.Decrement(ref pty.writers); }
            }
            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => await Take(System.Text.Encoding.UTF8.GetString(buffer.Span));
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Take(System.Text.Encoding.UTF8.GetString(buffer, offset, count));
            public override Task FlushAsync(CancellationToken ct) => Take(null);
        }
        public string Text { get { lock (Got) return Got.ToString(); } }
        public Stream ReaderStream { get; } = new MemoryStream();
        public Stream WriterStream { get; }
        public SlowPty() => WriterStream = new Out(this);
        public bool SupportsCancellableRead => true;
        public int Pid => 0;
        public int ExitCode => -1;
        public event EventHandler<Porta.Pty.PtyExitedEventArgs>? ProcessExited { add { } remove { } }
        public void Resize(int cols, int rows) { }
        public bool WaitForExit(int milliseconds) => Ended;
        public void Kill() { }
        public void Dispose() { }
    }

    [Fact]
    public async Task EverythingSentFromATerminalTakesItsTurn()
    {
        var time = new FakeTime();
        var keeper = new IdleKeeper(time);
        var pty = new SlowPty { Held = true };
        var watch = new IdleWatch(pty, keeper);
        int asked = 0;
        Task<bool> Beat() => watch.BeatAsync(() => { asked++; return " \x7f"; }, CancellationToken.None);
        // A paste the server does not take: nothing is sent on top of it, and nothing waits behind it.
        var paste = watch.WriterStream.WriteAsync("paste;"u8.ToArray()).AsTask();
        time.Pass(60);
        Assert.False(await Beat());
        Assert.False(await Beat());
        Assert.Equal(0, asked);
        // What the user and the settings send waits for its turn.
        var command = watch.SendAsync("cd /x\r"u8.ToArray(), byUser: true, CancellationToken.None);
        var key = watch.WriterStream.WriteAsync("k"u8.ToArray()).AsTask();
        using (var gaveUp = new CancellationTokenSource(50))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watch.SendAsync("never"u8.ToArray(), true, gaveUp.Token));
        Assert.Equal("", pty.Text);
        Assert.False(command.IsCompleted || key.IsCompleted);
        pty.Held = false;
        pty.Release.SetResult();
        await Task.WhenAll(paste, command, key).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("paste;cd /x\rk", pty.Text);
        Assert.Equal(0, keeper.Count);
        Assert.Null(keeper.Last);
        Assert.Null(keeper.Due(new StudioPreferences { IdleMinutes = 4 }, false, false).Text); // counted from when it had gone out
        // Now it is free: one, and counted.
        Assert.True(await Beat());
        Assert.Equal((1, "paste;cd /x\rk \x7f"), (keeper.Count, pty.Text));
        Assert.True(keeper.IsUnasked);
        // Nothing due any more: nothing.
        Assert.False(await watch.BeatAsync(() => null, CancellationToken.None));
        // One of its own that the server does not take: no second one, the user's keys wait, and none is sent twice.
        pty.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pty.Held = true;
        var own = Beat();
        Assert.False(await Beat());
        var after = watch.WriterStream.WriteAsync("u"u8.ToArray()).AsTask();
        Assert.Equal(1, keeper.Count);
        pty.Held = false;
        pty.Release.SetResult();
        Assert.True(await own.WaitAsync(TimeSpan.FromSeconds(5)));
        await after.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("paste;cd /x\rk \x7f \x7fu", pty.Text);
        Assert.Equal(2, keeper.Count);
        Assert.False(pty.Overlapped);
        // It counts from when it has gone out; one that the shell did not outlast does not count.
        pty.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pty.Held = true;
        own = Beat();
        time.Pass(60);
        pty.Held = false;
        pty.Release.SetResult();
        Assert.True(await own.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(keeper.Due(new StudioPreferences { IdleMinutes = 4 }, false, false).Text);
        pty.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pty.Held = true;
        own = Beat();
        pty.Ended = true;
        pty.Held = false;
        pty.Release.SetResult();
        Assert.False(await own.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(3, keeper.Count);
        pty.Ended = false;
        // What is written and then waits to be flushed counts from when it has been.
        await watch.WriterStream.WriteAsync("w"u8.ToArray());
        time.Pass(60);
        await watch.WriterStream.FlushAsync();
        Assert.Null(keeper.Due(new StudioPreferences { IdleMinutes = 4 }, false, false).Text);
        // Synchronous writers, too.
        watch.WriterStream.Write("s"u8.ToArray(), 0, 1);
        watch.WriterStream.Flush();
        await watch.WriterStream.FlushAsync();
        Assert.True(await Beat());
        // Into a shell that has ended: not sent, not counted.
        string whole = pty.Text;
        pty.Ended = true;
        Assert.False(await Beat());
        Assert.Equal((4, whole), (keeper.Count, pty.Text));
        // An error leaves nothing locked.
        pty.Ended = false;
        pty.Broken = true;
        await Assert.ThrowsAsync<IOException>(Beat);
        await Assert.ThrowsAsync<IOException>(() => watch.SendAsync("x"u8.ToArray(), true, CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(async () => await watch.WriterStream.WriteAsync("x"u8.ToArray()));
        await Assert.ThrowsAsync<IOException>(() => watch.WriterStream.FlushAsync());
        pty.Broken = false;
        Assert.True(await Beat());
        Assert.False(pty.Overlapped);
    }

    static bool Shows(StudioTerminalControl t, string text)
    {
        var lines = t.Terminal.Buffer.Lines;
        for (int i = 0; i < lines.Length; i++) if (lines[i]?.TranslateToString(true).Contains(text, StringComparison.Ordinal) == true) return true;
        return false;
    }

    static string Typed(LoopbackFixture fixture) => System.Text.Encoding.UTF8.GetString(fixture.Stats().GetProperty("input").EnumerateArray().SelectMany(x => Convert.FromHexString(x.GetString()!)).ToArray());

    [AvaloniaFact]
    public async Task IdleSessionsAreKeptOnlyWhenTheUserWantsIt()
    {
        Appearance.Apply(new StudioPreferences { CursorBlink = false });
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1240, Height = 720, DataContext = h.Model };
        window.Show();
        try
        {
            var time = new FakeTime();
            async Task<WorkspaceViewModel> Ready(WorkspaceViewModel w) { await Pump(() => w.Terminal?.IsLive == true, TimeSpan.FromSeconds(10)); Settle(window); return w; }
            var launched = await Ready((await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-newtab", "唤醒", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"])))!);
            void Clock(WorkspaceViewModel w) { w.Time = time; foreach (var t in w.Terminals) t.UseClockForTest(time); }
            Clock(launched);
            async Task Expect(string all, double minutes)
            {
                bool more = Typed(fixture) != all;
                time.Pass(minutes);
                h.Model.IdleTick();
                await Pump(() => more && Typed(fixture) == all, more ? TimeSpan.FromSeconds(8) : TimeSpan.FromMilliseconds(250));
                Assert.Equal(all, Typed(fixture));
            }

            // Off, as it comes: nothing, however long.
            Assert.False(launched.IsIdleOn || launched.ShowsIdle);
            await Expect("", 60);
            // On: after the time, once per time, whoever opened the session.
            Appearance.Apply(Appearance.Current with { IdleKeepAlive = true });
            Assert.True(launched.ShowsIdle);
            h.Interaction.ConfirmAnswer = false;
            launched.CdToTerminalCommand.Execute(null);
            Assert.Contains("空闲保活除外", h.Interaction.Confirms.Last());
            h.Interaction.ConfirmAnswer = true;
            await Expect(" \x7f", 0);
            await Expect(" \x7f", 1);
            await Expect(" \x7f", 2);
            await Expect(" \x7f \x7f", 1.1);
            Assert.Contains("2 次", launched.IdleText);
            Shot(window, "idle-keepalive");
            // Keys put it off; shortcuts of the window, which send nothing, do not.
            launched.Terminal!.FocusTerminal();
            window.KeyTextInput("ls");
            await Pump(() => Typed(fixture).EndsWith("ls", StringComparison.Ordinal), TimeSpan.FromSeconds(3));
            string before = Typed(fixture);
            await Expect(before, 3.5);
            Key_(window, Shortcuts.ZoomIn.Gesture.Key, Shortcuts.ZoomIn.Gesture.KeyModifiers);
            await Expect(before + " \x7f", 0.6);
            before = Typed(fixture);
            // A full-screen program is left alone.
            launched.Terminal.Terminal.Write("\x1b[?1049h");
            await Expect(before, 10);
            Appearance.Apply(Appearance.Current with { IdleFullScreen = true });
            await Expect(before + " \x7f", 0);
            launched.Terminal.Terminal.Write("\x1b[?1049l");
            Appearance.Apply(Appearance.Current with { IdleFullScreen = false });
            before = Typed(fixture);
            // Off for this session only, and on again.
            launched.ToggleIdleCommand.Execute(null);
            Assert.False(launched.ShowsIdle);
            Assert.Contains("打开", launched.IdleHeader);
            await Expect(before, 30);
            Appearance.Apply(Appearance.Current with { IdleKeepAlive = false });
            launched.ToggleIdleCommand.Execute(null); // only here
            Assert.True(launched.IsIdleOn);
            await Expect(before + " \x7f", 0);
            // The local shell is never kept; nor is a session that is gone.
            var local = WorkspaceViewModel.ForLocal(h.Interaction, h.KnownHosts);
            local.ToggleIdleCommand.Execute(null);
            Assert.False(local.CanIdle || local.IsIdleOn);
            local.Dispose();
            // Too long without the user.
            Appearance.Apply(Appearance.Current with { IdleHours = 1 });
            before = Typed(fixture);
            await Expect(before, 61);
            Assert.Contains("已到时限", launched.IdleText);
            window.KeyTextInput("\r");
            await Pump(() => Typed(fixture).Length > before.Length, TimeSpan.FromSeconds(3));
            before = Typed(fixture);
            await Expect(before + " \x7f", 4.1);
            // Every terminal of the session, also one that is not in front.
            await launched.AddTerminalAsync();
            await Pump(() => launched.Terminals.Count == 2 && launched.Terminals[1].HasConnection, TimeSpan.FromSeconds(5));
            Clock(launched);
            before = Typed(fixture);
            await Expect(before + " \x7f \x7f", 4.1);
            fixture.Dispose();
            await Pump(() => !launched.IsConnected, TimeSpan.FromSeconds(20));
            Assert.False(launched.ShowsIdle);
            time.Pass(60);
            h.Model.IdleTick(); // nothing, and no error
            await Pump(() => false, TimeSpan.FromMilliseconds(200));
            Assert.DoesNotContain("空闲保活", launched.Status);
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    /// <summary>A connection that can say whether input goes out just now.</summary>
    sealed class Direct : IImmediateInput
    {
        public readonly List<string> Sent = [];
        public bool Full { get; set; }
        public bool IsEnded { get; set; }
        public Action? Sending { get; set; }
        public bool TrySend(ReadOnlySpan<byte> bytes) { Sending?.Invoke(); if (Full || IsEnded) return false; lock (Sent) Sent.Add(System.Text.Encoding.ASCII.GetString(bytes)); return true; }
    }

    [Fact]
    public async Task ABeatGoesOutNowOrNotAtAll()
    {
        var time = new FakeTime();
        var keeper = new IdleKeeper(time);
        var pty = new SlowPty();
        var direct = new Direct { Full = true };
        var watch = new IdleWatch(pty, keeper, direct);
        Task<bool> Beat() => watch.BeatAsync(() => "\r", CancellationToken.None);
        keeper.Sent("x"u8);
        time.Pass(10);
        Assert.False(await Beat());
        Assert.False(await Beat());
        Assert.Equal((0, false, 0, ""), (keeper.Count, keeper.IsUnasked, direct.Sent.Count, pty.Text));
        Assert.NotNull(keeper.Due(new StudioPreferences { IdleMinutes = 4 }, false, false).Text); // still due
        // When the server reads again, none of those comes out late.
        direct.Full = false;
        await watch.SendAsync("n"u8.ToArray(), byUser: true, CancellationToken.None);
        Assert.Equal(("n", 0), (pty.Text, direct.Sent.Count));
        direct.Sending = () => Assert.True(keeper.IsUnasked); // before the server can answer
        Assert.True(await Beat());
        Assert.Equal((1, "\r"), (keeper.Count, string.Concat(direct.Sent)));
        Assert.Equal("n", pty.Text); // not a second time another way
        direct.Sending = () => direct.IsEnded = true;
        Assert.False(await Beat());
        Assert.True(watch.IsEnded);
        Assert.False(await Beat());
        Assert.Equal(1, keeper.Count);
    }

    [AvaloniaFact]
    public async Task AfterAnErrorATerminalIsNotKeptAnyMore()
    {
        var p = new StudioPreferences { IdleKeepAlive = true, IdleMinutes = 4 };
        var time = new FakeTime();
        var pty = new SlowPty { Broken = true };
        var tab = new TerminalTabViewModel("终端", new StudioTerminalControl()) { Time = time };
        Assert.False(tab.IsIdleKept);
        Assert.False(await tab.KeepAliveWhenIdleAsync(p, default)); // no connection yet
        tab.Attach(pty);
        Assert.True(tab.IsIdleKept);
        time.Pass(5);
        Assert.False(await tab.KeepAliveWhenIdleAsync(p with { IdleInput = "enter" }, default));
        Assert.NotEqual("", tab.IdleHeld);
        await Assert.ThrowsAsync<IOException>(() => tab.KeepAliveWhenIdleAsync(p, default));
        Assert.False(tab.IsIdleKept);
        pty.Broken = false;
        time.Pass(5);
        Assert.False(await tab.KeepAliveWhenIdleAsync(p, default));
        Assert.Equal(("", "", 0), (pty.Text, tab.IdleHeld, tab.Idle!.Count));
        // What the user and the program type still goes there.
        await tab.WriteAsync("cd /目录\r", byUser: true, default);
        Assert.Equal("cd /目录\r", pty.Text);
        tab.Dispose();
        Assert.Null(tab.Idle);
        await tab.WriteAsync("gone", byUser: true, default); // to nobody
        Assert.Equal("cd /目录\r", pty.Text);
    }

    /// <summary>The initial script and the keep-alive command of a connection count for the server, not as the user.</summary>
    [AvaloniaFact]
    public async Task WhatTheSettingsWriteIsNotTheUser()
    {
        Appearance.Apply(new StudioPreferences { CursorBlink = false, IdleKeepAlive = true, IdleMinutes = 4, IdleHours = 1 });
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1240, Height = 720, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "设置") with { DisablePwdFollow = true, DisableListMonitor = true, InitialScript = "cd /srv\nls", KeepAliveCommand = "uptime", KeepAliveCommandMs = 5000 });
            await Pump(() => w.Terminal?.IsLive == true && Typed(fixture).Length > 0, TimeSpan.FromSeconds(10));
            var tab = w.Terminals[0];
            Assert.Equal("cd /srv\rls\r", Typed(fixture));
            Assert.True(tab.Idle!.IsUnasked);
            w.CdToTerminalCommand.Execute(null);
            await Pump(() => Typed(fixture).EndsWith("cd /\r", StringComparison.Ordinal), TimeSpan.FromSeconds(3));
            Assert.False(tab.Idle.IsUnasked); // a command the user chose is
            var time = new FakeTime();
            tab.UseClockForTest(time);
            time.Pass(61);
            Assert.True(tab.Idle.IsOver(Appearance.Current));
            string before = Typed(fixture);
            await Pump(() => Typed(fixture).Length > before.Length, TimeSpan.FromSeconds(8));
            Assert.Equal(before + "uptime\r", Typed(fixture));
            await Pump(() => tab.Idle.IsUnasked, TimeSpan.FromSeconds(1));
            Assert.True(tab.Idle.IsUnasked && tab.Idle.IsOver(Appearance.Current));
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    /// <summary>The connection's timeout is for requests. What is typed waits for the server, is sent once, and nothing
    /// that waited is sent again with what comes next; nor does an Enter come out late.</summary>
    [AvaloniaFact]
    public async Task WhatIsTypedIsSentOnceHoweverLongTheServerTakes()
    {
        Appearance.Apply(new StudioPreferences { CursorBlink = false, IdleKeepAlive = true, IdleMinutes = 4, IdleInput = "enter" });
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_WINDOW"] = "32768", ["FIXTURE_HOLD"] = "1", ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1240, Height = 720, DataContext = h.Model };
        window.Show();
        try
        {
            var time = new FakeTime();
            var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "慢") with { DisablePwdFollow = true, DisableListMonitor = true, TimeoutMs = 5000 });
            await Pump(() => w.Terminal?.IsLive == true && w.HasFiles, TimeSpan.FromSeconds(10));
            foreach (var t in w.Terminals) t.UseClockForTest(time);
            // Exactly what the server lets us send ahead: it is sent, and the server reads none of it.
            string text = string.Concat(Enumerable.Range(0, 1638).Select(i => $"interface vlan {i:00000}")) + "12345678";
            Assert.Equal(32768, text.Length);
            await w.Terminal!.SendInputAsync(text).WaitAsync(TimeSpan.FromSeconds(5));
            w.Terminals[0].Idle!.Sent("\r"u8);
            w.Terminal.Terminal.Write("\r\nroot@switch:~# ");
            for (int i = 0; i < 3; i++)
            {
                time.Pass(4.5);
                h.Model.IdleTick();
                await Pump(() => false, TimeSpan.FromMilliseconds(150));
            }
            Assert.Equal(0, w.Terminals[0].Idle!.Count);
            Assert.Contains("没有在接收", w.IdleTip);
            // More, for longer than the timeout.
            string more = string.Concat(Enumerable.Range(0, 900).Select(i => $"vlan {i:00000}\r"));
            var sending = w.Terminal.SendInputAsync(more);
            await Pump(() => sending.IsCompleted, TimeSpan.FromSeconds(7));
            Assert.False(sending.IsCompleted);
            Assert.True(w.IsConnected && w.Terminal.IsLive);
            fixture.Command("release-shell");
            await Pump(() => Typed(fixture).Length >= text.Length + more.Length, TimeSpan.FromSeconds(10));
            w.Terminal.Terminal.Write("\r\nOverwrite? [Y/n] ");
            await w.Terminal.SendInputAsync("n");
            await Pump(() => false, TimeSpan.FromMilliseconds(500));
            Assert.Equal(text + more + "n", Typed(fixture));
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    /// <summary>The channel itself: once, in order, without a limit of time, and no more after it has ended.</summary>
    [Fact]
    public async Task InputGoesIntoTheChannelOnceAndInOrder()
    {
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_WINDOW"] = "1000", ["FIXTURE_HOLD"] = "1" });
        using var ssh = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), CancellationToken.None, temporary: true, options: new SessionConnectOptions { ConnectTimeoutSeconds = 5 });
        using var old = await ssh.OpenShellAsync(80, 24, CancellationToken.None, "xterm", []);
        Assert.IsType<Renci.SshNet.ShellStream>(old.WriterStream); // as it was, for who does not ask for flow control
        Assert.False(old.TrySend("x"u8));
        using var shell = await ssh.OpenShellAsync(80, 24, CancellationToken.None, "xterm", [], flowControl: true);
        var writer = shell.WriterStream;
        await writer.WriteAsync(new byte[0]);
        Assert.False(shell.TrySend(default));
        await writer.WriteAsync(System.Text.Encoding.ASCII.GetBytes(new string('a', 995)));
        Assert.True(shell.TrySend("bb"u8));
        Assert.False(shell.TrySend("cccc"u8)); // three would fit; all or nothing
        Assert.True(shell.TrySend("ddd"u8));
        Assert.False(shell.TrySend("e"u8));
        // One waits, the next after it; one that is given up sends nothing later.
        using var stop = new CancellationTokenSource(300);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await writer.WriteAsync("given up"u8.ToArray(), stop.Token));
        var first = writer.WriteAsync(System.Text.Encoding.ASCII.GetBytes(new string('f', 2500))).AsTask();
        var second = Task.Run(() => { writer.Write("gg"u8.ToArray(), 0, 2); writer.Flush(); });
        await Task.Delay(6000); // the connection's timeout, and more
        Assert.False(first.IsCompleted || second.IsCompleted);
        Assert.False(shell.TrySend("h"u8)); // not past those that wait
        Assert.False(shell.IsEnded);
        fixture.Command("release-shell");
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        for (int i = 0; i < 50 && Typed(fixture).Length < 3502; i++) await Task.Delay(50);
        Assert.Equal(new string('a', 995) + "bb" + "ddd" + new string('f', 2500) + "gg", Typed(fixture));
        Assert.True(shell.TrySend("i"u8));
        // Ended by the server: nothing more, and no error — as for what is typed into a shell that has exited.
        fixture.Command("close-shell");
        for (int i = 0; i < 100 && !shell.IsEnded; i++) await Task.Delay(50);
        Assert.True(shell.IsEnded);
        Assert.False(shell.TrySend("j"u8));
        await writer.WriteAsync("k"u8.ToArray()).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        await writer.FlushAsync();
    }

    /// <summary>Closing a tab ends what waits in it at once.</summary>
    [Fact]
    public async Task ClosingEndsInputThatWaits()
    {
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_WINDOW"] = "100", ["FIXTURE_HOLD"] = "1" });
        using var ssh = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), CancellationToken.None, temporary: true);
        var shell = await ssh.OpenShellAsync(80, 24, CancellationToken.None, "xterm", [], flowControl: true);
        var waiting = shell.WriterStream.WriteAsync(new byte[5000]).AsTask();
        await Task.Delay(300);
        Assert.False(waiting.IsCompleted);
        shell.Dispose();
        await waiting.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(shell.IsEnded);
        Assert.True(ssh.IsConnected);
        // The connection itself gone.
        var last = await ssh.OpenShellAsync(80, 24, CancellationToken.None, "xterm", [], flowControl: true);
        waiting = last.WriterStream.WriteAsync(new byte[5000]).AsTask();
        await Task.Delay(300);
        fixture.Dispose();
        await Task.WhenAny(waiting, Task.Delay(10_000));
        Assert.True(waiting.IsCompleted);
        last.Dispose();
    }

    [Fact]
    public async Task ATelnetConnectionSaysWhenItHasEnded()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var accepting = listener.AcceptTcpClientAsync();
            using var telnet = await TelnetPtyConnection.ConnectAsync("127.0.0.1", ((System.Net.IPEndPoint)listener.LocalEndpoint).Port, CancellationToken.None, flowControl: true);
            using var server = await accepting;
            Assert.False(telnet.IsEnded);
            Assert.True(telnet.TrySend(" \x7f"u8));
            var got = new byte[8];
            Assert.Equal(2, await server.GetStream().ReadAsync(got).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
            await server.GetStream().WriteAsync("never shown"u8.ToArray());
            server.Close();
            // Although nobody has read what it sent.
            for (int i = 0; i < 100 && !telnet.IsEnded; i++) await Task.Delay(30);
            Assert.True(telnet.IsEnded);
            Assert.False(telnet.TrySend(" "u8));
            var watch = new IdleWatch(telnet, new IdleKeeper(TimeProvider.System), telnet);
            Assert.False(await watch.BeatAsync(() => "x", CancellationToken.None));
        }
        finally { listener.Stop(); }
    }

    /// <summary>The tenth channel of a connection used to wait for ever; how many there may be is up to the server.</summary>
    [AvaloniaFact]
    public async Task ManyTerminalsOfOneSession()
    {
        Appearance.Apply(new StudioPreferences { CursorBlink = false });
        Assert.Equal(10, new SessionConnectOptions().MaxChannels); // as it was, for who does not ask
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionConnectOptions { MaxChannels = 0 }.Validate());
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var saved = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "许多") with { DisablePwdFollow = true, DisableListMonitor = true });
        var launched = (await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-newtab", "唤醒", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"])))!;
        foreach (var w in new[] { saved, launched })
        {
            await Pump(() => w.Terminals.Count == 1 && w.Terminals[0].HasConnection && w.HasFiles, TimeSpan.FromSeconds(10));
            for (int i = 0; i < 12; i++) Assert.NotNull(await w.AddTerminalAsync().WaitAsync(TimeSpan.FromSeconds(8)));
            Assert.Equal(13, w.Terminals.Count(t => t.HasConnection));
        }
    }

    /// <summary>A paste that a slow device takes its time for: nothing is put into it, and it arrives once. The
    /// commands the program types wait behind it.</summary>
    [AvaloniaFact]
    public async Task ASlowServerGetsAPasteOnceAndWhole()
    {
        foreach (string input in new[] { "space", "enter" })
        {
            Appearance.Apply(new StudioPreferences { CursorBlink = false, IdleKeepAlive = true, IdleMinutes = 4, IdleInput = input });
            using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_WINDOW"] = "32768", ["FIXTURE_HOLD"] = "1", ["FIXTURE_POSIX"] = "1" });
            using var h = new StudioHarness();
            var window = new MainWindow { Width = 1240, Height = 720, DataContext = h.Model };
            window.Show();
            try
            {
                var time = new FakeTime();
                var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "慢") with { DisablePwdFollow = true, DisableListMonitor = true });
                await Pump(() => w.Terminal?.IsLive == true && w.HasFiles, TimeSpan.FromSeconds(10));
                w.Time = time;
                foreach (var t in w.Terminals) t.UseClockForTest(time);
                w.Terminal!.Terminal.Write("root@switch:~# ");
                string text = string.Concat(Enumerable.Range(0, 1900).Select(i => $"interface vlan {i:00000}\r"));
                var sending = w.Terminal.SendInputAsync(text);
                await Pump(() => sending.IsCompleted, TimeSpan.FromMilliseconds(600));
                Assert.False(sending.IsCompleted); // held up
                w.CdToTerminalCommand.Execute(null);
                for (int i = 0; i < 3; i++)
                {
                    time.Pass(4.5);
                    h.Model.IdleTick();
                    await Pump(() => false, TimeSpan.FromMilliseconds(120));
                }
                Assert.Equal(0, w.Terminals[0].Idle!.Count);
                Assert.Contains("暂缓", w.IdleText);
                fixture.Command("release-shell");
                string want = text + "cd /\r";
                await Pump(() => Typed(fixture).Length >= want.Length, TimeSpan.FromSeconds(10));
                await Pump(() => false, TimeSpan.FromMilliseconds(400));
                string got = Typed(fixture);
                Assert.Equal(want.Length, got.Length);
                Assert.Equal(want, got);
                Assert.True(sending.IsCompleted);
                // It has only just been sent: the next one is due later.
                h.Model.IdleTick();
                await Pump(() => false, TimeSpan.FromMilliseconds(150));
                Assert.Equal(want, Typed(fixture));
                Assert.Equal("", w.Terminals[0].IdleHeld);
            }
            finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
        }
    }

    [AvaloniaFact]
    public async Task SessionsThatWereNeverShownAreKeptAndEndedShellsAreNot()
    {
        Appearance.Apply(new StudioPreferences { CursorBlink = false, IdleKeepAlive = true, IdleMinutes = 4 });
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1240, Height = 720, DataContext = h.Model };
        window.Show();
        try
        {
            var time = new FakeTime();
            var opening = new[] { "一", "二" }.Select(n => h.Model.OpenProfileAsync(h.Ssh(fixture.Port, n) with { DisablePwdFollow = true, DisableListMonitor = true })).ToArray();
            var both = await Task.WhenAll(opening);
            await Pump(() => both.All(w => w.Terminals.All(t => t.HasConnection)) && both[1].Terminal?.IsLive == true, TimeSpan.FromSeconds(10));
            foreach (var w in both) { w.Time = time; foreach (var t in w.Terminals) t.UseClockForTest(time); }
            Assert.True(both[0].Terminal!.HasPendingConnection); // not shown yet
            string status = both[0].Status;
            time.Pass(4.5);
            h.Model.IdleTick();
            await Pump(() => Typed(fixture).Length >= 4, TimeSpan.FromSeconds(3));
            Assert.Equal(" \x7f \x7f", Typed(fixture));
            Assert.Equal(status, both[0].Status);
            Assert.All(both, w => Assert.Contains("1 次", w.IdleText));
            // Its screen is not known, so Enter is not sent into it.
            Appearance.Apply(Appearance.Current with { IdleInput = "enter" });
            time.Pass(4.5);
            h.Model.IdleTick();
            await Pump(() => false, TimeSpan.FromMilliseconds(200));
            Assert.Equal(" \x7f \x7f", Typed(fixture));
            Assert.Contains("暂缓", both[0].IdleText);
            Assert.Contains("提示符", both[0].IdleTip);
            Appearance.Apply(Appearance.Current with { IdleInput = "space" });
            // A shell that has ended, while its session goes on: nothing to keep, and nothing counted.
            var ended = both[1]; // the last shell that was opened may be either's
            fixture.Command("close-shell");
            await Pump(() => both.Any(w => !w.Terminals[0].IsIdleKept), TimeSpan.FromSeconds(5));
            ended = both.Single(w => !w.Terminals[0].IsIdleKept);
            Assert.True(ended.IsConnected);
            Assert.False(ended.ShowsIdle);
            for (int i = 0; i < 3; i++)
            {
                time.Pass(4.5);
                h.Model.IdleTick();
                await Pump(() => false, TimeSpan.FromMilliseconds(120));
            }
            Assert.Equal(1, ended.Terminals[0].Idle!.Count);
            Assert.Equal(4, both.Single(w => w != ended).Terminals[0].Idle!.Count);
            Assert.Equal(" \x7f \x7f" + string.Concat(Enumerable.Repeat(" \x7f", 3)), Typed(fixture));
            Assert.DoesNotContain("空闲保活", ended.Status);
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    [AvaloniaFact]
    public async Task EnterIsSentOnlyAtAnEmptyPromptAndMovesNothing()
    {
        Appearance.Apply(new StudioPreferences { CursorBlink = false, IdleKeepAlive = true, IdleMinutes = 4, IdleInput = "enter" });
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1240, Height = 720, DataContext = h.Model };
        window.Show();
        try
        {
            var time = new FakeTime();
            var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "提示符") with { DisablePwdFollow = true, DisableListMonitor = true });
            await Pump(() => w.Terminal?.IsLive == true && w.FileRoots.Count > 0, TimeSpan.FromSeconds(10));
            Settle(window);
            foreach (var t in w.Terminals) t.UseClockForTest(time);
            var terminal = w.Terminal!;
            // What was shown, then four and a half minutes: what does the server get?
            async Task Shown(string output, string sent)
            {
                await Quiet();
                string before = Typed(fixture);
                terminal.Terminal.Write("\r\n" + output);
                time.Pass(4.5);
                h.Model.IdleTick();
                await Pump(() => sent.Length > 0 && Typed(fixture) == before + sent, sent.Length > 0 ? TimeSpan.FromSeconds(8) : TimeSpan.FromMilliseconds(250));
                Assert.Equal(before + sent, Typed(fixture));
            }
            // What the server echoes has arrived, and a look has been taken since.
            async Task Quiet()
            {
                long printed = -1;
                for (int i = 0; i < 40; i++)
                {
                    await Pump(() => false, TimeSpan.FromMilliseconds(60));
                    if (terminal.ReadPlace().Output == printed) break;
                    printed = terminal.ReadPlace().Output;
                }
                h.Model.IdleTick();
            }
            await Shown("root@web:~# ", "\r");
            await Shown("Do you want to continue? [Y/n] ", "");
            Assert.Contains("暂缓", w.IdleText);
            Assert.Contains("提示符", w.IdleTip);
            await Shown("Password: ", "");
            await Shown("root@web:~# rm -rf /data\b\b", ""); // something follows the cursor
            await Shown("root@web:~# \x1b[40C12:00\x1b[45D", ""); // a prompt on the right: not known, so no
            await Shown("> ", "");
            await Shown("\x1b[1;32mweb\x1b[0m:\x1b[34m~\x1b[0m$ ", "\r");
            Assert.DoesNotContain("暂缓", w.IdleText);
            // Scrolled back by the user it is still the line with the cursor that counts.
            await Shown(string.Concat(Enumerable.Range(0, 80).Select(i => i + "\r\n")) + "root@web:~# ", "\r");
            // While something is being printed there is no empty prompt, whatever the line looks like just now.
            await Quiet();
            string so = Typed(fixture);
            fixture.Command("emit");
            await Pump(() => Shows(terminal, "BACKGROUND OUTPUT"), TimeSpan.FromSeconds(5));
            terminal.Terminal.Write("root@web:~# ");
            time.Pass(4.5);
            h.Model.IdleTick();
            await Pump(() => false, TimeSpan.FromMilliseconds(250));
            Assert.Equal(so, Typed(fixture));
            h.Model.IdleTick(); // quiet since
            await Pump(() => Typed(fixture) != so, TimeSpan.FromSeconds(8));
            Assert.Equal(so + "\r", Typed(fixture));
            // A prompt longer than the window is wide; a question that only ends like one on its last row.
            await Shown(new string('x', terminal.Terminal.Cols - 12) + " delete all?# ", "");
            await Shown(new string('x', terminal.Terminal.Cols - 20) + " really delete all? " + "# ", "");
            await Shown("root@web:/" + new string('d', terminal.Terminal.Cols + 20) + "# ", "\r");
            // Half typed, then not.
            terminal.FocusTerminal();
            window.KeyTextInput("$");
            await Pump(() => Typed(fixture).EndsWith('$'), TimeSpan.FromSeconds(3));
            await Shown("root@web:~# echo $", "");
            Assert.Contains("一半", w.IdleTip);
            window.KeyTextInput("\r");
            await Pump(() => Typed(fixture).EndsWith('\r'), TimeSpan.FromSeconds(3));
            await Shown("root@web:~# ", "\r");
            // The prompt after it reports the directory again: the list stays where the user went with it.
            var tab = w.Terminals[0];
            w.OnTerminalDirectory(tab, "/");
            var a = w.FileRoots.First(n => n.IsDirectory);
            w.SelectedNode = a;
            await Shown("root@web:~# ", "\r");
            w.OnTerminalDirectory(tab, "/");
            Assert.Equal(a.Path, w.CurrentDirectory);
            // The user's own Enter brings it back, as before; and a directory the shell really went to is followed.
            window.KeyTextInput("\r");
            string sent = Typed(fixture);
            await Pump(() => !tab.Idle!.IsUnasked, TimeSpan.FromSeconds(3));
            w.OnTerminalDirectory(tab, "/");
            Assert.Equal("/", w.CurrentDirectory);
            await Shown("root@web:~# ", "\r");
            w.OnTerminalDirectory(tab, a.Path);
            await Pump(() => w.CurrentDirectory == a.Path, TimeSpan.FromSeconds(5));
            Assert.Equal(a.Path, w.CurrentDirectory);
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    [AvaloniaFact]
    public void IdleKeepAliveIsASettingThatBackupsDoNotSwitchOn()
    {
        string data = Paths.TempDirectory();
        var vm = new SettingsViewModel(data) { Section = "terminal" };
        var window = new SettingsWindow { DataContext = vm, Width = 900, Height = 900 };
        window.Show();
        using var a = new Source();
        try
        {
            Assert.False(vm.IdleKeepAlive);
            vm.IdleKeepAlive = true;
            vm.IdleMinutes = 2;
            vm.IdleInput = "回车";
            vm.IdleHours = 12;
            Assert.Contains("TMOUT", vm.IdleInputNote);
            var saved = Appearance.Load(data);
            Assert.Equal((true, 2, "enter", 12), (saved.IdleKeepAlive, saved.IdleMinutes, saved.IdleInput, saved.IdleHours));
            vm.SearchText = "防踢";
            Settle(window);
            Assert.True(vm.Shown["idle"] && vm.IsTerminal);
            Shot(window, "settings-idle");
            // From somebody's file it stays as it is here; from one's own it comes along.
            string file = Path.Combine(a.Data, "b.json");
            StudioBackup.Write(file, StudioBackup.Create(saved, a.Quick, a.Library, DateTimeOffset.Now));
            Appearance.Apply(new StudioPreferences());
            StudioBackup.Read(file).Restore(new RestoreChoice(true, true, true, false), a.Quick, a.Library, null, DateTime.Now);
            Assert.False(Appearance.Current.IdleKeepAlive);
            StudioBackup.Read(file).Restore(new RestoreChoice(true, true, true, true), a.Quick, a.Library, null, DateTime.Now);
            Assert.True(Appearance.Current.IdleKeepAlive);
        }
        finally { window.Close(); vm.Dispose(); Appearance.Apply(new StudioPreferences()); Directory.Delete(data, true); }
    }
}
