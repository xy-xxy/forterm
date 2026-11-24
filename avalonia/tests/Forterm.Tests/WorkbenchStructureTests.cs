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

public sealed class WorkbenchStructureTests
{
    static void Settle(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
    }
    static void Click(Window window, string name) => window.GetVisualDescendants().OfType<Button>()
        .Single(button => AutomationProperties.GetName(button) == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task LoadedWorkspaceUsesRailHeadersAndActualMetricsWhileLibraryStaysSeparate()
    {
        using var fixture = new TransportTests.Fixture(posix: true);
        fixture.SeedTree(); fixture.SeedText();
        string directory = Path.Combine(Path.GetTempPath(), "forterm-structure-" + Guid.NewGuid().ToString("N"));
        var profiles = Enumerable.Range(1, 12).Select(index => new ConnectionProfile($"测试节点 {index:00}", index <= 6 ? "开发环境" : "集成环境", "host-" + index + ".invalid", 22, "synthetic") { Favorite = index <= 2 }).ToArray();
        new ProfileStore(directory).Save(profiles);
        var window = new MainWindow(directory, new CredentialStoreTests.MemoryVault()) { Width = 1600, Height = 1000 };
        window.Show();
        try
        {
            Settle(window);
            window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "review-library-populated.png"));
            var open = window.AcceptLaunch(LaunchPlan.Parse(["-newtab", "合成 SSH 工作区", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"]));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (window.OwnedWindows.Count == 0) await Task.Delay(10, timeout.Token);
            var confirm = window.OwnedWindows.Single();
            confirm.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "确认")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await open;
            var pane = Assert.Single(window.Workspaces);
            var monitor = pane.GetVisualDescendants().OfType<MonitorPanel>().Single();
            Settle(window); await monitor.RefreshAsync(); await monitor.RefreshAsync(); Settle(window);
            Assert.True(pane.CurrentLayout.FilesVisible); Assert.True(pane.CurrentLayout.MonitorVisible);
            Assert.False(window.GetVisualDescendants().OfType<TreeView>().Single(tree => AutomationProperties.GetName(tree) == "连接分组").IsEffectivelyVisible);
            Assert.Equal(3, pane.GetVisualDescendants().OfType<Border>().Count(border => border.Classes.Contains("pane-header") && border.IsEffectivelyVisible));
            var list = pane.GetVisualDescendants().OfType<ListBox>().Single(listBox => AutomationProperties.GetName(listBox) == "远程文件");
            Assert.Contains(list.Items.Cast<RemoteEntry>(), entry => entry.Name == "edit.txt");
            window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "review-workspace-loaded.png"));
            Click(window, "文件工作区 · 最大化或还原"); Settle(window);
            Assert.False(pane.Terminal.IsEffectivelyVisible);
            Click(window, "文件工作区 · 最大化或还原"); Settle(window);
            Assert.True(pane.Terminal.IsEffectivelyVisible);
            Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
        }
        finally { window.Close(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void BrokerIdentityFollowsResolvedDataDirectory()
    {
        Assert.Equal(InstanceBroker.ForDataDirectory("/tmp/forterm-a"), InstanceBroker.ForDataDirectory("/tmp/forterm-a/../forterm-a"));
        Assert.NotEqual(InstanceBroker.ForDataDirectory("/tmp/forterm-a"), InstanceBroker.ForDataDirectory("/tmp/forterm-b"));
    }
}
