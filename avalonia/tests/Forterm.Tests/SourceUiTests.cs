using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Desktop;
using Xunit;
namespace Forterm.Tests;

public sealed class SourceUiTests
{
    sealed class Pickers : IFilePickers
    {
        public TaskCompletionSource<PickedLocalFile?> Pending = new(TaskCreationOptions.RunContinuationsAsynchronously); public bool Called;
        public Task<PickedLocalFile?> UploadAsync(Window owner) { Called = true; return Pending.Task; }
        public Task<string?> DownloadAsync(Window owner, string name) => Task.FromResult<string?>(null);
    }
    static async Task Until(Func<bool> check) { using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15)); while (!check()) { ct.Token.ThrowIfCancellationRequested(); await Task.Delay(10, ct.Token); Dispatcher.UIThread.RunJobs(); } }
    static void Click(Control root, string text) => root.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static async Task Open(MainWindow window, TransportTests.Fixture fixture, string protocol, string name)
    {
        var connecting = window.AcceptLaunch(LaunchPlan.Parse(new[] { "-newtab", name, "-url", $"{protocol}://synthetic:synthetic-only@127.0.0.1:{fixture.Port}" }));
        await Until(() => window.OwnedWindows.Count > 0); Click(window.OwnedWindows.Single(), "确认"); await connecting; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
    }
    [AvaloniaFact] public async Task ExplicitAssociationHidesOnlyFileTabAndPickerKeepsOriginalSourceWhileSwitching()
    {
        using var a = new TransportTests.Fixture(); using var b = new TransportTests.Fixture(); var picker = new Pickers();
        string directory = Path.Combine(Path.GetTempPath(), "forterm-sources-" + Guid.NewGuid().ToString("N")); string input = Path.Combine(Path.GetTempPath(), "forterm-source-upload-" + Guid.NewGuid().ToString("N")); await File.WriteAllTextAsync(input, "synthetic source-bound upload");
        var window = new MainWindow(directory, new CredentialStoreTests.MemoryVault(), picker); window.Show();
        try
        {
            await Open(window, a, "ssh", "合成终端 A"); await Open(window, b, "sftp", "合成文件 B");
            var owner = window.Workspaces[0]; var source = window.Workspaces[1]; var terminal = owner.Terminal.Terminal;
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single(t => AutomationProperties.GetName(t) == "会话标签"); tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(t => ReferenceEquals(t.Content, owner)); window.UpdateLayout();
            owner.GetVisualDescendants().OfType<Button>().Single(t => AutomationProperties.GetName(t) == "关联文件来源").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => window.OwnedWindows.Count > 0); var dialog = window.OwnedWindows.Single(); dialog.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex = 1; Click(dialog, "关联"); await Until(() => window.VisibleWorkspaceIds.Count == 1); window.UpdateLayout();
            Assert.Equal(owner.WorkspaceId, window.VisibleWorkspaceIds.Single()); Assert.Equal(2, window.Workspaces.Count); Assert.Same(terminal, owner.Terminal.Terminal); Assert.Equal(source.WorkspaceId, owner.ActiveFileSource); window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "associated-file-tab-hidden.png"));
            var view = owner.GetVisualDescendants().OfType<RemoteFilesView>().Single(); var path = view.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "远程路径"); path.Text = "/b"; Click(view, "上传"); await Until(() => picker.Called);
            owner.ShowFileSource(owner.WorkspaceId); window.UpdateLayout(); picker.Pending.SetResult(new(input, "bound.txt"));
            await Until(() => b.Files().TryGetProperty("b/bound.txt", out _)); Assert.False(a.Files().TryGetProperty("b/bound.txt", out _)); Assert.Equal(1, a.Stats().GetProperty("auth").GetInt32()); Assert.Equal(1, b.Stats().GetProperty("auth").GetInt32());
            window.AttachFiles(source, owner); Assert.Contains(owner.WorkspaceId, window.VisibleWorkspaceIds); // SSH never hides.
            window.DetachFiles(owner, source.WorkspaceId); Assert.Equal(2, window.VisibleWorkspaceIds.Count);
            window.AttachFiles(owner, source); Assert.Single(window.VisibleWorkspaceIds); await window.CloseWorkspaceAsync(owner); Assert.Equal(source.WorkspaceId, window.VisibleWorkspaceIds.Single()); Assert.True(source.HasFileCapability);
            window.UpdateLayout(); window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "source-restored-after-owner-close.png"));
        }
        finally { window.Close(); File.Delete(input); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [AvaloniaFact] public async Task DetachKeepsDraftAndOriginalSourceCloseMakesItReadOnly()
    {
        using var a = new TransportTests.Fixture(posix: true); using var b = new TransportTests.Fixture(posix: true); b.SeedText(); using var queue = new TransferQueue();
        var owner = new Window { Width = 1100, Height = 650 }; using var host = new SessionPane(owner, new("合成终端", "test", "127.0.0.1", a.Port, "synthetic"), transfers: queue); owner.Content = host; owner.Show();
        var connected = host.ConnectAsync("synthetic-only"); await Until(() => owner.OwnedWindows.Count > 0); Click(owner.OwnedWindows.Single(), "确认"); await connected;
        using var source = new SessionPane(owner, new("合成文件源", "test", "127.0.0.1", b.Port, "synthetic"), true, queue);
        connected = source.ConnectAsync("synthetic-only"); await Until(() => owner.OwnedWindows.Count > 0); Click(owner.OwnedWindows.Single(), "确认"); await connected;
        host.AttachFileSource(source); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var view = host.GetVisualDescendants().OfType<RemoteFilesView>().Single(); await view.RefreshAsync(CancellationToken.None); owner.UpdateLayout();
        var files = view.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "远程文件"); files.SelectedItem = files.Items.Cast<object>().Single(i => i.ToString()!.Contains("edit.txt")); Click(view, "编辑");
        await Until(() => host.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Any()); var text = host.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().Single(); text.Document.Insert(text.Document.TextLength, "未保存草稿");
        host.DetachFileSource(source.WorkspaceId); Assert.False(text.IsReadOnly); Assert.Contains("未保存草稿", text.Text); Assert.True(source.HasFileCapability);
        source.Dispose(); await Until(() => text.IsReadOnly); Assert.Contains("未保存草稿", text.Text); Assert.True(host.HasFileCapability);
        Assert.Equal(1, a.Stats().GetProperty("auth").GetInt32()); Assert.Equal(1, b.Stats().GetProperty("auth").GetInt32()); owner.Close();
    }
    [AvaloniaFact] public async Task LazyTreeLoadsOnlyExpandedBranchesAndIgnoresCollapsedLateReply()
    {
        using var fixture = new TransportTests.Fixture(listDelay: .2); fixture.SeedTree(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token); using var source = await root.OpenFilesAsync(ct.Token); using var catalog = new RemoteDirectoryCatalog(source); using var tree = new LazyRemoteTree(catalog);
        var owner = new Window { Width = 600, Height = 450, Content = tree }; tree.SetRoot("/"); owner.Show(); Assert.Empty(fixture.Stats().GetProperty("lists").EnumerateArray());
        var first = (TreeViewItem)tree.Items[0]!; first.IsExpanded = true; await Until(() => first.Items.OfType<TreeViewItem>().Any(n => Equals(n.Header, "a")));
        Assert.Equal(new[] { "/" }, fixture.Stats().GetProperty("lists").EnumerateArray().Select(x => x.GetString()));
        var a = first.Items.OfType<TreeViewItem>().Single(n => Equals(n.Header, "a")); a.IsExpanded = true; await Task.Delay(30, ct.Token); a.IsExpanded = false; await Task.Delay(350, ct.Token);
        Assert.Single(a.Items); Assert.Equal("展开载入…", ((TreeViewItem)a.Items[0]!).Header);
        a.IsExpanded = true; await Until(() => a.Items.OfType<TreeViewItem>().Any(n => Equals(n.Header, "nested"))); Assert.DoesNotContain(fixture.Stats().GetProperty("lists").EnumerateArray(), x => x.GetString() == "/a/nested");
        owner.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Assert.Contains(tree.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "nested"); owner.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "lazy-tree-expanded.png")); tree.Dispose(); using var still = await root.OpenShellAsync(80, 24, ct.Token); Assert.True(root.IsConnected); owner.Close();
    }
}
