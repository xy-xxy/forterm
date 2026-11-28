using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Studio.Views;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>Studio 0.4.0-alpha.16.</summary>
public sealed class Alpha16Tests
{
    static void Settle(Window window) { for (int i = 0; i < 3; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); } }

    /// <summary>"传输管理" opens from its button to the right and down, inside the window: the button is near the left edge of
    /// the window, and the list grew leftwards past it (a user's screenshot, alpha.15).</summary>
    [AvaloniaFact]
    public async Task TheTransferListOpensRightwardsFromItsButton()
    {
        using var shell = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1200, Height = 700, DataContext = h.Model };
        window.Show();
        try
        {
            var w = (await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-newtab", "终端", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{shell.Port}"])))!;
            var end = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < end && !w.HasFiles) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
            Settle(window);
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "传输管理" && b.IsEffectivelyVisible);
            // Headless, the screen is the window and a list that would leave it is turned back; on Windows the limit is the
            // screen, and it went past the window onto the desktop. So the side it opens to is part of the test.
            Assert.Equal(Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft, ((Flyout)button.Flyout!).Placement);
            button.Flyout!.ShowAt(button);
            Settle(window);
            var list = Menus.Presenter(button.Flyout)!;
            var at = list.TranslatePoint(new Point(0, 0), window)!.Value;
            var from = button.TranslatePoint(new Point(0, 0), window)!.Value;
            Assert.True(at.X >= from.X - 1, $"the list starts left of its button: {at.X} < {from.X}");
            Assert.True(at.Y >= from.Y + button.Bounds.Height - 1, "the list is below its button");
            Assert.True(at.X + list.Bounds.Width <= window.Bounds.Width, "the list ends inside the window");
            window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, "transfer-list-placement.png"));
            button.Flyout.Hide();
            Settle(window);
            foreach (var x in h.Model.Workspaces.ToList()) h.Model.Close(x);
        }
        finally { window.Close(); }
    }

    static async Task Pump(Func<bool> until, int seconds = 15)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end && !until()) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task<Forterm.Studio.ViewModels.WorkspaceViewModel> Files(StudioHarness h, int port)
    {
        var p = new Forterm.Studio.Library.StudioProfile { Name = "文件", Protocol = "ssh", Host = "127.0.0.1", Port = port, User = "synthetic", HasPassword = true };
        await h.Credentials.SavePasswordAsync(p.Id, "synthetic-only", CancellationToken.None);
        h.Store.Upsert(p);
        var w = await h.Model.OpenProfileAsync(p);
        Assert.True(w.HasFiles, w.FileMessage);
        return w;
    }

    /// <summary>A server that answers "failure" for the attributes of an open file (a bastion's SFTP, 2026-11-28: an upload
    /// failed at 100 %, right after its check): uploads, "新建文件", the editor's reads, saves and "另存为", and replacing a
    /// file are all checked by their path (owner and group kept, the mode set by the path), and committed.</summary>
    [AvaloniaFact]
    public async Task FilesWorkWhenTheServerCannotSayWhatAnOpenFileIs()
    {
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1", ["FIXTURE_NO_FSTAT"] = "1" });
        using var h = new StudioHarness();
        var w = await Files(h, fixture.Port);
        var a = w.FileRoots.First(n => n.Name == "a");
        w.SelectedNode = a;
        string local = Path.Combine(h.Data, "上传.txt");
        File.WriteAllText(local, "第一版\n");
        h.Interaction.OpenFiles = [local];
        w.UploadFilesCommand.Execute(null);
        await Pump(() => w.Transfers!.Visible.Count == 1 && !w.Transfers.Visible[0].IsActive, 30);
        var done = Assert.Single(w.Transfers!.Visible);
        Assert.True(done.IsDone, done.StateText);
        Assert.True(fixture.Stats().GetProperty("handleStats").GetInt32() > 0, "the server was asked by handle and refused");
        await Pump(() => a.Children.Any(c => c.Name == "上传.txt"));
        var node = a.Children.First(c => c.Name == "上传.txt");

        // The editor reads it, checked by its path; saves it, and saves it as another.
        var editor = await w.OpenEditorAsync(node);
        Assert.NotNull(editor);
        Assert.Contains("第一版", editor!.Document.Text);
        editor.Document.Text = "第一版，改过\n";
        editor.SaveCommand.Execute(null);
        await Pump(() => !editor.IsSaving && !editor.IsDirty, 30);
        Assert.False(editor.IsDirty, editor.Status);
        h.Interaction.PromptAnswer = "另存.txt";
        editor.SaveAsCommand.Execute(null);
        await Pump(() => editor.Status.StartsWith("已另存为", StringComparison.Ordinal) || editor.Status.StartsWith("保存未完成", StringComparison.Ordinal), 30);
        Assert.StartsWith("已另存为", editor.Status);
        // "新建文件".
        h.Interaction.PromptAnswer = "新建.txt";
        w.SelectedNode = a;
        w.NewFileCommand.Execute(null);
        await Pump(() => w.Transfers.Visible.Any(i => i.Name.Contains("新建.txt") && !i.IsActive), 30);
        var created = w.Transfers.Visible.Single(i => i.Name.Contains("新建.txt"));
        Assert.True(created.IsDone, created.StateText);

        // Replacing it by an upload: done, and nothing is left behind.
        File.WriteAllText(local, "第二版\n");
        h.Interaction.ConfirmAnswer = true;
        int before = w.Transfers.Visible.Count;
        w.UploadFilesCommand.Execute(null);
        await Pump(() => w.Transfers.Visible.Count == before + 1 && w.Transfers.Visible.All(i => !i.IsActive), 30);
        var replaced = w.Transfers.Visible.Last();
        Assert.True(replaced.IsDone, replaced.StateText);
        w.SelectedNode = a;
        w.RefreshDirectoryCommand.Execute(null);
        await Pump(() => !a.Children.Any(c => c.Name.StartsWith(".forterm-upload-", StringComparison.Ordinal)) && a.Children.Any(c => c.Name == "新建.txt"), 15);
        Assert.DoesNotContain(a.Children, c => c.Name.StartsWith(".forterm-upload-", StringComparison.Ordinal));
        foreach (var e in w.Editors.ToList()) e.CloseCommand?.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var now = await w.OpenEditorAsync(a.Children.First(c => c.Name == "上传.txt"));
        Assert.Equal("第二版\n", now!.Document.Text);
        foreach (var x in h.Model.Workspaces.ToList()) h.Model.Close(x);
    }

    /// <summary>A file channel the server takes and then ends without closing it (stderr, exit status, EOF: a bastion that
    /// could not log into the target) is said at once, with what the server wrote, instead of after the 30 s timeout; the
    /// connection report keeps the server's words out.</summary>
    [AvaloniaTheory]
    [InlineData("both")]
    [InlineData("eof")]
    [InlineData("exit")]
    [InlineData("exit-first")]
    [InlineData("close")]
    [InlineData("exit-close")]
    public async Task AFileChannelTheServerEndsIsSaidAtOnce(string how)
    {
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_SFTP_ENDS"] = "synthetic: login to the target failed", ["FIXTURE_SFTP_ENDS_WITH"] = how });
        using var h = new StudioHarness();
        var started = DateTime.UtcNow;
        var w = (await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-newtab", "终端", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"])))!;
        await Pump(() => w.FileMessage.Length > 0, 20);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(15), "said at once, not after the timeout");
        Assert.True(w.HasShell);
        Assert.Contains(how == "eof" ? "服务器结束了文件通道（SFTP），" : "服务器结束了文件通道（SFTP）（退出码 1）", w.FileMessage);
        Assert.Contains("服务器说：synthetic: login to the target failed", w.FileMessage);
        Assert.DoesNotContain("login to the target failed", w.Report());
        foreach (var x in h.Model.Workspaces.ToList()) h.Model.Close(x);
    }

    /// <summary>Without the options (Forterm.Desktop, which shares the library and Core) nothing changes: a file channel the
    /// server ends is still waited for, and the file source does not check by path.</summary>
    [AvaloniaFact]
    public async Task WithoutTheOptionsNothingChanges()
    {
        Assert.False(new SessionConnectOptions().SubsystemEndsWaits);
        Assert.False(new SessionConnectOptions().PathStampWhenHandleFails);
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_SFTP_ENDS"] = "synthetic" });
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var session = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token, options: new SessionConnectOptions());
        try
        {
            var open = session.OpenFilesAsync(ct.Token);
            Assert.NotSame(open, await Task.WhenAny(open, Task.Delay(3000)));
            _ = open.ContinueWith(t => t.Exception, TaskScheduler.Default);
        }
        finally { session.Dispose(); }
    }
}
