using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Studio.Controls;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>0.4.0-alpha.11: what alpha.10 left as limits — a transparent window ("全局透明度"), questions of connections
/// that wait under the lock screen, a stopped paste that never ends inside a character, and the close guard of the window
/// where something went away.</summary>
public sealed class Alpha11Tests
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

    // ---------- pastes stop between characters ----------

    [Fact]
    public void APieceIsWholeCharactersOfAtMostAKilobyte()
    {
        Assert.Equal(1024, IdleWatch.PieceLength(new string('a', 5000), 0));
        Assert.Equal(3, IdleWatch.PieceLength("abc", 0));
        // 3 bytes each: 341 of them are 1023 bytes, one more would be 1026.
        Assert.Equal(341, IdleWatch.PieceLength(new string('汉', 1000), 0));
        // A pair of surrogates is one character of 4 bytes: never half of it.
        string pairs = string.Concat(Enumerable.Repeat("😀", 600));
        Assert.Equal(512, IdleWatch.PieceLength(pairs, 0));
        Assert.Equal(512, IdleWatch.PieceLength(pairs, 2));
        string edge = new string('a', 1023) + "😀b";
        Assert.Equal(1023, IdleWatch.PieceLength(edge, 0));
        Assert.Equal(3, IdleWatch.PieceLength(edge, 1023));
        // Whatever the text, the pieces put together are the text, and each is valid UTF-8 on its own.
        var random = new Random(7);
        string mixed = new string(Enumerable.Range(0, 20000).Select(_ => random.Next(6) switch { 0 => 'é', 1 => '汉', 2 => '\uD83D', 3 => '\uDE00', _ => (char)random.Next(32, 127) }).ToArray());
        var pieces = new List<string>();
        for (int at = 0; at < mixed.Length; at += pieces[^1].Length)
        {
            pieces.Add(mixed.Substring(at, IdleWatch.PieceLength(mixed, at)));
            Assert.True(Encoding.UTF8.GetByteCount(pieces[^1]) <= IdleWatch.PastePiece);
            Assert.False(char.IsHighSurrogate(pieces[^1][^1]) && at + pieces[^1].Length < mixed.Length && char.IsLowSurrogate(mixed[at + pieces[^1].Length]));
        }
        Assert.Equal(mixed, string.Concat(pieces));
    }

    /// <summary>A server that does not read: the paste is stopped while a piece of it is half out. That piece goes out
    /// whole when the server reads again — never half a character — then the closing bracket; nothing after it. Until
    /// then the bar says what still waits, with nothing to press.</summary>
    [AvaloniaFact]
    public async Task AStoppedPasteEndsBetweenCharacters()
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
            // 3 bytes each: the window of 8192 ends inside a character.
            string text = new string('汉', 20000);
            await window.Clipboard!.SetTextAsync(text);
            w.PasteTerminalCommand.Execute(null);
            await Pump(() => w.IsPasteSlow);
            Assert.True(w.IsPasteSlow);

            w.CancelPasteCommand.Execute(null);
            Settle(window);
            Assert.True(w.IsPasteStopping);
            Assert.True(w.IsPasteBarShown);
            var bar = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PasteBar");
            Assert.True(bar.IsEffectivelyVisible);
            Assert.True(bar.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PasteStoppingText").IsEffectivelyVisible);
            Assert.DoesNotContain(bar.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible);
            Assert.True(tab.IsPasting);

            fixture.Command("release-shell");
            await Pump(() => !tab.IsPasting, 15);
            Assert.False(w.IsPasteBarShown);
            Assert.Contains("已补上结束标记", w.Status);
            await Pump(() => Typed(fixture).EndsWith("\u001b[201~"), 15);
            string typed = Typed(fixture);
            Assert.DoesNotContain('�', typed);
            string sent = typed["\u001b[200~".Length..^"\u001b[201~".Length];
            Assert.True(sent.Length < text.Length, $"{sent.Length} of {text.Length}");
            Assert.Equal(new string('汉', sent.Length), sent);
            Assert.Equal(0, sent.Length % 341);
        }
        finally { window.Close(); TerminalTabViewModel.SlowPaste = slow; Appearance.Apply(new StudioPreferences()); }
    }

    // ---------- close guard of the window where it happened ----------

    /// <summary>A tab goes away in the main window while the user is in another one (the settings, say): the × of the
    /// main window have moved, and are held; the other window's are not.</summary>
    [AvaloniaFact]
    public async Task WhatGoesAwayHoldsItsOwnWindow()
    {
        CloseGuard.Register();
        CloseGuard.ForgetForTest();
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        window.Show();
        var other = new Window { Width = 400, Height = 300, Content = new TextBox() };
        other.Show();
        try
        {
            h.Interaction.Window = window;
            var w = await Launch(h, fixture);
            w.AddTerminalCommand.Execute(null);
            await Pump(() => w.Terminals.Count == 2 && w.Terminals[1].HasConnection);
            Settle(window);
            Assert.Equal(2, w.Terminals.Count);
            CloseGuard.ForgetForTest();
            // The user is in the other window now.
            other.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
            int moved = h.Interaction.MovedCount;
            w.Terminals[1].CloseCommand!.Execute(null);
            await Pump(() => w.Terminals.Count == 1);
            Assert.Equal(moved + 1, h.Interaction.MovedCount);
            long now = Environment.TickCount64;
            Assert.True(CloseGuard.Blocks(window, 1, now));
            Assert.False(CloseGuard.Blocks(other, 1, now));
            // Columns too.
            CloseGuard.ForgetForTest();
            other.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
            w.HideMonitorCommand.Execute(null);
            now = Environment.TickCount64;
            Assert.True(CloseGuard.Blocks(window, 1, now));
            Assert.False(CloseGuard.Blocks(other, 1, now));
            // And a whole session.
            CloseGuard.ForgetForTest();
            other.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
            h.Model.Close(w);
            now = Environment.TickCount64;
            Assert.True(CloseGuard.Blocks(window, 1, now));
            Assert.False(CloseGuard.Blocks(other, 1, now));
        }
        finally { other.Close(); window.Close(); h.Interaction.Window = null; CloseGuard.ForgetForTest(); }
    }

    /// <summary>The program's own dialogs say which window they belong to.</summary>
    [AvaloniaFact]
    public void ADialogInteractionHoldsItsOwnWindow()
    {
        CloseGuard.Register();
        CloseGuard.ForgetForTest();
        var one = new Window { Width = 300, Height = 200, Content = new TextBox() };
        var two = new Window { Width = 300, Height = 200, Content = new TextBox() };
        one.Show();
        two.Show();
        try
        {
            two.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
            new DialogInteraction(() => one).Moved();
            long now = Environment.TickCount64;
            Assert.True(CloseGuard.Blocks(one, 1, now));
            Assert.False(CloseGuard.Blocks(two, 1, now));
            // Not known: the window the user acted in, as before.
            CloseGuard.ForgetForTest();
            two.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
            new DialogInteraction(() => null).Moved();
            now = Environment.TickCount64;
            Assert.False(CloseGuard.Blocks(one, 1, now));
            Assert.True(CloseGuard.Blocks(two, 1, now));
        }
        finally { one.Close(); two.Close(); CloseGuard.ForgetForTest(); }
    }

    // ---------- questions that wait under the lock screen ----------

    /// <summary>A host key question of a connection opened while locked: the lock screen says that a connection waits,
    /// until it is answered or given up.</summary>
    [AvaloniaFact]
    public async Task TheLockScreenSaysAConnectionWaits()
    {
        AppLock.ForgetForTest();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1000, Height = 600, DataContext = h.Model };
        window.Show();
        using var cancel = new CancellationTokenSource();
        try
        {
            Assert.Null(await AppLock.SetPasswordAsync("", "abcd"));
            Assert.Null(await AppLock.LockAsync());
            Settle(window);
            var lockView = window.FindControl<LockView>("LockLayer")!;
            Assert.Equal("", lockView.WaitingSaid);
            var asked = new DialogInteraction(() => window).ConfirmHostKeyAsync(new HostKeyQuestion("127.0.0.1:22", null, "SHA256:synthetic"), cancel.Token);
            await Pump(() => AppLock.Questions == 1);
            Settle(window);
            Assert.Equal(1, AppLock.Questions);
            Assert.Contains("1 个问题在等你回答", lockView.WaitingSaid);
            Assert.True(window.FindControl<LockView>("LockLayer")!.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "WaitingNote").IsEffectivelyVisible);
            // The question's own window is covered as well, with the same note.
            var dialog = window.OwnedWindows.Single();
            Settle(dialog);
            var cover = Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(dialog)!.Children.OfType<LockCover>().Single();
            Assert.Contains("1 个问题在等你回答", cover.GetVisualDescendants().OfType<LockView>().Single().WaitingSaid);
            cancel.Cancel();
            Assert.False(await asked);
            await Pump(() => AppLock.Questions == 0);
            Assert.Equal(0, AppLock.Questions);
            Assert.Equal("", lockView.WaitingSaid);
        }
        finally { cancel.Cancel(); window.Close(); AppLock.ForgetForTest(); }
    }

    /// <summary>Studio's connections do not count the time of an answer (see AnswerTimeTests in the Core tests).</summary>
    [AvaloniaFact]
    public void StudioConnectionsDoNotTimeTheAnswers()
    {
        using var h = new StudioHarness();
        Assert.True(ProfileConnector.OptionsFor(h.Ssh(22)).AnswersUntimed);
        Assert.False(new SessionConnectOptions().AnswersUntimed);
        var w = WorkspaceViewModel.ForLocal(h.Interaction, h.KnownHosts);
        try { Assert.True(w.ConnectOptions.AnswersUntimed); }
        finally { w.Dispose(); }
    }

    // ---------- 全局透明度 ----------

    [AvaloniaFact]
    public void AClearWindowShowsWhatIsBehindIt()
    {
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1000, Height = 600, DataContext = h.Model };
        window.Show();
        var settings = new SettingsWindow { DataContext = new SettingsViewModel(h.Data) };
        settings.Show();
        try
        {
            var solidHint = window.TransparencyLevelHint.ToArray();
            Appearance.Apply(new StudioPreferences { ClearWindow = true, BackgroundStrength = 0.4 });
            Settle(window);
            Assert.Equal([WindowTransparencyLevel.Transparent], window.TransparencyLevelHint);
            Assert.True(window.TryFindResource("St.Panel", window.ActualThemeVariant, out var panel));
            Assert.Equal(0.6, ((ISolidColorBrush)panel!).Opacity, 3);
            Assert.Equal(0.6, ((ISolidColorBrush)window.Background!).Opacity, 3);
            // Where the system cannot, the window looks as without.
            var fallback = Assert.IsAssignableFrom<ISolidColorBrush>(window.TransparencyBackgroundFallback);
            Assert.Equal(1, fallback.Opacity);
            Assert.Equal(255, fallback.Color.A);
            Assert.Equal(Appearance.Solid("St.Window")!.Color, fallback.Color);
            Assert.NotEqual(Colors.White, fallback.Color); // the dark theme's colour, not Avalonia's own white
            // Only the main window: the settings window and the application's own colours stay solid.
            Assert.True(Application.Current!.TryGetResource("St.Panel", Application.Current.ActualThemeVariant, out var appPanel));
            Assert.Equal(1, ((ISolidColorBrush)appPanel!).Opacity);
            Settle(settings);
            Assert.Equal(1, ((ISolidColorBrush)settings.Background!).Opacity);
            Assert.True(settings.TryFindResource("St.Card", settings.ActualThemeVariant, out var card));
            Assert.Equal(1, ((ISolidColorBrush)card!).Opacity);

            Appearance.Apply(new StudioPreferences { ClearWindow = true, ClearWindowBlur = true });
            Assert.Equal(WindowTransparencyLevel.AcrylicBlur, window.TransparencyLevelHint[0]);
            Assert.Contains(WindowTransparencyLevel.Transparent, window.TransparencyLevelHint);

            // With a picture, the picture is shown and the window is not clear.
            Appearance.Apply(new StudioPreferences { ClearWindow = true, BackgroundImage = "background-0123abcd.png" });
            Assert.False(Appearance.IsClear(Appearance.Current));
            Assert.DoesNotContain(WindowTransparencyLevel.Transparent, window.TransparencyLevelHint);

            Appearance.Apply(new StudioPreferences());
            Settle(window);
            Assert.Equal(solidHint, window.TransparencyLevelHint);
            Assert.False(window.Resources.ContainsKey("St.Panel"));
            Assert.Equal(1, ((ISolidColorBrush)window.Background!).Opacity);
        }
        finally { settings.Close(); window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    /// <summary>A background picture (alpha.10) made every window's colours see-through, also the settings window's,
    /// which then showed white behind them.</summary>
    [AvaloniaFact]
    public void APictureLeavesTheOtherWindowsSolid()
    {
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1000, Height = 600, DataContext = h.Model };
        window.Show();
        var settings = new SettingsWindow { DataContext = new SettingsViewModel(h.Data) };
        settings.Show();
        try
        {
            Appearance.Apply(new StudioPreferences { BackgroundImage = "background-0123abcd.png", BackgroundStrength = 0.5 });
            Settle(window);
            Settle(settings);
            Assert.Equal(0.5, ((ISolidColorBrush)window.Background!).Opacity, 3);
            Assert.Equal(1, ((ISolidColorBrush)settings.Background!).Opacity);
            foreach (string key in Appearance.Surfaces)
            {
                Assert.True(settings.TryFindResource(key, settings.ActualThemeVariant, out var brush), key);
                Assert.Equal(1, ((ISolidColorBrush)brush!).Opacity);
            }
            // Light and dark switched: the main window's colours follow, still see-through.
            var dark = window.Background as ISolidColorBrush;
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
            Settle(window);
            var light = (ISolidColorBrush)window.Background!;
            Assert.NotEqual(dark!.Color, light.Color);
            Assert.Equal(0.5, light.Opacity, 3);
        }
        finally { settings.Close(); window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    [AvaloniaFact]
    public void TheClearWindowSettings()
    {
        using var h = new StudioHarness();
        using var settings = new SettingsViewModel(h.Data);
        settings.SearchText = "透明度";
        Assert.True(settings.Shown["clear"]);
        settings.SearchText = "毛玻璃";
        Assert.True(settings.Shown["clear"]);
        settings.SearchText = "";
        Assert.False(settings.ClearShowsSlider);
        settings.ClearWindow = true;
        Assert.True(Appearance.Current.ClearWindow);
        // Its own "透出": a search for it leaves out the picture's card, which has the other one.
        Assert.True(settings.ClearShowsSlider);
        foreach (string q in new[] { "透明度", "全局透明度", "毛玻璃", "beta" })
        {
            settings.SearchText = q;
            Assert.True(settings.Shown["clear"], q);
        }
        settings.SearchText = "";
        Assert.False(settings.ClearWithPicture);
        settings.ClearWindowBlur = true;
        settings.BackgroundPercent = 70;
        settings.OpacityPercent = 50;
        Assert.Equal(0.7, Appearance.SeeThrough(Appearance.Current));
        Appearance.Apply(Appearance.Current with { BackgroundImage = "background-0123abcd.png" });
        Assert.True(settings.ClearWithPicture);
        Assert.False(settings.ClearShowsSlider); // the picture's card has it then
        // "恢复默认外观" also takes back what makes the window hard to read; the picture has its own button.
        settings.ResetCommand.Execute(null);
        Assert.False(Appearance.Current.ClearWindow);
        Assert.False(Appearance.Current.ClearWindowBlur);
        Assert.Equal(1, Appearance.Current.WindowOpacity);
        Assert.Equal(0.35, Appearance.Current.BackgroundStrength);
        Assert.Equal("background-0123abcd.png", Appearance.Current.BackgroundImage);
        Appearance.Apply(new StudioPreferences());
    }

    // ---------- review of alpha.11 ----------

    /// <summary>A stop while the last piece waits for the server stopped nothing: the bar does not speak of a closing
    /// bracket that a paste without brackets does not have, and the end says that all of it went out.</summary>
    [AvaloniaFact]
    public async Task AStopDuringTheLastPieceSaysNothingWasStopped()
    {
        Appearance.Apply(new StudioPreferences { ConfirmPaste = false });
        var slow = TerminalTabViewModel.SlowPaste;
        TerminalTabViewModel.SlowPaste = TimeSpan.FromMilliseconds(100);
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_HOLD"] = "1", ["FIXTURE_WINDOW"] = "512" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await Launch(h, fixture);
            Settle(window);
            var tab = w.Terminals.Single();
            tab.Control.Terminal.BracketedPasteMode = false;
            // One piece of 1000 bytes in a window of 512: the only piece is the last one.
            string text = new string('a', 1000);
            await window.Clipboard!.SetTextAsync(text);
            w.PasteTerminalCommand.Execute(null);
            await Pump(() => w.IsPasteSlow);
            w.CancelPasteCommand.Execute(null);
            Settle(window);
            var stopping = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PasteStoppingText");
            Assert.True(stopping.IsEffectivelyVisible);
            Assert.DoesNotContain("结束标记", stopping.Text);
            Assert.Contains("正在发的一小段", stopping.Text);

            fixture.Command("release-shell");
            await Pump(() => !tab.IsPasting, 15);
            await Pump(() => Typed(fixture).Length >= text.Length, 15);
            Assert.Equal(text, Typed(fixture));
            Assert.Equal("粘贴已全部发出：停止时已经在发最后一段", w.Status);
        }
        finally { window.Close(); TerminalTabViewModel.SlowPaste = slow; Appearance.Apply(new StudioPreferences()); }
    }

    /// <summary>A question's dialog that closes while locked (given up), and a main window closed by the program while
    /// locked, let go of their lock screens: nothing stays subscribed to the program-wide question count.</summary>
    [AvaloniaFact]
    public async Task AWindowClosedWhileLockedLetsGoOfItsLockScreen()
    {
        AppLock.ForgetForTest();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1000, Height = 600, DataContext = h.Model };
        window.Show();
        using var h2 = new StudioHarness();
        var second = new MainWindow { Width = 800, Height = 500, DataContext = h2.Model };
        second.Show();
        using var cancel = new CancellationTokenSource();
        try
        {
            Assert.Null(await AppLock.SetPasswordAsync("", "abcd"));
            Assert.Null(await AppLock.LockAsync());
            Settle(window);
            Settle(second);
            // Windows that earlier tests left open are locked too: counted from here.
            int locked = LockView.Listening;
            Assert.True(locked >= 2, locked.ToString());
            var asked = new DialogInteraction(() => window).ConfirmHostKeyAsync(new HostKeyQuestion("127.0.0.1:22", null, "SHA256:synthetic"), cancel.Token);
            await Pump(() => AppLock.Questions == 1 && window.OwnedWindows.Count == 1);
            Settle(window.OwnedWindows.Single());
            Assert.Equal(locked + 1, LockView.Listening);
            cancel.Cancel();
            Assert.False(await asked);
            await Pump(() => window.OwnedWindows.Count == 0);
            Assert.Equal(locked, LockView.Listening);
            second.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(locked - 1, LockView.Listening);
            var (unlocked, _) = await AppLock.UnlockAsync("abcd");
            Assert.True(unlocked);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, LockView.Listening);
        }
        finally { cancel.Cancel(); second.Close(); window.Close(); AppLock.ForgetForTest(); }
    }
}
