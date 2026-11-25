using System.Collections.Specialized;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Studio.Controls;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>"关联文件会话": the files of an open file session next to the terminal of another one.</summary>
public sealed class LinkTests
{
    sealed class Setup : IDisposable
    {
        public LoopbackFixture Shell { get; }
        public LoopbackFixture Files { get; } = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        public StudioHarness H { get; }
        public MainWindowViewModel Model => H.Model;
        public Setup(bool ownFiles = false, ScriptedInteraction? interaction = null)
        {
            Shell = LoopbackFixture.Ssh(ownFiles ? null : new Dictionary<string, string> { ["FIXTURE_DENY_SFTP"] = "1" });
            H = new StudioHarness(interaction);
        }
        public async Task<WorkspaceViewModel> Terminal(string title = "终端会话") => await Open("xshell", title, Shell.Port);
        public async Task<WorkspaceViewModel> Xftp(string title = "文件会话") => await Open("xftp", title, Files.Port);
        async Task<WorkspaceViewModel> Open(string entry, string title, int port)
        {
            var w = (await Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=" + entry, "-newtab", title, "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{port}"])))!;
            Dispatcher.UIThread.RunJobs();
            return w;
        }
        public void Dispose() { H.Dispose(); Shell.Dispose(); Files.Dispose(); }
    }

    static void Settle(Window window) { for (int i = 0; i < 3; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); } }
    static async Task Pump(Func<bool> until, int seconds = 10)
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        while (DateTime.UtcNow < end && !until()) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
        Dispatcher.UIThread.RunJobs();
    }
    static FileNodeViewModel Row(WorkspaceViewModel w, string name) => w.FileRoots.SelectMany(r => new[] { r }.Concat(r.Children)).First(n => n.Name == name);
    static string[] Titles(MainWindowViewModel m) => m.Workspaces.Where(w => w.IsTabShown).Select(w => w.Title).ToArray();

    [AvaloniaFact]
    public async Task AFileSessionIsShownNextToTheTerminalThatHasNoFiles()
    {
        using var s = new Setup();
        var terminal = await s.Terminal();
        Assert.True(terminal.HasShell);
        Assert.False(terminal.HasFiles);
        Assert.True(terminal.OffersLink);
        Assert.Empty(terminal.LinkChoices);
        var files = await s.Xftp();
        // Nothing by itself, although address and user are the same.
        Assert.Same(terminal, terminal.FileView);
        Assert.Equal(["终端会话", "文件会话"], Titles(s.Model));
        Assert.False(files.CanLinkFiles);
        Assert.Equal([files], terminal.LinkChoices);
        Assert.StartsWith("文件会话", terminal.LinkLabel(files));
        s.Model.Selected = terminal;
        terminal.ShowFiles = false;

        terminal.Link(files);
        Assert.Same(files, terminal.FileView);
        Assert.Equal("文件会话", terminal.FilesTitle);
        Assert.True(terminal.ShowFiles);
        Assert.True(terminal.ShowsLinkedFiles);
        Assert.False(terminal.OffersLink);
        Assert.Equal(["终端会话"], Titles(s.Model));
        Assert.Same(terminal, s.Model.Selected);
        Assert.Contains(s.H.Interaction.Toasts, t => t.Text.Contains("页签已收起"));
        // One connection and one login each, as before.
        Assert.Equal(1, s.Files.Stats().GetProperty("connections").GetInt32());
        Assert.Equal(1, s.Shell.Stats().GetProperty("connections").GetInt32());
        // Twice is once.
        terminal.Link(files);
        Assert.Same(terminal, files.Viewer);

        terminal.Unlink();
        Assert.Same(terminal, terminal.FileView);
        Assert.Equal(["终端会话", "文件会话"], Titles(s.Model));
        Assert.True(terminal.OffersLink);
        Assert.True(files.IsConnected);
        terminal.Unlink();
    }

    [AvaloniaFact]
    public async Task WhatHasNoTabIsNotSteppedToAndIsShownWhereItsFilesAre()
    {
        using var s = new Setup();
        var terminal = await s.Terminal();
        var files = await s.Xftp();
        var other = await s.Terminal("另一个");
        s.Model.Selected = terminal;
        terminal.Link(files);
        s.Model.StepSession(1);
        Assert.Same(other, s.Model.Selected);
        s.Model.StepSession(1);
        Assert.Same(terminal, s.Model.Selected);
        s.Model.StepSession(-1);
        Assert.Same(other, s.Model.Selected);
        s.Model.ShowSession(3);
        Assert.Same(other, s.Model.Selected);
        s.Model.ShowSession(1);
        Assert.Same(terminal, s.Model.Selected);
        s.Model.Selected = other;
        s.Model.Selected = files;
        Assert.Same(terminal, s.Model.Selected);
        s.Model.Selected = other;
        var focused = new List<WorkspaceViewModel>();
        s.Model.FocusRequested += focused.Add;
        files.ActivateCommand!.Execute(null);
        Assert.Equal([terminal], focused);
        s.Model.OpenPaletteCommand.Execute(null);
        Assert.DoesNotContain(s.Model.Palette.Matches, e => e.Group == "会话" && e.Title == "文件会话");
        Assert.Contains(s.Model.Palette.Matches, e => e.Group == "会话" && e.Title == "另一个");
        Assert.Contains(s.Model.Palette.Matches, e => e.Title == "上传文件到当前目录");
        var end = Assert.Single(s.Model.Palette.Matches, e => e.Title == "解除关联文件会话");
        s.Model.Palette.RunCommand.Execute(end);
        Assert.False(terminal.ShowsLinkedFiles);
        s.Model.OpenPaletteCommand.Execute(null);
        s.Model.Palette.RunCommand.Execute(Assert.Single(s.Model.Palette.Matches, e => e.Title == "关联文件会话：文件会话"));
        Assert.Same(files, terminal.FileView);
        // Linked while its own tab was in front: the session that shows it comes to the front.
        terminal.Unlink();
        s.Model.Selected = files;
        other.Link(files);
        Assert.Same(other, s.Model.Selected);
    }

    [AvaloniaFact]
    public async Task ClosingEitherSideEndsTheLinkAndLeavesTheOther()
    {
        using var s = new Setup();
        var terminal = await s.Terminal();
        var files = await s.Xftp();
        s.Model.Selected = terminal;
        terminal.Link(files);
        s.Model.Close(terminal);
        Assert.Equal(["文件会话"], Titles(s.Model));
        Assert.Same(files, s.Model.Selected);
        Assert.True(files.IsConnected);
        Assert.False(files.IsLinked);
        var again = await s.Terminal("再开");
        s.Model.Selected = again;
        again.Link(files);
        s.Model.Close(files);
        Assert.Same(again, again.FileView);
        Assert.Same(again, s.Model.Selected);
        Assert.True(again.OffersLink);
        Assert.True(again.IsConnected);
        Assert.Empty(again.LinkChoices);
        again.Link(files);
        Assert.False(again.ShowsLinkedFiles);
    }

    [AvaloniaFact]
    public async Task TheTabComesBackWithTheLastLink()
    {
        using var s = new Setup();
        var one = await s.Terminal("一");
        var two = await s.Terminal("二");
        var files = await s.Xftp();
        one.Link(files);
        two.Link(files);
        Assert.Contains("已关联到“一”", two.LinkLabel(files));
        s.Model.Selected = two;
        Assert.Same(two, files.Viewer);
        s.Model.Selected = one;
        Assert.Same(one, files.Viewer);
        one.Unlink();
        Assert.False(files.IsTabShown);
        two.Unlink();
        Assert.True(files.IsTabShown);
    }

    [AvaloniaFact]
    public async Task AnotherFileSessionTakesThePlaceOfTheFirst()
    {
        using var s = new Setup();
        var terminal = await s.Terminal();
        var first = await s.Xftp("甲");
        var second = (await s.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xftp", "-newtab", "乙", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{s.Shell.Port}"])))!;
        // Its own file channel is refused: there is nothing to show.
        Assert.False(second.HasFiles);
        Assert.Equal([first], terminal.LinkChoices);
        terminal.Link(second);
        Assert.False(terminal.ShowsLinkedFiles);
        using var more = LoopbackFixture.Ftp();
        var ftp = (await s.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xftp", "-newtab", "丙", "-url", $"ftp://synthetic:synthetic-only@127.0.0.1:{more.Port}"])))!;
        Assert.Equal([first, ftp], terminal.LinkChoices);
        terminal.Link(first);
        terminal.Link(ftp);
        Assert.Same(ftp, terminal.FileView);
        Assert.True(first.IsTabShown);
        Assert.False(ftp.IsTabShown);
        // A cd goes into the terminal of the session that shows the files, although FTP has none.
        Assert.True(ftp.CanUseTerminalFromFiles);
        terminal.Unlink();
        Assert.False(ftp.CanUseTerminalFromFiles);
    }

    [AvaloniaFact]
    public async Task EditorsAndCdGoToTheSessionThatShowsTheFilesAndStayWithTheirSource()
    {
        using var s = new Setup();
        s.Files.Command("seed-text");
        var terminal = await s.Terminal();
        var files = await s.Xftp();
        s.Model.Selected = terminal;
        terminal.Link(files);
        await files.ReloadDirectoryAsync(files.CurrentDirectory);
        files.SelectedNode = Row(files, "edit.txt");
        files.OpenEditorCommand.Execute(null);
        await Pump(() => terminal.Editors.Any());
        var editor = Assert.Single(terminal.Editors);
        Assert.Empty(files.Editors);
        Assert.Same(editor, terminal.ActiveTab);
        Assert.Contains("文件会话", editor.Details);
        // The same file again is the same tab.
        terminal.ActiveTab = terminal.Terminals[0];
        files.OpenEditorCommand.Execute(null);
        await Pump(() => ReferenceEquals(terminal.ActiveTab, editor));
        Assert.Single(terminal.Editors);

        files.SelectedNode = Row(files, "a");
        files.CdToTerminalCommand.Execute(null);
        string Typed() => string.Concat(s.Shell.Stats().GetProperty("input").EnumerateArray().Select(h => Encoding.UTF8.GetString(Convert.FromHexString(h.GetString()!))));
        await Pump(() => Typed().Length > 0);
        Assert.Equal("cd " + files.SelectedNode!.Path + "\r", Typed());
        Assert.Equal(0, s.Files.Stats().GetProperty("shells").GetInt32());
        Assert.Contains(s.H.Interaction.Confirms, c => c.Contains("向终端输入命令"));

        // The draft stays where it is, and with its own source, when the link ends.
        editor.Document.Insert(0, "x");
        Assert.True(editor.IsDirty);
        terminal.Unlink();
        Assert.Same(editor, Assert.Single(terminal.Editors));
        Assert.True(editor.CanEdit);
        Assert.True(files.NeedsCloseConfirmation);
        s.H.Interaction.ConfirmAnswer = false;
        Assert.False(await files.ConfirmCloseAsync());
        Assert.Contains("只读", s.H.Interaction.Confirms[^1]);
        Assert.Contains("edit.txt", s.H.Interaction.Confirms[^1]);
        // Everything at once: the session that shows the draft asks, and nobody else.
        s.H.Interaction.ConfirmAnswer = true;
        s.H.Interaction.Confirms.Clear();
        Assert.True(await s.Model.ConfirmCloseAllAsync());
        Assert.Single(s.H.Interaction.Confirms);
        s.H.Interaction.Confirms.Clear();
        editor.SaveCommand.Execute(null);
        await Pump(() => !editor.IsDirty && !editor.IsSaving);
        Assert.False(editor.IsDirty);
        // The save went through the queue of the file session.
        Assert.Contains(files.Transfers!.Visible, t => t.IsDone);
        Assert.Empty(terminal.Transfers!.Visible);
    }

    [AvaloniaFact]
    public async Task AFileSessionThatIsLostIsSaidToBe()
    {
        using var s = new Setup();
        var terminal = await s.Terminal();
        var files = await s.Xftp();
        s.Model.Selected = terminal;
        terminal.Link(files);
        Assert.False(files.IsLinkedSourceLost);
        s.Files.Command("drop");
        await Pump(() => !files.IsConnected);
        Assert.True(files.IsLinkedSourceLost);
        Assert.True(terminal.IsConnected);
        // Still shown, until the user decides; but nobody else can link it now.
        Assert.Same(files, terminal.FileView);
        Assert.Equal([files], terminal.LinkChoices);
        var other = await s.Terminal("另一个");
        Assert.Empty(other.LinkChoices);
        // "查看" leads to where it is.
        var toast = s.H.Interaction.Toasts.Last(t => t.Text.Contains("的连接已断开"));
        toast.Action!();
        Assert.Same(terminal, s.Model.Selected);
    }

    [AvaloniaFact]
    public async Task AFileSessionWithATerminalOfItsOwnKeepsItsTab()
    {
        using var s = new Setup();
        var terminal = await s.Terminal();
        var files = await s.Xftp();
        files.ShowTerminal = true;
        await Pump(() => files.HasShell);
        terminal.Link(files);
        Assert.True(files.IsTabShown);
        Assert.Contains(s.H.Interaction.Toasts, t => t.Text == "已关联“文件会话”的文件");
        // Its terminal goes, and its tab with it.
        s.Model.Selected = files;
        Assert.Same(files, s.Model.Selected);
        files.Terminals[0].CloseCommand!.Execute(null);
        Assert.False(files.IsTabShown);
        Assert.Same(terminal, s.Model.Selected);
    }

    [AvaloniaFact]
    public async Task TheColumnOffersTheFileSessionsAndShowsTheOneChosen()
    {
        using var s = new Setup();
        var window = new MainWindow { DataContext = s.Model, Width = 1200, Height = 720 };
        window.Show();
        try
        {
            var terminal = await s.Terminal();
            Settle(window);
            Button Named(string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            var offer = Named("OfferLink");
            Assert.True(offer.IsEffectivelyVisible);
            void Press(Control c) { var at = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value; window.MouseMove(at); window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left); Settle(window); }
            static string Text(MenuItem item) => ((TextBlock)item.Header!).Text!;
            Press(offer);
            var menu = (MenuFlyout)offer.Flyout!;
            Assert.Contains("没有打开的文件会话", Text((MenuItem)Assert.Single(menu.Items)!));
            menu.Hide();
            var files = await s.Xftp();
            s.Model.Selected = terminal;
            Settle(window);
            window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, "link-offer.png"));
            Press(offer);
            var item = (MenuItem)Assert.Single(menu.Items)!;
            Assert.StartsWith("文件会话", Text(item));
            item.Command!.Execute(null);
            menu.Hide();
            Settle(window);
            Assert.False(offer.IsEffectivelyVisible);
            var tree = window.GetVisualDescendants().OfType<TreeView>().Single(t => t.Name == "FileTree" && t.IsEffectivelyVisible);
            Assert.Same(files.FileRoots, tree.ItemsSource);
            Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("session-tab") && b.IsEffectivelyVisible);
            // A row of the tree, and the path bar, are the file session's.
            var row = tree.GetVisualDescendants().OfType<TreeViewItem>().First(r => (r.DataContext as FileNodeViewModel)?.Name == "a");
            var at = row.TranslatePoint(new Point(60, 12), window)!.Value;
            window.MouseMove(at); window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left); Settle(window);
            Assert.Equal("a", files.SelectedNode?.Name);
            Assert.Null(terminal.SelectedNode);
            Assert.Same(files.SelectedNode, tree.SelectedItem);
            // Another session and back: the row is still the one.
            s.Model.Selected = s.Model.Home; Settle(window);
            s.Model.Selected = terminal; Settle(window);
            tree = window.GetVisualDescendants().OfType<TreeView>().Single(t => t.Name == "FileTree" && t.IsEffectivelyVisible);
            Assert.Equal("a", (tree.SelectedItem as FileNodeViewModel)?.Name);
            files.NavigateRootCommand.Execute(null); Settle(window);
            Assert.Null(tree.SelectedItem);
            window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, "link-shown.png"));
            // A notice of the file session is seen here.
            files.Transfers!.ShowAllCommand.Execute(null);
            s.Files.Command("drop");
            await Pump(() => !files.IsConnected);
            Settle(window);
            Assert.True(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "LinkedSourceLost").IsEffectivelyVisible);
            window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, "link-lost.png"));
            var link = Named("LinkButton");
            Press(link);
            var entries = ((MenuFlyout)link.Flyout!).Items;
            Assert.Contains("解除关联", Text((MenuItem)entries[^1]!));
            ((MenuItem)entries[^1]!).Command!.Execute(null);
            ((MenuFlyout)link.Flyout!).Hide();
            Settle(window);
            Assert.Same(terminal, terminal.FileView);
            Assert.Equal(2, window.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("session-tab") && b.IsEffectivelyVisible));
            Assert.True(offer.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ASessionWithFilesOfItsOwnMayShowOthersAndGoBack()
    {
        using var s = new Setup(ownFiles: true);
        var terminal = await s.Terminal();
        Assert.True(terminal.HasFiles);
        Assert.False(terminal.OffersLink);
        var files = await s.Xftp();
        terminal.Link(files);
        Assert.Same(files, terminal.FileView);
        terminal.Unlink();
        Assert.Same(terminal, terminal.FileView);
        Assert.True(terminal.HasFiles);
    }
}

/// <summary>A × must not take what is next along.</summary>
public sealed class CloseGuardTests
{
    static void Settle(Window window) { for (int i = 0; i < 3; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); } }
    static Point Middle(Control c, Window window) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;
    static void Click(Window window, Point at) { window.MouseMove(at); window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left); Settle(window); }
    static Task<WorkspaceViewModel?> Launch(StudioHarness h, LoopbackFixture f, string title) =>
        h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-newtab", title, "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{f.Port}"]));

    [AvaloniaFact]
    public async Task ADoubleClickOnTheMonitorsCrossLeavesTheTerminal()
    {
        using var server = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { DataContext = h.Model, Width = 1300, Height = 760 };
        window.Show();
        try
        {
            var w = (await Launch(h, server, "会话"))!;
            Settle(window);
            await w.Monitor!.RefreshByUserAsync();
            Assert.True(w.Monitor.HasData);
            StudioPane Pane(string name) => window.GetVisualDescendants().OfType<StudioPane>().Single(p => p.Name == name);
            Button Cross(string pane) => Pane(pane).GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("collapse") && b.Classes.Contains("pane-icon"));
            CloseGuard.ForgetForTest();
            w.Terminal!.FocusTerminal();
            Settle(window);
            var at = Middle(Cross("MonitorPane"), window);
            Click(window, at);
            Assert.False(w.ShowMonitor);
            // The terminal's own × is where the monitor's was.
            Assert.Equal(at, Middle(Cross("TerminalPane"), window));
            // The keys are the terminal's again.
            Assert.True(w.Terminal!.IsKeyboardFocusWithin);
            Click(window, at);
            Click(window, at);
            Assert.True(w.ShowTerminal);
            Assert.True(w.HasShell);
            Assert.True(w.IsConnected);
            // Within the pause, but not a double click.
            window.MouseMove(new Point(at.X - 300, at.Y + 200));
            Click(window, new Point(at.X - 300, at.Y + 200));
            CloseGuard.Moved();
            Click(window, at);
            Assert.True(w.ShowTerminal);
            // Meant.
            CloseGuard.ForgetForTest();
            Click(window, new Point(at.X - 300, at.Y + 200));
            Click(window, at);
            Assert.False(w.ShowTerminal);
            Assert.True(w.HasShell);
            Assert.True(w.IsConnected);
            var toast = Assert.Single(h.Interaction.Toasts, t => t.Text.Contains("会话没有断开"));
            toast.Action!();
            Assert.True(w.ShowTerminal);
            Assert.Equal(1, server.Stats().GetProperty("connections").GetInt32());
            Assert.Equal(1, server.Stats().GetProperty("shells").GetInt32());
        }
        finally { window.Close(); CloseGuard.ForgetForTest(); }
    }

    [AvaloniaFact]
    public async Task ADoubleClickOnATabsCrossClosesOneSession()
    {
        using var server = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { DataContext = h.Model, Width = 1300, Height = 760 };
        window.Show();
        try
        {
            var first = (await Launch(h, server, "会话一"))!;
            var second = (await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xftp", "-newtab", "会话二", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{server.Port}"])))!;
            Settle(window);
            Button Cross(string title) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("session-tab") && (b.DataContext as WorkspaceViewModel)?.Title == title)
                .GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("chip-close"));
            CloseGuard.ForgetForTest();
            var at = Middle(Cross("会话一"), window);
            Click(window, at);
            var end = DateTime.UtcNow + TimeSpan.FromSeconds(8);
            while (DateTime.UtcNow < end && h.Model.Workspaces.Count == 2) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
            Settle(window);
            Assert.Equal([second], h.Model.Workspaces);
            Assert.InRange(Math.Abs(Middle(Cross("会话二"), window).X - at.X), 0, 3);
            Click(window, at);
            Click(window, at);
            await Task.Delay(200);
            Settle(window);
            Assert.Equal([second], h.Model.Workspaces);
            Assert.True(second.IsConnected);
            CloseGuard.ForgetForTest();
            Click(window, new Point(700, 400));
            Click(window, at);
            end = DateTime.UtcNow + TimeSpan.FromSeconds(8);
            while (DateTime.UtcNow < end && h.Model.Workspaces.Count == 1) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
            Assert.Empty(h.Model.Workspaces);
        }
        finally { window.Close(); CloseGuard.ForgetForTest(); }
    }
}
