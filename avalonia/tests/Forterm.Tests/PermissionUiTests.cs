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
public sealed class PermissionUiTests
{
    static async Task Until(Func<bool> check) { using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20)); while (!check()) { ct.Token.ThrowIfCancellationRequested(); await Task.Delay(10, ct.Token); Dispatcher.UIThread.RunJobs(); } }
    static void Click(Control root, string label) => root.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static TextBox Field(Control root, string label) => root.GetVisualDescendants().OfType<TextBox>().Single(b => AutomationProperties.GetName(b) == label);
    [AvaloniaFact] public async Task PermissionPreviewCancelAndApplyShowCountUnitsAndRequireFreshParameters()
    {
        using var fixture = new TransportTests.Fixture(); fixture.SeedTree(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token); using var files = await root.OpenFilesAsync(ct.Token); using var queue = new TransferQueue();
        var owner = new Window { Width = 1000, Height = 700 }; owner.Show(); var window = new PermissionWindow(files, "合成SFTP", "/a", queue); var showing = window.ShowDialog(owner);
        try
        {
            await Until(() => Field(window, "普通权限").Text?.Length == 3); Field(window, "普通权限").Text = "700"; window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content?.ToString()?.StartsWith("包含子目录") == true).IsChecked = true;
            Click(window, "预览计划"); await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("将修改") == true));
            Assert.Equal(493u, (await files.Client.LStatRawAsync("/a", ct.Token)).PermissionBits); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "permission-preview.png"));
            Click(window, "应用已确认计划"); await Until(() => window.OwnedWindows.Count > 0); Click(window.OwnedWindows.Single(), "取消"); await Task.Delay(30); Assert.Empty(queue.Snapshot());
            Click(window, "应用已确认计划"); await Until(() => window.OwnedWindows.Count > 0); Click(window.OwnedWindows.Single(), "确认"); await Until(() => queue.Snapshot().Any(t => t.State is TransferState.Completed or TransferState.Failed));
            Assert.Equal(TransferState.Completed, queue.Snapshot().Single().State); Assert.Equal("项", queue.Snapshot().Single().Unit); Assert.Equal(448u, (await files.Client.LStatRawAsync("/a", ct.Token)).PermissionBits);
            Field(window, "普通权限").Text = "755"; Assert.False(window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "应用已确认计划")).IsEnabled);
            window.Width = 540; window.Height = 380; window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "permission-small.png"));
        }
        finally { window.Close(); owner.Close(); await showing; }
    }
    [AvaloniaFact] public async Task SudoUiRequiresIdentityConfirmationAndOnlyPromptsAfterExecMarker()
    {
        using var fixture = new TransportTests.Fixture("FIXTURE_PERMISSION_EXEC"); fixture.SeedText(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token); using var files = await root.OpenFilesAsync(ct.Token); using var queue = new TransferQueue();
        var owner = new Window { Width = 1000, Height = 700 }; owner.Show(); var window = new PermissionWindow(files, "合成sudo通道", "/edit.txt", queue); var showing = window.ShowDialog(owner);
        try
        {
            await Until(() => Field(window, "普通权限").Text?.Length == 3); Field(window, "普通权限").Text = "600";
            window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content?.ToString()?.StartsWith("使用同一SSH") == true).IsChecked = true;
            Click(window, "预览计划"); await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("将修改") == true));
            var apply = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "应用已确认计划")); Assert.False(apply.IsEnabled); Assert.Equal(0, fixture.Stats().GetProperty("sudoPasswordBytes").GetInt32());
            window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content?.ToString()?.StartsWith("我确认此SSH") == true).IsChecked = true; Assert.True(apply.IsEnabled);
            Click(window, "应用已确认计划"); await Until(() => window.OwnedWindows.Count > 0); Click(window.OwnedWindows.Single(), "确认");
            await Until(() => window.OwnedWindows.Count > 0); var prompt = window.OwnedWindows.Single(); prompt.GetVisualDescendants().OfType<TextBox>().Single().Text = "synthetic-sudo-only"; Click(prompt, "继续");
            await Until(() => queue.Snapshot().Any(t => t.State is TransferState.Completed or TransferState.Failed)); Assert.Equal(TransferState.Completed, queue.Snapshot().Single().State);
            Assert.Equal(1, fixture.Stats().GetProperty("sudoPrompts").GetInt32()); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32()); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "sudo-permission-result.png"));
        }
        finally { window.Close(); owner.Close(); await showing; }
    }
}
