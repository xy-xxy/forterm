using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Desktop;
using Xunit;
namespace Forterm.Tests;
public sealed class FileActionUiTests
{
    static async Task Until(Func<bool> check) { using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15)); while (!check()) { ct.Token.ThrowIfCancellationRequested(); await Task.Delay(10, ct.Token); Dispatcher.UIThread.RunJobs(); } }
    static void Click(Control root, string label) => root.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static ListBox Files(Control root) => root.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "远程文件");
    static void FocusFiles(ListBox list) { var container = list.ContainerFromIndex(list.SelectedIndex >= 0 ? list.SelectedIndex : 0); if (container != null) container.Focus(); else list.Focus(); Assert.True(list.IsKeyboardFocusWithin, "file list did not receive keyboard focus"); }
    static void Chord(Window window, Key key, PhysicalKey physical) { window.KeyPress(key, RawInputModifiers.Control, physical, null); window.KeyRelease(key, RawInputModifiers.Control, physical, null); }
    [AvaloniaFact] public async Task DirectoryMenuCreatesRenamesAndRequiresDeleteConfirmation()
    {
        using var fixture = new TransportTests.Fixture(posix: true); using var queue = new TransferQueue(); var owner = new Window { Width = 900, Height = 650 };
        using var pane = new SessionPane(owner, new("合成目录操作", "test", "127.0.0.1", fixture.Port, "synthetic"), true, queue); owner.Content = pane; owner.Show();
        try
        {
            var connecting = pane.ConnectAsync("synthetic-only"); await Until(() => owner.OwnedWindows.Count > 0); Click(owner.OwnedWindows.Single(), "确认"); await connecting; owner.UpdateLayout();
            var view = pane.GetVisualDescendants().OfType<RemoteFilesView>().Single(); var files = Files(view);
            void Menu(string text) => files.ContextMenu!.ItemsSource!.Cast<MenuItem>().Single(m => Equals(m.Header, text)).Command!.Execute(null);
            Menu("新建目录"); await Until(() => owner.OwnedWindows.Count > 0); var prompt = owner.OwnedWindows.Single(); prompt.GetVisualDescendants().OfType<TextBox>().Single().Text = "new-dir"; Click(prompt, "确认");
            await Until(() => queue.Snapshot().Any(t => t.State is TransferState.Completed or TransferState.Failed)); Assert.Equal(TransferState.Completed, queue.Snapshot().Last().State);
            await view.RefreshAsync(CancellationToken.None); owner.UpdateLayout(); files.SelectedItem = files.Items.Cast<object>().Single(x => x.ToString()!.Contains("new-dir")); Menu("重命名节点");
            await Until(() => owner.OwnedWindows.Count > 0); prompt = owner.OwnedWindows.Single(); prompt.GetVisualDescendants().OfType<TextBox>().Single().Text = "renamed-dir"; Click(prompt, "确认");
            await Until(() => queue.Snapshot().Count >= 2 && queue.Snapshot().Last().State is TransferState.Completed or TransferState.Failed); Assert.Equal(TransferState.Completed, queue.Snapshot().Last().State);
            await view.RefreshAsync(CancellationToken.None); owner.UpdateLayout(); files.SelectedItem = files.Items.Cast<object>().Single(x => x.ToString()!.Contains("renamed-dir")); Menu("删除节点");
            await Until(() => owner.OwnedWindows.Count > 0); Click(owner.OwnedWindows.Single(), "取消"); await Task.Delay(30); Assert.Equal(2, queue.Snapshot().Count);
            Menu("删除节点"); await Until(() => owner.OwnedWindows.Count > 0); Click(owner.OwnedWindows.Single(), "确认");
            await Until(() => queue.Snapshot().Count >= 3 && queue.Snapshot().Last().State is TransferState.Completed or TransferState.Failed); Assert.Equal(TransferState.Completed, queue.Snapshot().Last().State); Assert.False(view.Source.Client.Exists("/renamed-dir"));
            Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32()); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs(); owner.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "directory-actions.png"));
        }
        finally { pane.Dispose(); owner.Close(); }
    }
    [AvaloniaFact] public async Task SftpPasteOverwriteRequiresConfirmAndKeepsChangedVersionThenPreservesMode()
    {
        using var fixture = new TransportTests.Fixture(posix: true); fixture.SeedText(); using var queue = new TransferQueue(); var owner = new Window { Width = 950, Height = 650 };
        using var pane = new SessionPane(owner, new("合成SFTP覆盖", "test", "127.0.0.1", fixture.Port, "synthetic"), true, queue); owner.Content = pane; owner.Show();
        try
        {
            var connecting = pane.ConnectAsync("synthetic-only"); await Until(() => owner.OwnedWindows.Count > 0); Click(owner.OwnedWindows.Single(), "确认"); await connecting; owner.UpdateLayout();
            var view = pane.GetVisualDescendants().OfType<RemoteFilesView>().Single(); var list = Files(view);
            await view.Source.UploadNewAsync(new MemoryStream("old"u8.ToArray()), "/b/target.txt", CancellationToken.None); view.Source.Client.ChangePermissions("/b/target.txt", 640);
            list.SelectedItem = list.Items.Cast<object>().Single(i => i.ToString()!.Contains("edit.txt")); FocusFiles(list); Chord(owner, Key.C, PhysicalKey.C); await Until(() => view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("已复制：") == true));
            view.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "远程路径").Text = "/b"; await view.RefreshAsync(CancellationToken.None); owner.UpdateLayout();
            async Task<Window> Paste()
            {
                FocusFiles(Files(view)); Chord(owner, Key.V, PhysicalKey.V); await Until(() => owner.OwnedWindows.Count > 0);
                var name = owner.OwnedWindows.Single(); name.GetVisualDescendants().OfType<TextBox>().Single().Text = "target.txt"; Click(name, "确认");
                await Until(() => owner.OwnedWindows.Any(w => w.Title == "确认覆盖已核对SFTP目标？")); return owner.OwnedWindows.Single(w => w.Title == "确认覆盖已核对SFTP目标？");
            }
            var confirm = await Paste(); confirm.UpdateLayout(); Dispatcher.UIThread.RunJobs(); confirm.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "sftp-copy-overwrite-confirm.png")); Click(confirm, "取消"); await Task.Delay(30); Assert.Empty(queue.Snapshot());
            confirm = await Paste(); using (var file = view.Source.Client.OpenWrite("/b/target.txt")) { file.SetLength(0); file.Write("changed"u8); file.Flush(); } Click(confirm, "确认");
            await Until(() => queue.Snapshot().Any(t => t.State == TransferState.Failed)); using (var bytes = new MemoryStream()) { await view.Source.Client.DownloadFileAsync("/b/target.txt", bytes, CancellationToken.None); Assert.StartsWith("changed", System.Text.Encoding.UTF8.GetString(bytes.ToArray())); }
            confirm = await Paste(); Click(confirm, "确认"); await Until(() => queue.Snapshot().Count >= 2 && queue.Snapshot().Last().State is TransferState.Completed or TransferState.Failed); Assert.Equal(TransferState.Completed, queue.Snapshot().Last().State);
            Assert.Equal(416u, (await view.Source.Client.LStatRawAsync("/b/target.txt", CancellationToken.None)).PermissionBits); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
        }
        finally { pane.Dispose(); owner.Close(); }
    }
    [AvaloniaFact] public async Task PrivateFileClipboardCopiesToOriginalDestinationAfterViewSwitchThenMovesSafely()
    {
        using var ssh = new TransportTests.Fixture(posix: true); ssh.SeedText(); using var ftp = new FtpTests.Fixture();
        string directory = Path.Combine(Path.GetTempPath(), "forterm-file-ui-" + Guid.NewGuid().ToString("N")); var window = new MainWindow(directory, new CredentialStoreTests.MemoryVault()); window.Show();
        try
        {
            var connecting = window.AcceptLaunch(LaunchPlan.Parse(new[] { "-newtab", "合成SSH", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{ssh.Port}" })); await Until(() => window.OwnedWindows.Count > 0); Click(window.OwnedWindows.Single(), "确认"); await connecting;
            await window.AcceptLaunch(LaunchPlan.Parse(new[] { "-newtab", "合成FTP", "-url", $"ftp://synthetic:synthetic-only@127.0.0.1:{ftp.Port}" }));
            var pane = window.Workspaces[0]; var other = window.Workspaces[1]; window.AttachFiles(pane, other); pane.ShowFileSource(pane.WorkspaceId); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var source = pane.GetVisualDescendants().OfType<RemoteFilesView>().Single(); var list = Files(source); list.SelectedItem = list.Items.Cast<object>().Single(i => i.ToString()!.Contains("edit.txt")); FocusFiles(list); Chord(window, Key.C, PhysicalKey.C);
            await Until(() => source.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("已复制：") == true));
            pane.ShowFileSource(other.WorkspaceId); window.UpdateLayout(); var target = pane.GetVisualDescendants().OfType<FtpFilesView>().Single(); target.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "远程路径").Text = "/b"; await target.RefreshAsync(CancellationToken.None); window.UpdateLayout();
            FocusFiles(Files(target)); Chord(window, Key.V, PhysicalKey.V); await Until(() => window.OwnedWindows.Count > 0); var prompt = window.OwnedWindows.Single(); prompt.GetVisualDescendants().OfType<TextBox>().Single().Text = "copied.txt";
            pane.ShowFileSource(pane.WorkspaceId); Click(prompt, "确认"); await Until(() => ftp.Files().TryGetProperty("b/copied.txt", out _)); Assert.True(ssh.Files().TryGetProperty("edit.txt", out _)); Assert.False(ssh.Files().TryGetProperty("b/copied.txt", out _));
            window.UpdateLayout(); list = Files(pane.GetVisualDescendants().OfType<RemoteFilesView>().Single()); list.SelectedItem = list.Items.Cast<object>().Single(i => i.ToString()!.Contains("edit.txt")); FocusFiles(list); Chord(window, Key.X, PhysicalKey.X);
            await Until(() => source.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("已标记剪切：") == true)); pane.ShowFileSource(other.WorkspaceId); window.UpdateLayout(); FocusFiles(Files(target)); Chord(window, Key.V, PhysicalKey.V);
            await Until(() => window.OwnedWindows.Count > 0); prompt = window.OwnedWindows.Single(); prompt.GetVisualDescendants().OfType<TextBox>().Single().Text = "moved.txt"; Click(prompt, "确认");
            await Until(() => ftp.Files().TryGetProperty("b/moved.txt", out _) && !ssh.Files().TryGetProperty("edit.txt", out _)); Assert.Equal(1, ssh.Stats().GetProperty("auth").GetInt32()); Assert.Equal(1, ftp.Stats().GetProperty("auth").GetInt32());
            await Until(() => Files(target).Items.Cast<object>().Any(i => i.ToString()!.Contains("moved.txt"))); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "cross-source-copy-move.png"));
        }
        finally { window.Close(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
