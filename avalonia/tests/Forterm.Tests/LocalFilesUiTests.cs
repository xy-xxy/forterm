using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Desktop;
using Xunit;

namespace Forterm.Tests;

public sealed class LocalFilesUiTests
{
    static async Task Until(Func<bool> check)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!check())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, timeout.Token);
        }
    }

    [AvaloniaFact]
    public async Task LocalFileBrowserListsHomeAndNavigatesIntoDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "forterm-local-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "documents"));
        await File.WriteAllTextAsync(Path.Combine(root, "readme.txt"), "local");
        try
        {
            using var source = new LocalFileSource(root);
            var owner = new Window { Width = 640, Height = 480 };
            using var view = new LocalFilesView(owner, "本地终端", source);
            owner.Content = view;
            owner.Show();

            await view.RefreshAsync(CancellationToken.None);
            var files = view.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationProperties.GetName(list) == "本机文件");
            var tree = view.GetVisualDescendants().OfType<TreeView>().Single(treeView => AutomationProperties.GetName(treeView) == "本机目录树");
            Assert.NotEmpty(tree.Items);
            var rootItem = Assert.IsType<TreeViewItem>(tree.Items[0]);
            rootItem.IsExpanded = true;
            await Until(() => rootItem.Items.OfType<TreeViewItem>().Any(item => Equals(item.Header, "documents")));
            Assert.Contains(rootItem.Items.OfType<TreeViewItem>(), item => Equals(item.Header, "readme.txt"));
            Assert.Contains(files.Items.Cast<object>(), item => item.ToString()!.Contains("documents"));
            Assert.Contains(files.Items.Cast<object>(), item => item.ToString()!.Contains("readme.txt"));

            files.SelectedItem = files.Items.Cast<object>().Single(item => item.ToString()!.Contains("documents"));
            view.OpenSelected();
            await Until(() => files.Items.Cast<object>().Any(item => item.ToString()!.Contains("..")));
            Assert.Contains("documents", view.CurrentPath, StringComparison.Ordinal);
            Assert.Contains(files.Items.Cast<object>(), item => item.ToString()!.Contains(".."));
            owner.Close();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [AvaloniaFact]
    public async Task LocalTerminalInstallsItsOwnFileWorkspace()
    {
        string root = Path.Combine(Path.GetTempPath(), "forterm-local-pane-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "local.txt"), "local");
        var owner = new Window { Width = 900, Height = 600 };
        using var pane = new SessionPane(owner, null, localRoot: root);
        owner.Content = pane;
        owner.Show();
        try
        {
            await pane.StartLocalAsync();
            await Until(() => pane.GetVisualDescendants().OfType<LocalFilesView>().Any());
            var files = pane.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationProperties.GetName(list) == "本机文件");
            await Until(() => files.Items.Cast<object>().Any(item => item.ToString()!.Contains("local.txt")));
            Assert.True(pane.HasFileCapability);
            Assert.Equal(root, pane.GetVisualDescendants().OfType<LocalFilesView>().Single().CurrentPath);
        }
        finally
        {
            owner.Close();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
