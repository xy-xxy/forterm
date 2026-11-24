using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.Library;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>Phase 5: built-in editor, permissions, compress, download to desktop, pwd follow.</summary>
public sealed class EditorAndAdvancedFileTests
{
    static readonly Dictionary<string, string> Posix = new() { ["FIXTURE_POSIX"] = "1" };

    static async Task Pump(Func<bool> until, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (!until() && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); await Task.Delay(40); }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task<WorkspaceViewModel> Open(StudioHarness h, int port, string protocol = "ssh", bool disablePwd = false)
    {
        var p = new StudioProfile { Name = "进阶", Protocol = protocol, Host = "127.0.0.1", Port = port, User = "synthetic", HasPassword = true, DisablePwdFollow = disablePwd };
        await h.Credentials.SavePasswordAsync(p.Id, "synthetic-only", CancellationToken.None);
        h.Store.Upsert(p);
        var w = await h.Model.OpenProfileAsync(p);
        Assert.True(w.IsConnected, w.Status);
        return w;
    }

    static async Task Drain(WorkspaceViewModel w, int atLeast)
    {
        await Pump(() => w.Transfers!.Visible.Count >= atLeast && w.Transfers.Visible.All(i => !i.IsActive), TimeSpan.FromSeconds(30));
        Assert.All(w.Transfers!.Visible, i => Assert.False(i.IsActive));
    }

    static FileNodeViewModel? Child(IEnumerable<FileNodeViewModel> level, string name) => level.FirstOrDefault(n => n.Name == name);

    static async Task<FileNodeViewModel> Upload(StudioHarness h, WorkspaceViewModel w, string name, byte[] content)
    {
        string local = Path.Combine(h.Data, name);
        File.WriteAllBytes(local, content);
        w.SelectedNode = null;
        h.Interaction.OpenFiles = [local];
        int before = w.Transfers!.Visible.Count;
        w.UploadFilesCommand.Execute(null);
        await Drain(w, before + 1);
        await Pump(() => Child(w.FileRoots, name) != null, TimeSpan.FromSeconds(10));
        return Child(w.FileRoots, name)!;
    }

    static async Task<byte[]> Download(StudioHarness h, WorkspaceViewModel w, FileNodeViewModel node)
    {
        string target = Path.Combine(h.Data, "back-" + Guid.NewGuid().ToString("N")[..6]);
        w.SelectedNode = node;
        h.Interaction.SaveFile = target;
        int before = w.Transfers!.Visible.Count;
        w.DownloadCommand.Execute(null);
        await Drain(w, before + 1);
        return File.ReadAllBytes(target);
    }

    [AvaloniaFact]
    public async Task EditorSavesKeepingBomAndCrlfAndRefusesUnsafeReplace()
    {
        using var fixture = LoopbackFixture.Ssh(Posix);
        using var h = new StudioHarness();
        var w = await Open(h, fixture.Port);
        byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("第一行\r\nsecond\r\n")];
        var node = await Upload(h, w, "说明.txt", original);

        var editor = (await w.OpenEditorAsync(node))!;
        Assert.Same(editor, w.ActiveTab);
        Assert.Equal("第一行\r\nsecond\r\n", editor.Document.Text);
        Assert.Contains("UTF-8 BOM", editor.Details);
        Assert.Contains("CRLF", editor.Details);
        Assert.Same(editor, await w.OpenEditorAsync(node)); // same file → same tab
        Assert.Single(w.Editors);

        editor.Document.Insert(editor.Document.TextLength, "第三行\r\n");
        Assert.True(editor.IsDirty);
        Assert.StartsWith("● ", editor.Title);
        Assert.True(w.HasUnsavedEditors);
        editor.SaveCommand.Execute(null);
        await Pump(() => !editor.IsSaving && !editor.IsDirty, TimeSpan.FromSeconds(20));
        Assert.False(editor.IsDirty, editor.Status);
        Assert.Contains(h.Interaction.Confirms, c => c.StartsWith("保存远程文件"));
        var saved = await Download(h, w, Child(w.FileRoots, "说明.txt")!);
        Assert.Equal([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("第一行\r\nsecond\r\n第三行\r\n")], saved);

        // Closing a dirty draft needs confirmation; declining keeps the tab.
        editor.Document.Insert(0, "x");
        h.Interaction.ConfirmAnswer = false;
        Assert.False(await w.ConfirmCloseAsync());
        editor.CloseCommand!.Execute(null);
        await Pump(() => false, TimeSpan.FromMilliseconds(200));
        Assert.Contains(editor, w.Editors);
        h.Interaction.ConfirmAnswer = true;
        editor.CloseCommand.Execute(null);
        await Pump(() => !w.Editors.Contains(editor), TimeSpan.FromSeconds(3));
        Assert.DoesNotContain(editor, w.Editors);
    }

    [AvaloniaFact]
    public async Task WithoutAtomicReplaceOnlySaveAsIsAllowed()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var w = await Open(h, fixture.Port);
        var node = await Upload(h, w, "plain.txt", Encoding.UTF8.GetBytes("a\n"));
        var editor = (await w.OpenEditorAsync(node))!;
        editor.Document.Text = "b\n";
        editor.SaveCommand.Execute(null);
        await Pump(() => editor.Status.Length > 0, TimeSpan.FromSeconds(3));
        Assert.Contains("另存为", editor.Status);
        h.Interaction.PromptAnswer = "plain-副本.txt";
        editor.SaveAsCommand.Execute(null);
        await Pump(() => editor.Status.StartsWith("已另存为"), TimeSpan.FromSeconds(20));
        Assert.StartsWith("已另存为", editor.Status);
        await Pump(() => Child(w.FileRoots, "plain-副本.txt") != null, TimeSpan.FromSeconds(10));
        Assert.Equal("b\n", Encoding.UTF8.GetString(await Download(h, w, Child(w.FileRoots, "plain-副本.txt")!)));
        Assert.Equal("a\n", Encoding.UTF8.GetString(await Download(h, w, Child(w.FileRoots, "plain.txt")!)));
    }

    [AvaloniaFact]
    public async Task EditorRefusesBinaryAndFtpEditorUsesSaveAs()
    {
        using var fixture = LoopbackFixture.Ftp();
        using var h = new StudioHarness();
        var w = await Open(h, fixture.Port, "ftp");
        var editor = (await w.OpenEditorAsync(Child(w.FileRoots, "edit.txt")!))!;
        Assert.True(w.ShowTerminal); // the centre column opens for the editor
        Assert.Contains("合成FTP文件", editor.Document.Text);
        editor.Document.Insert(0, "前缀\n");
        editor.SaveCommand.Execute(null);
        await Pump(() => editor.Status.Length > 0, TimeSpan.FromSeconds(3));
        Assert.Contains("FTP", editor.Status);

        var binary = await Upload(h, w, "blob.bin", [0, 1, 2, 0xFF, 0xFE]);
        w.SelectedNode = binary;
        w.DismissFileNoticeCommand.Execute(null);
        w.OpenEditorCommand.Execute(null);
        await Pump(() => w.FileNotice.Length > 0, TimeSpan.FromSeconds(10));
        Assert.Contains("UTF-8", w.FileNotice);
        Assert.Single(w.Editors);
    }

    [AvaloniaFact]
    public async Task PermissionsCompressAndDesktopDownload()
    {
        using var fixture = LoopbackFixture.Ssh(Posix);
        using var h = new StudioHarness();
        var w = await Open(h, fixture.Port);
        var node = await Upload(h, w, "mode.txt", Encoding.UTF8.GetBytes("x"));
        w.SelectedNode = node;
        h.Interaction.PermissionAnswer = new PermissionRequest(Convert.ToUInt32("640", 8), false, null);
        int before = w.Transfers!.Visible.Count;
        w.PermissionsCommand.Execute(null);
        await Drain(w, before + 1);
        Assert.NotNull(h.Interaction.PermissionAsked);
        var after = await w.Actions!.InspectPermissionsAsync("/mode.txt", CancellationToken.None);
        Assert.Equal("640", FileActions.Octal(after.Mode));

        // Compress runs tar on an exec channel with noclobber; the fixture only records the command.
        w.SelectedNode = Child(w.FileRoots, "a");
        before = w.Transfers.Visible.Count;
        w.CompressCommand.Execute(null);
        await Drain(w, before + 1);
        var command = fixture.Stats().GetProperty("commands").EnumerateArray().Select(c => c.GetString()!).Last(c => c.Contains("tar"));
        Assert.Equal("cd -- '/' && set -C && exec 3> 'a.tar.gz' && { tar -czf - -- 'a' >&3 || { s=$?; rm -f -- 'a.tar.gz'; exit $s; }; }", command);

        // Desktop download never replaces an existing desktop file.
        string desktop = Path.Combine(h.Data, "桌面");
        Directory.CreateDirectory(desktop);
        File.WriteAllText(Path.Combine(desktop, "mode.txt"), "existing");
        w.Actions.DesktopDirectory = desktop;
        w.SelectedNode = Child(w.FileRoots, "mode.txt");
        before = w.Transfers.Visible.Count;
        w.DownloadToDesktopCommand.Execute(null);
        await Drain(w, before + 1);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(desktop, "mode.txt")));
        Assert.Equal("x", File.ReadAllText(Path.Combine(desktop, "mode (1).txt")));
    }

    [AvaloniaFact]
    public async Task SudoPermissionsUseSeparateExecAndAskPasswordOnlyAtRandomPrompt()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The helper runs on the server, which is POSIX; on Windows the fixture cannot run it.");
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1", ["FIXTURE_PERMISSION_EXEC"] = "1" });
        using var h = new StudioHarness(new ScriptedInteraction(["synthetic-sudo-only"]));
        var w = await Open(h, fixture.Port, disablePwd: true);
        var node = await Upload(h, w, "root-owned.txt", Encoding.UTF8.GetBytes("sudo"));
        w.SelectedNode = node;
        h.Interaction.PermissionAnswer = new PermissionRequest(Convert.ToUInt32("600", 8), false, null, UseSudo: true);
        int before = w.Transfers!.Visible.Count;
        w.PermissionsCommand.Execute(null);
        await Drain(w, before + 1);
        Assert.True(h.Interaction.SudoOffered);
        Assert.Contains(h.Interaction.Confirms, c => c.Contains("同一文件系统"));
        var item = w.Transfers.Visible.Last();
        Assert.True(item.IsDone, item.StateText);
        Assert.Equal("600", FileActions.Octal((await w.Actions!.InspectPermissionsAsync("/root-owned.txt", CancellationToken.None)).Mode));
        Assert.Contains(h.Interaction.Challenges, c => c.Temporary && c.Questions.Single().Text.Contains("sudo"));
        var stats = fixture.Stats();
        Assert.Equal(1, stats.GetProperty("sudoPrompts").GetInt32());
        // Nothing is typed into the interactive terminal for this (pwd follow disabled here).
        string secretHex = Convert.ToHexString(Encoding.UTF8.GetBytes("synthetic-sudo-only")).ToLowerInvariant();
        Assert.DoesNotContain(stats.GetProperty("input").EnumerateArray(), i => i.GetString()!.Contains(secretHex));
        Assert.Equal(1, stats.GetProperty("auth").GetInt32());
        Assert.True(w.IsConnected);
    }

    [Fact]
    public async Task Osc7SnifferFindsDirectoriesAcrossSplitReads()
    {
        string output = "prompt\u001b]0;title\u0007\u001b]7;file://host/home/%E4%B8%AD%E6%96%87%20dir\u0007$ \u001b]7;file://h/tmp\u001b\\done";
        var bytes = Encoding.UTF8.GetBytes(output);
        var found = new List<string>();
        var sniffer = new Osc7Sniffer(new ChunkPty(bytes));
        sniffer.DirectoryChanged += found.Add;
        var buffer = new byte[3];
        var copy = new MemoryStream();
        int n;
        while ((n = await sniffer.ReaderStream.ReadAsync(buffer)) > 0) copy.Write(buffer, 0, n);
        Assert.Equal(bytes, copy.ToArray()); // output passes through unchanged
        Assert.Equal(["/home/中文 dir", "/tmp"], found);
        Assert.Null(Osc7Sniffer.PathFrom("http://x/y"));
        Assert.Null(Osc7Sniffer.PathFrom("file://host"));
    }

    sealed class ChunkPty(byte[] output) : Porta.Pty.IPtyConnection
    {
        public Stream ReaderStream { get; } = new MemoryStream(output);
        public Stream WriterStream { get; } = new MemoryStream();
        public bool SupportsCancellableRead => true;
        public int Pid => 0;
        public int ExitCode => 0;
        public event EventHandler<Porta.Pty.PtyExitedEventArgs>? ProcessExited { add { } remove { } }
        public void Resize(int cols, int rows) { }
        public bool WaitForExit(int milliseconds) => true;
        public void Kill() { }
        public void Dispose() { }
    }

    static string Typed(LoopbackFixture fixture) =>
        Encoding.UTF8.GetString(fixture.Stats().GetProperty("input").EnumerateArray().SelectMany(x => Convert.FromHexString(x.GetString()!)).ToArray());

    [AvaloniaFact]
    public async Task PwdFollowHookOnlyForSavedConnectionsAndTreeFollows()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var w = await Open(h, fixture.Port);
        await Pump(() => Typed(fixture).Contains("__ft_osc7"), TimeSpan.FromSeconds(5));
        Assert.Contains("__ft_osc7", Typed(fixture));
        w.OnTerminalDirectory(w.ActiveTerminal!, "/b");
        await Pump(() => w.SelectedNode?.Path == "/b", TimeSpan.FromSeconds(5));
        Assert.Equal("/b", w.CurrentDirectory);

        using var disabled = LoopbackFixture.Ssh();
        await Open(h, disabled.Port, disablePwd: true);
        await Pump(() => false, TimeSpan.FromMilliseconds(800));
        Assert.DoesNotContain("__ft_osc7", Typed(disabled));

        using var launch = LoopbackFixture.Ssh();
        await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{launch.Port}"]));
        await Pump(() => false, TimeSpan.FromMilliseconds(800));
        Assert.Equal("", Typed(launch)); // a controller launch writes nothing into the terminal
    }
}
