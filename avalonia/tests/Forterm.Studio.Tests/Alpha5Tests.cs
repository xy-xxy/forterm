using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Forterm.Studio.Controls;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>0.4.0-alpha.5: the settings page items from the reference screenshots — search, navigation
/// bar colour, system font, terminal font and colour scheme, local terminal arguments and TERM.</summary>
public sealed class Alpha5Tests
{
    static async Task Pump(Func<bool> until, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (!until() && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); await Task.Delay(40); }
        Dispatcher.UIThread.RunJobs();
    }

    static object? Resource(string key) =>
        Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) ? value : null;

    static Color Brush(string key) => Resource(key) is ISolidColorBrush b ? b.Color : default;

    static void Shot(Window window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, name + ".png"));
    }

    [AvaloniaFact]
    public void SettingsSearchNarrowsCategoriesAndCards()
    {
        string data = Paths.TempDirectory();
        try
        {
            var vm = new SettingsViewModel(data);
            Assert.True(vm.ShowThemeSection && vm.ShowTerminalSection && vm.ShowLocalSection && vm.ShowAboutSection);

            vm.SearchText = "字体";
            Assert.True(vm.ShowThemeSection);
            Assert.True(vm.ShowTerminalSection);
            Assert.False(vm.ShowLocalSection);
            Assert.False(vm.ShowAboutSection);
            Assert.True(vm.ShowUiFont);
            Assert.False(vm.ShowPresets);
            Assert.True(vm.ShowTermFont && vm.ShowTermSize);
            Assert.False(vm.ShowScheme);
            Assert.Equal("theme", vm.Section);

            // The current category has no match: the first category that has one is shown.
            vm.SearchText = "term";
            Assert.Equal("local", vm.Section);
            Assert.True(vm.ShowLocalTerm);
            Assert.False(vm.ShowShell);

            // Several words must all match, in any order; 重置 finds the appearance reset.
            vm.SearchText = "字体 终端";
            Assert.True(vm.ShowTermFont && !vm.ShowUiFont);
            vm.SearchText = "重置";
            Assert.True(vm.ShowPresets && !vm.ShowAccent);

            vm.SearchText = "没有这一项xyz";
            Assert.True(vm.NoMatches);
            Assert.False(vm.ShowThemeSection || vm.ShowTerminalSection || vm.ShowLocalSection || vm.ShowAboutSection);

            vm.ClearSearchCommand.Execute(null);
            Assert.False(vm.NoMatches);
            Assert.True(vm.ShowPresets && vm.ShowShell && vm.ShowAbout);

            vm.Dispose();
            vm = new SettingsViewModel(data);
            vm.SearchText = "配色";
            var window = new SettingsWindow { DataContext = vm };
            window.Show();
            Shot(window, "settings-search");
            window.Close();
        }
        finally
        {
            Appearance.Apply(new StudioPreferences());
            Directory.Delete(data, true);
        }
    }

    [AvaloniaFact]
    public void NavigationBarFontsAndSchemeApplyAndPersist()
    {
        string data = Paths.TempDirectory();
        using var h = new StudioHarness();
        var main = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        main.Show();
        var terminal = new StudioTerminalControl();
        try
        {
            var vm = new SettingsViewModel(data);
            var bar = main.FindControl<Border>("TitleBar")!;
            Assert.DoesNotContain("solid", bar.Classes);

            // 导航栏颜色: a dark colour gets light text, a light colour dark text.
            vm.SelectNavColorCommand.Execute(vm.NavColors.Single(c => c.Color == "#2F9E63"));
            Assert.Equal(Color.Parse("#2F9E63"), Brush("St.TitleBar"));
            Assert.Equal(Color.Parse("#F2F2F2"), Brush("St.TitleBarText"));
            Assert.Contains("solid", bar.Classes);
            Assert.Single(vm.NavColors, c => c.IsSelected);
            Shot(main, "library-nav-color");
            vm.SelectNavColorCommand.Execute(vm.NavColors.Single(c => c.Color == "#B9A88F"));
            Assert.Equal(Color.Parse("#1E1E1E"), Brush("St.TitleBarText"));
            // A background preset does not undo the chosen bar colour.
            vm.SelectThemeCommand.Execute(vm.Presets.Single(p => p.Name == "深蓝"));
            Assert.Equal(Color.Parse("#B9A88F"), Brush("St.TitleBar"));
            vm.SelectNavColorCommand.Execute(null);
            Assert.True(vm.NavColorIsDefault);
            Assert.DoesNotContain("solid", bar.Classes);
            Assert.Equal(Color.Parse("#14213A"), Brush("St.TitleBar")); // back to the preset's own colour

            // 系统字体: the Fluent window font resource, picked up by windows.
            if (vm.UiFonts.Length > 1)
            {
                string font = vm.UiFonts.First(f => f != SettingsViewModel.DefaultFont);
                vm.UiFont = font;
                Assert.Equal(font, (Resource("ContentControlThemeFontFamily") as FontFamily)?.Name);
                var probe = new Window { Content = new TextBlock { Text = "字体" } };
                probe.Show();
                Assert.Equal(font, probe.FontFamily.Name);
                probe.Close();
                vm.UiFont = SettingsViewModel.DefaultFont;
                Assert.Equal("", Appearance.Current.UiFont);
            }

            // 终端配色 and 终端字体 apply to a terminal that is already open.
            vm.Scheme = vm.Schemes.Single(s => s.Name == "Dracula");
            Assert.Equal(Color.Parse("#282A36"), ((ISolidColorBrush)terminal.Background!).Color);
            Assert.Equal("#282A36", terminal.Options!.Theme!.Background);
            Assert.Equal(Color.Parse("#282A36"), Brush("St.TerminalBack"));
            vm.Scheme = vm.Schemes.Single(s => s.Name == TerminalSchemes.Auto);
            Assert.Equal(StudioTerminalControl.DarkBackground, ((ISolidColorBrush)terminal.Background!).Color);
            vm.Scheme = vm.Schemes.Single(s => s.Name == "Solarized 亮色");
            if (vm.TerminalFonts.Length > 1)
            {
                string mono = vm.TerminalFonts[1];
                vm.TerminalFont = mono;
                // The chosen family first, the built-in monospace stack kept behind it.
                Assert.Equal(mono, terminal.FontFamily.FamilyNames[0]);
                Assert.Contains("monospace", terminal.FontFamily.FamilyNames);
            }

            var saved = Appearance.Load(data);
            Assert.Equal("Solarized 亮色", saved.TerminalScheme);
            Assert.Equal("", saved.TitleBarColor);
            // 恢复默认外观 keeps terminal settings.
            vm.SelectNavColorCommand.Execute(vm.NavColors[0]);
            vm.ResetCommand.Execute(null);
            Assert.Equal("", Appearance.Current.TitleBarColor);
            Assert.Equal("Solarized 亮色", Appearance.Current.TerminalScheme);
            vm.Dispose();
        }
        finally
        {
            terminal.Dispose();
            main.Close();
            Appearance.Apply(new StudioPreferences());
            Directory.Delete(data, true);
        }
    }

    /// <summary>The terminal in a real window, with its template and emulator, as users see it.</summary>
    static (Window Window, StudioTerminalControl Terminal) ShowTerminal()
    {
        var terminal = new StudioTerminalControl();
        var window = new Window { Width = 700, Height = 400, Content = terminal };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (window, terminal);
    }

    static int Rgb(string hex) { var c = Color.Parse(hex); return (c.R << 16) | (c.G << 8) | c.B; }

    static void AssertColors(StudioTerminalControl t, string fg, string bg, string cursor)
    {
        Assert.Equal(Color.Parse(fg), ((ISolidColorBrush)t.Foreground!).Color);
        Assert.Equal(Color.Parse(bg), ((ISolidColorBrush)t.Background!).Color);
        Assert.Equal(Rgb(fg), t.Terminal.Colors.Foreground);
        Assert.Equal(Rgb(bg), t.Terminal.Colors.Background);
        Assert.Equal(Color.Parse(cursor), t.CursorColor);
    }

    [AvaloniaFact]
    public void TerminalColoursReachTheEmulatorAndSurviveReattaching()
    {
        Appearance.Apply(new StudioPreferences());
        var (window, t) = ShowTerminal();
        try
        {
            // 暗夜 + 跟随主题: the scheme's own foreground, not the vendor template's white.
            AssertColors(t, "#D7DEE0", "#00161C", "#3FB176");
            // A light theme has dark text.
            Appearance.Apply(new StudioPreferences { Theme = "明亮" });
            window.UpdateLayout();
            AssertColors(t, "#24292F", "#FBFBFB", "#2FA765");
            // A light scheme in a dark theme.
            Appearance.Apply(new StudioPreferences { TerminalScheme = "Solarized 亮色" });
            AssertColors(t, "#657B83", "#FDF6E3", "#586E75");
            Assert.NotNull(t.SelectionBrush);
            Assert.True(((ISolidColorBrush)t.SelectionBrush!).Color.A < 255); // drawn over text
            // What a tab switch does: the control leaves the tree and comes back.
            window.Content = null;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.Content = t;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AssertColors(t, "#657B83", "#FDF6E3", "#586E75");
            // Back to following the theme.
            Appearance.Apply(new StudioPreferences { Theme = "明亮" });
            AssertColors(t, "#24292F", "#FBFBFB", "#2FA765");
        }
        finally
        {
            window.Close();
            t.Dispose();
            Appearance.Apply(new StudioPreferences());
        }
    }

    [AvaloniaFact]
    public void UnrelatedSettingChangesKeepColoursAProgramSet()
    {
        string data = Paths.TempDirectory();
        Appearance.Apply(new StudioPreferences());
        var (window, t) = ShowTerminal();
        var vm = new SettingsViewModel(data);
        try
        {
            // What an OSC 11 from a program does.
            t.Terminal.Colors.SetBackground(0x550000);
            vm.LocalShell = "/bin/a";
            vm.LocalShell = "/bin/ab";
            vm.TerminalFontSize = 18;
            Assert.Equal(0x550000, t.Terminal.Colors.Background);
            // Choosing another scheme is a real change and re-themes.
            vm.Scheme = vm.Schemes.Single(s => s.Name == "Nord");
            Assert.Equal(Rgb("#2E3440"), t.Terminal.Colors.Background);
        }
        finally
        {
            vm.Dispose();
            window.Close();
            t.Dispose();
            Appearance.Apply(new StudioPreferences());
            Directory.Delete(data, true);
        }
    }

    [AvaloniaFact]
    public void HidingATerminalKeepsItsColoursInEveryTheme()
    {
        foreach (string theme in new[] { "明亮", "暗夜" })
        {
            Appearance.Apply(new StudioPreferences { Theme = theme });
            var (window, t) = ShowTerminal();
            try
            {
                t.Terminal.Colors.SetBackground(0x550000); // OSC 11
                window.Content = null; // another tab is shown
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0x550000, t.Terminal.Colors.Background);
                window.Content = t;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0x550000, t.Terminal.Colors.Background);

                // A theme change while hidden is picked up.
                window.Content = null;
                window.UpdateLayout();
                Appearance.Apply(new StudioPreferences { Theme = theme == "明亮" ? "暗夜" : "明亮" });
                window.Content = t;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Rgb(theme == "明亮" ? "#00161C" : "#FBFBFB"), t.Terminal.Colors.Background);
            }
            finally
            {
                window.Close();
                t.Dispose();
            }
        }
        Appearance.Apply(new StudioPreferences());
    }

    [Fact]
    public void SelectedTextStaysReadableInEveryScheme()
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
                Color fg = Color.Parse(palette.Foreground!), bg = Color.Parse(palette.Background!), overlay = TerminalSchemes.SelectionOverlay(palette);
                double text = TerminalSchemes.Contrast(Over(fg, overlay), Over(bg, overlay)), highlight = TerminalSchemes.Contrast(Over(bg, overlay), bg);
                Assert.True(text >= 2.9, $"{scheme.Name}: selected text contrast {text:0.00}");
                Assert.True(highlight >= 1.3, $"{scheme.Name}: highlight contrast {highlight:0.00}");
            }
    }

    [AvaloniaFact]
    public void ASecondSettingsWindowNeverWipesAValueItDoesNotList()
    {
        string data = Paths.TempDirectory();
        Appearance.Apply(new StudioPreferences { LocalTerminalType = "xterm-kitty" });
        var first = new SettingsViewModel(data); // lists xterm-kitty
        first.Section = "local";
        var a = new SettingsWindow { DataContext = first };
        a.Show();
        first.LocalTerminalType = "vt100";
        var second = new SettingsViewModel(data); // does not
        second.Section = "local";
        var b = new SettingsWindow { DataContext = second };
        b.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            first.LocalTerminalType = "xterm-kitty";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("xterm-kitty", Appearance.Current.LocalTerminalType);
            Assert.Equal("xterm-kitty", Appearance.Load(data).LocalTerminalType);
            second.UiFont = null!;
            second.TerminalFont = null!;
            second.LocalTerminalType = null!;
            Assert.Equal("xterm-kitty", Appearance.Current.LocalTerminalType);
        }
        finally
        {
            a.Close();
            b.Close();
            Appearance.Apply(new StudioPreferences());
            Directory.Delete(data, true);
        }
    }

    [AvaloniaFact]
    public void OpenSettingsWindowsShareLivePreferences()
    {
        string data = Paths.TempDirectory();
        Appearance.Apply(new StudioPreferences());
        var first = new SettingsViewModel(data);
        var second = new SettingsViewModel(data);
        try
        {
            // The title bar toggle changes the theme behind the settings windows' back.
            Appearance.Apply(Appearance.Current with { Theme = "明亮" });
            first.TerminalFontSize = 18;
            Assert.Equal("明亮", Appearance.Current.Theme); // not reverted to the stale theme
            Assert.Equal(18, second.TerminalFontSize);     // and the second window sees the first's change
            second.UiScalePercent = 120;
            Assert.Equal(18, Appearance.Current.TerminalFontSize);
            Assert.Equal(120, first.UiScalePercent);
            Assert.Equal("明亮", Appearance.Load(data).Theme);
        }
        finally
        {
            first.Dispose();
            second.Dispose();
            Appearance.Apply(new StudioPreferences());
            Directory.Delete(data, true);
        }
    }

    [AvaloniaFact]
    public void UnwritableDirectoryShowsAnErrorInsteadOfCrashing()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX permission bits");
        string data = Paths.TempDirectory();
        var vm = new SettingsViewModel(data);
        try
        {
            File.SetUnixFileMode(data, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            vm.TerminalFontSize = 17;
            Assert.Equal(17, Appearance.Current.TerminalFontSize); // applied
            Assert.True(vm.HasSaveError);
            Assert.Contains("没有保存", vm.SaveError);
            File.SetUnixFileMode(data, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            // A save from another window writes everything, including this window's change.
            using (var other = new SettingsViewModel(data)) other.UiScalePercent = 110;
            Assert.False(vm.HasSaveError);
            Assert.Equal(17, Appearance.Load(data).TerminalFontSize);
        }
        finally
        {
            vm.Dispose();
            File.SetUnixFileMode(data, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Appearance.Apply(new StudioPreferences());
            Directory.Delete(data, true);
        }
    }

    [AvaloniaFact]
    public void HostileFontNamesNeverReachTheLayout()
    {
        foreach (string name in new[] { "/etc#x", "avares://x#y", "fonts:nope#x", "A#B", "a:b", ".AppleSystemUIFont", "x/../y" })
        {
            var p = new StudioPreferences { UiFont = name, TerminalFont = name }.Validated();
            Assert.Equal("", p.UiFont);
            Assert.Equal("", p.TerminalFont);
        }
        Assert.Equal("微软雅黑", new StudioPreferences { UiFont = "微软雅黑" }.Validated().UiFont);
        // A name that is simply not installed falls back quietly.
        Appearance.Apply(new StudioPreferences { UiFont = "Forterm No Such Font", TerminalFont = "Forterm No Such Font" });
        var (window, t) = ShowTerminal();
        try { Assert.NotNull(t.Terminal); }
        finally
        {
            window.Close();
            t.Dispose();
            Appearance.Apply(new StudioPreferences());
        }
    }

    [AvaloniaFact]
    public void ValuesOutsideTheListsStaySelectedAndRejectedTextIsReported()
    {
        string data = Paths.TempDirectory();
        Appearance.Apply(new StudioPreferences { LocalTerminalType = "xterm-kitty", TerminalFont = "menlo" });
        var vm = new SettingsViewModel(data);
        try
        {
            Assert.Equal("xterm-kitty", vm.LocalTerminalType);
            Assert.Contains("xterm-kitty", vm.TerminalTypes);
            Assert.Contains(vm.TerminalFont, vm.TerminalFonts);

            vm.LocalShellArgs = "ok";
            vm.LocalShellArgs = "-c	echo";
            Assert.Equal("-c	echo", vm.LocalShellArgs); // still in the box
            Assert.Contains("没有保存", vm.LocalShellArgsStatus);
            Assert.True(vm.LocalShellArgsInvalid);
            Assert.Equal("ok", Appearance.Current.LocalShellArgs);
            vm.LocalShellArgs = "-l";
            Assert.Equal("-l", Appearance.Current.LocalShellArgs);
            Assert.False(vm.LocalShellArgsInvalid);

            vm.LocalShell = new string('x', 2000);
            Assert.Contains("没有保存", vm.LocalShellStatus);
            Assert.Equal("", Appearance.Current.LocalShell);
        }
        finally
        {
            vm.Dispose();
            Appearance.Apply(new StudioPreferences());
            Directory.Delete(data, true);
        }
    }

    [Fact]
    public void PreferencesRejectUnsafeOrUnknownValues()
    {
        var p = new StudioPreferences
        {
            TitleBarColor = "#123456", UiFont = "Menlo,Comic Sans MS", TerminalFont = "Menlo\n", TerminalScheme = "bogus",
            LocalTerminalType = "xterm 256", LocalShellArgs = "-c \u001b[2J", LocalShell = " /bin/zsh ",
        }.Validated();
        Assert.Equal("", p.TitleBarColor);
        Assert.Equal("", p.UiFont);
        Assert.Equal("", p.TerminalFont);
        Assert.Equal(TerminalSchemes.Auto, p.TerminalScheme);
        Assert.Equal("", p.LocalTerminalType);
        Assert.Equal("", p.LocalShellArgs);
        Assert.Equal(" /bin/zsh ", p.LocalShell); // not trimmed while typing; trimmed when used
        Assert.Equal("vt100", new StudioPreferences { LocalTerminalType = "vt100" }.Validated().LocalTerminalType);
        Assert.Equal("-l --login", new StudioPreferences { LocalShellArgs = "-l --login" }.Validated().LocalShellArgs);

        string dir = Paths.TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "preferences.json"), "{\"TerminalScheme\":null,\"UiFont\":null,\"TitleBarColor\":\"#2F9E63\"}");
            var loaded = Appearance.Load(dir);
            Assert.Equal(TerminalSchemes.Auto, loaded.TerminalScheme);
            Assert.Equal("", loaded.UiFont);
            Assert.Equal("#2F9E63", loaded.TitleBarColor);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ArgumentsAreSplitWithoutAnyShellSyntax()
    {
        Assert.Equal(["-l", "--login"], CommandLineArgs.Split("  -l   --login "));
        Assert.Equal(["a b", "c"], CommandLineArgs.Split("\"a b\" c"));
        Assert.Equal([""], CommandLineArgs.Split("\"\""));
        Assert.Equal(["say \"hi\" now"], CommandLineArgs.Split("\"say \\\"hi\\\" now\""));
        Assert.Equal(["ab"], CommandLineArgs.Split("a\"b\""));
        Assert.Equal(["$HOME", "*.txt", "|", "rm", "-rf", "`id`"], CommandLineArgs.Split("$HOME *.txt | rm -rf `id`"));
        Assert.Equal(["C:\\Program Files\\x"], CommandLineArgs.Split("\"C:\\Program Files\\x\""));
        Assert.Empty(CommandLineArgs.Split(""));
        Assert.Empty(CommandLineArgs.Split(null));
        Assert.Throws<FormatException>(() => CommandLineArgs.Split("\"unclosed"));
        Assert.Throws<FormatException>(() => CommandLineArgs.Split(string.Join(" ", Enumerable.Range(0, CommandLineArgs.MaxArguments + 1))));
        Assert.NotNull(CommandLineArgs.TrySplit("\"x").Error);
    }

    [AvaloniaFact]
    public void LocalTerminalGetsArgumentsAndTermButFallbackGetsNoArguments()
    {
        string dir = Paths.TempDirectory();
        // Any existing file serves as the configured program; nothing is launched here.
        string program = Path.Combine(dir, "forterm shell.bin"), batch = Path.Combine(dir, "tool.cmd");
        File.WriteAllText(program, "");
        File.WriteAllText(batch, "");
        var control = new StudioTerminalControl();
        try
        {
            // macOS / Linux: the argument array goes straight to exec.
            Appearance.Apply(new StudioPreferences { LocalShell = program, LocalShellArgs = "-c \"echo hi\"", LocalTerminalType = "vt100" });
            Assert.Null(WorkspaceViewModel.ConfigureLocal(control, windows: false));
            Assert.Equal(program, control.Process);
            Assert.Equal(["-c", "echo hi"], control.ProcessArgs);
            Assert.False(control.VerbatimCommandLine);
            Assert.Equal("vt100", control.EnvironmentVariables!["TERM"]);

            // Windows: exactly what was typed, so quotes keep their meaning for programs such as cmd.exe
            // ("R&D" unquoted would run a second command) and empty arguments are not lost.
            foreach (string typed in new[] { "-d C:\\ -x \"\" \"a b\"", "/k cd /d \"C:\\R&D\"", "/k echo \"a>b\"", "/k echo %PATH%" })
            {
                Appearance.Apply(new StudioPreferences { LocalShell = program, LocalShellArgs = "  " + typed + " " });
                Assert.Null(WorkspaceViewModel.ConfigureLocal(control, windows: true));
                Assert.True(control.VerbatimCommandLine);
                Assert.Equal(typed, Assert.Single(control.ProcessArgs));
            }
            Assert.Null(control.EnvironmentVariables);
            Appearance.Apply(new StudioPreferences { LocalShell = program });
            Assert.Null(WorkspaceViewModel.ConfigureLocal(control, windows: true));
            Assert.Empty(control.ProcessArgs);
            Assert.False(control.VerbatimCommandLine);

            // cmd.exe would interpret batch file arguments, so none are passed and the reason is shown.
            Appearance.Apply(new StudioPreferences { LocalShell = batch, LocalShellArgs = "%TEMP%" });
            string? note = WorkspaceViewModel.ConfigureLocal(control, windows: true);
            Assert.Contains("批处理", note);
            Assert.Empty(control.ProcessArgs);
            Assert.Null(WorkspaceViewModel.ConfigureLocal(control, windows: false));

            // A configured program that is missing: the fallback shell starts without those arguments.
            Appearance.Apply(new StudioPreferences { LocalShell = Path.Combine(dir, "missing"), LocalShellArgs = "--only-for-that-program" });
            note = WorkspaceViewModel.ConfigureLocal(control, windows: false);
            Assert.NotEqual(Path.Combine(dir, "missing"), control.Process);
            Assert.Empty(control.ProcessArgs);
            Assert.Contains("未使用默认程序参数", note);

            // Arguments that cannot be parsed are left out with a reason, never guessed.
            Appearance.Apply(new StudioPreferences { LocalShellArgs = "\"unclosed" });
            Assert.Contains("无法解析", WorkspaceViewModel.ConfigureLocal(control, windows: false));
            Assert.Empty(control.ProcessArgs);
        }
        finally
        {
            control.Dispose();
            Appearance.Apply(new StudioPreferences());
            Directory.Delete(dir, true);
        }
    }

    [AvaloniaFact]
    public async Task SettingsPagesAndSchemeRender()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        string data = Paths.TempDirectory();
        var window = new MainWindow { Width = 1440, Height = 860, DataContext = h.Model };
        window.Show();
        try
        {
            var vm = new SettingsViewModel(data);
            vm.SelectNavColorCommand.Execute(vm.NavColors.Single(c => c.Color == "#2F9E63"));
            vm.Scheme = vm.Schemes.Single(s => s.Name == "Dracula");
            var settings = new SettingsWindow { DataContext = vm };
            settings.Show();
            Shot(settings, "settings-theme-nav-font");
            vm.ShowSectionCommand.Execute("terminal");
            Shot(settings, "settings-terminal");
            vm.LocalShellArgs = "-l \"--rcfile=/etc/forterm demo\"";
            vm.LocalTerminalType = "screen-256color";
            vm.ShowSectionCommand.Execute("local");
            Shot(settings, "settings-local");
            settings.Close();

            var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "配色截图"));
            Assert.True(w.IsConnected, w.Status);
            await Pump(() => w.HasShell, TimeSpan.FromSeconds(10));
            await Pump(() => false, TimeSpan.FromMilliseconds(500));
            Shot(window, "workspace-dracula-nav-color");
            // Already open terminal, light theme following the application.
            Appearance.Apply(new StudioPreferences { Theme = "明亮" });
            await Pump(() => false, TimeSpan.FromMilliseconds(300));
            Shot(window, "workspace-light-terminal");
        }
        finally
        {
            window.Close();
            Appearance.Apply(new StudioPreferences());
            Directory.Delete(data, true);
        }
    }
}
