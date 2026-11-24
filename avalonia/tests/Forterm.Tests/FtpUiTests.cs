using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Forterm.Core;
using Forterm.Desktop;
using Iciclecreek.Terminal;
using Xunit;
namespace Forterm.Tests;
public sealed class FtpUiTests
{
    sealed class Pickers(string upload, string download) : IFilePickers
    {
        public Task<PickedLocalFile?> UploadAsync(Window owner) => Task.FromResult<PickedLocalFile?>(new(upload, "传输 空格%20.bin"));
        public Task<string?> DownloadAsync(Window owner, string name) => Task.FromResult<string?>(download);
    }
    static async Task Until(Func<bool> check) { using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20)); while (!check()) { ct.Token.ThrowIfCancellationRequested(); await Task.Delay(10, ct.Token); Dispatcher.UIThread.RunJobs(); } }
    static void Click(Control root, string text) => root.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static TextBox Field(Control root, string name) => root.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == name);
    [AvaloniaFact] public async Task FtpEntryAssociatesWithSshAndTransfersEditsMonitorCoexistWithoutReauthentication()
    {
        using var ssh = new TransportTests.Fixture(); using var ftp = new FtpTests.Fixture();
        string folder = Path.Combine(Path.GetTempPath(), "forterm-ftp-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); string input = Path.Combine(folder, "input"), output = Path.Combine(folder, "output"); await File.WriteAllTextAsync(input, "synthetic FTP GUI upload");
        var window = new MainWindow(folder, new CredentialStoreTests.MemoryVault(), new Pickers(input, output)); window.Show();
        try
        {
            var connecting = window.AcceptLaunch(LaunchPlan.Parse(new[] { "-newtab", "合成 SSH", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{ssh.Port}" }));
            await Until(() => window.OwnedWindows.Count > 0); Click(window.OwnedWindows.Single(), "确认"); await connecting;
            await window.AcceptLaunch(LaunchPlan.Parse(new[] { "-newtab", "合成 FTP", "-url", $"ftp://synthetic:synthetic-only@127.0.0.1:{ftp.Port}" }));
            var owner = window.Workspaces[0]; var source = window.Workspaces[1]; Assert.True(source.HasFileCapability); window.AttachFiles(owner, source); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(owner.WorkspaceId, window.VisibleWorkspaceIds.Single()); var view = owner.GetVisualDescendants().OfType<FtpFilesView>().Single();
            Field(view, "远程路径").Text = "/b"; Click(view, "上传"); await Until(() => ftp.Files().TryGetProperty("b/传输 空格%20.bin", out _));
            Field(view, "远程路径").Text = "/"; await view.RefreshAsync(CancellationToken.None); window.UpdateLayout();
            var list = view.GetVisualDescendants().OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "远程文件"); list.SelectedItem = list.Items.Cast<object>().Single(x => x.ToString()!.Contains("edit.txt"));
            Click(view, "下载"); await Until(() => File.Exists(output)); Assert.Equal("合成FTP文件\r\nline\n", await File.ReadAllTextAsync(output));
            Click(view, "编辑"); await Until(() => owner.GetVisualDescendants().OfType<TextEditor>().Any()); var editor = owner.GetVisualDescendants().OfType<TextEditor>().Single(); editor.Document.Insert(editor.Document.TextLength, "draft");
            var panel = owner.GetVisualDescendants().OfType<RemoteEditorPanel>().Single(); Click(panel, "保存"); Assert.Empty(window.OwnedWindows); Assert.True(panel.HasUnsaved);
            Click(panel, "另存新名"); await Until(() => window.OwnedWindows.Count > 0); var name = window.OwnedWindows.Single(); name.GetVisualDescendants().OfType<TextBox>().Single().Text = "保存 %20.txt"; Click(name, "新建草稿");
            await Until(() => window.OwnedWindows.Any(w => w.Title == "确认保存远程文件")); Click(window.OwnedWindows.Single(w => w.Title == "确认保存远程文件"), "确认");
            await Until(() => !panel.HasUnsaved); Assert.True(ftp.Files().TryGetProperty("保存 %20.txt", out _)); Assert.False(ftp.Files().TryGetProperty("保存  .txt", out _));
            owner.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "资源监控")).IsChecked = true; var monitor = owner.GetVisualDescendants().OfType<MonitorPanel>().Single(); await monitor.RefreshAsync(); await monitor.RefreshAsync();
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Assert.True(owner.Terminal.IsLive); Assert.Equal(1, ssh.Stats().GetProperty("shells").GetInt32()); var terminal = owner.Terminal.GetVisualDescendants().OfType<TerminalView>().Single(); terminal.Focus(); Assert.True(terminal.IsFocused); window.KeyTextInput("ssh-still-alive"); await Until(() => ssh.Stats().GetProperty("input").GetArrayLength() > 0);
            Assert.Equal(1, ssh.Stats().GetProperty("auth").GetInt32()); Assert.Equal(1, ftp.Stats().GetProperty("auth").GetInt32()); Assert.Equal(1, ftp.Stats().GetProperty("connections").GetInt32());
            window.Width = 1200; window.Height = 800; window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "ssh-ftp-four-areas.png"));
            editor.Document.Insert(editor.Document.TextLength, "-retained"); Assert.True(await window.CloseWorkspaceAsync(source)); await Until(() => editor.IsReadOnly); Assert.Contains("-retained", editor.Text); Assert.True(owner.HasFileCapability);
            // Explicitly acknowledge only the owned synthetic draft on normal main-window close.
            window.Close(); await Until(() => window.OwnedWindows.Count > 0); Click(window.OwnedWindows.Single(), "确认"); await Until(() => !window.IsVisible);
        }
        finally { if (window.IsVisible) { foreach (var pane in window.Workspaces) pane.Dispose(); window.Close(); } Directory.Delete(folder, true); }
    }
    [AvaloniaFact] public async Task EditedEndpointTestCannotReadOrSendPreviousSavedPassword()
    {
        using var server = new TransportTests.Fixture();
        var original = new ConnectionProfile("saved", "group", "original.invalid", 22, "synthetic") { HasPassword = true };
        var vault = new CredentialStoreTests.MemoryVault(); vault.Values[ProfileRepository.PasswordKey(original)] = System.Text.Encoding.UTF8.GetBytes("synthetic-only");
        var window = new ConnectionWindow(original, new ProfileRepository(new ProfileStore(null), vault)); window.Show();
        Field(window, "主机地址").Text = "127.0.0.1"; Field(window, "端口").Text = server.Port.ToString(); Click(window, "✓  测试连接");
        await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("旧密码不会自动转用") == true));
        Assert.Equal(0, vault.Reads + vault.Writes + vault.Deletes); Assert.Equal(0, server.Stats().GetProperty("connections").GetInt32()); window.Close();
    }
    [AvaloniaFact] public async Task FtpConnectionTestUsesOnlyFtpAuthenticationAndPreservesInputs()
    {
        using var fixture = new FtpTests.Fixture(); var window = new ConnectionWindow(); window.Show();
        window.GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "连接协议").SelectedIndex = 2;
        Field(window, "主机地址").Text = "127.0.0.1"; Field(window, "端口").Text = fixture.Port.ToString(); Field(window, "登录用户").Text = "synthetic"; Field(window, "登录密码").Text = "synthetic-only";
        Click(window, "✓  测试连接"); await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("FTP认证成功") == true));
        Assert.Equal("synthetic-only", Field(window, "登录密码").Text); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
        Assert.DoesNotContain(fixture.Stats().GetProperty("commands").EnumerateArray(), c => c.GetProperty("command").GetString() is "LIST" or "STOR" or "RETR");
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "ftp-connection-test.png")); window.Close();
    }
}
