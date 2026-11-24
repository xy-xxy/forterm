using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Forterm.Studio.Library;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>Phase 3: file actions, transfer queue, terminal tabs and terminal/file linkage,
/// against owned loopback SSH/SFTP and FTP fixtures.</summary>
public sealed class FileWorkspaceTests
{
    static readonly Dictionary<string, string> Posix = new() { ["FIXTURE_POSIX"] = "1" };

    static async Task Pump(Func<bool> until, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (!until() && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); await Task.Delay(40); }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task<WorkspaceViewModel> Open(StudioHarness h, int port, string protocol = "ssh")
    {
        var p = new StudioProfile { Name = "文件", Protocol = protocol, Host = "127.0.0.1", Port = port, User = "synthetic", HasPassword = true };
        await h.Credentials.SavePasswordAsync(p.Id, "synthetic-only", CancellationToken.None);
        h.Store.Upsert(p);
        var w = await h.Model.OpenProfileAsync(p);
        Assert.True(w.IsConnected, w.Status);
        Assert.True(w.HasFiles, w.FileMessage);
        return w;
    }

    /// <summary>Waits until every queued action has finished; returns the finished items.</summary>
    static async Task<List<TransferItemViewModel>> Drain(WorkspaceViewModel w, int atLeast)
    {
        await Pump(() => w.Transfers!.Visible.Count >= atLeast && w.Transfers.Visible.All(i => !i.IsActive), TimeSpan.FromSeconds(30));
        return w.Transfers!.Visible.ToList();
    }

    static FileNodeViewModel? Child(IEnumerable<FileNodeViewModel> level, string name) => level.FirstOrDefault(n => n.Name == name);

    static void Run(System.Windows.Input.ICommand command) => command.Execute(null);

    [AvaloniaFact]
    public async Task UploadDownloadRoundTripAndConfirmedOverwrite()
    {
        using var fixture = LoopbackFixture.Ssh(Posix);
        using var h = new StudioHarness();
        var w = await Open(h, fixture.Port);
        string local = Path.Combine(h.Data, "上传 测试.txt");
        File.WriteAllText(local, "第一版\nline 2\n");
        var a = Child(w.FileRoots, "a")!;
        w.SelectedNode = a;
        Assert.Equal("/a", w.CurrentDirectory);

        h.Interaction.OpenFiles = [local];
        Run(w.UploadFilesCommand);
        var done = await Drain(w, 1);
        Assert.True(done.Single().IsDone, done.Single().StateText);
        Assert.True(done.Single().IsUpload);
        await Pump(() => Child(a.Children, "上传 测试.txt") != null, TimeSpan.FromSeconds(10));
        var uploaded = Child(a.Children, "上传 测试.txt");
        Assert.NotNull(uploaded);

        // Overwrite needs confirmation; declining queues nothing.
        File.WriteAllText(local, "第二版\n");
        h.Interaction.ConfirmAnswer = false;
        w.DismissFileNoticeCommand.Execute(null);
        Run(w.UploadFilesCommand);
        await Pump(() => w.FileNotice.Length > 0, TimeSpan.FromSeconds(5));
        Assert.Equal("未上传任何文件", w.FileNotice);
        Assert.Contains(h.Interaction.Confirms, c => c.StartsWith("覆盖远程文件"));
        h.Interaction.ConfirmAnswer = true;
        Run(w.UploadFilesCommand);
        done = await Drain(w, 2);
        Assert.All(done, i => Assert.True(i.IsDone, i.StateText));

        // Download the replaced file and compare bytes.
        await Pump(() => Child(a.Children, "上传 测试.txt") != null, TimeSpan.FromSeconds(10));
        w.SelectedNode = Child(a.Children, "上传 测试.txt");
        string back = Path.Combine(h.Data, "下载.txt");
        h.Interaction.SaveFile = back;
        Run(w.DownloadCommand);
        done = await Drain(w, 3);
        Assert.All(done, i => Assert.True(i.IsDone, i.StateText));
        Assert.Equal("第二版\n", File.ReadAllText(back));
        Assert.Equal(1, w.Transfers!.DownloadCount);
        Assert.Equal(2, w.Transfers.UploadCount);
    }

    [AvaloniaFact]
    public async Task FolderUploadDownloadAndRecursiveDelete()
    {
        using var fixture = LoopbackFixture.Ssh(Posix);
        using var h = new StudioHarness();
        var w = await Open(h, fixture.Port);
        string tree = Path.Combine(h.Data, "工程");
        Directory.CreateDirectory(Path.Combine(tree, "sub", "deep"));
        File.WriteAllText(Path.Combine(tree, "x.txt"), "x");
        File.WriteAllText(Path.Combine(tree, "sub", "y.txt"), "yy");
        File.WriteAllText(Path.Combine(tree, "sub", "deep", "z.txt"), "zzz");
        File.CreateSymbolicLink(Path.Combine(tree, "link-to-x"), Path.Combine(tree, "x.txt"));

        w.SelectedNode = null; // root
        h.Interaction.Folder = tree;
        Run(w.UploadFolderCommand);
        var done = await Drain(w, 1);
        Assert.True(done.Single().IsDone, done.Single().StateText);
        Assert.Contains(h.Interaction.Confirms, c => c.Contains("3 个文件") && c.Contains("2 个子目录") && c.Contains("跳过 1 个符号链接"));
        await Pump(() => Child(w.FileRoots, "工程") != null, TimeSpan.FromSeconds(10));
        var uploaded = Child(w.FileRoots, "工程")!;

        // Uploading the same folder again is refused instead of merging/overwriting.
        w.DismissFileNoticeCommand.Execute(null);
        Run(w.UploadFolderCommand);
        await Pump(() => w.FileNotice.Contains("已存在"), TimeSpan.FromSeconds(5));
        Assert.Contains("已存在", w.FileNotice);

        // Download the folder back and compare.
        string dest = Path.Combine(h.Data, "下载目录");
        Directory.CreateDirectory(dest);
        w.SelectedNode = uploaded;
        h.Interaction.Folder = dest;
        Run(w.DownloadCommand);
        done = await Drain(w, 2);
        Assert.All(done, i => Assert.True(i.IsDone, i.StateText));
        Assert.Equal("x", File.ReadAllText(Path.Combine(dest, "工程", "x.txt")));
        Assert.Equal("yy", File.ReadAllText(Path.Combine(dest, "工程", "sub", "y.txt")));
        Assert.Equal("zzz", File.ReadAllText(Path.Combine(dest, "工程", "sub", "deep", "z.txt")));

        // Recursive delete: planned, confirmed with counts, removed bottom-up.
        w.SelectedNode = Child(w.FileRoots, "工程");
        Run(w.DeleteCommand);
        done = await Drain(w, 3);
        Assert.All(done, i => Assert.True(i.IsDone, i.StateText));
        Assert.Contains(h.Interaction.Confirms, c => c.StartsWith("删除目录") && c.Contains("3 个文件") && c.Contains("2 个子目录") && c.Contains("共 6 项"));
        await Pump(() => Child(w.FileRoots, "工程") == null, TimeSpan.FromSeconds(10));
        Assert.Null(Child(w.FileRoots, "工程"));
    }

    [AvaloniaFact]
    public async Task CreateRenameCopyPathAndExclusiveCreate()
    {
        using var fixture = LoopbackFixture.Ssh(Posix);
        using var h = new StudioHarness();
        var w = await Open(h, fixture.Port);
        w.SelectedNode = null;
        h.Interaction.PromptAnswer = "新目录";
        Run(w.NewFolderCommand);
        await Drain(w, 1);
        await Pump(() => Child(w.FileRoots, "新目录") != null, TimeSpan.FromSeconds(10));
        w.SelectedNode = Child(w.FileRoots, "新目录");
        h.Interaction.PromptAnswer = "改名后";
        Run(w.RenameCommand);
        await Drain(w, 2);
        await Pump(() => Child(w.FileRoots, "改名后") != null, TimeSpan.FromSeconds(10));
        Assert.Null(Child(w.FileRoots, "新目录"));
        w.SelectedNode = Child(w.FileRoots, "改名后");
        Run(w.CopyPathCommand);
        await Pump(() => h.Interaction.Clipboard.Length > 0, TimeSpan.FromSeconds(3));
        Assert.Equal("/改名后", h.Interaction.Clipboard);

        h.Interaction.PromptAnswer = "空文件.txt";
        Run(w.NewFileCommand);
        await Drain(w, 3);
        Run(w.NewFileCommand); // same name again: exclusive create must fail, not overwrite
        var items = await Drain(w, 4);
        Assert.Equal(1, items.Count(i => i.IsFailed));
        await Pump(() => Child(Child(w.FileRoots, "改名后")!.Children, "空文件.txt") != null, TimeSpan.FromSeconds(10));

        h.Interaction.PromptAnswer = "a/b";
        w.DismissFileNoticeCommand.Execute(null);
        Run(w.NewFolderCommand);
        await Pump(() => w.FileNotice.Contains("名称"), TimeSpan.FromSeconds(3));
        Assert.Contains("名称", w.FileNotice);
    }

    [AvaloniaFact]
    public async Task CdToTerminalAndNewTerminalHere()
    {
        using var fixture = LoopbackFixture.Ssh(Posix);
        using var h = new StudioHarness();
        var w = await Open(h, fixture.Port);
        w.SelectedNode = Child(w.FileRoots, "a");
        Run(w.CdToTerminalCommand);
        await Pump(() => Typed(fixture).Contains("cd /a\r"), TimeSpan.FromSeconds(5));
        Assert.Contains("cd /a\r", Typed(fixture));

        Run(w.NewTerminalHereCommand);
        await Pump(() => w.Terminals.Count == 2 && Typed(fixture).Split("cd /a\r").Length == 3, TimeSpan.FromSeconds(8));
        Assert.Equal(2, w.Terminals.Count);
        Assert.Same(w.Terminals[1], w.ActiveTerminal);
        Assert.Equal(2, fixture.Stats().GetProperty("shells").GetInt32());

        // Paths with spaces or quotes are single-quoted.
        Assert.Equal("cd -- '/my dir/it'\\''s'\r", WorkspaceViewModel.CdCommand("/my dir/it's"));
        Assert.Equal("cd /plain/path-1.2\r", WorkspaceViewModel.CdCommand("/plain/path-1.2"));

        // Closing a tab keeps the other shell.
        w.Terminals[0].CloseCommand!.Execute(null);
        Assert.Single(w.Terminals);
        Assert.True(w.HasShell);
    }

    static string Typed(LoopbackFixture fixture) =>
        Encoding.UTF8.GetString(fixture.Stats().GetProperty("input").EnumerateArray().SelectMany(x => Convert.FromHexString(x.GetString()!)).ToArray());

    [AvaloniaFact]
    public async Task FtpRefusesOverwriteAndHasNoTerminalLinkage()
    {
        using var fixture = LoopbackFixture.Ftp();
        using var h = new StudioHarness();
        var w = await Open(h, fixture.Port, "ftp");
        Assert.False(w.CanUseTerminalFromFiles);
        string local = Path.Combine(h.Data, "edit.txt");
        File.WriteAllText(local, "local");
        h.Interaction.OpenFiles = [local];
        w.SelectedNode = null;
        Run(w.UploadFilesCommand);
        await Pump(() => w.FileNotice.Length > 0, TimeSpan.FromSeconds(5));
        Assert.Contains("已存在", w.FileNotice);
        Assert.Empty(w.Transfers!.Visible);

        string fresh = Path.Combine(h.Data, "fresh.txt");
        File.WriteAllText(fresh, "new ftp file");
        h.Interaction.OpenFiles = [fresh];
        Run(w.UploadFilesCommand);
        var done = await Drain(w, 1);
        Assert.True(done.Single().IsDone, done.Single().StateText);
        await Pump(() => Child(w.FileRoots, "fresh.txt") != null, TimeSpan.FromSeconds(10));
        Assert.NotNull(Child(w.FileRoots, "fresh.txt"));
    }

    [AvaloniaFact]
    public async Task PathBarNavigatesAndWorkspaceRenders()
    {
        using var fixture = LoopbackFixture.Ssh(Posix);
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1440, Height = 860, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await Open(h, fixture.Port);
            var a = Child(w.FileRoots, "a")!;
            w.SelectedNode = a;
            Assert.Equal(["a"], w.PathSegments.Select(s => s.Name));
            w.NavigateRootCommand.Execute(null);
            await Pump(() => w.SelectedNode == null, TimeSpan.FromSeconds(3));
            Assert.Empty(w.PathSegments);
            w.NavigateCommand.Execute(new PathSegment("a", "/a"));
            await Pump(() => w.SelectedNode?.Path == "/a", TimeSpan.FromSeconds(3));
            Assert.Equal("/a", w.SelectedNode?.Path);
            await w.AddTerminalAsync();
            h.Interaction.PromptAnswer = "截图目录";
            Run(w.NewFolderCommand);
            await Drain(w, 1);
            await Pump(() => false, TimeSpan.FromMilliseconds(400));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, "workspace-files-and-terminal-tabs.png"));
        }
        finally { window.Close(); }
    }
}
