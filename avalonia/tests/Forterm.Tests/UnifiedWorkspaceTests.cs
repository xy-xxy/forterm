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
using Xunit;
namespace Forterm.Tests;

public sealed class UnifiedWorkspaceTests
{
    static async Task Until(Func<bool> check) { using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15)); while (!check()) { ct.Token.ThrowIfCancellationRequested(); await Task.Delay(10, ct.Token); Dispatcher.UIThread.RunJobs(); } }
    static void Click(Control root, string label) => root.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    [AvaloniaFact] public async Task FourAreasRenderTogetherAndCtrlSSavesThroughRealQueue()
    {
        Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        using var fixture = new TransportTests.Fixture(posix: true); fixture.SeedText();
        using var queue = new TransferQueue(); var owner = new Window { Width = 1150, Height = 760, Title = "合成回环 · 指标来自夹具，仅软件渲染" };
        var pane = new SessionPane(owner, new("synthetic", "test", "127.0.0.1", fixture.Port, "synthetic"), transfers: queue);
        var grid = new DockPanel(); var banner = new TextBlock { Text = "合成 SSH 回环 / 指标来自测试夹具 / 软件渲染，非实际系统窗口" }; DockPanel.SetDock(banner, Dock.Top); grid.Children.Add(banner); grid.Children.Add(pane); owner.Content = grid; owner.Show();
        var connection = pane.ConnectAsync("synthetic-only"); await Until(() => owner.OwnedWindows.Count > 0); Click(owner.OwnedWindows.Single(), "确认"); await connection; owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var lists = pane.GetVisualDescendants().OfType<ListBox>().Where(l => AutomationProperties.GetName(l) == "远程文件").ToArray();
        Assert.True(lists.Length == 1, "file lists=" + lists.Length + "; " + string.Join(" | ", pane.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
        var list = lists.Single(); Assert.True(list.Items.Cast<object>().Any(i => i.ToString()!.Contains("edit.txt")), "entries=" + string.Join(" | ", list.Items.Cast<object>()) + "; " + string.Join(" | ", pane.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
        list.SelectedItem = list.Items.Cast<object>().Single(i => i.ToString()!.Contains("edit.txt"));
        Click(pane, "编辑"); await Until(() => pane.GetVisualDescendants().OfType<TextEditor>().Any());
        pane.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "资源监控")).IsChecked = true;
        var monitor = pane.GetVisualDescendants().OfType<MonitorPanel>().Single(); await monitor.RefreshAsync(); await monitor.RefreshAsync();
        var editor = pane.GetVisualDescendants().OfType<TextEditor>().Single(); Assert.Equal("合成文件\r\nline\n", editor.Text);
        editor.Document.Insert(editor.Document.TextLength, "draft"); Assert.True(pane.GetVisualDescendants().OfType<RemoteEditorPanel>().Single().HasUnsaved);
        var placement = pane.GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "工作区排列");
        foreach (int layout in new[] { 1, 2, 0 })
        {
            placement.SelectedIndex = layout; owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Same(editor, pane.GetVisualDescendants().OfType<TextEditor>().Single()); Assert.EndsWith("draft", editor.Text); Assert.True(pane.Terminal.IsLive); Assert.Equal(1, fixture.Stats().GetProperty("shells").GetInt32());
            owner.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "workspace-live-layout-" + pane.CurrentLayout.Placement + ".png"));
        }
        editor.Document.UndoStack.Undo(); Assert.Equal("合成文件\r\nline\n", editor.Text); editor.Document.UndoStack.Redo();
        owner.UpdateLayout(); editor.TextArea.Focus(); Assert.True(editor.TextArea.IsFocused); owner.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, null); owner.KeyRelease(Key.S, RawInputModifiers.Control, PhysicalKey.S, null);
        await Until(() => owner.OwnedWindows.Count > 0); Click(owner.OwnedWindows.Single(), "取消"); await Until(() => !pane.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("保存中") == true));
        Assert.Empty(queue.Snapshot());
        Click(pane.GetVisualDescendants().OfType<RemoteEditorPanel>().Single(), "保存"); await Until(() => owner.OwnedWindows.Count > 0); Click(owner.OwnedWindows.Single(), "确认");
        await Until(() => queue.Snapshot().Any(t => t.State == TransferState.Completed)); await Until(() => !pane.GetVisualDescendants().OfType<RemoteEditorPanel>().Single().HasUnsaved);
        Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
        foreach (var size in new[] { new Size(1150, 760), new Size(768, 506), new Size(700, 433) })
        {
            owner.Width = size.Width; owner.Height = size.Height; owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.True(editor.IsEffectivelyVisible); Assert.True(monitor.IsEffectivelyVisible); Assert.True(list.IsEffectivelyVisible); Assert.True(pane.Terminal.IsEffectivelyVisible);
            foreach (var area in new Control[] { editor, (Control)monitor.Parent!, list, pane.Terminal }) { var point = area.TranslatePoint(default, owner)!.Value; Assert.True(area.Bounds.Width > 0 && area.Bounds.Height > 0, $"{area.GetType().Name} empty {area.Bounds} at {size}"); Assert.True(point.X >= 0 && point.Y >= 0 && point.X + area.Bounds.Width <= owner.Width + 1 && point.Y + area.Bounds.Height <= owner.Height + 1, $"{area.GetType().Name} {point} {area.Bounds}"); }
            owner.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, $"four-areas-{size.Width}.png"));
        }
        Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light; owner.Width = 1150; owner.Height = 760; owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        owner.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "four-areas-light.png"));
        Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; pane.Dispose(); owner.Close();
    }
    [AvaloniaFact] public async Task HiddenMonitorCancelsAndPausesAutomaticRefresh()
    {
        using var fixture = new TransportTests.Fixture("FIXTURE_EXEC_SLOW"); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var panel = new MonitorPanel(() => root); var parent = new Grid { Children = { panel } }; var owner = new Window { Content = parent, Width = 900, Height = 400 }; owner.Show();
        panel.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
        var active = panel.RefreshAsync(); await Until(() => fixture.Stats().GetProperty("execs").GetInt32() == 1);
        parent.IsVisible = false; await active.WaitAsync(ct.Token); await Task.Delay(3400, ct.Token);
        Assert.Equal(1, fixture.Stats().GetProperty("execs").GetInt32()); Assert.True(root.IsConnected); owner.Close();
    }
    [AvaloniaFact] public async Task QueuedSaveCancellationRetainsDraftAndNewerEditsStayDirty()
    {
        using var fixture = new TransportTests.Fixture(posix: true); fixture.SeedText(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var source = await root.OpenFilesAsync(ct.Token); using var queue = new TransferQueue(); var owner = new Window { Width = 650, Height = 400 };
        using var panel = new RemoteEditorPanel(owner, queue); owner.Content = panel; owner.Show(); await panel.OpenAsync(await new SafeTransfers().CaptureDirectoryAsync(source, "/", ct.Token), "edit.txt", ct.Token);
        var editor = panel.GetVisualDescendants().OfType<TextEditor>().Single(); editor.Document.Insert(editor.Document.TextLength, "draft");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var blocker = queue.Add("blocker", "/", (_, t) => release.Task.WaitAsync(t));
        Click(panel, "保存"); await Until(() => owner.OwnedWindows.Count > 0); Click(owner.OwnedWindows.Single(), "确认"); await Until(() => queue.Snapshot().Count == 2);
        var pending = queue.Snapshot().Single(t => t.Name.Contains("编辑保存")); Assert.Equal(TransferState.Queued, pending.State);
        editor.Document.Insert(editor.Document.TextLength, "-newer"); queue.Cancel(pending.Id);
        await Until(() => !panel.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("保存中") == true));
        Assert.True(panel.HasUnsaved); Assert.Contains("-newer", editor.Text); release.SetResult(); await blocker.Completion.WaitAsync(ct.Token);
        Click(panel, "保存"); await Until(() => owner.OwnedWindows.Count > 0); Click(owner.OwnedWindows.Single(), "确认");
        await Until(() => !panel.HasUnsaved); Assert.Equal("合成文件\r\nline\ndraft-newer", (await new SafeTransfers().OpenTextAsync(await new SafeTransfers().CaptureDirectoryAsync(source, "/", ct.Token), "edit.txt", ct.Token)).Text);
        owner.Close();
    }
    [AvaloniaFact] public async Task AutomaticRefreshRequiresOptInAndStopsWhenHidden()
    {
        using var fixture = new TransportTests.Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var panel = new MonitorPanel(() => root); var owner = new Window { Width = 900, Height = 400, Content = panel }; owner.Show();
        await Task.Delay(300, ct.Token); Assert.Equal(0, fixture.Stats().GetProperty("execs").GetInt32());
        panel.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
        await Until(() => fixture.Stats().GetProperty("execs").GetInt32() >= 2); panel.IsVisible = false;
        int count = fixture.Stats().GetProperty("execs").GetInt32(); await Task.Delay(3300, ct.Token); Assert.Equal(count, fixture.Stats().GetProperty("execs").GetInt32()); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
        owner.Close();
    }
    [AvaloniaFact] public async Task ClosedFileSourceRetainsReadOnlyDraftAndCloseRequiresConfirmation()
    {
        using var fixture = new TransportTests.Fixture(posix: true); fixture.SeedText(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var source = await root.OpenFilesAsync(ct.Token); using var queue = new TransferQueue(); var owner = new Window { Width = 600, Height = 400 };
        using var panel = new RemoteEditorPanel(owner, queue); owner.Content = panel; owner.Show(); await panel.OpenAsync(await new SafeTransfers().CaptureDirectoryAsync(source, "/", ct.Token), "edit.txt", ct.Token);
        var editor = panel.GetVisualDescendants().OfType<TextEditor>().Single(); editor.Document.Insert(editor.Document.TextLength, "unsaved"); source.Dispose();
        await Until(() => editor.IsReadOnly); Assert.Contains("unsaved", editor.Text); Assert.True(panel.HasUnsaved);
        var close = panel.ConfirmCloseAsync(); await Until(() => owner.OwnedWindows.Count > 0); Click(owner.OwnedWindows.Single(), "取消"); Assert.False(await close); Assert.Contains("unsaved", editor.Text);
        owner.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "offline-draft.png")); owner.Close();
    }
}
