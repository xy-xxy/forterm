using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Desktop;
using Xunit;

namespace Forterm.Tests;

public sealed class VisualPolishUiTests
{
    static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
    }

    [AvaloniaFact]
    public void ConnectionLibraryUsesCompactNavigationRowsAndProtocolBadges()
    {
        var window = new MainWindow(null, new CredentialStoreTests.MemoryVault()) { Width = 1150, Height = 760 };
        window.Show();
        try
        {
            Settle(window);
            var groups = window.GetVisualDescendants().OfType<TreeView>().Single(t => AutomationProperties.GetName(t) == "连接分组");
            var root = groups.GetVisualDescendants().OfType<TreeViewItem>().Single();
            Assert.Contains("all-connections", root.Classes);
            Assert.Contains(groups.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "全部连接");
            Assert.Contains(groups.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("nav-count"));
            window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "polished-library-empty.png"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ConnectionRowsExposeExplicitConnectEditAndMoreActions()
    {
        var dir = Path.Combine(Path.GetTempPath(), "forterm-row-actions-" + Guid.NewGuid().ToString("N"));
        new ProfileStore(dir).Save([new ConnectionProfile("合成服务器", "测试", "example.invalid", 22, "user")]);
        var window = new MainWindow(dir, new CredentialStoreTests.MemoryVault()) { Width = 1150, Height = 760 };
        window.Show();
        try
        {
            Settle(window);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "连接"));
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "编辑"));
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "⋯"));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "user@example.invalid:22");
            window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "connection-row-actions.png"));
        }
        finally
        {
            window.Close();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [AvaloniaFact]
    public void ConnectionLibrarySortAndFavoritePersistInRows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "forterm-library-state-" + Guid.NewGuid().ToString("N"));
        new ProfileStore(dir).Save([new ConnectionProfile("Beta", "测试", "z.example", 22, "user"), new ConnectionProfile("Alpha", "测试", "a.example", 22, "user")]);
        var window = new MainWindow(dir, new CredentialStoreTests.MemoryVault()) { Width = 1150, Height = 760 };
        window.Show();
        try
        {
            Settle(window);
            var sort = window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationProperties.GetName(combo) == "连接排序");
            sort.SelectedIndex = 1; Settle(window);
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(listBox => AutomationProperties.GetName(listBox) == "连接列表");
            Assert.Equal("Alpha", ((ConnectionProfile)list.Items[0]!).Name);
            list.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetName(button) == "收藏 Alpha").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            sort.SelectedIndex = 3; Settle(window);
            Assert.True(((ConnectionProfile)list.Items[0]!).Favorite);
        }
        finally
        {
            window.Close();
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [AvaloniaFact]
    public void TopNavigationMatchesCompactXTerminalModeBar()
    {
        var window = new MainWindow(null, new CredentialStoreTests.MemoryVault()) { Width = 1150, Height = 760 };
        window.Show();
        try
        {
            Settle(window);
            var nav = window.GetVisualDescendants().OfType<WrapPanel>().Single(panel => panel.Classes.Contains("top-navigation"));
            var buttons = nav.Children.OfType<Button>().ToArray();
            Assert.Equal(new[] { "连接库", "新建", "本地终端", "验证器", "导航", "工作区", "快捷键" }, buttons.Select(button => button.Content as string));
            Assert.Single(buttons, button => button.Classes.Contains("active"));
            Assert.Contains(buttons, button => Equals(button.Content, "连接库") && button.Classes.Contains("active"));
            Assert.All(buttons, button => { Assert.Equal(32, button.Height); Assert.Equal(0, button.MinWidth); Assert.Equal(0, button.BorderThickness.Left); Assert.Equal(0, button.BorderThickness.Bottom); });
            buttons.Single(button => Equals(button.Content, "工作区")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle(window);
            Assert.Contains(buttons, button => Equals(button.Content, "连接库") && button.Classes.Contains("active"));
            Assert.False(buttons.Single(button => Equals(button.Content, "工作区")).IsEnabled);
            window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "top-navigation-xterminal-modebar.png"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EnteringWorkspaceReplacesConnectionNavWithSessionFilePane()
    {
        var window = new MainWindow(null, new CredentialStoreTests.MemoryVault()) { Width = 1150, Height = 760 };
        window.Show();
        try
        {
            Settle(window);
            window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "本地终端")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 100 && (window.Workspaces.Count == 0 || !window.GetVisualDescendants().OfType<LocalFilesView>().Any()); i++) await Task.Delay(10);
            Settle(window);
            var groups = window.GetVisualDescendants().OfType<TreeView>().Single(tree => AutomationProperties.GetName(tree) == "连接分组");
            var pane = Assert.Single(window.Workspaces);
            Assert.False(groups.IsEffectivelyVisible);
            Assert.True(pane.IsEffectivelyVisible);
            Assert.True(pane.GetVisualDescendants().OfType<CheckBox>().Single(button => Equals(button.Content, "资源监控")).IsChecked);
            Assert.Contains(pane.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("本机文件") == true);
            Assert.Contains(pane.GetVisualDescendants().OfType<ListBox>(), list => AutomationProperties.GetName(list) == "本机文件");
            Assert.DoesNotContain(pane.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("file-empty-state") && border.IsEffectivelyVisible);
            Assert.DoesNotContain(pane.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "关联已打开的文件会话") && button.IsEffectivelyVisible);
            Assert.True(pane.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("file-source-toolbar")).IsEffectivelyVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("activity-rail"));
            Assert.Contains(window.GetVisualDescendants().OfType<TabControl>(), tabs => tabs.Classes.Contains("workspace-tabs"));
            window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "workspace-file-pane-replaces-connection-nav.png"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PaneHeadersMaximizeAndRestoreWithoutReplacingContent()
    {
        var owner = new Window { Width = 1280, Height = 800 };
        using var pane = new SessionPane(owner, null);
        owner.Content = pane; owner.Show();
        try
        {
            Settle(owner);
            var terminal = pane.Terminal;
            var before = pane.CurrentLayout;
            var maximize = pane.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "终端 · 最大化或还原");
            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(owner);
            Assert.False(pane.CurrentLayout.FilesVisible);
            maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(owner);
            Assert.Equal(before, pane.CurrentLayout);
            Assert.Same(terminal, pane.Terminal);
            var rail = pane.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("activity-rail"));
            Assert.Equal(40, rail.Bounds.Width);
            pane.ToggleCapability(3); Settle(owner);
            owner.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "workbench-rail-and-headers.png"));
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public async Task WorkspaceToolbarGroupsCapabilitiesSourcesAndPlacement()
    {
        var owner = new Window { Width = 1150, Height = 760 };
        using var pane = new SessionPane(owner, null);
        owner.Content = pane;
        owner.Show();
        try
        {
            Settle(owner);
            var toggles = pane.GetVisualDescendants().OfType<CheckBox>().Where(c => c.Classes.Contains("capability-toggle")).ToArray();
            Assert.Equal(4, toggles.Length);
            Assert.All(toggles, toggle => Assert.Equal(34, toggle.MinHeight));
            Assert.Contains(pane.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("file-source-toolbar"));
            Assert.Contains(pane.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("activity-rail"));
            Assert.Equal(30, pane.GetVisualDescendants().OfType<ComboBox>().Single(combo => combo.Classes.Contains("source-picker")).Height);
            var placement = pane.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationProperties.GetName(combo) == "工作区排列");
            Assert.Contains("placement-picker", placement.Classes);
            Assert.Equal(28, placement.Height);
            Assert.Equal(100, placement.MinWidth);
            pane.ToggleCapability(2);
            Settle(owner);
            placement.IsDropDownOpen = true;
            Settle(owner);
            await Task.Delay(220);
            Settle(owner);
            var popup = owner.GetVisualDescendants().OfType<Popup>().First(p => p.IsOpen);
            Assert.Contains("forterm-dropdown", placement.Classes);
            Assert.NotNull(popup.Transitions);
            Assert.Equal(1, popup.Opacity, precision: 2);
            Assert.Equal(0, popup.VerticalOffset, precision: 2);
            Assert.Contains(owner.GetVisualDescendants().OfType<ComboBoxItem>(), item => item.MinHeight == 34);
            File.WriteAllText(Path.Combine(UiEvidence.DirectoryPath, "combo-popup-visual-types.txt"), string.Join("\n", owner.GetVisualDescendants().Select(control => control.GetType().FullName)));
            owner.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "polished-combo-popup.png"));
            placement.IsDropDownOpen = false;
            owner.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "polished-workspace-local.png"));
        }
        finally
        {
            owner.Close();
        }
    }
}
