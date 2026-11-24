using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Studio.Controls;
using Forterm.Studio.Library;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Forterm.Studio.Views.Dialogs;
using Iciclecreek.Terminal;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>0.4.0-alpha.6: the "通用终端" settings from the reference (right-click menu, paste review, highlight
/// rules, cursor and scrollback, recording), find in the terminal and quick commands.</summary>
public sealed class Alpha6Tests
{
    static async Task Pump(Func<bool> until, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (!until() && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); await Task.Delay(40); }
        Dispatcher.UIThread.RunJobs();
    }

    static void Shot(Window window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, name + ".png"));
    }

    static string Typed(LoopbackFixture fixture) =>
        Encoding.UTF8.GetString(fixture.Stats().GetProperty("input").EnumerateArray().SelectMany(x => Convert.FromHexString(x.GetString()!)).ToArray());

    static string Screen(StudioTerminalControl t)
    {
        var text = new StringBuilder();
        var lines = t.Terminal.Buffer.Lines;
        for (int i = 0; i < lines.Length; i++) text.Append(lines[i]?.TranslateToString(true)).Append('\n');
        return text.ToString();
    }

    /// <summary>A terminal in its own window, without a shell.</summary>
    static (Window Window, StudioTerminalControl Terminal) ShowTerminal()
    {
        var terminal = new StudioTerminalControl();
        var window = new Window { Width = 700, Height = 400, Content = terminal };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (window, terminal);
    }

    /// <summary>A main window on a loopback session: saved connection, or opened as a controller does.</summary>
    static async Task<(MainWindow Window, WorkspaceViewModel Workspace)> OpenSession(StudioHarness h, LoopbackFixture fixture, bool launched = false, QuickCommandStore? commands = null)
    {
        var model = commands == null ? h.Model : new MainWindowViewModel(h.Interaction, h.KnownHosts, h.Store, h.Credentials, quickCommands: commands, settingsDirectory: h.Data);
        var window = new MainWindow { Width = 1280, Height = 760, DataContext = model };
        window.Show();
        var w = launched
            ? await window.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-newtab", "唤醒", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"]))
            : await model.OpenProfileAsync(h.Ssh(fixture.Port) is var p ? h.Store.Find(p.Id)! with { DisablePwdFollow = true } : null!);
        Assert.True(w!.IsConnected && w.HasShell, w.Status);
        await Pump(() => w.Terminal is { } t && t.IsLive && Screen(t).Contains("SYNTHETIC"), TimeSpan.FromSeconds(10));
        return (window, w);
    }

    /// <summary>Reads a file that is still being written (Windows refuses without the sharing).</summary>
    static string ReadShared(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(file, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>The size as it is, not as the directory says (Windows updates that late).</summary>
    static long SharedLength(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return file.Length;
    }

    /// <summary>A shell whose output the test provides and that remembers what is typed into it.</summary>
    sealed class ScriptedPty : Porta.Pty.IPtyConnection
    {
        sealed class Output : Stream
        {
            readonly System.Threading.Channels.Channel<byte[]> queue = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
            byte[] current = [];
            public void Add(byte[] data) => queue.Writer.TryWrite(data);
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            {
                if (current.Length == 0)
                {
                    if (!await queue.Reader.WaitToReadAsync(ct) || !queue.Reader.TryRead(out var next)) return 0;
                    current = next;
                }
                int n = Math.Min(buffer.Length, current.Length);
                current.AsMemory(0, n).CopyTo(buffer);
                current = current[n..];
                return n;
            }
            public override Task<int> ReadAsync(byte[] b, int o, int n, CancellationToken ct) => ReadAsync(b.AsMemory(o, n), ct).AsTask();
            public override int Read(byte[] b, int o, int n) => ReadAsync(b.AsMemory(o, n)).AsTask().GetAwaiter().GetResult();
            protected override void Dispose(bool disposing) { queue.Writer.TryComplete(); base.Dispose(disposing); }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
            public override void SetLength(long l) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int n) => throw new NotSupportedException();
        }

        readonly Output output = new();
        readonly MemoryStream input = new();
        public void Emit(string text) => output.Add(Encoding.UTF8.GetBytes(text));
        public string Typed { get { lock (input) return Encoding.UTF8.GetString(input.ToArray()); } }
        public Stream ReaderStream => output;
        public Stream WriterStream => input;
        public bool SupportsCancellableRead => true;
        public int Pid => 0;
        public int ExitCode => 0;
        public event EventHandler<Porta.Pty.PtyExitedEventArgs>? ProcessExited { add { } remove { } }
        public void Resize(int cols, int rows) { }
        public bool WaitForExit(int milliseconds) => false;
        public void Kill() => Dispose();
        public void Dispose() => output.Dispose();
    }

    static async Task SetClipboard(Window window, string text) => await window.Clipboard!.SetTextAsync(text);

    // ---------- paste ----------

    [Fact]
    public void PasteReviewRemovesControlCharactersAndKeepsText()
    {
        var plain = PasteGuard.Review("ls -la /tmp");
        Assert.Equal("ls -la /tmp", plain.Send);
        Assert.False(plain.NeedsConfirmation);

        var lines = PasteGuard.Review("cd /tmp\r\nls\nrm a\r");
        Assert.Equal("cd /tmp\rls\rrm a\r", lines.Send);
        Assert.Equal("cd /tmp\nls\nrm a\n", lines.Preview);
        Assert.Equal(3, lines.Lines);
        Assert.True(lines.EndsWithEnter && lines.NeedsConfirmation);
        // One line with Enter runs at once: it asks too.
        Assert.True(PasteGuard.Review("reboot\n").NeedsConfirmation);

        // ESC cannot end bracketed paste, ^C ^U DEL and C1 CSI cannot act on the line editor.
        var hostile = PasteGuard.Review("a\u001b[201~b\u0003c\u0015d\u007fe\u009b1mf\0g\th");
        Assert.Equal("a[201~bcde1mfg\th", hostile.Send);
        Assert.Equal(6, hostile.Removed);
        Assert.True(hostile.NeedsConfirmation);
        Assert.Contains("已去掉 6 个控制字符", hostile.Summary);
        // Text that displays in another order than it is sent.
        Assert.Equal("echo abc", PasteGuard.Review("echo \u202ea\u2066bc\u2069\u202c").Send);
        Assert.Equal("中文 émoji 😀", PasteGuard.Review("中文 émoji 😀").Send);
        Assert.Equal("", PasteGuard.Review("\u001b\u0007").Send);
        Assert.True(PasteGuard.Review(new string('x', PasteGuard.LongText + 1)).NeedsConfirmation);
    }

    [Fact]
    public void CopiedTextLosesTheGridPaddingOnly()
    {
        Assert.Equal("a  b\n\n  c", PasteGuard.Copied("a  b     \n     \n  c  \n    \n   "));
        Assert.Equal("one \\\ntwo", PasteGuard.Copied("one \\      \ntwo"));
        Assert.Equal("   ", PasteGuard.Copied("   "));
        Assert.Equal("", PasteGuard.Copied(""));
    }

    [AvaloniaFact]
    public async Task EveryPastePathIsReviewed()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var (window, w) = await OpenSession(h, fixture);
        try
        {
            var t = w.Terminal!;
            // One plain line: no question.
            await SetClipboard(window, "echo one");
            w.PasteTerminalCommand.Execute(null);
            await Pump(() => Typed(fixture).EndsWith("echo one"), TimeSpan.FromSeconds(5));
            Assert.Equal("echo one", Typed(fixture));
            Assert.Empty(h.Interaction.Pastes);

            // Several lines, declined: nothing is sent.
            h.Interaction.PasteAnswer = false;
            await SetClipboard(window, "a\nb\n");
            w.PasteTerminalCommand.Execute(null);
            await Pump(() => h.Interaction.Pastes.Count == 1, TimeSpan.FromSeconds(5));
            await Pump(() => false, TimeSpan.FromMilliseconds(200));
            Assert.Equal("echo one", Typed(fixture));

            // The keyboard shortcut.
            h.Interaction.PasteAnswer = true;
            t.FocusTerminal();
            var paste = TerminalKeyGestures.Paste[0];
            window.KeyPress(paste.Key, (RawInputModifiers)(int)paste.KeyModifiers, PhysicalKey.V, null);
            await Pump(() => Typed(fixture).Contains("a\rb\r"), TimeSpan.FromSeconds(5));
            Assert.Equal("echo onea\rb\r", Typed(fixture));
            Assert.Equal(2, h.Interaction.Pastes.Count);

            // The right click, when it is set to copy/paste.
            Appearance.Apply(new StudioPreferences { RightClickMenu = false });
            await SetClipboard(window, "c\nd");
            RightClick(window, t);
            await Pump(() => Typed(fixture).Contains("c\rd"), TimeSpan.FromSeconds(5));
            Assert.Equal("echo onea\rb\rc\rd", Typed(fixture));
            Assert.Equal(3, h.Interaction.Pastes.Count);

            // Bracketed paste: wrapped, and the text cannot close the wrapper itself.
            t.Terminal.BracketedPasteMode = true;
            await SetClipboard(window, "x\u001b[201~\ny");
            w.PasteTerminalCommand.Execute(null);
            await Pump(() => Typed(fixture).Contains("201~\ry"), TimeSpan.FromSeconds(5));
            Assert.EndsWith("\u001b[200~x[201~\ry\u001b[201~", Typed(fixture));
            t.Terminal.BracketedPasteMode = false;

            // Review switched off: no question, control characters still go.
            Appearance.Apply(new StudioPreferences { ConfirmPaste = false });
            await SetClipboard(window, "e\u0003\nf");
            int asked = h.Interaction.Pastes.Count;
            w.PasteTerminalCommand.Execute(null);
            await Pump(() => Typed(fixture).EndsWith("e\rf"), TimeSpan.FromSeconds(5));
            Assert.EndsWith("e\rf", Typed(fixture));
            Assert.Equal(asked, h.Interaction.Pastes.Count);
            Assert.Contains("去掉了 1 个控制字符", w.Status);

            // Too much, or nothing but control characters: refused with the reason.
            int sent = Typed(fixture).Length;
            await SetClipboard(window, new string('x', PasteGuard.MaxLength + 1));
            w.PasteTerminalCommand.Execute(null);
            await Pump(() => w.Status.Contains("1 MiB"), TimeSpan.FromSeconds(5));
            Assert.Contains("1 MiB", w.Status);
            await SetClipboard(window, "\u0003\u001b");
            w.PasteTerminalCommand.Execute(null);
            await Pump(() => w.Status.Contains("只有控制字符"), TimeSpan.FromSeconds(5));
            Assert.Contains("只有控制字符", w.Status);
            Assert.Equal(sent, Typed(fixture).Length);
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    [AvaloniaFact]
    public async Task APasteWaitingForAnAnswerIsNotSentToAnotherShell()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var (window, w) = await OpenSession(h, fixture);
        try
        {
            var answer = new TaskCompletionSource();
            h.Interaction.OnPaste = () => answer.Task;
            await SetClipboard(window, "first\nsecond\n");
            w.PasteTerminalCommand.Execute(null);
            await Pump(() => h.Interaction.Pastes.Count == 1, TimeSpan.FromSeconds(5));
            // A second paste while the question is open is dropped, not queued behind it.
            _ = w.Terminal!.PasteAsync();
            await Pump(() => false, TimeSpan.FromMilliseconds(150));
            Assert.Single(h.Interaction.Pastes);
            // The tab is closed and another shell opened before the answer.
            var next = await w.AddTerminalAsync();
            Assert.NotNull(next);
            w.Terminals[0].CloseCommand!.Execute(null);
            answer.SetResult();
            await Pump(() => false, TimeSpan.FromMilliseconds(300));
            Assert.DoesNotContain("first", Typed(fixture));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task APasteReviewedForOneShellIsNotSentToTheNextOneInTheSameTab()
    {
        Appearance.Apply(new StudioPreferences());
        var (window, t) = ShowTerminal();
        using var first = new ScriptedPty();
        using var second = new ScriptedPty();
        try
        {
            t.AttachConnection(first);
            var answer = new TaskCompletionSource<string?>();
            t.PasteFilter = _ => answer.Task;
            await SetClipboard(window, "one\ntwo");
            var paste = t.PasteAsync();
            await Pump(() => false, TimeSpan.FromMilliseconds(100));
            t.AttachConnection(second);
            answer.SetResult("one\rtwo");
            await paste;
            Assert.Equal("", first.Typed + second.Typed);
            t.PasteFilter = text => Task.FromResult<string?>(text);
            await t.PasteAsync();
            Assert.Equal("one\ntwo", second.Typed); // the same path, when nothing changed meanwhile
        }
        finally { window.Close(); t.Dispose(); }
    }

    [AvaloniaFact]
    public void PasteDialogShowsTheText()
    {
        var dialog = new PasteDialog(PasteGuard.Review("cd /data/app\nsystemctl restart demo\u001b\ntail -f /var/log/demo.log\n"));
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        var box = dialog.GetVisualDescendants().OfType<TextBox>().Single();
        Assert.True(box.IsReadOnly);
        Assert.Equal("cd /data/app\nsystemctl restart demo\ntail -f /var/log/demo.log\n", box.Text);
        Shot(dialog, "paste-review");
        dialog.Close();
    }

    // ---------- mouse ----------

    static Point Centre(Window window, Visual visual) => visual.TranslatePoint(new Point(120, 60), window)!.Value;

    static void RightClick(Window window, Visual visual, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var at = Centre(window, visual);
        window.MouseMove(at, modifiers);
        window.MouseDown(at, MouseButton.Right, modifiers);
        window.MouseUp(at, MouseButton.Right, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    static ContextMenu Menu(Window window) => window.GetVisualDescendants().OfType<WorkspaceView>().Single().FindControl<ContextMenu>("TerminalMenu")!;

    [AvaloniaFact]
    public async Task RightClickShowsTheMenuUnlessTheProgramOwnsTheMouse()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var store = new QuickCommandStore(h.Data);
        store.Upsert(new QuickCommand { Name = "a_b 磁盘", Text = "df -h" });
        var (window, w) = await OpenSession(h, fixture, commands: store);
        try
        {
            var t = w.Terminal!;
            var menu = Menu(window);
            await SetClipboard(window, "must not be pasted");
            RightClick(window, t);
            Assert.True(menu.IsOpen);
            Assert.False(w.HasTerminalSelection);
            Assert.True(w.IsTerminalLive);
            var copy = menu.Items.OfType<MenuItem>().Single(m => m.Name == "MenuCopy");
            Assert.False(copy.IsEffectivelyEnabled);
            var quick = menu.Items.OfType<MenuItem>().Single(m => m.Name == "MenuQuick");
            // The name is shown as it is ("_" is not an access key).
            Assert.Equal("a_b 磁盘", ((TextBlock)((MenuItem)quick.Items[0]!).Header!).Text);
            Shot(window, "terminal-context-menu");
            menu.Close();
            Assert.Equal("", Typed(fixture));

            // With a selection the menu offers to copy it, without the grid's padding.
            t.Terminal.Write("alpha   \r\nbeta\r\n");
            t.SelectAll();
            RightClick(window, t);
            Assert.True(menu.IsOpen && w.HasTerminalSelection);
            Assert.True(copy.IsEffectivelyEnabled);
            menu.Close();
            w.CopyTerminalCommand.Execute(null);
            await Pump(() => false, TimeSpan.FromMilliseconds(100));
            string copied = (await window.Clipboard!.TryGetTextAsync())!.Replace("\r\n", "\n");
            Assert.Equal("SYNTHETIC SSH\nalpha\nbeta", copied);
            t.Terminal.Selection.ClearSelection();

            // A program that tracks the mouse gets the click; Shift takes it back.
            t.Terminal.Write("\u001b[?1000h\u001b[?1006h");
            RightClick(window, t);
            Assert.False(menu.IsOpen);
            await Pump(() => Typed(fixture).Contains("\u001b[<2;"), TimeSpan.FromSeconds(5));
            Assert.Contains("\u001b[<2;", Typed(fixture));
            RightClick(window, t, RawInputModifiers.Shift);
            Assert.True(menu.IsOpen);
            menu.Close();
            t.Terminal.Write("\u001b[?1000l");

            // An editor in the same column has no terminal menu.
            string local = Path.Combine(h.Data, "a.txt");
            File.WriteAllText(local, "hello\n");
            h.Interaction.OpenFiles = [local];
            w.SelectedNode = null;
            w.UploadFilesCommand.Execute(null);
            await Pump(() => w.FileRoots.Any(n => n.Name == "a.txt"), TimeSpan.FromSeconds(15));
            var editor = await w.OpenEditorAsync(w.FileRoots.First(n => n.Name == "a.txt"));
            Assert.NotNull(editor);
            Assert.False(w.IsTerminalShown);
            window.UpdateLayout();
            RightClick(window, window.GetVisualDescendants().OfType<EditorView>().Single());
            Assert.False(menu.IsOpen);
            // Terminal commands do nothing to the hidden terminal.
            int before = Typed(fixture).Length;
            w.PasteTerminalCommand.Execute(null);
            w.RunQuickCommand.Execute(store.Commands[0]);
            await Pump(() => false, TimeSpan.FromMilliseconds(200));
            Assert.Equal(before, Typed(fixture).Length);
            Assert.Contains("没有可输入的终端", w.Status);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CopyOnSelectCopiesOnlyANewSelection()
    {
        Appearance.Apply(new StudioPreferences());
        var (window, t) = ShowTerminal();
        try
        {
            t.Terminal.Write("hello world\r\n");
            await SetClipboard(window, "before");
            var row = t.GetVisualDescendants().OfType<TerminalView>().Single().TranslatePoint(new Point(4, 8), window)!.Value;
            async Task DoubleClick()
            {
                for (int i = 0; i < 2; i++) { window.MouseDown(row, MouseButton.Left); window.MouseUp(row, MouseButton.Left); }
                await Pump(() => false, TimeSpan.FromMilliseconds(120));
            }
            await DoubleClick();
            Assert.True(t.HasSelection);
            Assert.Equal("before", await window.Clipboard!.TryGetTextAsync()); // off by default
            t.Terminal.Selection.ClearSelection();
            await Pump(() => false, TimeSpan.FromMilliseconds(700)); // the next click starts a new sequence
            Appearance.Apply(new StudioPreferences { CopyOnSelect = true });
            await DoubleClick();
            Assert.Equal("hello", await window.Clipboard!.TryGetTextAsync());

            // The scroll bar selects nothing: the clipboard another program filled meanwhile stays.
            t.Terminal.Write(string.Concat(Enumerable.Range(0, 100).Select(i => "line " + i + "\r\n")));
            t.Refresh();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(t.HasSelection);
            await SetClipboard(window, "other");
            var bar = t.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>().Single();
            var on = bar.TranslatePoint(new Point(bar.Bounds.Width / 2, bar.Bounds.Height / 2), window)!.Value;
            window.MouseDown(on, MouseButton.Left);
            window.MouseUp(on, MouseButton.Left);
            await Pump(() => false, TimeSpan.FromMilliseconds(120));
            Assert.Equal("other", await window.Clipboard!.TryGetTextAsync());

            // Dragging over the very text that is selected is a new selection.
            t.ClearScreen();
            t.Terminal.Write("hello world\r\n");
            t.Refresh();
            await Pump(() => false, TimeSpan.FromMilliseconds(700));
            row = t.GetVisualDescendants().OfType<TerminalView>().Single().TranslatePoint(new Point(4, 8), window)!.Value;
            async Task Drag()
            {
                window.MouseDown(row, MouseButton.Left);
                window.MouseMove(row + new Point(60, 0), RawInputModifiers.LeftMouseButton);
                window.MouseUp(row + new Point(60, 0), MouseButton.Left);
                await Pump(() => false, TimeSpan.FromMilliseconds(700));
            }
            await Drag();
            string dragged = (await window.Clipboard!.TryGetTextAsync())!;
            Assert.StartsWith("hello", dragged);
            await SetClipboard(window, "other");
            await Drag();
            Assert.Equal(dragged, await window.Clipboard!.TryGetTextAsync());

            // Ctrl+click on a link opens it and leaves the selection as it was.
            t.Terminal.Write("https://example.com/ here\r\n");
            t.Refresh();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            await SetClipboard(window, "other");
            var view = t.GetVisualDescendants().OfType<TerminalView>().Single();
            var link = view.TranslatePoint(new Point(60, view.Bounds.Height / t.Terminal.Rows * 1.5), window)!.Value;
            window.MouseDown(link, MouseButton.Left, RawInputModifiers.Control);
            window.MouseUp(link, MouseButton.Left, RawInputModifiers.Control);
            await Pump(() => false, TimeSpan.FromMilliseconds(120));
            Assert.True(t.HasSelection);
            Assert.Equal("other", await window.Clipboard!.TryGetTextAsync());

            // A program that tracks the mouse gets the click: the old selection is not copied again, not
            // even when it moves meanwhile because the full buffer loses lines at the top.
            t.Terminal.Write(string.Concat(Enumerable.Repeat("x\r\n", 5100)) + "hello world");
            t.ScrollToBottom();
            t.Refresh();
            await Pump(() => false, TimeSpan.FromMilliseconds(700));
            row = view.TranslatePoint(new Point(4, view.Bounds.Height / t.Terminal.Rows * (t.Terminal.Buffer.Y + 0.5)), window)!.Value;
            await DoubleClick();
            Assert.Equal("hello", await window.Clipboard!.TryGetTextAsync());
            await SetClipboard(window, "other");
            await Pump(() => false, TimeSpan.FromMilliseconds(700));
            t.Terminal.Write("\u001b[?1000h");
            window.MouseDown(row, MouseButton.Left);
            t.Terminal.Write("\r\ny\r\ny\r\n");
            Assert.True(t.HasSelection);
            window.MouseUp(row, MouseButton.Left);
            await Pump(() => false, TimeSpan.FromMilliseconds(120));
            Assert.Equal("other", await window.Clipboard!.TryGetTextAsync());
        }
        finally { window.Close(); t.Dispose(); Appearance.Apply(new StudioPreferences()); }
    }

    // ---------- highlight ----------

    [AvaloniaFact]
    public void HighlightRulesMatchWhatTheyShould()
    {
        var palette = TerminalSchemes.StudioDark();
        var h = new Highlighter(Highlighter.Defaults(), palette);
        Color? At(string text, int index)
        {
            // The first span covering the index wins, as in the view.
            foreach (var span in h.Match(text))
                if (index >= span.Start && index < span.Start + span.Length) return ((ISolidColorBrush)span.Brush).Color;
            return null;
        }
        string line = "see https://example.com/a?b=1 from 10.20.30.40 ver 1.2.3 ERROR warn success";
        Assert.Equal(Color.Parse(palette.Yellow!), At(line, line.IndexOf("https", StringComparison.Ordinal)));
        Assert.Equal(Color.Parse(palette.Magenta!), At(line, line.IndexOf("10.20", StringComparison.Ordinal)));
        Assert.Equal(Color.Parse(palette.Magenta!), At(line, line.IndexOf("40 ver", StringComparison.Ordinal)));
        Assert.Equal(Color.Parse(palette.Cyan!), At(line, line.IndexOf("1.2.3", StringComparison.Ordinal)));
        Assert.Equal(Color.Parse(palette.Red!), At(line, line.IndexOf("ERROR", StringComparison.Ordinal)));
        Assert.Equal(Color.Parse(palette.Green!), At(line, line.IndexOf("success", StringComparison.Ordinal)));
        Assert.Null(At(line, 0));
        Assert.Null(At("mirrors 256.1 token", 0));
        Assert.Equal(Color.Parse(palette.Red!), At("登录失败", 2));

        // Unusable patterns are explained and match nothing; the others still work.
        Assert.Null(Highlighter.Check(@"\d+"));
        Assert.Equal("不能为空", Highlighter.Check(""));
        Assert.Contains("写法不正确", Highlighter.Check("(a"));
        Assert.Contains("不支持", Highlighter.Check("(?=a)b"));
        Assert.Contains("不支持", Highlighter.Check(@"(a)\1"));
        Assert.Contains("不支持", Highlighter.Check("((a{1,900}){1,900}){1,900}"));
        Assert.Contains("控制字符", Highlighter.Check("a\tb"));
        var mixed = new Highlighter([new() { Pattern = "(a" }, new() { Pattern = "b", Color = "green" }, new() { Pattern = "c", Enabled = false }], palette);
        Assert.Single(mixed.Match("abc"));
        Assert.True(new Highlighter([new() { Pattern = "" }], palette).IsEmpty);

        // A rule that is slow on several rows is switched off and says so; the others go on.
        var slow = new HighlightRule { Pattern = "slow-" + Guid.NewGuid().ToString("N") };
        var limited = new Highlighter([slow], palette, TimeSpan.FromTicks(-1));
        Assert.False(Highlighter.IsSlow(slow));
        for (int i = 0; i < 2; i++) Assert.Single(limited.Match(slow.Pattern));
        Assert.False(Highlighter.IsSlow(slow)); // a busy moment is not the pattern's fault
        limited.RowLimit = TimeSpan.FromHours(1);
        limited.Match(slow.Pattern); // a quick row in between: counting starts over
        limited.RowLimit = TimeSpan.FromTicks(-1);
        for (int i = 0; i < 2; i++) Assert.Single(limited.Match(slow.Pattern));
        Assert.False(Highlighter.IsSlow(slow));
        Assert.Single(limited.Match(slow.Pattern));
        Assert.True(Highlighter.IsSlow(slow));
        Assert.Empty(limited.Match(slow.Pattern)); // not even in the highlighter that found it
        Assert.True(new Highlighter([slow], palette).IsEmpty); // and stays off with another scheme
        Assert.False(Highlighter.IsSlow(slow with { Pattern = slow.Pattern + "x" })); // rewritten: tried again

        // A pattern that explodes on a backtracking engine stays quick.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        new Highlighter([new() { Pattern = "(a+)+$" }], palette).Match(new string('a', 400) + "!");
        Assert.True(clock.ElapsedMilliseconds < 2000, clock.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public void StoredHighlightRulesAreBounded()
    {
        Assert.Null(Highlighter.Validated(null));
        var rules = Highlighter.Validated([null!, new HighlightRule { Name = "a\nb", Pattern = new string('x', 900), Color = "plaid" }, new HighlightRule { Name = null!, Pattern = null! },
            .. Enumerable.Range(0, 60).Select(i => new HighlightRule { Pattern = "p" + i })])!;
        Assert.Equal(Highlighter.MaxRules, rules.Length);
        Assert.Equal("ab", rules[0].Name);
        Assert.Equal(Highlighter.MaxPattern, rules[0].Pattern.Length);
        Assert.Equal("yellow", rules[0].Color);
        Assert.Equal("", rules[1].Pattern);

        string data = Paths.TempDirectory();
        try
        {
            // The largest rules that can be stored are read back (the file has a size limit).
            var full = Enumerable.Range(0, Highlighter.MaxRules).Select(_ => new HighlightRule { Name = new string('名', Highlighter.MaxName), Pattern = new string('<', Highlighter.MaxPattern) }).ToArray();
            var prefs = new StudioPreferences { HighlightRules = full, TerminalScheme = "Nord", LocalShell = new string('中', 1024), LocalShellArgs = new string('中', 1024) }.Validated();
            Appearance.Save(data, prefs);
            var back = Appearance.Load(data);
            Assert.Equal("Nord", back.TerminalScheme);
            Assert.True(Highlighter.Same(full, back.HighlightRules));
            // Hand-edited nulls do not stop the application.
            File.WriteAllText(Path.Combine(data, "preferences.json"), """{"TerminalScheme":"Nord","HighlightRules":[null,{"Pattern":null,"Color":null}],"CursorStyle":null,"Scrollback":-5}""");
            back = Appearance.Load(data);
            Assert.Equal("Nord", back.TerminalScheme);
            Assert.Equal("bar", back.CursorStyle);
            Assert.Equal(100, back.Scrollback);
            Assert.Single(back.HighlightRules!);
        }
        finally { Directory.Delete(data, true); }
    }

    /// <summary>How many pixels of a rendered frame are (nearly) this colour, within rows of the terminal.</summary>
    static int Pixels(Window window, Color color, Rect area)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        double scale = window.RenderScaling;
        int count = 0;
        bool bgra = buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888;
        byte[] row = new byte[buffer.RowBytes];
        for (int y = (int)(area.Y * scale); y < Math.Min(buffer.Size.Height, area.Bottom * scale); y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, row.Length);
            for (int x = (int)(area.X * scale); x < Math.Min(buffer.Size.Width, area.Right * scale); x++)
            {
                int r = row[x * 4 + (bgra ? 2 : 0)], g = row[x * 4 + 1], b = row[x * 4 + (bgra ? 0 : 2)];
                if (Math.Abs(r - color.R) < 12 && Math.Abs(g - color.G) < 12 && Math.Abs(b - color.B) < 12) count++;
            }
        }
        return count;
    }

    [AvaloniaFact]
    public void HighlightColoursOnlyTextInTheDefaultColour()
    {
        Appearance.Apply(new StudioPreferences { CursorBlink = false });
        var (window, t) = ShowTerminal();
        try
        {
            var all = new Rect(0, 0, 680, 400);
            Color red = Color.Parse("#E5534B"), magenta = Color.Parse("#C678DD"), green = Color.Parse("#4EC27F");
            t.Terminal.Write("plain text\r\n");
            t.Refresh();
            Assert.Equal(0, Pixels(window, red, all));
            t.Terminal.Write("FAILED at 10.1.2.3\r\n中文 FAILED 失败\r\n");
            t.Refresh();
            Assert.True(Pixels(window, red, all) > 20);
            Assert.True(Pixels(window, magenta, all) > 20);

            // Off, and on again, for a terminal that is already open.
            Appearance.Apply(Appearance.Current with { Highlight = false });
            Assert.Equal(0, Pixels(window, red, all));
            Appearance.Apply(Appearance.Current with { Highlight = true });
            Assert.True(Pixels(window, red, all) > 20);

            // The scheme's own red.
            Appearance.Apply(Appearance.Current with { TerminalScheme = "Nord" });
            Assert.Equal(0, Pixels(window, red, all));
            Assert.True(Pixels(window, Color.Parse("#BF616A"), all) > 20);
            Appearance.Apply(Appearance.Current with { TerminalScheme = TerminalSchemes.Auto });

            // Colours the program chose stay; so do inverse text and a full-screen program.
            t.ClearScreen();
            t.Terminal.Write("\u001b[32mFAILED\u001b[0m \u001b[7mFAILED\u001b[0m\r\n");
            t.Refresh();
            Assert.True(Pixels(window, green, all) > 20);
            Assert.Equal(0, Pixels(window, red, all));
            // On a background of the program's choosing the highlight could be the same colour.
            t.ClearScreen();
            t.Terminal.Write("\u001b[41m FAILED \u001b[0m\r\n");
            t.Refresh();
            Assert.True(Pixels(window, Color.Parse("#D7DEE0"), all) > 20);
            t.ClearScreen();
            t.Terminal.Write("\u001b[?1049h\u001b[HFAILED");
            t.Refresh();
            Assert.Equal(0, Pixels(window, red, all));
            t.Terminal.Write("\u001b[?1049l");

            // A rule of the user's.
            Appearance.Apply(Appearance.Current with { HighlightRules = [new HighlightRule { Pattern = "needle", Color = "magenta" }] });
            t.Terminal.Write("a needle FAILED\r\n");
            t.Refresh();
            Assert.True(Pixels(window, magenta, all) > 20);
            Assert.Equal(0, Pixels(window, red, all));
            // A rule that cannot be used changes nothing.
            Appearance.Apply(Appearance.Current with { HighlightRules = [new HighlightRule { Pattern = "(needle" }] });
            Assert.Equal(0, Pixels(window, magenta, all));
        }
        finally { window.Close(); t.Dispose(); Appearance.Apply(new StudioPreferences()); }
    }

    // ---------- cursor and scrollback ----------

    [AvaloniaFact]
    public void CursorAndScrollbackFollowTheSettings()
    {
        Appearance.Apply(new StudioPreferences());
        var (window, t) = ShowTerminal();
        try
        {
            Assert.Equal(XTerm.Common.CursorStyle.Bar, t.CursorStyle);
            Assert.True(t.CursorBlink);
            Assert.Equal(5000, t.Terminal.Options.Scrollback);
            Appearance.Apply(new StudioPreferences { CursorStyle = "block", CursorBlink = false, Scrollback = 1000 });
            Assert.Equal(XTerm.Common.CursorStyle.Block, t.Terminal.Options.CursorStyle);
            Assert.False(t.Terminal.Options.CursorBlink);
            Assert.Equal(5000, t.Terminal.Options.Scrollback); // an open terminal keeps its history
            // A program's own cursor survives unrelated settings.
            t.Terminal.Write("\u001b[4 q");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(XTerm.Common.CursorStyle.Underline, t.Terminal.Options.CursorStyle);
            Appearance.Apply(Appearance.Current with { TerminalFontSize = 18 });
            Assert.Equal(XTerm.Common.CursorStyle.Underline, t.Terminal.Options.CursorStyle);
            var (other, next) = ShowTerminal();
            Assert.Equal(1000, next.Terminal.Options.Scrollback);
            Assert.Equal(XTerm.Common.CursorStyle.Block, next.Terminal.Options.CursorStyle);
            other.Close();
            next.Dispose();
        }
        finally { window.Close(); t.Dispose(); Appearance.Apply(new StudioPreferences()); }
    }

    // ---------- find ----------

    [AvaloniaFact]
    public async Task FindStepsThroughTheScrollbackNewestFirst()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var (window, w) = await OpenSession(h, fixture);
        try
        {
            var t = w.Terminal!;
            t.Terminal.Write(string.Concat(Enumerable.Range(1, 200).Select(i => i % 50 == 0 ? $"line {i} Needle\r\n" : $"line {i}\r\n")));
            t.Refresh();
            // The shortcut opens the bar and puts the focus in it.
            t.FocusTerminal();
            var gesture = Shortcuts.Find.Gesture;
            window.KeyPress(gesture.Key, (RawInputModifiers)(int)gesture.KeyModifiers, PhysicalKey.F, null);
            await Pump(() => w.Find.IsOpen, TimeSpan.FromSeconds(2));
            Assert.True(w.Find.IsOpen);
            var box = window.GetVisualDescendants().OfType<WorkspaceView>().Single().FindControl<TextBox>("FindBox")!;
            Assert.True(box.IsFocused);
            Assert.Equal("", Typed(fixture)); // the shortcut is not typed into the shell

            w.Find.Text = "needle";
            Assert.Equal("4/4", w.Find.Summary);
            Shot(window, "terminal-find");
            box.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal("3/4", w.Find.Summary);
            window.KeyPress(Key.Enter, RawInputModifiers.Shift, PhysicalKey.Enter, null);
            Assert.Equal("4/4", w.Find.Summary);
            w.Find.NextCommand.Execute(null);
            Assert.Equal("1/4", w.Find.Summary); // wraps
            Assert.True(t.ViewportY < t.MaxScrollback); // scrolled up to it
            w.Find.CaseSensitive = true;
            Assert.Equal("无匹配", w.Find.Summary);
            w.Find.Text = "Needle";
            Assert.Equal("4/4", w.Find.Summary);

            // A match that wraps over the edge is one match.
            t.Terminal.Write(new string('-', t.Terminal.Cols - 3) + "Wrapped\r\n");
            w.Find.Text = "Wrapped";
            Assert.Equal("1/1", w.Find.Summary);
            Assert.Equal(2, t.SearchHitCount);
            w.Find.Text = "Needle";
            Assert.Equal("4/4", w.Find.Summary);

            // Output that arrives later is included in the next step, which stays where it was.
            await t.SendInputAsync("Needle again\r");
            await Pump(() => Screen(t).Contains("Needle again"), TimeSpan.FromSeconds(5));
            w.Find.PreviousCommand.Execute(null);
            Assert.Equal("3/5", w.Find.Summary);

            // Another tab: the search moves to it; back again it searches anew.
            var second = await w.AddTerminalAsync();
            Assert.NotNull(second);
            await Pump(() => second.Control.IsLive, TimeSpan.FromSeconds(5));
            Assert.Equal("无匹配", w.Find.Summary);
            Assert.Equal(0, t.SearchHitCount);
            w.ActiveTab = w.Terminals[0];
            window.UpdateLayout();
            await Pump(() => w.Find.Summary == "5/5", TimeSpan.FromSeconds(3));
            Assert.Equal("5/5", w.Find.Summary);

            // Another session, and back: the same.
            var main = (MainWindowViewModel)window.DataContext!;
            main.Selected = main.Home;
            window.UpdateLayout();
            await Pump(() => false, TimeSpan.FromMilliseconds(100));
            main.Selected = w;
            window.UpdateLayout();
            await Pump(() => t.SearchMatchCount == 5, TimeSpan.FromSeconds(3));
            Assert.Equal(5, t.SearchMatchCount);
            box = window.GetVisualDescendants().OfType<WorkspaceView>().Single().FindControl<TextBox>("FindBox")!;

            // Escape closes, removes the highlights and returns the focus.
            box.Focus();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(w.Find.IsOpen);
            Assert.Equal(0, t.SearchHitCount);
            await Pump(() => t.GetVisualDescendants().OfType<TerminalView>().Single().IsFocused, TimeSpan.FromSeconds(1));
            Assert.True(t.GetVisualDescendants().OfType<TerminalView>().Single().IsFocused);
            // Open again: the last text is searched.
            w.FindInTerminalCommand.Execute(null);
            Assert.Equal("5/5", w.Find.Summary);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void FindKeepsTheNewestMatches()
    {
        Appearance.Apply(new StudioPreferences { Scrollback = 20_000 });
        var (window, t) = ShowTerminal();
        try
        {
            var find = new TerminalFindViewModel(() => t);
            for (int i = 0; i < 12; i++) t.Terminal.Write(string.Concat(Enumerable.Range(0, 1000).Select(n => $"needle {i * 1000 + n}\r\n")));
            t.Refresh();
            find.Text = "needle";
            find.Open();
            Assert.Equal("10000/10000+", find.Summary);
            Assert.True(t.ViewportY >= t.MaxScrollback - t.Terminal.Rows, $"{t.ViewportY} of {t.MaxScrollback}");
            find.PreviousCommand.Execute(null);
            Assert.Equal("9999/10000+", find.Summary);
            // More output pushes old matches out of the list; the current one stays the current one.
            t.Terminal.Write("needle a\r\nneedle b\r\n");
            find.PreviousCommand.Execute(null);
            Assert.Equal("9996/10000+", find.Summary);
            find.Close();
        }
        finally { window.Close(); t.Dispose(); Appearance.Apply(new StudioPreferences()); }
    }

    /// <summary>The vendored search itself: the screen switches, the buffer trims.</summary>
    [Fact]
    public void SearchFollowsTheBufferThatIsShown()
    {
        var terminal = new XTerm.Terminal(new XTerm.Options.TerminalOptions { Cols = 20, Rows = 5, Scrollback = 10, ReflowOnResize = false });
        terminal.Write("\u001b[?1049h\u001b[Hneedle");
        using var search = new XTerm.Search.BufferSearch(terminal);
        Assert.Equal(1, search.Find("needle"));
        Assert.True(search.TryMovePrevious(out _));
        terminal.Write("\u001b[?1049l");
        // The other screen's rows are not these rows.
        for (int i = 0; i < 5; i++) Assert.Equal(0, search.HitsOnRow(i).Length);
        Assert.False(search.TryMoveNext(out _));
        for (int i = 0; i < 12; i++) terminal.Write(i == 10 ? "needle\r\n" : "x\r\n");
        Assert.Equal(1, search.Refresh());
        Assert.Equal(-1, search.CurrentMatch);
        Assert.True(search.TryMovePrevious(out var hit));
        Assert.Equal(10, hit.BufferRow);
        for (int i = 0; i < 4; i++) terminal.Write("y\r\n");
        Assert.Equal(15, terminal.Buffer.Lines.Length); // full: two rows went off the top
        Assert.Equal(1, search.HitsOnRow(8).Length);
        Assert.Equal(1, search.Refresh());
        Assert.Equal(0, search.CurrentMatch); // still the same one
    }

    [Fact]
    public void FindMatchesStayReadableInEveryScheme()
    {
        static Color Over(Color below, Color over)
        {
            double a = over.A / 255d;
            byte Mix(byte x, byte y) => (byte)Math.Round(x + (y - x) * a);
            return Color.FromRgb(Mix(below.R, over.R), Mix(below.G, over.G), Mix(below.B, over.B));
        }
        foreach (bool appDark in new[] { true, false })
            foreach (var scheme in TerminalSchemes.All)
            {
                var palette = TerminalSchemes.PaletteFor(scheme.Name, appDark);
                Color fg = Color.Parse(palette.Foreground!), bg = Color.Parse(palette.Background!);
                var (match, current) = TerminalSchemes.FindOverlays(palette);
                foreach (var overlay in new[] { match, current })
                {
                    Assert.True(TerminalSchemes.Contrast(Over(fg, overlay), Over(bg, overlay)) >= 2.9, scheme.Name + " text");
                    Assert.True(TerminalSchemes.Contrast(Over(bg, overlay), bg) >= 1.2, scheme.Name + " match");
                }
            }
    }

    // ---------- quick commands ----------

    [Fact]
    public void QuickCommandsAreValidatedAndSurviveADamagedFile()
    {
        string data = Paths.TempDirectory();
        try
        {
            var store = new QuickCommandStore(data);
            store.Upsert(new QuickCommand { Name = " 磁盘 ", Text = "df -h\r\ndu -sh .\r\n\r\n" });
            store.Upsert(new QuickCommand { Name = "草稿", Text = "systemctl status ", Enter = false, Confirm = true });
            Assert.Equal("df -h\ndu -sh .", store.Commands[0].Text);
            Assert.Equal("磁盘", store.Commands[0].Name);
            Assert.Equal("df -h\rdu -sh .\r", store.Commands[0].Typed);
            Assert.Equal("df -h …（2 行）", store.Commands[0].Preview);
            Assert.Equal("systemctl status ", store.Commands[1].Typed);
            store.Move(store.Commands[1].Id, -1);
            store.Move(store.Commands[0].Id, -1); // already first
            var back = new QuickCommandStore(data);
            Assert.Equal(["草稿", "磁盘"], back.Commands.Select(c => c.Name));
            Assert.True(back.Commands[0].Confirm && !back.Commands[0].Enter);

            foreach (var bad in new QuickCommand[]
            {
                new() { Name = "", Text = "x" }, new() { Name = "n", Text = " \n " }, new() { Name = "n", Text = "a\u0003b" }, new() { Name = "n", Text = "a\u001b[A" },
                new() { Name = "n", Text = "a\u202eb" }, new() { Name = "a\nb", Text = "x" }, new() { Name = "n", Text = "a\nb", Enter = false },
                new() { Name = new string('n', 41), Text = "x" }, new() { Name = "n", Text = new string('x', 4001) }, new() { Id = "../x", Name = "n", Text = "x" },
            }) Assert.Throws<InvalidDataException>(() => store.Upsert(bad));
            Assert.Equal(2, store.Commands.Count);

            foreach (string damaged in new[] { "{", """[{"id":"a","name":null,"text":"x"}]""", """[{"id":"a","name":"n","text":"a\u0003"}]""", """[{"id":"a","name":"n","text":"x"},{"id":"a","name":"m","text":"y"}]""" })
            {
                string file = Path.Combine(data, "quick-commands.json");
                File.WriteAllText(file, damaged);
                Assert.ThrowsAny<Exception>(() => new QuickCommandStore(data));
                Assert.Equal(damaged, File.ReadAllText(file));
            }
        }
        finally { Directory.Delete(data, true); }
    }

    [AvaloniaFact]
    public async Task QuickCommandsTypeIntoTheShownTerminal()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var store = new QuickCommandStore(h.Data);
        store.Upsert(new QuickCommand { Name = "多行", Text = "cd /tmp\nls" });
        store.Upsert(new QuickCommand { Name = "只填入", Text = "tail -f ", Enter = false });
        store.Upsert(new QuickCommand { Name = "要确认", Text = "reboot", Confirm = true });
        var (window, w) = await OpenSession(h, fixture, commands: store);
        try
        {
            w.RunQuickCommand.Execute(store.Commands[0]);
            await Pump(() => Typed(fixture).Contains("ls\r"), TimeSpan.FromSeconds(5));
            Assert.Equal("cd /tmp\rls\r", Typed(fixture));
            w.RunQuickCommand.Execute(store.Commands[1]);
            await Pump(() => Typed(fixture).EndsWith("tail -f "), TimeSpan.FromSeconds(5));
            Assert.EndsWith("tail -f ", Typed(fixture));
            Assert.Empty(h.Interaction.Confirms); // a saved connection: the user's own action, no question

            h.Interaction.ConfirmAnswer = false;
            w.RunQuickCommand.Execute(store.Commands[2]);
            await Pump(() => h.Interaction.Confirms.Count == 1, TimeSpan.FromSeconds(2));
            Assert.Contains("reboot", h.Interaction.Confirms[0]);
            Assert.DoesNotContain("reboot", Typed(fixture));

            // Confirmed, but the terminal was closed and replaced meanwhile: nothing is sent.
            var next = await w.AddTerminalAsync();
            Assert.NotNull(next);
            w.ActiveTab = w.Terminals[0];
            h.Interaction.ConfirmAnswer = true;
            h.Interaction.OnConfirm = _ => { h.Interaction.OnConfirm = null; w.Terminals[0].CloseCommand!.Execute(null); };
            w.RunQuickCommand.Execute(store.Commands[2]);
            await Pump(() => w.Status.Contains("没有发送"), TimeSpan.FromSeconds(2));
            Assert.Contains("终端已关闭", w.Status);
            Assert.DoesNotContain("reboot", Typed(fixture));

            // Confirmed, but the tab runs another shell by then.
            using var other = new ScriptedPty();
            h.Interaction.OnConfirm = _ => { h.Interaction.OnConfirm = null; w.Terminal!.AttachConnection(other); };
            w.RunQuickCommand.Execute(store.Commands[2]);
            await Pump(() => false, TimeSpan.FromMilliseconds(300));
            Assert.Null(h.Interaction.OnConfirm); // it was asked
            Assert.Contains("终端已关闭", w.Status);
            Assert.Equal("", other.Typed);
            Assert.DoesNotContain("reboot", Typed(fixture));
            var third = await w.AddTerminalAsync();
            Assert.NotNull(third);
            await Pump(() => third.Control.IsLive, TimeSpan.FromSeconds(5));

            // The list filters by every word; clicking an entry in it sends and closes it.
            w.QuickFilter = "只 TAIL";
            Assert.Equal("只填入", Assert.Single(w.VisibleQuickCommands).Name);
            w.QuickFilter = "没有";
            Assert.Equal("没有匹配的快速命令", w.QuickCommandsHint);
            w.QuickFilter = "";
            window.UpdateLayout();
            var button = window.GetVisualDescendants().OfType<WorkspaceView>().Single().FindControl<Button>("QuickButton")!;
            var flyout = (Flyout)button.Flyout!;
            flyout.ShowAt(button);
            Dispatcher.UIThread.RunJobs();
            Shot(window, "quick-commands-flyout");
            var entries = ((Control)flyout.Content!).GetVisualDescendants().OfType<Button>().Where(b => b.CommandParameter is QuickCommand).ToArray();
            Assert.Equal(3, entries.Length);
            string before = Typed(fixture);
            var at = entries[1].TranslatePoint(new Point(entries[1].Bounds.Width / 2, entries[1].Bounds.Height / 2), TopLevel.GetTopLevel(entries[1])!)!.Value;
            var host = (TopLevel)TopLevel.GetTopLevel(entries[1])!;
            host.MouseMove(at);
            host.MouseDown(at, MouseButton.Left);
            host.MouseUp(at, MouseButton.Left);
            await Pump(() => Typed(fixture).Length > before.Length, TimeSpan.FromSeconds(5));
            Assert.EndsWith("tail -f ", Typed(fixture));
            Assert.False(flyout.IsOpen);
            // Another session comes to the front (a wake-up, say) while the list is open: it closes,
            // because its entries would now type into that session.
            flyout.ShowAt(button);
            Dispatcher.UIThread.RunJobs();
            Assert.True(flyout.IsOpen);
            var main = (MainWindowViewModel)window.DataContext!;
            var second = await main.OpenProfileAsync(h.Store.Profiles[0]);
            Assert.Same(second, main.Selected);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.False(flyout.IsOpen);
            main.Selected = w;
            // "管理" opens the settings at that page.
            w.ManageQuickCommandsCommand.Execute(null);
            Assert.True(h.Interaction.SettingsShown!.IsQuick);
            Assert.Equal(3, h.Interaction.SettingsShown.QuickCommands.Count);
        }
        finally { window.Close(); h.Interaction.SettingsShown?.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ALaunchedSessionAsksBeforeTheFirstQuickCommand()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var store = new QuickCommandStore(h.Data);
        store.Upsert(new QuickCommand { Name = "磁盘", Text = "df -h" });
        h.Interaction.ConfirmAnswer = false;
        var (window, w) = await OpenSession(h, fixture, launched: true, commands: store);
        try
        {
            Assert.Equal("", Typed(fixture)); // a wake-up writes nothing by itself
            w.RunQuickCommand.Execute(store.Commands[0]);
            await Pump(() => h.Interaction.Confirms.Count == 1, TimeSpan.FromSeconds(2));
            Assert.Contains("临时会话", h.Interaction.Confirms[0]);
            Assert.Contains("df -h", h.Interaction.Confirms[0]);
            await Pump(() => false, TimeSpan.FromMilliseconds(200));
            Assert.Equal("", Typed(fixture));
            h.Interaction.ConfirmAnswer = true;
            w.RunQuickCommand.Execute(store.Commands[0]);
            await Pump(() => Typed(fixture).Length > 0, TimeSpan.FromSeconds(5));
            w.RunQuickCommand.Execute(store.Commands[0]);
            await Pump(() => Typed(fixture) == "df -h\rdf -h\r", TimeSpan.FromSeconds(5));
            Assert.Equal("df -h\rdf -h\r", Typed(fixture));
            Assert.Equal(2, h.Interaction.Confirms.Count); // asked again after the refusal, then not any more
            // That answer is about quick commands; "CD 到终端" still asks for itself.
            w.CdToTerminalCommand.Execute(null);
            await Pump(() => h.Interaction.Confirms.Count == 3, TimeSpan.FromSeconds(3));
            Assert.Equal(3, h.Interaction.Confirms.Count);
        }
        finally { window.Close(); }
    }

    // ---------- recording ----------

    static string Transcribe(params string[] chunks) => TranscribeAt(80, chunks);

    static string TranscribeAt(int columns, params string[] chunks)
    {
        var lines = new List<string>();
        var transcript = new TerminalTranscript(lines.Add);
        foreach (string chunk in chunks) transcript.Write(Encoding.UTF8.GetBytes(chunk), columns);
        transcript.Finish();
        return string.Join("\n", lines);
    }

    /// <summary>Commands longer than the window, as real shells (bash 3.2, zsh 5.9; 40 columns) print them:
    /// typed, edited at the start and the end, deleted back over the edge, with wide characters, and
    /// killed and replaced by a short one.</summary>
    [Theory]
    [InlineData("$ echo one two three four five six seven eight nine ten\none two three four five six seven eight nine ten", "$ echo one two three four five six seven \r eight nine ten\r\none two three four five six seven eight nine ten\r\n")] // bash-long
    [InlineData("$ echo ZZ one two three four five six seven eight nine ten end\nZZ one two three four five six seven eight nine ten end", "$ echo one two three four five six seven \r eight nine ten\u001b[A\r\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[CZone two three four five six seven\u001b[1@ \u001b[A\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[CZone two three four five six seve\u001b[1@n\u001b[A\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C one two three four five six sev\u001b[1@e\u001b[A\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\r\n\r\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C end\r\nZZ one two three four five six seven eight nine ten end\r\n")] // bash-edit
    [InlineData("$ echo aaaaaaaaaaaaaaaaaaaaaadone\naaaaaaaaaaaaaaaaaaaaaadone", "$ echo aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa \raaaaaaaaaaaaaaaaaaa\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\r\u001b[K\u001b[A\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[K\r\n\r\u001b[K\u001b[A\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\u001b[C\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[K\b\u001b[Kdone\r\naaaaaaaaaaaaaaaaaaaaaadone\r\n")] // bash-back
    [InlineData("$ echo 一二三四五六七八九十一二三四五六 七八九十甲乙丙\n一二三四五六七八九十一二三四五六七八九十甲乙丙", "$ echo 一二三四五六七八九十一二三四五六 \u001b[K七\r七八九十甲乙丙\r\n一二三四五六七八九十一二三四五六七八九十甲乙丙\r\n")] // bash-wide
    [InlineData("$ echo short\nshort", "$ echo one two three four five six seven \r eight nine ten\u001b[A\r\u001b[C\u001b[C\u001b[K\r\n\r\u001b[K\u001b[A\u001b[C\u001b[Cecho short\r\nshort\r\n")] // bash-kill
    [InlineData("$ echo one two three four five six seven eight nine ten\none two three four five six seven eight nine ten", "$ \u001b[K\u001b[?2004he\becho one two three four five six seven \r\u001b[K \r eight nine ten\u001b[?2004l\r\r\none two three four five six seven eight nine ten\r\n")] // zsh-long
    [InlineData("$ echo ZZ one two three four five six seven eight nine ten end\nZZ one two three four five six seven eight nine ten end", "$ \u001b[K\u001b[?2004he\becho one two three four five six seven \r\u001b[K \r eight nine ten\u001b[A\u001b[13D\u001b[1C\u001b[1C\u001b[1C\u001b[1C\u001b[1CZone two three four five six seven eight nine ten\u001b[A\u001b[8DZone two three four five six seven eight nine ten\u001b[A\u001b[8D one two three four five six seven eight nine ten\u001b[A\u001b[8D\u001b[1B\u001b[8C end\u001b[?2004l\r\r\nZZ one two three four five six seven eight nine ten end\r\n")] // zsh-edit
    [InlineData("$ echo aaaaaaaaaaaaaaaaaaaaaadone\naaaaaaaaaaaaaaaaaaaaaadone", "$ \u001b[K\u001b[?2004he\becho aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa \r\u001b[Ka\raaaaaaaaaaaaaaaaaaa\b \b\b \b\b \b\b \b\b \b\b \b\b \b\b \b\b \b\b \b\b \b\b \b\b \b\b \b\b \b\b \b\b \b\ra \b\r\u001b[K\u001b[A\u001b[39C\u001b[K\u001b[1B\r\u001b[K\u001b[A\u001b[39C\b \b\b \b\b \b\b \b\b \b\b \b\b \b\b \b\b \b\b \bdone\u001b[?2004l\u001b[1B\raaaaaaaaaaaaaaaaaaaaaadone\r\n")] // zsh-back
    [InlineData("$ echo 一二三四五六七八九十一二三四五六 七八九十甲乙丙\n一二三四五六七八九十一二三四五六七八九十甲乙丙", "$ \u001b[K\u001b[?2004he\becho 一二三四五六七八九十一二三四五六\u001b[7m \u001b[27m七\u001b[K八九十甲乙丙\u001b[?2004l\r\r\n一二三四五六七八九十一二三四五六七八九十甲乙丙\r\n")] // zsh-wide
    [InlineData("$ echo short\nshort", "$ \u001b[K\u001b[?2004he\becho one two three four five six seven \r\u001b[K \r eight nine ten\u001b[A\u001b[13D\u001b[K\u001b[1B\r\u001b[K\u001b[A\u001b[2Ce\becho short\u001b[?2004l\u001b[1B\rshort\r\n")] // zsh-kill
    public void TranscriptFollowsCommandsThatWrap(string expected, string output)
    {
        Assert.Equal(expected, TranscribeAt(40, output));
        // However the output is split up.
        Assert.Equal(expected, TranscribeAt(40, output.Select(c => c.ToString()).ToArray()));
    }

    [Fact]
    public void TranscriptRecordsWhatALineFinallyLookedLike()
    {
        Assert.Equal("a\nb", Transcribe("a\r\nb"));
        Assert.Equal("red plain", Transcribe("\u001b[1;31mred\u001b[0m plain\r\n"));
        // Progress bars, backspace, erase-in-line and cursor movement inside the line.
        Assert.Equal("100%", Transcribe("10%\r 50%\r100%\r\n"));
        Assert.Equal("$ ls -la", Transcribe("$ lx\b \bs -la\r\n"));
        Assert.Equal("$ new", Transcribe("$ old command\r\u001b[K$ new\r\n"));
        Assert.Equal("abXYef", Transcribe("abcdef\u001b[4DXY\r\n"));
        Assert.Equal("ab  ef", Transcribe("abcdef\u001b[4D\u001b[2X\r\n"));
        Assert.Equal("abef", Transcribe("abcdef\u001b[4D\u001b[2P\r\n"));
        Assert.Equal("ab  cdef", Transcribe("abcdef\u001b[4D\u001b[2@\r\n"));
        Assert.Equal("a       b", Transcribe("a\tb\r\n"));
        Assert.Equal("x         y", Transcribe("x\u001b[11Gy\r\n"));
        // Wide characters take two columns, so editing over them lines up.
        Assert.Equal("中x", Transcribe("中文\u001b[2Dx\u001b[K\r\n"));
        Assert.Equal("  文", Transcribe("中文\r \r\n"));
        Assert.Equal("é e\u0301 😀", Transcribe("é e\u0301 😀\r\n"));
        // Strings (title, hyperlinks, DCS) are skipped, however they end and wherever they are split.
        Assert.Equal("ok", Transcribe("\u001b]0;title\u0007o\u001b]8;;https://x\u001b\\k\r\n"));
        Assert.Equal("ok", Transcribe("\u001b]7;file://h/p", "ath\u001b", "\\ok\r\n"));
        Assert.Equal("ok", Transcribe("\u001bPq~~~\u001b\\ok\r\n"));
        Assert.Equal("ok", Transcribe("\u001b(B\u001b[?2004hok\u001b[?2004l\r\n"));
        Assert.Equal("a\nb", Transcribe("a\u001b[2J\u001b[Hb\r\n"));
        // Full-screen programs are not recorded.
        Assert.Equal("$ vim\n" + TerminalTranscript.FullScreenNote + "\n$ ls", Transcribe("$ vim\r\n\u001b[?1049hscreen\r\nscreen\u001b[10;10H~\u001b[?1049l$ ls\r\n"));
        // A character split between reads.
        var lines = new List<string>();
        var t = new TerminalTranscript(lines.Add);
        foreach (byte b in Encoding.UTF8.GetBytes("中文😀\r\n")) t.Write([b]);
        Assert.Equal("中文😀", Assert.Single(lines));
        // Nothing the server sends can make a line grow without bound or swallow the rest for ever.
        Assert.Contains("end", Transcribe(new string('x', 200_000), "\u001b[999999999Cy\u001b[999999999@\r\n", "\u001b]0;" + new string('z', 1_100_000) + "\r\nend\r\n"));
        Assert.True(Transcribe(new string('x', 200_000)).Split('\n').All(l => l.Length <= 32 * 1024));
        Assert.Equal(200_000, Transcribe(new string('x', 200_000)).Count(c => c == 'x'));
        // Nor can a few bytes from the server fill the disk: a movement stays inside the window.
        foreach (string move in new[] { "\u001b[32767G", "\u001b[32767C", "\u001b[1;32767H", "x\r\u001b[32767@", "\t\t\t\t\t\t\t\t\t\t\t\t\t" })
            Assert.True(TranscribeAt(80, string.Concat(Enumerable.Repeat(move + "x\r\n", 100))).Length < 100 * 82, move);
        // A full row: the next character wraps, but CR does not; without wrapping the last column is overwritten.
        Assert.Equal("12345678ab", TranscribeAt(8, "12345678ab\r\n"));
        Assert.Equal("ab345678", TranscribeAt(8, "12345678\rab\r\n"));
        Assert.Equal("1234567b", TranscribeAt(8, "\u001b[?7l12345678ab\r\n\u001b[?7h"));
        Assert.Equal("一二三四五", TranscribeAt(9, "一二三四五\r\n")); // no blank where a wide character did not fit
        // Movement stops at the last column; what an insertion pushes out of the row is gone.
        Assert.Equal("       x", TranscribeAt(8, "\u001b[99Gx\r\n"));
        Assert.Equal("       x", TranscribeAt(8, "\u001b[99Cx\r\n"));
        Assert.Equal("  abcdef", TranscribeAt(8, "abcdefgh\r\u001b[2@\r\n"));
        Assert.Equal("a\nb", TranscribeAt(8, "a\r\n\u001b[3Bb\r\n")); // moving down past the end is no blank lines
        // Several lines of progress redrawn in place are recorded one after the other, without blank lines.
        Assert.Equal("a 1\nb 1\na 2\nb 2\nend", Transcribe("a 1\r\nb 1\r\n\u001b[2A\ra 2\u001b[K\u001b[1B\rb 2\u001b[K\u001b[1B\rend\r\n"));
        // The window is made narrower or wider in the middle of a line.
        var resized = new List<string>();
        var r = new TerminalTranscript(resized.Add);
        r.Write("0123456789"u8, 40);
        r.Write("ab\r\nnext\r\n"u8, 8);
        Assert.Equal("next", resized[^1]);
    }

    [AvaloniaFact]
    public async Task AStuckDiskStopsTheRecordingNotTheTerminal()
    {
        Appearance.Apply(new StudioPreferences());
        var (window, t) = ShowTerminal();
        using var pty = new ScriptedPty();
        using var disk = new StuckStream();
        try
        {
            t.AttachConnection(pty);
            int ui = Environment.CurrentManagedThreadId, reader = ui;
            t.OutputReceived += (_, _) => reader = Environment.CurrentManagedThreadId;
            string? reason = null;
            using var recorder = new SessionRecorder(t, disk, "stuck", "# header", r => reason = r, limit: 1 << 30, maxPending: 100_000, diskWait: TimeSpan.FromMilliseconds(300));
            disk.Go.Reset();
            for (int i = 0; i < 20; i++) pty.Emit(string.Concat(Enumerable.Range(0, 100).Select(n => new string('x', 98) + "\r\n")) + $"block {i}\r\n");
            await Pump(() => Screen(t).Contains("block 19"), TimeSpan.FromSeconds(10));
            Assert.Contains("block 19", Screen(t)); // the terminal did not wait
            Assert.NotEqual(ui, reader); // nor does the window, for output it does not draw
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Assert.False(recorder.Stop());
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
            disk.Go.Set();
            await Pump(() => reason != null, TimeSpan.FromSeconds(5));
            Assert.Contains("没有响应", reason);
            string text = Encoding.UTF8.GetString(disk.ToArray());
            Assert.StartsWith("# header", text);
            Assert.Contains("block 0", text); // what was taken before is there
            Assert.DoesNotContain("block 19", text);
            Assert.EndsWith("# 记录在此停止：磁盘没有响应", text.TrimEnd()); // and it does not look complete
        }
        finally { disk.Go.Set(); window.Close(); t.Dispose(); }
    }

    sealed class StuckStream : MemoryStream
    {
        public ManualResetEventSlim Go { get; } = new(true);
        public int Delay { get; set; }
        public override void Write(ReadOnlySpan<byte> buffer) => Write(buffer.ToArray(), 0, buffer.Length);
        public override void Write(byte[] buffer, int offset, int count) { Go.Wait(); if (Delay > 0) Thread.Sleep(Delay); base.Write(buffer, offset, count); }
    }

    /// <summary>A disk that is merely slow holds the output up; it costs nothing of the record.</summary>
    [AvaloniaFact]
    public async Task ASlowDiskIsWaitedFor()
    {
        Appearance.Apply(new StudioPreferences());
        var (window, t) = ShowTerminal();
        using var pty = new ScriptedPty();
        using var disk = new StuckStream();
        try
        {
            t.AttachConnection(pty);
            string? reason = null;
            using var recorder = new SessionRecorder(t, disk, "slow", "# header", r => reason = r, limit: 1 << 30, maxPending: 2_000, diskWait: TimeSpan.FromSeconds(3));
            disk.Delay = 2;
            for (int i = 0; i < 400; i++) pty.Emit($"line {i}\r\n");
            await Pump(() => Screen(t).Contains("line 399"), TimeSpan.FromSeconds(20));
            await Pump(() => Encoding.UTF8.GetString(disk.ToArray()).Contains("line 399"), TimeSpan.FromSeconds(10));
            Assert.True(recorder.Stop());
            Assert.Null(reason);
            string[] lines = Encoding.UTF8.GetString(disk.ToArray()).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(Enumerable.Range(0, 400).Select(i => "line " + i), lines.Skip(1));
        }
        finally { window.Close(); t.Dispose(); }
    }

    /// <summary>Recording usually starts with a prompt on the screen, or with a command half typed.</summary>
    [AvaloniaFact]
    public async Task RecordingStartsOnTheLineTheScreenShows()
    {
        Appearance.Apply(new StudioPreferences());
        var (window, t) = ShowTerminal();
        using var pty = new ScriptedPty();
        try
        {
            t.AttachConnection(pty);
            int cols = t.Terminal.Cols;
            string Recorded(MemoryStream s) => string.Join("\n", Encoding.UTF8.GetString(s.ToArray()).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Skip(1));
            SessionRecorder Start(MemoryStream s) => new(t, s, "", "# header", null, limit: 1 << 30, maxPending: 1 << 20, diskWait: TimeSpan.FromSeconds(3));

            // A prompt; then a command longer than the window, wrapped the way bash does it.
            pty.Emit("earlier\r\nuser@host:~$ ");
            await Pump(() => Screen(t).Contains("host"), TimeSpan.FromSeconds(5));
            var first = new MemoryStream();
            var recorder = Start(first);
            string command = "echo " + string.Join(' ', Enumerable.Range(100, 60)), shown = "user@host:~$ " + command;
            string wrapped = "";
            for (int i = 13; i < shown.Length; i++) wrapped += shown[i] + ((i + 1) % cols == 0 && i + 1 < shown.Length ? shown[i + 1] + "\r" : "");
            pty.Emit(wrapped + "\r\ndone\r\n");
            await Pump(() => Screen(t).Contains("done"), TimeSpan.FromSeconds(5));
            Assert.True(recorder.Stop());
            Assert.Equal(shown + "\ndone", Recorded(first));

            // Half typed, already over two rows; the cursor is back in the first one.
            pty.Emit("$ " + new string('a', cols) + "\u001b[A\r\u001b[2C");
            await Pump(() => Screen(t).Contains("aaaa"), TimeSpan.FromSeconds(5));
            var second = new MemoryStream();
            recorder = Start(second);
            pty.Emit("BB\r\n\r\nnext\r\n");
            await Pump(() => Screen(t).Contains("next"), TimeSpan.FromSeconds(5));
            Assert.True(recorder.Stop());
            Assert.Equal("$ BB" + new string('a', cols - 2) + "\nnext", Recorded(second));

            // Half typed over two rows, the cursor behind it in the second one.
            pty.Emit("$ " + new string('c', cols));
            await Pump(() => Screen(t).Contains("cccc"), TimeSpan.FromSeconds(5));
            var behind = new MemoryStream();
            recorder = Start(behind);
            pty.Emit("DD\r\nafter\r\n");
            await Pump(() => Screen(t).Contains("after"), TimeSpan.FromSeconds(5));
            Assert.True(recorder.Stop());
            Assert.Equal("$ " + new string('c', cols) + "DD\nafter", Recorded(behind));

            // In a full-screen program: nothing of it.
            pty.Emit("\u001b[?1049h\u001b[Hscreen");
            await Pump(() => Screen(t).Contains("screen"), TimeSpan.FromSeconds(5));
            var third = new MemoryStream();
            recorder = Start(third);
            pty.Emit("\u001b[2;1Hmore of it\u001b[?1049l$ ls\r\n");
            await Pump(() => Screen(t).Contains("$ ls"), TimeSpan.FromSeconds(5));
            Assert.True(recorder.Stop());
            Assert.Equal(TerminalTranscript.FullScreenNote + "\n$ ls", Recorded(third));

            // Started in the middle of a stream of output, nothing is missing and nothing is there twice.
            var emitting = Task.Run(async () => { for (int i = 0; i < 3000; i++) { pty.Emit($"n{i:0000}\r\n"); if (i % 20 == 0) await Task.Delay(1); } });
            for (int round = 0; round < 8; round++)
            {
                await Pump(() => false, TimeSpan.FromMilliseconds(15));
                var log = new MemoryStream();
                recorder = Start(log);
                await Pump(() => false, TimeSpan.FromMilliseconds(40));
                Assert.True(recorder.Stop());
                int[] numbers = Recorded(log).Split('\n').Where(l => l.Length == 5 && l[0] == 'n').Select(l => int.Parse(l.AsSpan(1))).ToArray();
                if (numbers.Length > 0) Assert.Equal(Enumerable.Range(numbers[0], numbers.Length), numbers);
            }
            await emitting;
        }
        finally { window.Close(); t.Dispose(); }
    }

    // ---------- flow control ----------

    static async Task<(SharedSshSession Session, SshPtyConnection Shell)> OpenShell(LoopbackFixture fixture, CancellationToken ct)
    {
        var session = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct);
        return (session, await session.OpenShellAsync(80, 24, ct, "xterm-256color", [], flowControl: true));
    }

    static async Task<long> ReadUntil(Stream stream, Func<string, bool> done, char counted, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        long count = 0;
        string tail = "";
        while (!done(tail))
        {
            int n = await stream.ReadAsync(buffer, ct);
            if (n == 0) break;
            string text = Encoding.UTF8.GetString(buffer, 0, n);
            count += text.Count(c => c == counted);
            tail = (tail + text)[^Math.Min(200, tail.Length + text.Length)..];
        }
        return count;
    }

    [Fact]
    public async Task AFloodedTerminalSlowsTheServerDownInsteadOfClosing()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var (session, shell) = await OpenShell(fixture, ct.Token);
        using (session) using (shell)
        {
            fixture.Command("flood"); // 6 MiB, and nobody reads
            await Task.Delay(1500, ct.Token);
            Assert.Null(shell.CloseReason);
            Assert.False(shell.WaitForExit(0));
            // The other channels are not held up.
            using (var files = await session.OpenFilesAsync(ct.Token)) await files.UploadNewAsync(new MemoryStream([7, 8, 9]), "/while-flooded", ct.Token);
            // Read on: everything arrives, and the shell still answers.
            long xs = 0;
            var buffer = new byte[64 * 1024];
            while (xs < 6 * 1024 * 1024) { int n = await shell.ReaderStream.ReadAsync(buffer, ct.Token); Assert.True(n > 0); xs += buffer.Take(n).Count(b => b == (byte)'X'); }
            Assert.Equal(6 * 1024 * 1024, xs);
            await shell.WriterStream.WriteAsync("alive"u8.ToArray(), ct.Token);
            await shell.WriterStream.FlushAsync(ct.Token);
            await ReadUntil(shell.ReaderStream, t => t.Contains("alive"), 'X', ct.Token);
            Assert.True(session.IsConnected);
            Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
        }
    }

    [Fact]
    public async Task ManySmallPacketsDoNotCloseAnUnreadTerminal()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var (session, shell) = await OpenShell(fixture, ct.Token);
        using (session) using (shell)
        {
            for (int i = 0; i < 400; i++) fixture.Command("emit");
            await Task.Delay(500, ct.Token);
            Assert.Null(shell.CloseReason);
            Assert.Equal(400, await Occurrences(shell.ReaderStream, "BACKGROUND", 400, ct.Token));
        }
    }

    static async Task<int> Occurrences(Stream stream, string word, int expected, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var text = new StringBuilder();
        while (true)
        {
            int found = text.ToString().Split(word).Length - 1;
            if (found >= expected) return found;
            int n = await stream.ReadAsync(buffer, ct);
            if (n == 0) return found;
            text.Append(Encoding.UTF8.GetString(buffer, 0, n));
        }
    }

    [Fact]
    public async Task AFloodedTelnetTerminalSlowsTheServerDownToo()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            const int total = 12 * 1024 * 1024;
            var serving = Task.Run(async () =>
            {
                using var accepted = await listener.AcceptTcpClientAsync(ct.Token);
                await accepted.GetStream().WriteAsync(new byte[total].AsMemory(), ct.Token);
            }, ct.Token);
            using var telnet = await TelnetPtyConnection.ConnectAsync("127.0.0.1", ((System.Net.IPEndPoint)listener.LocalEndpoint).Port, ct.Token, flowControl: true);
            await Task.Delay(1000, ct.Token); // nobody reads
            Assert.Null(telnet.CloseReason);
            var reader = telnet.ReaderStream;
            var buffer = new byte[64 * 1024];
            int received = 0, n;
            while (received < total && (n = await reader.ReadAsync(buffer, ct.Token)) > 0) received += n;
            Assert.Equal(total, received);
            await serving;

            // Closed while the server waits: nothing hangs and nothing is reported as a failure.
            var blocked = Task.Run(async () =>
            {
                using var accepted = await listener.AcceptTcpClientAsync(ct.Token);
                try { await accepted.GetStream().WriteAsync(new byte[total].AsMemory(), ct.Token); } catch (IOException) { }
            }, ct.Token);
            var second = await TelnetPtyConnection.ConnectAsync("127.0.0.1", ((System.Net.IPEndPoint)listener.LocalEndpoint).Port, ct.Token, flowControl: true);
            await Task.Delay(500, ct.Token);
            second.Dispose();
            await blocked.WaitAsync(TimeSpan.FromSeconds(10), ct.Token);
            Assert.Null(second.CloseReason);
        }
        finally { listener.Stop(); }
    }

    /// <summary>A server that ignores the window it was given still cannot fill the memory.</summary>
    [Fact]
    public async Task AServerThatIgnoresTheWindowLosesOnlyTheTerminal()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var (session, shell) = await OpenShell(fixture, ct.Token);
        using (session) using (shell)
        {
            fixture.Command("flood-past-window");
            while (!shell.WaitForExit(0)) await Task.Delay(20, ct.Token);
            Assert.Contains("4MiB", shell.CloseReason);
            Assert.True(session.IsConnected);
            using var files = await session.OpenFilesAsync(ct.Token);
            await files.UploadNewAsync(new MemoryStream([1]), "/still-there", ct.Token);
        }
    }

    [AvaloniaFact]
    public async Task AFloodIsShownAndRecordedWholeWhileSearching()
    {
        Appearance.Apply(new StudioPreferences { Scrollback = 50_000 });
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var (window, w) = await OpenSession(h, fixture);
        try
        {
            string log = Path.Combine(h.Data, "flood.log");
            h.Interaction.SaveFile = log;
            w.RecordTerminalCommand.Execute(null);
            await Pump(() => w.IsRecording, TimeSpan.FromSeconds(2));
            w.Find.Text = "XX";
            w.FindInTerminalCommand.Execute(null);
            fixture.Command("flood");
            var end = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            // The last line has no end yet, so it is not in the file.
            while (DateTime.UtcNow < end && SharedLength(log) < 6 * 1024 * 1024 - 40_000) { w.Find.PreviousCommand.Execute(null); Dispatcher.UIThread.RunJobs(); await Task.Delay(30); }
            Assert.True(w.Terminal!.IsLive, w.Status);
            Assert.True(w.IsRecording, w.Status);
            await w.Terminal.SendInputAsync("still alive\r\n");
            await Pump(() => Screen(w.Terminal).Contains("still alive"), TimeSpan.FromSeconds(10));
            Assert.Contains("still alive", Screen(w.Terminal));
            w.RecordTerminalCommand.Execute(null);
            await Pump(() => !w.IsRecording, TimeSpan.FromSeconds(5));
            Assert.Equal(6 * 1024 * 1024, File.ReadAllText(log).Count(c => c == 'X'));
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    [AvaloniaFact]
    public async Task RecordingWritesOutputUntilStopped()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var (window, w) = await OpenSession(h, fixture);
        try
        {
            string log = Path.Combine(h.Data, "会话.log");
            h.Interaction.SaveFile = null; // the picker was cancelled
            w.RecordTerminalCommand.Execute(null);
            await Pump(() => false, TimeSpan.FromMilliseconds(100));
            Assert.False(w.IsRecording);

            h.Interaction.SaveFile = log;
            w.RecordTerminalCommand.Execute(null);
            await Pump(() => w.IsRecording, TimeSpan.FromSeconds(2));
            Assert.True(w.IsRecording, w.Status);
            Assert.Equal("停止记录会话", w.RecordHeader);
            Assert.Empty(h.Interaction.Confirms);
            if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(log));
            await w.Terminal!.SendInputAsync("\u001b[31mhello\u001b[0m 中文\r\n");
            await Pump(() => ReadShared(log).Contains("hello 中文"), TimeSpan.FromSeconds(5));
            Assert.Contains("hello 中文", ReadShared(log)); // while it is going on, not only at the end
            Shot(window, "terminal-recording");
            // Hidden behind another tab it goes on; only this terminal is recorded.
            var second = await w.AddTerminalAsync();
            Assert.False(w.IsRecording);
            Assert.True(w.Terminals[0].IsRecording);
            await second!.Control.SendInputAsync("other terminal\r\n");
            await Pump(() => Screen(second.Control).Contains("other terminal"), TimeSpan.FromSeconds(5));
            w.ActiveTab = w.Terminals[0];
            w.RecordTerminalCommand.Execute(null);
            await Pump(() => !w.IsRecording, TimeSpan.FromSeconds(2));
            string text = File.ReadAllText(log);
            Assert.StartsWith("# Forterm 会话记录 ", text);
            Assert.DoesNotContain('\u001b', text);
            Assert.DoesNotContain("other terminal", text);
            await w.Terminal!.SendInputAsync("after the stop\r\n");
            await Pump(() => Screen(w.Terminal!).Contains("after the stop"), TimeSpan.FromSeconds(5));
            Assert.Equal(text, File.ReadAllText(log));

            // An existing file is never overwritten.
            w.RecordTerminalCommand.Execute(null);
            await Pump(() => w.Status.Contains("不覆盖"), TimeSpan.FromSeconds(2));
            Assert.False(w.IsRecording);
            Assert.Equal(text, File.ReadAllText(log));

            // Closing the tab ends the recording and keeps what was not yet a whole line.
            string last = Path.Combine(h.Data, "last.log");
            h.Interaction.SaveFile = last;
            w.RecordTerminalCommand.Execute(null);
            await Pump(() => w.IsRecording, TimeSpan.FromSeconds(2));
            await w.Terminal!.SendInputAsync("unfinished");
            await Pump(() => Screen(w.Terminal!).Contains("unfinished"), TimeSpan.FromSeconds(5));
            w.Terminals[0].CloseCommand!.Execute(null);
            Assert.Contains("unfinished", File.ReadAllText(last));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task RecordingALaunchedSessionAsksAndAFullFileStopsIt()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        h.Interaction.ConfirmAnswer = false;
        var (window, w) = await OpenSession(h, fixture, launched: true);
        try
        {
            string log = Path.Combine(h.Data, "a.log");
            h.Interaction.SaveFile = log;
            w.RecordTerminalCommand.Execute(null);
            await Pump(() => h.Interaction.Confirms.Count == 1, TimeSpan.FromSeconds(2));
            Assert.Contains("临时会话", h.Interaction.Confirms[0]);
            Assert.False(w.IsRecording);
            Assert.False(File.Exists(log));

            // The size limit: recording ends with a reason, the terminal goes on.
            string? reason = null;
            using var recorder = SessionRecorder.Start(w.Terminal!, log, "# header", r => reason = r, limit: 760);
            for (int i = 0; i < 10; i++) await w.Terminal!.SendInputAsync(new string('x', 80) + "\r\n");
            await Pump(() => reason != null, TimeSpan.FromSeconds(5));
            Assert.Contains("上限", reason);
            Assert.True(new FileInfo(log).Length <= 760);
            Assert.Contains("# 记录在此停止：记录文件达到上限", File.ReadAllText(log));
            await w.Terminal!.SendInputAsync("still alive\r\n");
            await Pump(() => Screen(w.Terminal!).Contains("still alive"), TimeSpan.FromSeconds(5));
            Assert.Contains("still alive", Screen(w.Terminal!));
        }
        finally { window.Close(); }
    }

    // ---------- settings ----------

    [AvaloniaFact]
    public void TerminalSettingsPersistAndResetKeepsThem()
    {
        string data = Paths.TempDirectory();
        Appearance.Apply(new StudioPreferences());
        var vm = new SettingsViewModel(data, new QuickCommandStore(data));
        var window = new SettingsWindow { DataContext = vm, Width = 900, Height = 720 };
        window.Show();
        try
        {
            vm.Section = "terminal";
            vm.CursorStyle = "方块";
            vm.CursorBlink = false;
            vm.Scrollback = 20000;
            vm.RightClick = vm.RightClickChoices[1];
            vm.ConfirmPaste = false;
            vm.CopyOnSelect = true;
            // What the ComboBoxes do when they cannot find a value is not a choice.
            vm.CursorStyle = null;
            vm.RightClick = null;
            vm.Scrollback = null;
            Shot(window, "settings-general-terminal");
            vm.SelectThemeCommand.Execute(vm.Presets.Single(p => p.Name == "深蓝"));
            vm.ResetCommand.Execute(null);
            var saved = Appearance.Load(data);
            Assert.Equal("暗夜", saved.Theme);
            Assert.Equal("block", saved.CursorStyle);
            Assert.False(saved.CursorBlink);
            Assert.Equal(20000, saved.Scrollback);
            Assert.False(saved.RightClickMenu);
            Assert.False(saved.ConfirmPaste);
            Assert.True(saved.CopyOnSelect);
            Assert.True(saved.Highlight);
            Assert.Null(saved.HighlightRules);

            // Search reaches the new cards.
            vm.SearchText = "粘贴";
            Assert.True(vm.ShowPaste && vm.ShowRightClick && !vm.ShowCursor);
            vm.SearchText = "正则";
            Assert.Equal("highlight", vm.Section);
            Assert.True(vm.ShowHighlightSection && !vm.ShowTerminalSection);
            vm.SearchText = "常用命令";
            Assert.Equal("quick", vm.Section);
            vm.SearchText = "";
            vm.Section = "no such section";
            Assert.True(vm.IsTheme);
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); Directory.Delete(data, true); }
    }

    [Fact]
    public void UnreadableSettingsAreKeptUnderAnotherName()
    {
        string data = Paths.TempDirectory(), file = Path.Combine(data, "preferences.json");
        try
        {
            foreach (string damaged in new[] { "{", """{"HighlightRules":"x"}""", new string(' ', 600 * 1024) })
            {
                File.WriteAllText(file, damaged);
                Assert.Equal(new StudioPreferences().Validated(), Appearance.Load(data, out string? problem));
                Assert.NotNull(problem);
                Assert.Equal(damaged, File.ReadAllText(file)); // reading changes nothing
                Appearance.Save(data, new StudioPreferences { TerminalScheme = "Nord" });
                Assert.Equal("Nord", Appearance.Load(data, out problem).TerminalScheme);
                Assert.Null(problem);
                string kept = Assert.Single(Directory.GetFiles(data, "preferences.unreadable-*.json"));
                Assert.Equal(damaged, File.ReadAllText(kept));
                Appearance.Save(data, new StudioPreferences()); // a readable file is simply replaced
                Assert.Single(Directory.GetFiles(data, "preferences.unreadable-*.json"));
                File.Delete(kept);
            }
            Assert.Empty(Directory.GetFiles(data, "*.tmp"));
        }
        finally { Directory.Delete(data, true); }
    }

    [AvaloniaFact]
    public void HighlightRulesAreEditedInPlace()
    {
        string data = Paths.TempDirectory();
        Appearance.Apply(new StudioPreferences());
        var vm = new SettingsViewModel(data) { Section = "highlight" };
        var window = new SettingsWindow { DataContext = vm, Width = 900, Height = 640 };
        window.Show();
        try
        {
            Assert.Equal(Highlighter.Defaults().Length, vm.Rules.Count);
            Assert.Null(Appearance.Current.HighlightRules); // looking at them does not fix them
            window.UpdateLayout();
            var first = vm.Rules[0];
            foreach (string text in new[] { "(", "(a", "(ab", "(ab)" }) first.Pattern = text;
            Assert.Same(first, vm.Rules[0]); // the row being typed in is not rebuilt
            Assert.False(first.HasError);
            Assert.Equal("(ab)", Appearance.Current.HighlightRules![0].Pattern);
            first.Pattern = "(ab";
            Assert.Contains("不生效", first.Error);
            first.Color = first.Colors.Single(c => c.Key == "cyan");
            first.Color = null; // a ComboBox losing its list
            vm.Rules[1].Enabled = false;
            first.DownCommand.Execute(null);
            Assert.Same(first, vm.Rules[1]);
            vm.Rules[0].UpCommand.Execute(null); // already first
            vm.AddRuleCommand.Execute(null);
            vm.Rules[^1].Name = "我的";
            vm.Rules[^1].Pattern = "demo\t!";
            Assert.Contains("控制字符", vm.Rules[^1].Error);
            Shot(window, "settings-highlight");
            var saved = Appearance.Load(data).HighlightRules!;
            Assert.Equal(Highlighter.Defaults().Length + 1, saved.Length);
            Assert.Equal("(ab", saved[1].Pattern);
            Assert.Equal("cyan", saved[1].Color);
            Assert.False(saved[0].Enabled);
            Assert.Equal("demo!", saved[^1].Pattern);

            // Another window sees the same rules, and changes there are followed here.
            using (var other = new SettingsViewModel(data))
            {
                Assert.Equal("我的", other.Rules[^1].Name);
                other.Rules[0].RemoveCommand.Execute(null);
                Assert.Equal(other.Rules.Count, vm.Rules.Count);
                Assert.Equal("cyan", vm.Rules[0].Color!.Key);
            }
            while (vm.CanAddRule) vm.AddRuleCommand.Execute(null);
            Assert.Equal(Highlighter.MaxRules, vm.Rules.Count);
            vm.AddRuleCommand.Execute(null);
            Assert.Equal(Highlighter.MaxRules, vm.Rules.Count);
            vm.ResetRulesCommand.Execute(null);
            Assert.Null(Appearance.Load(data).HighlightRules);
            Assert.Equal(Highlighter.Defaults().Length, vm.Rules.Count);
            foreach (var rule in vm.Rules.ToArray()) rule.RemoveCommand.Execute(null);
            Assert.Empty(Appearance.Load(data).HighlightRules!); // none is not the same as the built-in set
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); Directory.Delete(data, true); }
    }

    [AvaloniaFact]
    public void QuickCommandsAreManagedInTheSettings()
    {
        string data = Paths.TempDirectory();
        Appearance.Apply(new StudioPreferences());
        var store = new QuickCommandStore(data);
        var vm = new SettingsViewModel(data, store) { Section = "quick" };
        var window = new SettingsWindow { DataContext = vm, Width = 900, Height = 640 };
        window.Show();
        try
        {
            Assert.True(vm.NoQuickCommands);
            vm.NewQuickCommand.Execute(null);
            Assert.True(vm.IsEditingQuick && !vm.IsExistingQuick);
            vm.QuickText = "df -h";
            vm.SaveQuickCommand.Execute(null);
            Assert.Contains("名称不能为空", vm.QuickError);
            Assert.True(vm.IsEditingQuick); // nothing typed is lost
            vm.QuickName = "磁盘";
            vm.SaveQuickCommand.Execute(null);
            Assert.False(vm.IsEditingQuick || vm.HasQuickError);
            vm.NewQuickCommand.Execute(null);
            vm.QuickName = "重启服务";
            vm.QuickText = "systemctl restart demo\r\nsystemctl status demo";
            vm.QuickConfirm = true;
            vm.SaveQuickCommand.Execute(null);
            vm.QuickDownCommand.Execute(store.Commands[0]);
            Assert.Equal(["重启服务", "磁盘"], new QuickCommandStore(data).Commands.Select(c => c.Name));
            vm.EditQuickCommand.Execute(store.Commands[0]);
            Assert.True(vm.IsExistingQuick && vm.QuickConfirm);
            Shot(window, "settings-quick-commands");

            // Deleted elsewhere while the form is open: saving must not bring it back unnoticed.
            store.Remove(store.Commands[0].Id);
            Assert.False(vm.IsEditingQuick);
            vm.EditQuickCommand.Execute(store.Commands[0]);
            vm.CancelQuickCommand.Execute(null);
            Assert.Single(store.Commands);
            vm.EditQuickCommand.Execute(store.Commands[0]);
            vm.DeleteQuickCommand.Execute(null);
            Assert.Empty(new QuickCommandStore(data).Commands);
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); Directory.Delete(data, true); }
    }

    [AvaloniaFact]
    public async Task ClosedWorkspacesLetGoOfSharedObjects()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var store = new QuickCommandStore(h.Data);
        var (window, w) = await OpenSession(h, fixture, commands: store);
        int Subscribers() => ((Delegate?)typeof(QuickCommandStore).GetField("Changed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(store))?.GetInvocationList().Length ?? 0;
        try
        {
            Assert.Equal(1, Subscribers());
            w.FindInTerminalCommand.Execute(null);
            w.Find.Text = "SYN";
            ((MainWindowViewModel)window.DataContext!).Close(w);
            Assert.Equal(0, Subscribers());
            Assert.Null(w.QuickCommands);
            Assert.False(w.Find.IsOpen);
            store.Upsert(new QuickCommand { Name = "n", Text = "x" }); // nobody left to fail
            w.RunQuickCommand.Execute(store.Commands[0]);
            w.PasteTerminalCommand.Execute(null);
            w.RecordTerminalCommand.Execute(null);
            await Pump(() => false, TimeSpan.FromMilliseconds(200));
            Assert.DoesNotContain("x", Typed(fixture));
        }
        finally { window.Close(); }
    }
}
