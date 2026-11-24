using System.Text;
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

public sealed class RealPathRecoveryTests
{
    [Theory] [InlineData("FIXTURE_DENY_REALPATH")] [InlineData("FIXTURE_UNSUPPORTED_REALPATH")]
    public async Task UnknownCwdNeedsExplicitAbsoluteAuthorizationAndProbeWaitsForVerifiedDirectory(string mode)
    {
        using var fixture = new TransportTests.Fixture(mode); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token, options: new() { KeepAliveSeconds = 5 });
        using var files = await root.OpenFilesAsync(ct.Token); using var shell = await root.OpenShellAsync(80, 24, ct.Token); var safe = new SafeTransfers();
        Assert.False(files.HasWorkingDirectory); Assert.Equal("", files.Client.WorkingDirectory);
        await Assert.ThrowsAsync<IOException>(() => safe.CaptureDirectoryAsync(files, ".", ct.Token)); await Assert.ThrowsAsync<IOException>(() => safe.CaptureDirectoryAsync(files, "/a", ct.Token));
        await Task.Delay(5200, ct.Token); Assert.True(files.IsAvailable); Assert.Equal(0, fixture.Stats().GetProperty("lstats").GetInt32());
        files.AuthorizeAbsoluteDirectory("/a/./"); var directory = await safe.CaptureDirectoryAsync(files, "/a", ct.Token); Assert.Equal("/a", directory.Path);
        await files.UploadNewAsync(new MemoryStream(Encoding.UTF8.GetBytes("explicit absolute only")), "/a/recovery.txt", ct.Token);
        using var copy = new MemoryStream(); await files.Client.DownloadFileAsync("/a/recovery.txt", copy, ct.Token); Assert.Equal("explicit absolute only", Encoding.UTF8.GetString(copy.ToArray()));
        await Assert.ThrowsAsync<IOException>(() => safe.CaptureDirectoryAsync(files, "/b", ct.Token)); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32()); Assert.True(root.IsConnected);
    }
    sealed class Pickers(string path) : IFilePickers
    {
        public int Calls;
        public Task<PickedLocalFile?> UploadAsync(Window owner) { Calls++; return Task.FromResult<PickedLocalFile?>(new(path, "recovered.txt")); }
        public Task<string?> DownloadAsync(Window owner, string name) => Task.FromResult<string?>(null);
    }
    static async Task Until(Func<bool> check) { using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15)); while (!check()) { ct.Token.ThrowIfCancellationRequested(); await Task.Delay(10, ct.Token); Dispatcher.UIThread.RunJobs(); } }
    static void Click(Control root, string label) => root.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    [AvaloniaFact] public async Task UiShowsRealpathDenialAndCancelDoesNotSilentlyReadOrUpload()
    {
        using var fixture = new TransportTests.Fixture("FIXTURE_DENY_REALPATH"); string input = Path.Combine(Path.GetTempPath(), "forterm-realpath-" + Guid.NewGuid().ToString("N")); await File.WriteAllTextAsync(input, "synthetic recovery upload");
        var picker = new Pickers(input); using var queue = new TransferQueue(); var owner = new Window { Width = 900, Height = 650 }; using var pane = new SessionPane(owner, new("合成受限SFTP", "test", "127.0.0.1", fixture.Port, "synthetic"), true, queue, picker); owner.Content = pane; owner.Show();
        try
        {
            var connecting = pane.ConnectAsync("synthetic-only"); await Until(() => owner.OwnedWindows.Count > 0); Click(owner.OwnedWindows.Single(), "确认"); await connecting; owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var view = pane.GetVisualDescendants().OfType<RemoteFilesView>().Single(); Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("REALPATH") == true);
            var path = view.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "远程路径"); path.Text = "/a"; Click(view, "上传"); await Task.Delay(30); Assert.Equal(0, picker.Calls);
            Click(view, "刷新"); await Until(() => owner.OwnedWindows.Any(w => w.Title == "确认直接读取绝对目录")); Click(owner.OwnedWindows.Single(), "取消"); await Task.Delay(30);
            Assert.Empty(fixture.Stats().GetProperty("lists").EnumerateArray()); Assert.Empty(queue.Snapshot());
            Click(view, "刷新"); await Until(() => owner.OwnedWindows.Count > 0); Click(owner.OwnedWindows.Single(), "确认"); await Until(() => view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("已按明确绝对目录") == true));
            Click(view, "上传"); await Until(() => queue.Snapshot().Any(t => t.State is TransferState.Completed or TransferState.Failed)); Assert.Equal(TransferState.Completed, queue.Snapshot().Single().State);
            await Until(() => view.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "远程文件").Items.Cast<object>().Any(i => i.ToString()!.Contains("recovered.txt"))); Assert.True(fixture.Files().TryGetProperty("a/recovered.txt", out _)); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32()); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs(); owner.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "realpath-explicit-recovery.png"));
        }
        finally { pane.Dispose(); owner.Close(); File.Delete(input); }
    }
}
