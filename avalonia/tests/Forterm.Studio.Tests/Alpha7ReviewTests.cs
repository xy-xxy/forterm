using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Studio.Controls;
using Forterm.Studio.Library;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Forterm.Studio.Views.Dialogs;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>What the independent review of alpha.7 found, and what the mutation check showed was not covered.</summary>
public sealed class Alpha7ReviewTests
{
    static async Task Pump(Func<bool> until, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end && !until()) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task MakeDirectory(WorkspaceViewModel w, ScriptedInteraction interaction, string name)
    {
        interaction.PromptAnswer = name;
        w.SelectedNode = null;
        w.NewFolderCommand.Execute(null);
        await Pump(() => w.FileRoots.Any(n => n.Name == name), TimeSpan.FromSeconds(10));
        Assert.Contains(w.FileRoots, n => n.Name == name);
    }

    static void Settle(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    static void Down(Window window, Shortcut s) => window.KeyPress(s.Gesture.Key, (RawInputModifiers)(int)s.Gesture.KeyModifiers, PhysicalKey.None, null);
    static void Up(Window window, Shortcut s) => window.KeyRelease(s.Gesture.Key, (RawInputModifiers)(int)s.Gesture.KeyModifiers, PhysicalKey.None, null);
    static void Press(Window window, Shortcut s) { Down(window, s); Up(window, s); }
    static string Typed(LoopbackFixture fixture) => System.Text.Encoding.UTF8.GetString(fixture.Stats().GetProperty("input").EnumerateArray().SelectMany(x => Convert.FromHexString(x.GetString()!)).ToArray());
    static LaunchPlan Launch(LoopbackFixture fixture, string name = "唤醒") => LaunchPlan.Parse(["--entry=xshell", "-newtab", name, "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"]);

    sealed class Scene : IDisposable
    {
        public LoopbackFixture Fixture { get; } = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        public StudioHarness Harness { get; } = new();
        public MainWindowViewModel Model { get; }
        public MainWindow Window { get; }
        public StudioProfile Profile { get; }

        public Scene()
        {
            Appearance.Apply(new StudioPreferences { CursorBlink = false });
            var quick = new QuickCommandStore(Harness.Data);
            quick.Upsert(new QuickCommand { Name = "磁盘标记", Text = "df-marker" });
            Profile = Harness.Ssh(Fixture.Port, "回环主机") with { DisablePwdFollow = true, DisableListMonitor = true };
            Harness.Store.Upsert(Profile);
            Model = new MainWindowViewModel(Harness.Interaction, Harness.KnownHosts, Harness.Store, Harness.Credentials, quickCommands: quick, settingsDirectory: Harness.Data);
            Window = new MainWindow { Width = 1200, Height = 720, DataContext = Model };
            Window.Show();
        }

        public async Task<WorkspaceViewModel> Open()
        {
            var w = await Model.OpenProfileAsync(Profile);
            await Pump(() => w.Terminal?.IsLive == true, TimeSpan.FromSeconds(10));
            Settle(Window);
            return w;
        }

        public IInputElement? Focused => Window.FocusManager!.GetFocusedElement();
        public bool InTerminal(WorkspaceViewModel w) => Focused is Visual v && w.Terminal!.IsVisualAncestorOf(v);

        public void Dispose()
        {
            Window.Close();
            Model.Dispose();
            Harness.Dispose();
            Fixture.Dispose();
            Appearance.Apply(new StudioPreferences());
        }
    }

    // ---------- the palette is about the session that is shown ----------

    [AvaloniaFact]
    public async Task ThePaletteNeverActsOnASessionThatIsNotShown()
    {
        using var s = new Scene();
        var first = await s.Open();
        Press(s.Window, Shortcuts.Palette);
        var quick = s.Model.Palette.Matches.First(m => m.Title == "磁盘标记");
        var clear = s.Model.Palette.Matches.First(m => m.Title.StartsWith("清空显示", StringComparison.Ordinal));
        // The controller opens another session meanwhile: the palette goes away with what it offered.
        var second = await s.Model.AcceptLaunchAsync(Launch(s.Fixture));
        Assert.Same(second, s.Model.Selected);
        Assert.False(s.Model.Palette.IsOpen);
        quick.Run();
        clear.Run();
        await Pump(() => false, TimeSpan.FromMilliseconds(500));
        Assert.Equal("", Typed(s.Fixture));
        Assert.DoesNotContain("已输入", first.Status);
        // Going to another session by hand closes it just the same.
        s.Model.Palette.Open();
        s.Model.Selected = first;
        Assert.False(s.Model.Palette.IsOpen);
        s.Model.Selected = second;
        // And back on the session it was opened on it works.
        s.Model.Selected = first;
        quick.Run();
        await Pump(() => Typed(s.Fixture).Length > 0, TimeSpan.FromSeconds(5));
        Assert.StartsWith("df-marker", Typed(s.Fixture));
    }

    [AvaloniaFact]
    public async Task AClosedSessionIsNotShownAgain()
    {
        using var s = new Scene();
        var first = await s.Open();
        var second = await s.Open();
        Press(s.Window, Shortcuts.Palette);
        var entry = s.Model.Palette.Matches.First(m => m.Group == "会话");
        Press(s.Window, Shortcuts.Palette);
        s.Model.Close(first);
        entry.Run();
        Assert.Same(second, s.Model.Selected);
        first.ActivateCommand!.Execute(null);
        Assert.Same(second, s.Model.Selected);
        Assert.Contains(s.Model.Selected, s.Model.Workspaces);
    }

    [AvaloniaFact]
    public async Task WhileThePaletteIsOpenNothingReachesTheShell()
    {
        using var s = new Scene();
        var w = await s.Open();
        w.Terminal!.FocusTerminal();
        Press(s.Window, Shortcuts.Palette);
        var box = s.Window.FindControl<TextBox>("PaletteText")!;
        await Pump(() => box.IsFocused, TimeSpan.FromSeconds(2));
        // Tab moves along the list, it does not leave it.
        for (int i = 0; i < 8; i++) s.Window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, null);
        Assert.True(box.IsFocused);
        Assert.Equal(((-8 % s.Model.Palette.Matches.Count) + s.Model.Palette.Matches.Count) % s.Model.Palette.Matches.Count, s.Model.Palette.Selected);
        for (int i = 0; i < 8; i++) s.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Assert.Equal(0, s.Model.Palette.Selected);
        // Focus that was taken away (a terminal that has just come up) does not take the keys.
        w.Terminal.FocusTerminal();
        Assert.False(box.IsFocused);
        s.Window.KeyTextInput("rm ");
        s.Window.KeyPress(Key.X, RawInputModifiers.None, PhysicalKey.X, "x");
        s.Window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
        Assert.True(box.IsFocused);
        await Pump(() => false, TimeSpan.FromMilliseconds(300));
        Assert.Equal("", Typed(s.Fixture));
        // Closed, the keyboard is where it was.
        s.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await Pump(() => s.InTerminal(w), TimeSpan.FromSeconds(2));
        Assert.True(s.InTerminal(w));
        s.Window.KeyTextInput("ok");
        await Pump(() => Typed(s.Fixture).Length > 0, TimeSpan.FromSeconds(3));
        Assert.Equal("ok", Typed(s.Fixture));
    }

    // ---------- closing ----------

    [AvaloniaFact]
    public async Task HoldingTheCloseKeyClosesOneTab()
    {
        using var s = new Scene();
        var first = await s.Open();
        var second = await s.Open();
        await second.AddTerminalAsync();
        Assert.Equal(2, second.Terminals.Count);
        for (int i = 0; i < 8; i++) Down(s.Window, Shortcuts.CloseTab); // the key repeats
        await Pump(() => second.Terminals.Count == 1, TimeSpan.FromSeconds(3));
        await Pump(() => false, TimeSpan.FromMilliseconds(200));
        Assert.Single(second.Terminals);
        Assert.Equal(2, s.Model.Workspaces.Count);
        Up(s.Window, Shortcuts.CloseTab);
        Press(s.Window, Shortcuts.CloseTab); // pressed again: the session itself
        await Pump(() => s.Model.Workspaces.Count == 1, TimeSpan.FromSeconds(5));
        Assert.Same(first, Assert.Single(s.Model.Workspaces));
        await Pump(() => s.InTerminal(first), TimeSpan.FromSeconds(3));
        Assert.True(s.InTerminal(first)); // the keyboard goes on in the session that is left
        // New terminals neither.
        for (int i = 0; i < 5; i++) Down(s.Window, Shortcuts.NewTerminal);
        await Pump(() => first.Terminals.Count == 2, TimeSpan.FromSeconds(5));
        await Pump(() => false, TimeSpan.FromMilliseconds(300));
        Assert.Equal(2, first.Terminals.Count);
    }

    [AvaloniaFact]
    public async Task AKeyAloneDoesNotEndASessionOfTheController()
    {
        using var s = new Scene();
        var w = (await s.Model.AcceptLaunchAsync(Launch(s.Fixture, "唤醒会话")))!;
        await Pump(() => w.Terminal?.IsLive == true, TimeSpan.FromSeconds(10));
        s.Harness.Interaction.ConfirmAnswer = false;
        Press(s.Window, Shortcuts.CloseTab);
        await Pump(() => s.Harness.Interaction.Confirms.Count > 0, TimeSpan.FromSeconds(2));
        Assert.Contains("唤醒会话", Assert.Single(s.Harness.Interaction.Confirms));
        Assert.Same(w, Assert.Single(s.Model.Workspaces));
        s.Harness.Interaction.ConfirmAnswer = true;
        Press(s.Window, Shortcuts.CloseTab);
        await Pump(() => s.Model.Workspaces.Count == 0, TimeSpan.FromSeconds(5));
        Assert.Empty(s.Model.Workspaces);
    }

    [AvaloniaFact]
    public async Task ATerminalThatIsNotShownIsNotClosed()
    {
        using var s = new Scene();
        var w = await s.Open();
        await w.AddTerminalAsync();
        w.ToggleTerminalCommand.Execute(null);
        Assert.False(w.ShowTerminal);
        Press(s.Window, Shortcuts.CloseTab);
        await Pump(() => false, TimeSpan.FromMilliseconds(200));
        Assert.Equal(2, w.Terminals.Count);
        Assert.Contains("没有显示", w.Status);
        // Covered by a maximised column is the same.
        w.ToggleTerminalCommand.Execute(null);
        Settle(s.Window);
        s.Window.GetVisualDescendants().OfType<StudioPane>().First(p => p.Name == "MonitorPane").MaximizeCommand!.Execute(null);
        await Task.Delay(1100, CancellationToken.None);
        Press(s.Window, Shortcuts.CloseTab);
        await Pump(() => false, TimeSpan.FromMilliseconds(200));
        Assert.Equal(2, w.Terminals.Count);
        Assert.Single(s.Model.Workspaces);
    }

    [AvaloniaFact]
    public async Task NewTerminalStaysInItsSession()
    {
        using var ftp = LoopbackFixture.Ftp();
        using var h = new StudioHarness();
        var w = (await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xftp", "-url", $"ftp://synthetic:synthetic-only@127.0.0.1:{ftp.Port}"])))!;
        Assert.Same(w, h.Model.Selected);
        h.Model.NewTerminal();
        Assert.Same(w, Assert.Single(h.Model.Workspaces)); // not a local shell in its place
        Assert.Contains("不能", w.Status);
    }

    // ---------- keys ----------

    [Fact]
    public void ShortcutsLeaveOtherWritingAlone()
    {
        KeyEventArgs Key_(Key key, KeyModifiers modifiers) => new() { Key = key, KeyModifiers = modifiers };
        foreach (var s in Shortcuts.All)
            foreach (var g in (s.Also ?? []).Append(s.Gesture))
            {
                // Ctrl+Alt is AltGr; Ctrl+Shift with a digit or "-" is Ctrl+@, Ctrl+^, Ctrl+_.
                Assert.False(g.KeyModifiers.HasFlag(KeyModifiers.Control) && g.KeyModifiers.HasFlag(KeyModifiers.Alt), s.Title);
                if (g.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)) Assert.True(g.Key is >= Key.A and <= Key.Z or Key.Tab or Key.OemComma or Key.OemPlus, $"{s.Title} {g}");
            }
        for (var k = Key.D0; k <= Key.D9; k++)
        {
            Assert.Equal(0, Shortcuts.SessionNumber(Key_(k, KeyModifiers.Control | KeyModifiers.Alt)));
            Assert.Equal(0, Shortcuts.SessionNumber(Key_(k, KeyModifiers.Alt)));
            Assert.Equal(OperatingSystem.IsMacOS() && k != Key.D0 ? k - Key.D0 : 0, Shortcuts.SessionNumber(Key_(k, KeyModifiers.Meta)));
        }
        // Zooming takes exactly the keys the terminal takes anyway.
        foreach (var (s, own) in new[] { (Shortcuts.ZoomIn, Iciclecreek.Terminal.TerminalKeyGestures.ZoomIn), (Shortcuts.ZoomOut, Iciclecreek.Terminal.TerminalKeyGestures.ZoomOut), (Shortcuts.ZoomReset, Iciclecreek.Terminal.TerminalKeyGestures.ResetZoom) })
            foreach (var g in own) Assert.True(Shortcuts.Matches(s, Key_(g.Key, g.KeyModifiers)), $"{s.Title} {g}");
        Assert.False(Shortcuts.Matches(Shortcuts.ZoomOut, Key_(Key.OemMinus, KeyModifiers.Control | KeyModifiers.Shift)));
    }

    [AvaloniaFact]
    public async Task ZoomIsOneSettingHoweverItIsChanged()
    {
        using var s = new Scene();
        var w = await s.Open();
        w.Terminal!.FocusTerminal();
        var main = (RawInputModifiers)(int)Iciclecreek.Terminal.TerminalKeyGestures.CommandModifiers;
        s.Window.KeyPress(Key.OemPlus, main | RawInputModifiers.Shift, PhysicalKey.Equal, "+"); // how "+" is typed
        Assert.Equal(15, Appearance.Current.TerminalFontSize);
        Assert.Equal(1, w.Terminal.Zoom);
        s.Window.KeyPress(Key.D0, main, PhysicalKey.Digit0, "0");
        Assert.Equal(14, Appearance.Current.TerminalFontSize);
        Assert.Equal(1, w.Terminal.Zoom);
        // A touchpad turns the wheel by fractions.
        Settle(s.Window);
        var over = w.Terminal.TranslatePoint(new Point(60, 60), s.Window)!.Value;
        for (int i = 0; i < 12; i++) s.Window.MouseWheel(over, new Vector(0, 0.1), main);
        Assert.Equal(15, Appearance.Current.TerminalFontSize);
        s.Window.MouseWheel(over, new Vector(0, -0.5), main); // the other way starts over
        Assert.Equal(15, Appearance.Current.TerminalFontSize);
        s.Window.MouseWheel(over, new Vector(0, -0.5), main);
        Assert.Equal(14, Appearance.Current.TerminalFontSize);
        s.Window.MouseWheel(over, new Vector(0, 1), main);
        Assert.Equal(15, Appearance.Current.TerminalFontSize);
        Assert.Equal(15, Appearance.Load(s.Harness.Data).TerminalFontSize);
        s.Window.MouseWheel(over, new Vector(0, 1), RawInputModifiers.None); // scrolling
        Assert.Equal(15, Appearance.Current.TerminalFontSize);
    }

    [AvaloniaFact]
    public async Task AShortcutIsHandledBeforeAnythingElseSeesIt()
    {
        using var s = new Scene();
        var w = await s.Open();
        w.Terminal!.FocusTerminal();
        Settle(s.Window);
        var seen = new List<(Key Key, bool Handled)>();
        s.Window.AddHandler(InputElement.KeyDownEvent, (_, e) => seen.Add((e.Key, e.Handled)), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        foreach (var shortcut in new[] { Shortcuts.ZoomIn, Shortcuts.ZoomReset, Shortcuts.NextSession, Shortcuts.Settings }) Press(s.Window, shortcut);
        Assert.Equal(4, seen.Count);
        Assert.All(seen, x => Assert.True(x.Handled, x.Key.ToString()));
        Assert.Equal("", Typed(s.Fixture));
    }

    // ---------- the keyboard follows the user, and only the user ----------

    [AvaloniaFact]
    public async Task TheKeyboardFollowsNavigationButNotALaunch()
    {
        using var s = new Scene();
        var first = await s.Open();
        await Pump(() => s.InTerminal(first), TimeSpan.FromSeconds(3));
        Assert.True(s.InTerminal(first)); // the user opened it
        var second = await s.Open();
        await Pump(() => s.InTerminal(second), TimeSpan.FromSeconds(3));
        Press(s.Window, Shortcuts.PreviousSession);
        await Pump(() => s.InTerminal(first), TimeSpan.FromSeconds(3));
        Assert.True(s.InTerminal(first));
        await first.AddTerminalAsync();
        await Pump(() => s.InTerminal(first), TimeSpan.FromSeconds(3));
        Assert.True(s.InTerminal(first)); // the new tab
        Press(s.Window, Shortcuts.CloseTab);
        await Pump(() => first.Terminals.Count == 1 && s.InTerminal(first), TimeSpan.FromSeconds(3));
        Assert.True(s.InTerminal(first));
        // A click on a tab of the title bar is going there, too.
        Settle(s.Window);
        var tab = s.Window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("session-tab") && ReferenceEquals(b.DataContext, second));
        var on = tab.TranslatePoint(new Point(tab.Bounds.Width / 3, tab.Bounds.Height / 2), s.Window)!.Value;
        s.Window.MouseDown(on, MouseButton.Left);
        s.Window.MouseUp(on, MouseButton.Left);
        Assert.Same(second, s.Model.Selected);
        await Pump(() => s.InTerminal(second), TimeSpan.FromSeconds(3));
        Assert.True(s.InTerminal(second));
        // What the user is typing here must not end up in a session that came by itself.
        var launched = (await s.Model.AcceptLaunchAsync(Launch(s.Fixture)))!;
        await Pump(() => launched.Terminal?.IsLive == true, TimeSpan.FromSeconds(10));
        Settle(s.Window);
        await Pump(() => false, TimeSpan.FromMilliseconds(300));
        Assert.False(s.InTerminal(launched));
        int before = Typed(s.Fixture).Length;
        s.Window.KeyTextInput("secret");
        await Pump(() => false, TimeSpan.FromMilliseconds(300));
        Assert.Equal(before, Typed(s.Fixture).Length);
        // Going there is another matter.
        Press(s.Window, Shortcuts.PreviousSession);
        Press(s.Window, Shortcuts.NextSession);
        await Pump(() => s.InTerminal(launched), TimeSpan.FromSeconds(3));
        Assert.True(s.InTerminal(launched));
    }

    /// <summary>Avalonia's keyboard focus is one for all windows: whoever takes it gets what is typed elsewhere.</summary>
    [AvaloniaFact]
    public async Task TheKeyboardIsNeverTakenOutOfAnotherWindow()
    {
        using var s = new Scene();
        var first = await s.Open();
        var box = new TextBox();
        var dialog = new Window { Content = box, Width = 300, Height = 120 };
        await Pump(() => s.InTerminal(first), TimeSpan.FromSeconds(3));
        Assert.True(s.InTerminal(first));
        s.Model.Palette.Open(); // over a terminal that had the keyboard
        Settle(s.Window);
        dialog.Show(s.Window);
        Settle(dialog);
        box.Focus();
        Assert.True(box.IsFocused);
        s.Model.Palette.Close(); // it would go back to that terminal
        await Pump(() => false, TimeSpan.FromMilliseconds(200));
        Assert.True(box.IsFocused);
        // A connection that finishes while a password is being typed, the next terminal, going through sessions.
        var second = await s.Open();
        await Pump(() => false, TimeSpan.FromMilliseconds(300));
        Assert.True(box.IsFocused);
        await second.AddTerminalAsync();
        s.Model.StepSession(1);
        Settle(s.Window);
        await Pump(() => false, TimeSpan.FromMilliseconds(300));
        Assert.True(box.IsFocused);
        first.Terminal!.FocusTerminal();
        second.Terminal!.FocusTerminal();
        Assert.True(box.IsFocused);
        dialog.KeyTextInput("My-Secret");
        Assert.Equal("My-Secret", box.Text);
        // The palette closes by itself, or by the user: neither takes the keyboard from there.
        s.Model.Palette.Open();
        box.Focus();
        await Pump(() => false, TimeSpan.FromMilliseconds(200));
        Assert.True(box.IsFocused);
        s.Model.Close((WorkspaceViewModel)s.Model.Selected == first ? second : first);
        Assert.False(s.Model.Palette.IsOpen);
        await Pump(() => false, TimeSpan.FromMilliseconds(200));
        Assert.True(box.IsFocused);
        dialog.Close();
        await Pump(() => false, TimeSpan.FromMilliseconds(100));
        Assert.Equal("", Typed(s.Fixture));
    }

    [AvaloniaFact]
    public async Task TheKeyboardIsNotTakenFromAFieldOfTheWindow()
    {
        using var s = new Scene();
        var w = await s.Open();
        // The list of quick commands is open, its field has the keyboard.
        var button = s.Window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "QuickButton");
        button.Flyout!.ShowAt(button);
        await Pump(() => s.Focused is TextBox, TimeSpan.FromSeconds(2));
        var field = Assert.IsType<TextBox>(s.Focused);
        await w.AddTerminalAsync();
        s.Model.ShowSession(1);
        Settle(s.Window);
        await Pump(() => false, TimeSpan.FromMilliseconds(300));
        Assert.Same(field, s.Focused);
        button.Flyout.Hide();
        // Find, too.
        w.FindInTerminalCommand.Execute(null);
        await Pump(() => s.Focused is TextBox, TimeSpan.FromSeconds(2));
        s.Model.ShowSession(1);
        await Pump(() => false, TimeSpan.FromMilliseconds(200));
        Assert.IsType<TextBox>(s.Focused);
        Assert.Equal("", Typed(s.Fixture));
    }

    [AvaloniaFact]
    public async Task AFocusThatComesTooLateIsDropped()
    {
        using var s = new Scene();
        var first = await s.Open();
        var second = await s.Open();
        s.Model.ShowSession(1); // asked for the first …
        s.Model.Selected = second; // … and something else is shown before that could be done
        await Pump(() => false, TimeSpan.FromMilliseconds(300));
        Assert.False(s.InTerminal(second));
        // Whoever was typing in the palette when it went away is nowhere, not in a shell.
        s.Model.ShowSession(1);
        await Pump(() => s.InTerminal(first), TimeSpan.FromSeconds(2));
        Assert.True(s.InTerminal(first));
        Press(s.Window, Shortcuts.Palette);
        await Pump(() => s.Window.FindControl<TextBox>("PaletteText")!.IsFocused, TimeSpan.FromSeconds(2));
        s.Model.Close(second);
        await Pump(() => false, TimeSpan.FromMilliseconds(300));
        Assert.False(s.Model.Palette.IsOpen);
        s.Window.KeyTextInput("nginx");
        await Pump(() => false, TimeSpan.FromMilliseconds(300));
        Assert.Equal("", Typed(s.Fixture));
    }

    [AvaloniaFact]
    public async Task TheKeyboardGoesBackToTheEditor()
    {
        using var s = new Scene();
        var w = await s.Open();
        string local = Path.Combine(s.Harness.Data, "notes.yml");
        File.WriteAllText(local, "a: 1\n");
        await w.DropAsync("/", [local]);
        await Pump(() => w.FileRoots.Any(n => n.Name == "notes.yml"), TimeSpan.FromSeconds(15));
        await w.OpenEditorAsync(w.FileRoots.First(n => n.Name == "notes.yml"));
        Settle(s.Window);
        var editor = s.Window.GetVisualDescendants().OfType<EditorView>().Single();
        editor.FocusText();
        Assert.True(editor.IsKeyboardFocusWithin);
        Press(s.Window, Shortcuts.Palette);
        await Pump(() => s.Window.FindControl<TextBox>("PaletteText")!.IsFocused, TimeSpan.FromSeconds(2));
        s.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await Pump(() => editor.IsKeyboardFocusWithin, TimeSpan.FromSeconds(2));
        Assert.True(editor.IsKeyboardFocusWithin);
        // To a row of the file list as well, although the editor is in front.
        var row = s.Window.GetVisualDescendants().OfType<TreeViewItem>().First();
        Assert.True(row.Focus());
        Press(s.Window, Shortcuts.Palette);
        await Pump(() => s.Window.FindControl<TextBox>("PaletteText")!.IsFocused, TimeSpan.FromSeconds(2));
        s.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await Pump(() => row.IsFocused, TimeSpan.FromSeconds(2));
        Assert.True(row.IsFocused);
    }

    // ---------- mouse and keys of the window ----------

    [AvaloniaFact]
    public async Task TheMiddleButtonClosesTheTabUnderIt()
    {
        using var s = new Scene();
        var first = await s.Open();
        var second = await s.Open();
        await second.AddTerminalAsync();
        Settle(s.Window);
        Point Middle(Button b) => b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), s.Window)!.Value;
        void Click(Point at) { s.Window.MouseDown(at, MouseButton.Middle); s.Window.MouseUp(at, MouseButton.Middle); }
        var chip = s.Window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("chip-tab") && ReferenceEquals(b.DataContext, second.Terminals[0]));
        Click(Middle(chip));
        await Pump(() => second.Terminals.Count == 1, TimeSpan.FromSeconds(3));
        Assert.Single(second.Terminals);
        Settle(s.Window);
        var tab = s.Window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("session-tab") && ReferenceEquals(b.DataContext, first));
        Click(Middle(tab));
        await Pump(() => s.Model.Workspaces.Count == 1, TimeSpan.FromSeconds(5));
        Assert.Same(second, Assert.Single(s.Model.Workspaces));
    }

    [AvaloniaFact]
    public async Task FileKeysDoWhatTheMenuDoes()
    {
        using var s = new Scene();
        var w = await s.Open();
        string local = Path.Combine(s.Harness.Data, "keys.txt");
        File.WriteAllText(local, "a\n");
        await w.DropAsync("/", [local]);
        await Pump(() => w.FileRoots.Any(n => n.Name == "keys.txt"), TimeSpan.FromSeconds(15));
        Settle(s.Window);
        void Row(string name)
        {
            w.SelectedNode = w.FileRoots.First(n => n.Name == name);
            Settle(s.Window);
            Assert.True(s.Window.GetVisualDescendants().OfType<TreeViewItem>().First(r => ReferenceEquals(r.DataContext, w.SelectedNode)).Focus());
        }
        Row("keys.txt");
        s.Harness.Interaction.PromptAnswer = "renamed.txt";
        s.Window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        await Pump(() => w.FileRoots.Any(n => n.Name == "renamed.txt"), TimeSpan.FromSeconds(10));
        Assert.Contains(w.FileRoots, n => n.Name == "renamed.txt");
        // Deleting asks; a no keeps the file.
        Row("renamed.txt");
        int asked = s.Harness.Interaction.Confirms.Count;
        s.Harness.Interaction.ConfirmAnswer = false;
        s.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        await Pump(() => s.Harness.Interaction.Confirms.Count > asked, TimeSpan.FromSeconds(3));
        Assert.Equal(asked + 1, s.Harness.Interaction.Confirms.Count);
        s.Harness.Interaction.ConfirmAnswer = true;
        // Created behind our back; F5 sees it.
        var other = await s.Open();
        await MakeDirectory(other, s.Harness.Interaction, "late");
        s.Model.Selected = w;
        Settle(s.Window);
        Row("renamed.txt");
        Assert.DoesNotContain(w.FileRoots, n => n.Name == "late");
        s.Window.KeyPress(Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
        await Pump(() => w.FileRoots.Any(n => n.Name == "late"), TimeSpan.FromSeconds(10));
        Assert.Contains(w.FileRoots, n => n.Name == "late");
        Assert.Contains(w.FileRoots, n => n.Name == "renamed.txt");
        Assert.Equal("", Typed(s.Fixture));
    }

    [AvaloniaFact]
    public async Task EnterNeverConfirmsWhatCannotBeUndone()
    {
        var owner = new Window { Width = 600, Height = 400 };
        owner.Show();
        try
        {
            async Task<bool> Ask(bool danger, Key key, bool focused = true)
            {
                var answer = SimpleDialog.ConfirmAsync(owner, "删除", "删除吗", "删除", danger);
                await Pump(() => owner.OwnedWindows.Count > 0, TimeSpan.FromSeconds(2));
                var dialog = owner.OwnedWindows[0];
                Settle(dialog);
                if (!focused) dialog.FocusManager!.Focus(null);
                dialog.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
                if (dialog.IsVisible) dialog.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
                await Pump(() => answer.IsCompleted, TimeSpan.FromSeconds(focused ? 2 : 0.3));
                if (!answer.IsCompleted) dialog.Close();
                return await answer;
            }
            Assert.False(await Ask(danger: true, Key.Enter));
            Assert.False(await Ask(danger: true, Key.Escape));
            Assert.False(await Ask(danger: true, Key.Space)); // the button in focus is "取消"
            Assert.False(await Ask(danger: true, Key.Enter, focused: false)); // it is not the default button either
            Assert.True(await Ask(danger: false, Key.Enter));
            Assert.False(await Ask(danger: false, Key.Escape));
        }
        finally { owner.Close(); }
    }

    // ---------- toasts ----------

    [AvaloniaFact]
    public async Task AToastDoesWhatItWasShownFor()
    {
        var center = new ToastCenter();
        var opened = new List<int>();
        center.Show(new ToastMessage(ToastKind.Success, "已完成下载 1 项", "打开文件夹", () => opened.Add(1)));
        center.Show(new ToastMessage(ToastKind.Success, "已完成下载 1 项", "打开文件夹", () => opened.Add(2)));
        Assert.Single(center.Items);
        center.Items[0].ActionCommand.Execute(null);
        Assert.Equal([2], opened);
        // With animations they leave a moment later, once.
        Motion.Apply(Application.Current!, true);
        try
        {
            center.Show(new ToastMessage(ToastKind.Info, "一"));
            var item = center.Items[0];
            item.DismissCommand.Execute(null);
            item.DismissCommand.Execute(null);
            Assert.True(item.IsLeaving);
            Assert.Single(center.Items);
            await Pump(() => center.Items.Count == 0, TimeSpan.FromSeconds(2));
            Assert.Empty(center.Items);
        }
        finally { Motion.Apply(Application.Current!, false); }
    }

    // ---------- dropping files ----------

    [AvaloniaFact]
    public async Task OneRefusedItemDoesNotKeepTheOthersBack()
    {
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "回环主机"));
        await MakeDirectory(w, h.Interaction, "clash");
        string one = Path.Combine(h.Data, "one.txt"), clash = Path.Combine(h.Data, "clash"), folder = Path.Combine(h.Data, "dir");
        File.WriteAllText(one, "1\n");
        File.WriteAllText(clash, "2\n");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "in.txt"), "3\n");
        await w.DropAsync("/", [clash, Path.Combine(h.Data, "gone.txt"), one, folder], unreachable: 2);
        Assert.Contains("1 个文件、1 个文件夹 → /", w.FileNotice);
        Assert.Contains("4 项没有上传", w.FileNotice);
        Assert.Contains("clash", w.FileNotice);
        Assert.Contains("“gone.txt”已不存在", w.FileNotice);
        Assert.Contains("2 项不是本机文件", w.FileNotice);
        await Pump(() => w.FileRoots.Any(n => n.Name == "one.txt") && w.FileRoots.Any(n => n.Name == "dir"), TimeSpan.FromSeconds(20));
        Assert.Contains(w.FileRoots, n => n.Name == "one.txt");
        Assert.Contains(w.FileRoots, n => n.Name == "dir");
        // A row that only says something stands for the directory it is in.
        var directory = w.FileRoots.First(n => n.Name == "clash");
        directory.IsExpanded = true;
        await Pump(() => directory.Children.Any(c => c.IsPlaceholder && c.Name == "空目录"), TimeSpan.FromSeconds(5));
        Assert.Equal("/clash", w.DropDirectory(directory.Children.Single()));
        await w.DropAsync("/", [Path.Combine(h.Data, "nothing")]);
        Assert.StartsWith("未上传任何文件", w.FileNotice);
        // Closed or cut off in the middle: the rest is not passed over in silence.
        var many = Enumerable.Range(0, 6).Select(i => Path.Combine(h.Data, $"m{i}.txt")).ToArray();
        foreach (string m in many) File.WriteAllText(m, "x\n");
        await w.DropAsync("/", many[..2]);
        await Pump(() => w.FileRoots.Any(n => n.Name == "m1.txt"), TimeSpan.FromSeconds(15));
        h.Interaction.OnConfirm = _ => fixture.Dispose(); // while it asks about the first
        await w.DropAsync("/", many);
        h.Interaction.OnConfirm = null;
        int missing = int.Parse(System.Text.RegularExpressions.Regex.Match(w.FileNotice, @"(\d+) 项没有上传").Groups[1].Value);
        Assert.InRange(missing, 5, 6);
    }

    [AvaloniaFact]
    public async Task FilesAreDroppedOnTheRowUnderThePointer()
    {
        using var s = new Scene();
        var w = await s.Open();
        await Pump(() => w.FileRoots.Any(n => n.IsDirectory), TimeSpan.FromSeconds(10));
        Settle(s.Window);
        string local = Path.Combine(s.Harness.Data, "dropped.txt");
        File.WriteAllText(local, "x\n");
        var files = new DataTransfer();
        files.Add(DataTransferItem.CreateFile((await s.Window.StorageProvider.TryGetFileFromPathAsync(local))!));
        var directory = w.FileRoots.First(n => n.IsDirectory);
        var row = s.Window.GetVisualDescendants().OfType<TreeViewItem>().First(i => ReferenceEquals(i.DataContext, directory));
        var at = row.TranslatePoint(new Point(60, 10), s.Window)!.Value;
        var hint = s.Window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "DropHint");
        s.Window.DragDrop(at, RawDragEventType.DragEnter, files, DragDropEffects.Copy);
        s.Window.DragDrop(at, RawDragEventType.DragOver, files, DragDropEffects.Copy);
        Assert.True(hint.IsVisible);
        Assert.Equal(directory.Path, s.Window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "DropTarget").Text);
        s.Window.DragDrop(at, RawDragEventType.Drop, files, DragDropEffects.Copy);
        Assert.False(hint.IsVisible);
        directory.IsExpanded = true;
        await Pump(() => directory.Children.Any(c => c.Name == "dropped.txt"), TimeSpan.FromSeconds(15));
        Assert.Contains(directory.Children, c => c.Name == "dropped.txt");
        // Text is not a file.
        var text = new DataTransfer();
        text.Add(DataTransferItem.CreateText("rm -rf /"));
        s.Window.DragDrop(at, RawDragEventType.DragEnter, text, DragDropEffects.Copy);
        Assert.False(hint.IsVisible);
        // Over a terminal nothing is dropped at all.
        int asked = s.Harness.Interaction.Confirms.Count;
        var over = w.Terminal!.TranslatePoint(new Point(40, 40), s.Window)!.Value;
        s.Window.DragDrop(over, RawDragEventType.DragEnter, files, DragDropEffects.Copy);
        s.Window.DragDrop(over, RawDragEventType.Drop, files, DragDropEffects.Copy);
        await Pump(() => false, TimeSpan.FromMilliseconds(300));
        Assert.Equal(asked, s.Harness.Interaction.Confirms.Count);
        Assert.Equal("", Typed(s.Fixture));
    }

    // ---------- monitor ----------

    const string Typed64 = "Filesystem Type 1024-blocks Used Available Capacity Mounted on\n/dev/sda1 ext4 1000 250 750 25% /\ntmpfs tmpfs 100 100 0 100% /run/full\n/dev/sr0 iso9660 100 100 0 100% /media/cdrom\n";
    const string Untyped = "Filesystem 1024-blocks Used Available Capacity Mounted on\n/dev/sda1 1000 250 750 25% /\ntmpfs 100 100 0 100% /run/full\n/dev/sr0 100 100 0 100% /media/cdrom\n";

    [Fact]
    public void DfIsReadOnce()
    {
        var sample = MonitorProbe.Parse("FTS1\nDF\n" + Typed64 + Untyped + "PS\n");
        var root = Assert.Single(sample.Mounts, m => m.Mount == "/");
        Assert.Equal("ext4", root.Type);
        Assert.DoesNotContain(sample.Mounts, m => m.Mount == "/run/full");
        Assert.Empty(MonitorFindings.Evaluate(MonitorProbe.Compare(sample, null), []));
        Assert.Equal(3, MonitorProbe.Parse("FTS1\nDF\n" + Untyped + "PS\n").Mounts.Count); // only the untyped listing there is
        Assert.DoesNotContain("||  df", MonitorProbe.Command);
        Assert.Contains("[ -n \"$o\" ] || o=$(df -kP", MonitorProbe.Command);
    }

    [Fact]
    public void NumbersThatCannotBeAreNotNumbers()
    {
        string ps = string.Join("\n", Enumerable.Range(0, 3).Select(i => $"  {i + 10} app Sl {(i == 0 ? "1e308" : "5.0")} {(i == 1 ? "18446744073709551615" : "1000")} Sat Nov 22 15:39:59 2026 /bin/x"));
        var sample = MonitorProbe.Parse("FTS1\nUPTIME\n1e300 1\nLOAD\n1e200 2 3 1/1 1\nMEM\nMemTotal: 18446744073709551615 kB\nMemFree: 1 kB\nDF\nFilesystem Type 1024-blocks Used Available Capacity Mounted on\n/dev/a ext4 18446744073709551615 18446744073709551615 1 99% /huge\n" +
            "NET\n a\u001b[31mb: 18446744073709551615 0 0 0 0 0 0 0 18446744073709551615 0 0 0 0 0 0 0\n eth1: 18446744073709551615 0 0 0 0 0 0 0 18446744073709551615 0 0 0 0 0 0 0\nPS\n" + ps + "\n");
        Assert.Null(sample.Uptime);
        Assert.Null(sample.Load);
        Assert.Empty(sample.MemoryKb.Where(m => m.Key == "MemTotal"));
        Assert.Empty(sample.Mounts);
        Assert.DoesNotContain(sample.Network.Keys, k => k.Any(char.IsControl));
        Assert.Equal(12, Assert.Single(sample.Processes!).Pid);
        foreach (string word in new[] { "nan", "NaN", "Infinity", "-1", "1e400" }) Assert.Null(MonitorProbe.Parse($"FTS1\nUPTIME\n{word} 1\nPS\n").Uptime);
        using var vm = new MonitorViewModel("x", () => null, autoRefresh: false);
        vm.Apply(MonitorProbe.Compare(sample, null), new DateTime(2026, 11, 24, 10, 0, 0)); // the sums do not overflow
        vm.Apply(MonitorProbe.Compare(sample, sample), new DateTime(2026, 11, 24, 10, 0, 30));
    }

    [AvaloniaFact]
    public void AMeterComesBackFromWhatIsNotANumber()
    {
        Motion.Apply(Application.Current!, true);
        try
        {
            var meter = new Meter { Width = 100, Height = 6 };
            var window = new Window { Content = meter, Width = 120, Height = 40 };
            window.Show();
            foreach (double v in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -5, 1e308 })
            {
                meter.Value = v;
                meter.Secondary = v;
                Assert.InRange(meter.Value, 0, 100);
                Assert.InRange(meter.Secondary, 0, 100);
                meter.Value = 40;
                for (int i = 0; i < 80 && Math.Abs(meter.Value - 40) > 0.01; i++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
                Assert.Equal(40, meter.Value, 1);
            }
            window.Close();
        }
        finally { Motion.Apply(Application.Current!, false); }
    }

    static MonitorReading Reading(double? cpu, MemoryReading? memory = null, IReadOnlyList<MountReading>? mounts = null, double[]? load = null, IReadOnlyList<InterfaceReading>? interfaces = null) =>
        new(MonitorProbe.Parse("FTS1\n" + (load == null ? "" : $"LOAD\n{string.Join(' ', load)} 1/1 1\n") + "PS\n"), cpu, [], memory, null, null, mounts ?? []) { Interfaces = interfaces ?? [] };

    const ulong G = 1UL << 30;
    static readonly MemoryReading Calm = new(16 * G, 5 * G, 6 * G, 5 * G, 10 * G, 4 * G, 0);

    [Fact]
    public void NothingIsCalledHealthyBeforeItWasLookedAt()
    {
        var t = new DateTime(2026, 11, 24, 10, 0, 0);
        using var empty = new MonitorViewModel("x", () => null, autoRefresh: false);
        empty.Apply(Reading(null), t);
        Assert.Equal("没有读到可用的数据", empty.Headline);
        Assert.False(empty.IsHealthy || empty.IsWarned || empty.IsSerious);
        Assert.DoesNotContain("0 核", empty.SystemLine);

        // By hand, five minutes apart: each value is the average since the last one.
        using var byHand = new MonitorViewModel("x", () => null, autoRefresh: false);
        byHand.Apply(Reading(null, Calm), t);
        Assert.Equal("暂无异常，CPU 观察中", byHand.Headline);
        // Nothing is found before and after; the headline changes nevertheless, and says so to the view.
        using var calm = new MonitorViewModel("x", () => null, autoRefresh: false);
        var raised = new List<string?>();
        calm.Apply(Reading(null, Calm), t);
        calm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        calm.Apply(Reading(5, Calm), t.AddMinutes(5));
        Assert.Equal("运行正常", calm.Headline);
        Assert.Contains(nameof(MonitorViewModel.Headline), raised);
        byHand.Apply(Reading(99, Calm), t.AddMinutes(5));
        Assert.Contains("CPU 近 5 分钟平均 99%", Assert.Single(byHand.Findings).Text);
        Assert.Equal(MonitorLevel.Danger, byHand.Vitals[0].Level);
        byHand.Apply(Reading(5, Calm), t.AddMinutes(10));
        Assert.Equal("运行正常", byHand.Headline);
        Assert.True(byHand.IsHealthy);

        // After a long gap: the fresh readings count as soon as there are enough of them; the time named is theirs.
        MonitorPoint P(double second, double? cpu) => new(t.AddSeconds(second), cpu, 10, 0, 0, 0, 0, 0, 0);
        var fresh = new List<MonitorPoint> { P(0, null), P(3, 50), P(7203, 80) };
        for (int i = 1; i <= 5; i++) fresh.Add(P(7203 + i * 3, 100));
        var (early, overGap) = MonitorFindings.Average(fresh, p => p.Cpu)!.Value; // 15 fresh seconds are too few
        Assert.Equal((80.04, 2), (Math.Round(early, 2), Math.Round(overGap.TotalHours)));
        for (int i = 6; i <= 10; i++) fresh.Add(P(7203 + i * 3, 100));
        Assert.Equal((100d, TimeSpan.FromSeconds(30)), MonitorFindings.Average(fresh, p => p.Cpu));
        // The clock was set back an hour: what lies in the future does not count.
        for (int i = 0; i <= 10; i++) fresh.Add(P(3600 + i * 3, 20));
        Assert.Equal((20d, TimeSpan.FromSeconds(30)), MonitorFindings.Average(fresh, p => p.Cpu));

        Assert.Equal("近 40 秒", MonitorFindings.Over(TimeSpan.FromSeconds(40)));
        Assert.Equal("近 1 分钟", MonitorFindings.Over(TimeSpan.FromSeconds(75)));
        Assert.Equal("近 2 小时", MonitorFindings.Over(TimeSpan.FromMinutes(118)));
    }

    [Fact]
    public void FindingsAreWorstFirstAndInterfacesBusiestFirst()
    {
        MountReading Mount(string path, double percent) => new(new MountInfo("/dev/x" + path, "ext4", 100 * G, (ulong)(percent * G), (ulong)((100 - percent) * G), path), null, null);
        var found = MonitorFindings.Evaluate(Reading(null, new MemoryReading(100 * G, 98 * G, 0, 2 * G, 2 * G, 0, 0), [Mount("/a", 88)]), []);
        Assert.Equal([MonitorLevel.Danger, MonitorLevel.Warning], found.Select(f => f.Level)); // memory is judged after disks

        string Net(int n) => $"NET\n quiet: {1000 + n} 0 0 0 0 0 0 0 10 0 0 0 0 0 0 0\n busy: {900000 + n * 5000} 0 0 0 0 0 0 0 800000 0 0 0 0 0 0 0\n";
        var r = MonitorProbe.Compare(MonitorProbe.Parse("FTS1\nUPTIME\n103 1\n" + Net(1) + "PS\n"), MonitorProbe.Parse("FTS1\nUPTIME\n100 1\n" + Net(0) + "PS\n"));
        Assert.Equal(["busy", "quiet"], r.Interfaces.Select(i => i.Name));
    }

    [AvaloniaFact]
    public async Task AFailedReadingPausesUntilTheUserAsksAgainWhereverThatIs()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var first = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "一") with { DisableListMonitor = true });
        var second = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "二") with { DisableListMonitor = true });
        var session = first.Session;
        using var vm = new MonitorViewModel("x", () => session, autoRefresh: true);
        int readings = 0;
        vm.Updated += () => readings++;
        vm.SetVisible(true);
        await Pump(() => readings > 0, TimeSpan.FromSeconds(10));
        Assert.True(readings > 0);
        h.Model.Close(first); // the session under it goes away
        await Pump(() => vm.IsStalled, TimeSpan.FromSeconds(15));
        Assert.True(vm.IsStalled);
        Assert.Equal("已暂停", vm.RefreshText);
        Assert.Equal(0.5, vm.ContentOpacity);
        Assert.Contains("已停止自动刷新", vm.Message);
        int then = readings;
        vm.RefreshCommand.Execute(null); // still dead: it stays paused
        await Pump(() => false, TimeSpan.FromSeconds(1));
        Assert.True(vm.IsStalled);

        // The process window's button is the user asking, too; from then on it goes by itself again.
        session = second.Session;
        using var list = new ProcessListViewModel(vm, () => session, h.Interaction);
        list.RefreshCommand.Execute(null);
        await Pump(() => readings >= then + 3, TimeSpan.FromSeconds(12));
        Assert.True(readings >= then + 3, $"{readings - then} readings");
        Assert.False(vm.IsStalled);
        Assert.Equal("3 秒", vm.RefreshText);

        // Its own refreshing ends with the first failure.
        vm.AutoRefresh = false;
        list.AutoRefresh = true;
        h.Model.Close(second);
        await Pump(() => !list.AutoRefresh, TimeSpan.FromSeconds(15));
        Assert.False(list.AutoRefresh);
        Assert.Contains("监控读取失败", list.Notice);
    }

    [AvaloniaFact]
    public void ManyFindingsScrollAndLeaveRoomForTheRest()
    {
        using var vm = new MonitorViewModel("web-01", () => null, autoRefresh: true);
        var mounts = Enumerable.Range(0, 20).Select(i => new MountReading(new MountInfo("/dev/s" + i, "ext4", 100 * G, 97 * G, 3 * G, "/data/volume-" + i), null, null)).ToArray();
        vm.Apply(Reading(null, Calm, mounts), new DateTime(2026, 11, 24, 10, 0, 0));
        vm.ToggleFindingsCommand.Execute(null);
        Assert.Equal(20, vm.ShownFindings.Count);
        var view = new MonitorView { DataContext = vm };
        var window = new Window { Width = 280, Height = 780, Content = view };
        window.Show();
        Settle(window);
        var fold = view.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.Command, vm.ToggleFindingsCommand));
        Assert.InRange(fold.TranslatePoint(default, window)!.Value.Y, 0, 320);
        var details = view.GetVisualDescendants().OfType<ScrollViewer>().First(x => x.Name == "Scroll");
        Assert.True(details.Bounds.Height > 200, details.Bounds.Height.ToString());
        window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, "monitor-many-findings.png"));
        window.Close();
    }

    [AvaloniaFact]
    public void ChartsShowGapsAndRowsStayInside()
    {
        // A minute without readings is a gap in the line, not a line across it.
        int Inked(TimeSpan gap)
        {
            var end = new DateTime(2026, 11, 24, 10, 3, 0);
            var chart = new TrendChart
            {
                Width = 300, Height = 80, Window = TimeSpan.FromMinutes(3), Gap = gap, Stroke = Avalonia.Media.Brushes.Red,
                Values = Enumerable.Range(0, 61).Where(i => i is < 20 or > 40).Select(i => new TrendValue(end.AddSeconds((i - 60) * 3), 50)).ToArray(),
            };
            var window = new Window { Content = chart, Width = 300, Height = 80, Background = Avalonia.Media.Brushes.Black };
            window.Show();
            Settle(window);
            using var frame = window.CaptureRenderedFrame()!;
            using var buffer = frame.Lock();
            int count = 0;
            for (int x = 130; x < 170; x++)
                for (int y = 0; y < 80; y++)
                {
                    int pixel = System.Runtime.InteropServices.Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4);
                    int b0 = pixel & 255, green = pixel >> 8 & 255, b2 = pixel >> 16 & 255;
                    if (Math.Max(b0, b2) > 150 && green < 80) { count++; break; }
                }
            window.Close();
            return count;
        }
        Assert.Equal(0, Inked(TimeSpan.FromSeconds(12)));
        Assert.True(Inked(TimeSpan.FromMinutes(5)) > 30);

        // A row that was measured with more room gets what there is in the end.
        var row = new Border { Child = new TextBlock { Text = new string('宽', 200) }, Height = 10 };
        var panel = new RowsPanel { Children = { row, new Border { Height = 10 } } };
        var host = new Window { Content = new Canvas { Children = { panel } }, Width = 400, Height = 60 };
        host.Show();
        panel.Measure(new Size(300, 60));
        panel.Arrange(new Rect(0, 0, 200, 60));
        Assert.Equal(200, row.Bounds.Width);
        host.Close();
    }
}
