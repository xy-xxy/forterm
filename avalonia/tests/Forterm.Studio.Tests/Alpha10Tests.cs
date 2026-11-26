using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
using Porta.Pty;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>alpha.10: the settings of the reference that were missing (lock, background picture, opacity, where the tabs
/// are, terminals that start unseen) and the small gaps that were known (stopping a paste, a quick command that cannot be
/// written, more prompts, find, patterns that look around, the close guard of each window, a jump host that ends, an entry
/// started hidden).</summary>
public sealed class Alpha10Tests
{
    static void Settle(Window window) { for (int i = 0; i < 3; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); } }
    static async Task Pump(Func<bool> until, int seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until()) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
        Dispatcher.UIThread.RunJobs();
    }
    static string Typed(LoopbackFixture f) => Encoding.UTF8.GetString(f.Stats().GetProperty("input").EnumerateArray().SelectMany(h => Convert.FromHexString(h.GetString()!)).ToArray());
    static async Task<WorkspaceViewModel> Launch(StudioHarness h, LoopbackFixture fixture, string title = "唤醒")
    {
        var w = (await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-newtab", title, "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"])))!;
        await Pump(() => w.Terminal is { IsLive: true });
        return w;
    }
    static Point Middle(Control control, Window window) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    sealed class FakeTime : TimeProvider
    {
        long now = 1;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Volatile.Read(ref now);
        public void Pass(double minutes) => Interlocked.Add(ref now, (long)(minutes * 60_000));
    }

    // ---------- close guard of each window ----------

    [AvaloniaFact]
    public void WhatMovedInOneWindowHasNotMovedInAnother()
    {
        CloseGuard.Register();
        var one = new Window { Width = 300, Height = 200, Content = new Border { Background = Brushes.Gray } };
        var two = new Window { Width = 300, Height = 200, Content = new Border { Background = Brushes.Gray } };
        one.Show();
        two.Show();
        try
        {
            CloseGuard.ForgetForTest();
            // Something closed in the first window, where the user clicked.
            one.MouseDown(new Point(50, 50), MouseButton.Left);
            one.MouseUp(new Point(50, 50), MouseButton.Left);
            CloseGuard.Moved();
            long now = Environment.TickCount64;
            Assert.True(CloseGuard.Blocks(one, 1, now));
            Assert.False(CloseGuard.Blocks(two, 1, now));
            Assert.False(CloseGuard.Blocks(one, 1, now + 600));
            Assert.True(CloseGuard.Blocks(one, 2, now + 700)); // its double click
            // The second click of a double click is counted in its own window: one that acted elsewhere is no "first".
            CloseGuard.ForgetForTest();
            Assert.False(CloseGuard.Blocks(one, 1, now));
            Assert.False(CloseGuard.Blocks(two, 2, now + 100));
            Assert.True(CloseGuard.Blocks(two, 2, now + 200));
            // Nothing clicked yet: everywhere, as before.
            CloseGuard.ForgetForTest();
            CloseGuard.Moved();
            now = Environment.TickCount64;
            Assert.True(CloseGuard.Blocks(one, 1, now));
            Assert.True(CloseGuard.Blocks(two, 1, now));
        }
        finally { one.Close(); two.Close(); CloseGuard.ForgetForTest(); }
    }

    // ---------- find ----------

    [AvaloniaFact]
    public void StepsReadTheScrollbackAgainOnlyAfterItChanged()
    {
        var terminal = new StudioTerminalControl();
        var window = new Window { Width = 700, Height = 400, Content = terminal };
        window.Show();
        Settle(window);
        try
        {
            terminal.Terminal.Write(string.Concat(Enumerable.Range(0, 3000).Select(n => $"needle {n}\r\n")));
            var find = new TerminalFindViewModel(() => terminal) { Text = "needle" };
            find.Open();
            Assert.StartsWith("3000/3000", find.Summary);
            for (int i = 0; i < 20; i++) find.PreviousCommand.Execute(null);
            Assert.Equal(0, find.Scans);
            Assert.Equal("2980/3000", find.Summary);
            terminal.Terminal.Write("needle more\r\n");
            find.PreviousCommand.Execute(null);
            Assert.Equal(1, find.Scans);
            Assert.EndsWith("/3001", find.Summary);
            find.NextCommand.Execute(null);
            Assert.Equal(1, find.Scans);
            terminal.ClearScreen();
            find.NextCommand.Execute(null);
            Assert.Equal(2, find.Scans);
        }
        finally { window.Close(); terminal.Dispose(); }
    }

    // ---------- paste ----------

    /// <summary>A server that does not read: the paste waits, and can be stopped. What went out stays; a bracket that was
    /// opened is closed, or the shell would take what is typed next as part of the paste.</summary>
    [AvaloniaFact]
    public async Task APasteTheServerDoesNotTakeCanBeStopped()
    {
        Appearance.Apply(new StudioPreferences { ConfirmPaste = false });
        var slow = TerminalTabViewModel.SlowPaste;
        TerminalTabViewModel.SlowPaste = TimeSpan.FromMilliseconds(100);
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_HOLD"] = "1", ["FIXTURE_WINDOW"] = "8192" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await Launch(h, fixture);
            Settle(window);
            var tab = w.Terminals.Single();
            tab.Control.Terminal.BracketedPasteMode = true;
            string text = string.Concat(Enumerable.Range(0, 3000).Select(n => $"echo line{n:D5}\n"));
            await window.Clipboard!.SetTextAsync(text);
            w.PasteTerminalCommand.Execute(null);
            await Pump(() => w.IsPasteSlow);
            Assert.True(w.IsPasteSlow);
            Settle(window);
            var bar = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PasteBar");
            Assert.True(bar.IsEffectivelyVisible);
            // Another paste meanwhile (here with the keys) is refused, not queued behind it.
            var paste = Iciclecreek.Terminal.TerminalKeyGestures.Paste[0];
            tab.Control.FocusTerminal();
            Settle(window);
            window.KeyPress(paste.Key, (RawInputModifiers)(int)paste.KeyModifiers, PhysicalKey.V, null);
            await Pump(() => w.Status.Contains("上一次粘贴"));
            Assert.Contains("上一次粘贴还没有发送完", w.Status);

            w.CancelPasteCommand.Execute(null);
            Assert.False(w.IsPasteSlow);
            Assert.Contains("正在停止粘贴", w.Status);
            fixture.Command("release-shell");
            await Pump(() => !tab.IsPasting, 15);
            Assert.False(tab.IsPasting);
            Assert.False(w.IsPasteSlow);
            Assert.Contains("已停止粘贴", w.Status);
            await Pump(() => Typed(fixture).EndsWith("\u001b[201~"), 15);
            string typed = Typed(fixture);
            Assert.StartsWith("\u001b[200~echo line00000\r", typed);
            Assert.EndsWith("\u001b[201~", typed);
            string sent = typed["\u001b[200~".Length..^"\u001b[201~".Length];
            Assert.True(sent.Length < text.Length, $"{sent.Length} of {text.Length}");
            Assert.StartsWith(sent, text.Replace('\n', '\r'));

            // The terminal goes on: what is typed now arrives after the closed bracket.
            await tab.WriteAsync("x", byUser: true, CancellationToken.None);
            await Pump(() => Typed(fixture).EndsWith("\u001b[201~x"));
            Assert.EndsWith("\u001b[201~x", Typed(fixture));
        }
        finally { window.Close(); TerminalTabViewModel.SlowPaste = slow; Appearance.Apply(new StudioPreferences()); }
    }

    /// <summary>A paste that goes through normally: in one piece, bracketed, and no bar.</summary>
    [AvaloniaFact]
    public async Task APasteGoesOutWhole()
    {
        Appearance.Apply(new StudioPreferences { ConfirmPaste = false });
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await Launch(h, fixture);
            Settle(window);
            var tab = w.Terminals.Single();
            tab.Control.Terminal.BracketedPasteMode = true;
            // More than one piece, with a pair of surrogates on the border of the first.
            string text = new string('a', IdleWatch.PastePiece - 1) + "😀" + new string('b', 5000);
            await window.Clipboard!.SetTextAsync(text);
            w.PasteTerminalCommand.Execute(null);
            await Pump(() => Typed(fixture).EndsWith("\u001b[201~"), 15);
            Assert.Equal("\u001b[200~" + text + "\u001b[201~", Typed(fixture));
            Assert.False(w.IsPasteSlow);
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    // ---------- quick commands ----------

    /// <summary>A connection whose writing fails while it looks alive.</summary>
    sealed class BrokenWriter : IPtyConnection
    {
        sealed class Waiting : Stream
        {
            readonly TaskCompletionSource never = new();
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => 0;
            public override long Position { get => 0; set { } }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) { never.Task.Wait(); return 0; }
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) { await never.Task.WaitAsync(ct); return 0; }
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { await never.Task.WaitAsync(ct); return 0; }
            public override long Seek(long offset, SeekOrigin origin) => 0;
            public override void SetLength(long value) { }
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
        sealed class Failing : Stream
        {
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => 0;
            public override long Position { get => 0; set { } }
            public override void Flush() { }
            public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
            public override int Read(byte[] buffer, int offset, int count) => 0;
            public override long Seek(long offset, SeekOrigin origin) => 0;
            public override void SetLength(long value) { }
            public override void Write(byte[] buffer, int offset, int count) => throw new IOException("写入通道失败（测试）");
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => ValueTask.FromException(new IOException("写入通道失败（测试）"));
        }
        public Stream ReaderStream { get; } = new Waiting();
        public Stream WriterStream { get; } = new Failing();
        public bool SupportsCancellableRead => true;
        public int Pid => 1;
        public int ExitCode => 0;
        public event EventHandler<PtyExitedEventArgs>? ProcessExited { add { } remove { } }
        public void Resize(int cols, int rows) { }
        public bool WaitForExit(int milliseconds) => false;
        public void Kill() { }
        public void Dispose() { }
    }

    [AvaloniaFact]
    public async Task AQuickCommandThatCannotBeWrittenSaysSo()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await Launch(h, fixture);
            Settle(window);
            var tab = w.Terminals.Single();
            tab.Attach(new BrokenWriter());
            Settle(window);
            await Pump(() => tab.Control.IsLive);
            w.RunQuickCommand.Execute(new QuickCommand { Name = "磁盘", Text = "df -h" });
            await Pump(() => w.Status.Contains("快速命令"));
            Assert.Contains("快速命令没有发送", w.Status);
            Assert.Contains("写入通道失败", w.Status);
            Assert.DoesNotContain("已输入", w.Status);
        }
        finally { window.Close(); }
    }

    // ---------- prompts ----------

    [Theory]
    [InlineData("❯ ", true)]
    [InlineData("~/src/forterm on main ❯ ", true)]
    [InlineData("? Pick one ❯ ", false)]
    [InlineData("1) a ❯", true)]
    [InlineData("a❯", false)]
    [InlineData("➜  ~ ", true)]
    [InlineData("➜  forterm git:(main) ✗ ", true)]
    [InlineData("➜  forterm git:(main) ", true)]
    [InlineData("➜  ~ ls -la", false)]
    [InlineData("➜  forterm git:(main) rm -rf", false)]
    [InlineData("C:\\Users\\deploy>", true)]
    [InlineData("PS C:\\Users\\deploy> ", true)]
    [InlineData("PS /home/deploy> ", true)]
    [InlineData("deploy@web ~> ", true)]
    [InlineData("deploy@web /etc/nginx> ", true)]
    [InlineData("> ", false)]
    [InlineData(">> ", false)]
    [InlineData(">>> ", false)]
    [InlineData("quote> ", false)]
    [InlineData("for quote> ", false)]
    [InlineData("    -> ", false)]
    [InlineData("mysql> ", false)]
    [InlineData("Opt> ", false)]
    [InlineData("Router> ", false)]
    [InlineData("<HUAWEI>", false)]
    [InlineData("Continue? > ", false)]
    [InlineData("C:\\Users\\deploy>dir", false)]
    public void MorePromptsAreKnown(string line, bool prompt) => Assert.Equal(prompt, StudioTerminalControl.LooksLikePrompt(line));

    [Fact]
    public void APromptOnTheRightIsNotText()
    {
        static string[] Cells(string text) => text.Select(c => c.ToString()).ToArray();
        // zsh's RPROMPT: apart from the cursor, against the right edge (one column left free).
        Assert.True(StudioTerminalControl.IsRightPrompt(Cells("          [10:31:22]"), 59, 80));
        Assert.True(StudioTerminalControl.IsRightPrompt(Cells("                    ~/src"), 55, 80));
        // Typed and stepped back over: right behind the cursor, or not at the edge.
        Assert.False(StudioTerminalControl.IsRightPrompt(Cells("ls -la"), 20, 80));
        Assert.False(StudioTerminalControl.IsRightPrompt(Cells(" x"), 77, 80));
        Assert.False(StudioTerminalControl.IsRightPrompt(Cells("   echo hi"), 20, 80));
        Assert.False(StudioTerminalControl.IsRightPrompt(Cells("    "), 20, 80));
        // Most of the line is not a prompt on the side.
        Assert.False(StudioTerminalControl.IsRightPrompt(Cells("  " + new string('x', 60)), 17, 80));
    }

    [AvaloniaFact]
    public void TheScreenIsReadWithItsRightPrompt()
    {
        var terminal = new StudioTerminalControl();
        var window = new Window { Width = 700, Height = 400, Content = terminal };
        window.Show();
        Settle(window);
        try
        {
            int cols = terminal.Terminal.Cols;
            // A prompt, the cursor after it, and a clock on the right that zsh puts there and comes back from.
            terminal.Terminal.Write("root@web:~# \u001b7\u001b[1;" + (cols - 7) + "H[10:31]\u001b8");
            Assert.True(terminal.ReadPlace().AtPrompt);
            // Something typed behind the cursor is no prompt on the side.
            terminal.Terminal.Write("\u001b[2J\u001b[Hroot@web:~# \u001b7ls -la\u001b8");
            Assert.False(terminal.ReadPlace().AtPrompt);
        }
        finally { window.Close(); terminal.Dispose(); }
    }

    // ---------- editor patterns ----------

    [AvaloniaFact]
    public async Task APatternThatLooksAroundIsStoppedWhenItTakesTooLong()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1240, Height = 720, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "回环主机") with { DisablePwdFollow = true, DisableListMonitor = true });
            string local = Path.Combine(h.Data, "slow.txt");
            File.WriteAllText(local, new string('a', 4000) + "\nkey=value\nkey=value\n");
            await w!.DropAsync("/", [local]);
            await Pump(() => w.FileRoots.Any(n => n.Name == "slow.txt"), 15);
            var tab = (await w.OpenEditorAsync(w.FileRoots.First(n => n.Name == "slow.txt")))!;
            Settle(window);
            var find = tab.Find;
            find.Open(replace: false);
            find.UseRegex = true;
            // Back-references and looking around use the engine that tries one way after another.
            find.Query = "^(\\w+)=(?!\\1)";
            Assert.Equal(2, find.Matches.Count);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            find.Query = "^(a+)+\\1(?=b)";
            clock.Stop();
            Assert.True(find.IsBad, find.Status);
            Assert.Contains("太慢", find.Status);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), clock.Elapsed.ToString());
        }
        finally { window.Close(); }
    }

    // ---------- a jump host that ends ----------

    static async Task<(SharedSshSession Session, SshTrace Trace)> ThroughJump(StudioHarness h, LoopbackFixture jump, LoopbackFixture target)
    {
        var hop = h.Ssh(jump.Port, "跳板一号");
        var end = h.Ssh(target.Port, "目标机", true, hop.Id);
        var trace = new SshTrace();
        var session = await h.Connector.ConnectSshAsync(h.Store.Find(end.Id)!, CancellationToken.None, trace: trace);
        return (session, trace);
    }

    [Fact]
    public async Task AJumpHostThatEndsIsNamedAsTheCause()
    {
        using var jump = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_FORWARD"] = "1", ["FIXTURE_TEXT"] = "jumpgate maintenance" });
        using var target = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var (session, trace) = await ThroughJump(h, jump, target);
        using (session)
        {
            Assert.True(session.IsConnected);
            jump.Command("bye");
            var end = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < end && session.CloseReason == null) await Task.Delay(20);
            Assert.NotNull(session.CloseReason);
            Assert.StartsWith("跳板机的连接先断开了：", session.CloseReason);
            Assert.Contains("jumpgate maintenance", session.CloseReason);
            Assert.Contains("经它到这台服务器的连接也随之中断", session.CloseReason);
            Assert.Equal(session.CloseReason, trace.Explanation);
            string report = trace.Report(["Forterm Studio"], []);
            Assert.Contains("结果：跳板机的连接先断开了", report);
            Assert.DoesNotContain("127.0.0.1", report);
            Assert.DoesNotContain("跳板一号", report);
            Assert.DoesNotContain("synthetic", report);
        }
    }

    [Fact]
    public async Task ATargetThatEndsIsNotBlamedOnTheJumpHost()
    {
        using var jump = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_FORWARD"] = "1" });
        using var target = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_TEXT"] = "target closed" });
        using var h = new StudioHarness();
        var (session, trace) = await ThroughJump(h, jump, target);
        using (session)
        {
            target.Command("bye");
            var end = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < end && session.CloseReason == null) await Task.Delay(20);
            Assert.NotNull(session.CloseReason);
            Assert.DoesNotContain("跳板机", session.CloseReason);
            Assert.Contains("target closed", session.CloseReason);
        }
    }

    // ---------- lock ----------

    [Fact]
    public void TheLockPasswordIsKeptOnlyAsASlowCheck()
    {
        string check = LockPassword.Make("correct horse");
        Assert.StartsWith("pbkdf2-sha256$300000$", check);
        Assert.DoesNotContain("correct", check);
        Assert.True(LockPassword.Check("correct horse", check));
        Assert.False(LockPassword.Check("correct horsE", check));
        Assert.False(LockPassword.Check("", check));
        Assert.NotEqual(check, LockPassword.Make("correct horse")); // salted
        Assert.False(LockPassword.IsCheck("plain"));
        Assert.False(LockPassword.IsCheck("pbkdf2-sha256$10$AAAAAAAAAAAAAAAAAAAAAA==$" + Convert.ToBase64String(new byte[32]))); // too few rounds
        Assert.False(LockPassword.Check("x", "pbkdf2-sha256$10$AAAAAAAAAAAAAAAAAAAAAA==$" + Convert.ToBase64String(new byte[32])));
    }

    [AvaloniaFact]
    public async Task LockedNothingReachesTheSessions()
    {
        AppLock.ForgetForTest();
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await Launch(h, fixture);
            Settle(window);
            Assert.Null(await AppLock.SetPasswordAsync("", "correct horse"));
            Assert.Null(h.Vault.Items.Keys.FirstOrDefault(k => k.Contains("password") && k.Contains("lock")));
            Assert.Contains("studio/lock/verifier", h.Vault.Items.Keys);
            w.Terminal!.FocusTerminal();
            Settle(window);

            // A menu that is open is closed by the lock: it would be drawn over the lock screen.
            var quick = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "QuickButton");
            quick.Flyout!.ShowAt(quick);
            Assert.True(quick.Flyout.IsOpen);

            h.Model.LockCommand.Execute(null);
            await Pump(() => AppLock.IsLocked);
            Settle(window);
            var view = window.GetVisualDescendants().OfType<LockView>().Single();
            Assert.True(view.IsEffectivelyVisible);
            Assert.False(quick.Flyout.IsOpen);
            // What is under it is hidden, not only covered: nothing of it for a screen reader to read or press.
            var paletteButton = window.FindControl<Button>("PaletteButton")!;
            Assert.False(paletteButton.IsEffectivelyVisible);
            Assert.False(w.Terminal!.IsEffectivelyVisible);
            static IEnumerable<Avalonia.Automation.Peers.AutomationPeer> Peers(Avalonia.Automation.Peers.AutomationPeer peer) => peer.GetChildren().SelectMany(c => new[] { c }.Concat(Peers(c)));
            var owners = Peers(Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(window)).OfType<Avalonia.Automation.Peers.ControlAutomationPeer>().Select(p => p.Owner).ToArray();
            Assert.DoesNotContain(paletteButton, owners);
            Assert.Contains(owners, o => o is TextBox { Name: "PasswordBox" });
            window.CaptureRenderedFrame()?.Dispose();
            // A click on the terminal lands on the lock screen.
            var hit = window.InputHitTest(new Point(600, 400)) as Visual;
            Assert.NotNull(hit?.FindAncestorOfType<LockView>(includeSelf: true));
            // Typing goes into the password box, never to the server.
            window.KeyTextInput("whoami");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Pump(() => view.FindControl<TextBlock>("ProblemText")!.IsVisible);
            Assert.True(AppLock.IsLocked);
            Assert.Equal("密码不对", view.FindControl<TextBlock>("ProblemText")!.Text);
            Assert.Equal("", Typed(fixture));
            // The shortcuts of the window do nothing either.
            int sessions = h.Model.Workspaces.Count;
            var palette = Shortcuts.Palette.Gesture;
            window.KeyPress(palette.Key, (RawInputModifiers)(int)palette.KeyModifiers, PhysicalKey.None, null);
            Assert.False(h.Model.Palette.IsOpen);

            window.KeyTextInput("correct horse");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Pump(() => !AppLock.IsLocked);
            Settle(window);
            Assert.False(AppLock.IsLocked);
            Assert.False(view.IsVisible);
            Assert.Equal(sessions, h.Model.Workspaces.Count);
            // The keyboard is where it was: the terminal.
            window.KeyTextInput("ok");
            await Pump(() => Typed(fixture).Contains("ok"));
            Assert.Equal("ok", Typed(fixture));
            Assert.True(w.IsConnected);
        }
        finally { window.Close(); AppLock.ForgetForTest(); }
    }

    [AvaloniaFact]
    public async Task EveryOtherWindowIsCoveredWhileLocked()
    {
        AppLock.ForgetForTest();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1000, Height = 600, DataContext = h.Model };
        window.Show();
        var field = new TextBox { Text = "abc" };
        var before = new Window { Width = 400, Height = 300, Content = field };
        before.Show();
        Window? later = null;
        try
        {
            Assert.Null(await AppLock.SetPasswordAsync("", "abcd"));
            Assert.Null(await AppLock.LockAsync());
            Settle(before);
            var cover = Assert.Single(OverlayLayer.GetOverlayLayer(before)!.Children.OfType<LockCover>());
            Assert.Equal(before.ClientSize.Width, cover.Width);
            // Its field had the keyboard: what is typed there now goes nowhere.
            field.Focus();
            before.KeyTextInput("secret");
            before.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            Assert.Equal("abc", field.Text);
            Assert.False(field.IsEffectivelyVisible);
            // A dialog that opens meanwhile and waits for an answer (a host key question of a launch, say) is covered too,
            // and since it keeps the main window from taking keys, it can be unlocked from there.
            later = new Window { Width = 360, Height = 260, Content = new TextBlock { Text = "后来的" } };
            _ = later.ShowDialog(window);
            Settle(later);
            var second = Assert.Single(OverlayLayer.GetOverlayLayer(later)!.Children.OfType<LockCover>());
            later.CaptureRenderedFrame()?.Dispose();
            Assert.IsType<LockCover>((later.InputHitTest(new Point(150, 100)) as Visual)?.GetSelfAndVisualAncestors().OfType<LockCover>().FirstOrDefault());
            var box = second.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PasswordBox");
            box.Focus();
            later.KeyTextInput("abcd");
            later.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Pump(() => !AppLock.IsLocked);
            Assert.False(AppLock.IsLocked);
            Assert.True(field.IsEffectivelyVisible);
            Assert.Empty(OverlayLayer.GetOverlayLayer(before)!.Children.OfType<LockCover>());
            Assert.Empty(OverlayLayer.GetOverlayLayer(later)!.Children.OfType<LockCover>());
        }
        finally { later?.Close(); before.Close(); window.Close(); AppLock.ForgetForTest(); }
    }

    [AvaloniaFact]
    public async Task StudioLocksByItselfAfterAWhileWithoutTheUser()
    {
        AppLock.ForgetForTest();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1000, Height = 600, DataContext = h.Model };
        window.Show();
        var time = new FakeTime();
        try
        {
            AppLock.Time = time;
            Assert.Null(await AppLock.SetPasswordAsync("", "abcd"));
            Appearance.Apply(new StudioPreferences { LockMinutes = 10 });
            window.MouseMove(new Point(100, 100));
            time.Pass(9);
            AppLock.Tick();
            await Pump(() => AppLock.IsLocked, 1);
            Assert.False(AppLock.IsLocked);
            // The user is there: it starts again.
            window.MouseMove(new Point(120, 100));
            time.Pass(9);
            AppLock.Tick();
            await Pump(() => AppLock.IsLocked, 1);
            Assert.False(AppLock.IsLocked);
            time.Pass(1.5);
            AppLock.Tick();
            await Pump(() => AppLock.IsLocked);
            Assert.True(AppLock.IsLocked);
            // Moving the mouse over a locked window is not the user coming back.
            Assert.True((await AppLock.UnlockAsync("abcd")).Unlocked);
            Appearance.Apply(new StudioPreferences());
            time.Pass(100);
            AppLock.Tick();
            await Pump(() => AppLock.IsLocked, 1);
            Assert.False(AppLock.IsLocked);
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); AppLock.ForgetForTest(); }
    }

    [AvaloniaFact]
    public async Task WrongPasswordsWaitLongerEachTime()
    {
        AppLock.ForgetForTest();
        using var h = new StudioHarness();
        _ = h.Model;
        var time = new FakeTime();
        try
        {
            AppLock.Time = time;
            Assert.Null(await AppLock.SetPasswordAsync("", "abcd"));
            Assert.Null(await AppLock.LockAsync());
            for (int i = 1; i < AppLock.FreeTries; i++) Assert.Equal((false, "密码不对"), await AppLock.UnlockAsync("nope"));
            var fifth = await AppLock.UnlockAsync("nope");
            Assert.Contains("30 秒后再试", fifth.Say);
            // Not even the right one, until the time is up.
            Assert.Contains("秒后再试", (await AppLock.UnlockAsync("abcd")).Say);
            Assert.True(AppLock.IsLocked);
            time.Pass(0.5);
            Assert.Contains("60 秒后再试", (await AppLock.UnlockAsync("nope")).Say);
            time.Pass(1);
            Assert.True((await AppLock.UnlockAsync("abcd")).Unlocked);
            // Counted anew.
            Assert.Null(await AppLock.LockAsync());
            Assert.Equal((false, "密码不对"), await AppLock.UnlockAsync("nope"));
            Assert.True((await AppLock.UnlockAsync("abcd")).Unlocked);
        }
        finally { AppLock.ForgetForTest(); }
    }

    [AvaloniaFact]
    public async Task ThePasswordIsChangedAndRemovedOnlyWithTheOldOne()
    {
        AppLock.ForgetForTest();
        using var h = new StudioHarness();
        _ = h.Model;
        try
        {
            Assert.Contains("还没有设置", await AppLock.LockAsync());
            Assert.False(AppLock.IsLocked);
            Assert.Contains("至少 4", await AppLock.SetPasswordAsync("", "abc"));
            Assert.Null(await AppLock.SetPasswordAsync("", "abcd"));
            Assert.Equal("现在的密码不对", await AppLock.SetPasswordAsync("wrong", "efgh"));
            Assert.Equal("现在的密码不对", await AppLock.RemovePasswordAsync("wrong"));
            Assert.Null(await AppLock.SetPasswordAsync("abcd", "efgh"));
            Assert.Null(await AppLock.LockAsync());
            Assert.False((await AppLock.UnlockAsync("abcd")).Unlocked);
            Assert.True((await AppLock.UnlockAsync("efgh")).Unlocked);
            Assert.Null(await AppLock.RemovePasswordAsync("efgh"));
            Assert.DoesNotContain("studio/lock/verifier", h.Vault.Items.Keys);
            Assert.NotNull(await AppLock.LockAsync());

            // Locked at the start without a password: it lets go by itself.
            AppLock.LockAtStart();
            Assert.True(AppLock.IsLocked);
            await Pump(() => !AppLock.IsLocked, 5);
            Assert.False(AppLock.IsLocked);
        }
        finally { AppLock.ForgetForTest(); }
    }

    [AvaloniaFact]
    public void ABackupOfSomebodyElseDoesNotTakeTheLockAway()
    {
        using var h = new StudioHarness();
        var mine = new StudioPreferences { LockMinutes = 0, LockAtStart = false, BackgroundImage = "background-ffffffff.png", WindowOpacity = 0.8 };
        string file = Path.Combine(h.Data, "backup.json");
        StudioBackup.Write(file, StudioBackup.Create(mine, new QuickCommandStore(null), h.Store, DateTimeOffset.Now));
        var here = new StudioPreferences { LockMinutes = 15, LockAtStart = true, BackgroundImage = "background-0123abcd.png" };
        try
        {
            Appearance.Apply(here);
            StudioBackup.Read(file).Restore(new RestoreChoice(Settings: true, QuickCommands: false, Connections: false, Trusted: false), new QuickCommandStore(null), h.Store, h.Data, DateTime.Now);
            Assert.Equal(15, Appearance.Current.LockMinutes);
            Assert.True(Appearance.Current.LockAtStart);
            Assert.Equal("background-0123abcd.png", Appearance.Current.BackgroundImage);
            Assert.Equal(0.8, Appearance.Current.WindowOpacity);
            // One's own backup brings the lock settings back, but the picture is a file of this computer.
            Appearance.Apply(here);
            StudioBackup.Read(file).Restore(new RestoreChoice(Settings: true, QuickCommands: false, Connections: false, Trusted: true), new QuickCommandStore(null), h.Store, h.Data, DateTime.Now);
            Assert.Equal(0, Appearance.Current.LockMinutes);
            Assert.Equal("background-0123abcd.png", Appearance.Current.BackgroundImage);
        }
        finally { Appearance.Apply(new StudioPreferences()); }
    }

    // ---------- background picture and opacity ----------

    static string Picture(string folder, string name, int width, int height)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        using (var pixels = bitmap.Lock())
        {
            var row = new int[width];
            Array.Fill(row, unchecked((int)0xFF3366CC));
            for (int y = 0; y < height; y++) System.Runtime.InteropServices.Marshal.Copy(row, 0, pixels.Address + y * pixels.RowBytes, width);
        }
        string path = Path.Combine(folder, name);
        bitmap.Save(path);
        return path;
    }

    /// <summary>A PNG that says it is huge, with nothing behind its header.</summary>
    static string HugeHeader(string folder)
    {
        static uint Crc(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1; }
            return ~crc;
        }
        static byte[] Big(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
        byte[] head = [.. "IHDR"u8, .. Big(16000), .. Big(16000), 8, 6, 0, 0, 0];
        byte[] end = [.. "IEND"u8];
        var packed = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(packed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true)) z.Write(new byte[64]);
        byte[] data = [.. "IDAT"u8, .. packed.ToArray()];
        byte[] png = [0x89, .. "PNG\r\n\u001a\n"u8, .. Big(13), .. head, .. Big(Crc(head)), .. Big((uint)(data.Length - 4)), .. data, .. Big(Crc(data)), .. Big(0), .. end, .. Big(Crc(end))];
        string path = Path.Combine(folder, "huge.png");
        File.WriteAllBytes(path, png);
        return path;
    }

    [AvaloniaFact]
    public async Task APictureIsCopiedSmallerAndPlain()
    {
        using var h = new StudioHarness();
        string wide = Picture(h.Data, "wide-source.png", 5000, 200);
        string name = await Backdrop.ImportAsync(wide, h.Data);
        Assert.True(Backdrop.IsName(name));
        using (var copy = new Bitmap(Path.Combine(h.Data, name)))
        {
            Assert.Equal(3840, copy.PixelSize.Width);
            Assert.Equal(154, copy.PixelSize.Height, 1.0);
        }
        string tall = await Backdrop.ImportAsync(Picture(h.Data, "tall.png", 300, 8000), h.Data);
        using (var copy = new Bitmap(Path.Combine(h.Data, tall)))
            Assert.True(copy.PixelSize.Height <= 3840 && copy.PixelSize.Width == 144, copy.PixelSize.ToString());
        string small = await Backdrop.ImportAsync(Picture(h.Data, "small.png", 300, 200), h.Data);
        Assert.NotEqual(name, small);
        Backdrop.Forget(h.Data, small);
        Assert.False(File.Exists(Path.Combine(h.Data, name)));
        Assert.False(File.Exists(Path.Combine(h.Data, tall)));
        Assert.True(File.Exists(Path.Combine(h.Data, small)));
        Assert.True(File.Exists(wide)); // the original is left alone

        var huge = await Assert.ThrowsAsync<IOException>(() => Backdrop.ImportAsync(HugeHeader(h.Data), h.Data));
        Assert.Contains("图片太大", huge.Message);
        File.WriteAllText(Path.Combine(h.Data, "not.png"), "not a picture");
        var not = await Assert.ThrowsAsync<IOException>(() => Backdrop.ImportAsync(Path.Combine(h.Data, "not.png"), h.Data));
        Assert.Contains("不是能识别的图片", not.Message);
        Assert.Single(Directory.GetFiles(h.Data, "background-*.png"));
        // Only names it makes itself are read.
        Assert.Equal("", new StudioPreferences { BackgroundImage = "../preferences.json" }.Validated().BackgroundImage);
        Assert.Equal("", new StudioPreferences { BackgroundImage = "background-0123abcd.png\n" }.Validated().BackgroundImage);
    }

    [AvaloniaFact]
    public async Task ThePictureShowsThroughTheColumnsAndTheTerminal()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        string name = await Backdrop.ImportAsync(Picture(h.Data, "p.png", 400, 300), h.Data);
        var saved = Environment.GetEnvironmentVariable("FORTERM_STUDIO_DATA");
        Environment.SetEnvironmentVariable("FORTERM_STUDIO_DATA", h.Data);
        var window = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await Launch(h, fixture);
            Appearance.Apply(new StudioPreferences { BackgroundImage = name, BackgroundStrength = 0.4 });
            Settle(window);
            var image = window.FindControl<Image>("BackdropImage")!;
            Assert.True(image.IsVisible);
            Assert.NotNull(image.Source);
            Assert.True(Application.Current!.TryGetResource("St.Panel", Application.Current.ActualThemeVariant, out var panel));
            Assert.Equal(0.6, ((ISolidColorBrush)panel!).Opacity, 3);
            Assert.Equal(0.6, ((ISolidColorBrush)w.Terminal!.Background!).Opacity, 3);
            // Dialogs stay solid, and so does the lock screen: nothing of the sessions shows through it.
            Assert.True(Application.Current.TryGetResource("St.Dialog", Application.Current.ActualThemeVariant, out var dialog));
            Assert.Equal(1, ((ISolidColorBrush)dialog!).Opacity);
            var lockView = window.GetVisualDescendants().OfType<LockView>().Single();
            Assert.Equal(1, ((ISolidColorBrush)lockView.Background!).Opacity);
            Assert.Equal(255, ((ISolidColorBrush)lockView.Background!).Color.A);
            Appearance.Apply(new StudioPreferences());
            Settle(window);
            Assert.False(image.IsVisible);
            Assert.True(Application.Current.TryGetResource("St.Panel", Application.Current.ActualThemeVariant, out panel));
            Assert.Equal(1, ((ISolidColorBrush)panel!).Opacity);
            Assert.Equal(1, ((ISolidColorBrush)w.Terminal!.Background!).Opacity);

            Appearance.Apply(new StudioPreferences { WindowOpacity = 0.75 });
            Assert.Equal(0.75, window.Opacity);
            Assert.Contains(WindowTransparencyLevel.Transparent, window.TransparencyLevelHint);
            Appearance.Apply(new StudioPreferences());
            Assert.Equal(1, window.Opacity);
            Assert.DoesNotContain(WindowTransparencyLevel.Transparent, window.TransparencyLevelHint);
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); Environment.SetEnvironmentVariable("FORTERM_STUDIO_DATA", saved); }
    }

    // ---------- where the tabs are, and terminals that start unseen ----------

    [AvaloniaFact]
    public async Task TheTerminalTabsCanBeBelow()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await Launch(h, fixture);
            Settle(window);
            var pane = window.GetVisualDescendants().OfType<StudioPane>().Single(p => p.Name == "TerminalPane");
            Control Header() => pane.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Header");
            double above = Header().TranslatePoint(default, window)!.Value.Y, terminal = w.Terminal!.TranslatePoint(default, window)!.Value.Y;
            Assert.True(above < terminal);
            Appearance.Apply(new StudioPreferences { TabsAtBottom = true });
            Settle(window);
            Assert.True(pane.HeaderAtBottom);
            Assert.True(Header().TranslatePoint(default, window)!.Value.Y > w.Terminal!.TranslatePoint(default, window)!.Value.Y + w.Terminal.Bounds.Height - 1);
            Appearance.Apply(new StudioPreferences());
            Settle(window);
            Assert.Equal(above, Header().TranslatePoint(default, window)!.Value.Y, 1.0);
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ATerminalCanStartBeforeItIsShown(bool early)
    {
        var delay = TerminalWarmUp.Delay;
        TerminalWarmUp.Delay = TimeSpan.FromMilliseconds(50);
        Appearance.Apply(new StudioPreferences { StartHiddenTerminals = early });
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await Launch(h, fixture);
            Settle(window);
            h.Model.Selected = h.Model.Home;
            Settle(window);
            w.AddTerminalCommand.Execute(null);
            await Pump(() => w.Terminals.Count == 2 && w.Terminals[1].HasConnection);
            var unseen = w.Terminals[1];
            await Pump(() => !unseen.Control.HasPendingConnection && unseen.Control.Parent == null, 3);
            Assert.Equal(early, !unseen.Control.HasPendingConnection);
            Assert.Null(unseen.Control.Parent);
            // Shown later, in its place, without trouble.
            h.Model.Selected = w;
            Settle(window);
            await Pump(() => unseen.Control.IsLive);
            Assert.True(unseen.Control.IsLive);
            Assert.NotNull(unseen.Control.GetVisualParent());
            Assert.True(unseen.Control.Bounds.Width > 100);
        }
        finally { window.Close(); TerminalWarmUp.Delay = delay; Appearance.Apply(new StudioPreferences()); }
    }

    /// <summary>A terminal still in the hidden place when it is shown, chosen or closed: it is let go first (a control has
    /// one parent).</summary>
    [AvaloniaFact]
    public async Task ATerminalBeingStartedUnseenCanBeShownChosenAndClosed()
    {
        var (delay, hold) = (TerminalWarmUp.Delay, TerminalWarmUp.Hold);
        (TerminalWarmUp.Delay, TerminalWarmUp.Hold) = (TimeSpan.FromMilliseconds(30), TimeSpan.FromSeconds(30));
        Appearance.Apply(new StudioPreferences { StartHiddenTerminals = true });
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await Launch(h, fixture);
            Settle(window);
            var host = window.FindControl<Canvas>("WarmUpHost")!;
            h.Model.Selected = h.Model.Home;
            Settle(window);
            w.AddTerminalCommand.Execute(null);
            await Pump(() => w.Terminals.Count == 2 && w.Terminals[1].HasConnection);
            w.AddTerminalCommand.Execute(null);
            await Pump(() => w.Terminals.Count == 3 && w.Terminals.Skip(1).All(t => t.HasConnection));
            Assert.Equal(3, w.Terminals.Count);
            await Pump(() => host.Children.Count == 2);
            Assert.Equal(2, host.Children.Count);
            var (second, third) = (w.Terminals[1], w.Terminals[2]);
            Assert.Same(third, w.ActiveTab);
            // Shown with its session.
            h.Model.Selected = w;
            Settle(window);
            Assert.NotSame(host, third.Control.Parent);
            Assert.NotNull(third.Control.GetVisualParent());
            // The other one chosen while it is still there.
            w.ActiveTab = second;
            Settle(window);
            Assert.NotSame(host, second.Control.Parent);
            Assert.True(second.Control.IsEffectivelyVisible);
            Assert.Empty(host.Children);
            // Closed while it is there.
            h.Model.Selected = h.Model.Home;
            w.AddTerminalCommand.Execute(null);
            await Pump(() => host.Children.Count == 1);
            Assert.Single(host.Children);
            var fourth = w.Terminals[3];
            fourth.CloseCommand!.Execute(null);
            Settle(window);
            Assert.Empty(host.Children);
            Assert.True(fourth.Control.IsDisposed);
        }
        finally { window.Close(); (TerminalWarmUp.Delay, TerminalWarmUp.Hold) = (delay, hold); Appearance.Apply(new StudioPreferences()); }
    }

    /// <summary>A second main window opens, or the first one closes, while a terminal is laid out unseen in the first one.</summary>
    [AvaloniaFact]
    public async Task ATerminalBeingStartedUnseenIsLetGoWhateverWindowItIsIn()
    {
        var (delay, hold) = (TerminalWarmUp.Delay, TerminalWarmUp.Hold);
        (TerminalWarmUp.Delay, TerminalWarmUp.Hold) = (TimeSpan.FromMilliseconds(30), TimeSpan.FromSeconds(30));
        Appearance.Apply(new StudioPreferences { StartHiddenTerminals = true });
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        using var other = new StudioHarness();
        var window = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        window.Show();
        MainWindow? second = null;
        try
        {
            var w = await Launch(h, fixture);
            Settle(window);
            var host = window.FindControl<Canvas>("WarmUpHost")!;
            h.Model.Selected = h.Model.Home;
            Settle(window);
            w.AddTerminalCommand.Execute(null);
            await Pump(() => host.Children.Count == 1);
            var parked = w.Terminals[1];
            Assert.Same(host, parked.Control.Parent);
            second = new MainWindow { Width = 900, Height = 600, DataContext = other.Model };
            second.Show();
            // Shown in its own window although another one is newer.
            h.Model.Selected = w;
            Settle(window);
            Assert.NotSame(host, parked.Control.Parent);
            Assert.True(parked.Control.IsEffectivelyVisible);
            // Laid out unseen in the window that closes: let go.
            h.Model.Selected = h.Model.Home;
            w.AddTerminalCommand.Execute(null);
            var secondHost = second.FindControl<Canvas>("WarmUpHost")!;
            await Pump(() => secondHost.Children.Count == 1);
            var third = w.Terminals[2];
            Assert.Same(secondHost, third.Control.Parent);
            second.Close();
            second = null;
            Assert.Null(third.Control.Parent);
            h.Model.Selected = w;
            Settle(window);
            Assert.True(third.Control.IsEffectivelyVisible);
        }
        finally { second?.Close(); window.Close(); (TerminalWarmUp.Delay, TerminalWarmUp.Hold) = (delay, hold); Appearance.Apply(new StudioPreferences()); }
    }

    // ---------- the settings page ----------

    [AvaloniaFact]
    public void TheNewSettingsCanBeFound()
    {
        using var h = new StudioHarness();
        using var settings = new SettingsViewModel(h.Data);
        settings.SearchText = "锁屏";
        Assert.True(settings.ShowLockSection);
        Assert.Equal("lock", settings.Section);
        settings.SearchText = "背景 图片";
        Assert.True(settings.Shown["bgimage"]);
        settings.SearchText = "不透明度";
        Assert.True(settings.Shown["opacity"]);
        settings.SearchText = "标签渲染";
        Assert.True(settings.Shown["tabrender"]);
        settings.SearchText = "本地终端 标签";
        Assert.True(settings.Shown["tabpos"]);
        settings.SearchText = "";
        settings.TabPosition = "底部";
        Assert.True(Appearance.Current.TabsAtBottom);
        settings.TabRender = SettingsViewModel.TabRenders[1];
        Assert.True(Appearance.Current.StartHiddenTerminals);
        settings.LockChoice = "15 分钟";
        Assert.Equal(15, Appearance.Current.LockMinutes);
        settings.OpacityPercent = 64;
        Assert.Equal(0.64, Appearance.Current.WindowOpacity);
        settings.OpacityPercent = 10;
        Assert.Equal(0.3, Appearance.Current.WindowOpacity);
        Appearance.Apply(new StudioPreferences());
    }
}
