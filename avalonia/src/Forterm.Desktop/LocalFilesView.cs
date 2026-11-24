using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Forterm.Core;

namespace Forterm.Desktop;

/// <summary>Local equivalent of the SFTP/FTP file pane. The view is scoped to
/// the local terminal's home directory and does not use a remote protocol.</summary>
public sealed class LocalFilesView : FileBrowserPanel
{
    readonly Window owner;
    readonly string title;
    readonly LocalFileSource source;
    readonly ListBox files = new() { Focusable = true, MinWidth = 120 };
    readonly LazyRemoteTree tree;
    readonly TextBox path = new() { IsReadOnly = true };
    readonly TextBlock state = UI.Label("本机文件 · 仅浏览本地目录", true);
    readonly SemaphoreSlim operations = new(1, 1);
    readonly CancellationTokenSource lifetime = new();
    int disposed;
    string currentPath;

    public LocalFilesView(Window owner, string title, LocalFileSource source)
    {
        this.owner = owner;
        this.title = title;
        this.source = source;
        currentPath = source.Root;

        Margin = new Thickness(8, 4);
        files.Classes.Add("file-entry-list");
        files.ItemTemplate = WorkspaceChrome.FileEntries;
        tree = new(async (directory, ct) => await ListTreeEntriesAsync(directory, ct), includeFiles: true) { IsVisible = true };
        tree.SetRoot(source.Root);
        tree.Classes.Add("file-directory-tree");
        tree.MaxHeight = double.PositiveInfinity;
        tree.MinHeight = 180;
        AutomationProperties.SetName(tree, "本机目录树");
        tree.Navigate += target =>
        {
            try
            {
                string resolved = source.Resolve(target);
                if (!PathEquals(currentPath, resolved)) { currentPath = resolved; Run(RefreshAsync); }
            }
            catch (Exception e) { state.Text = "本机目录不可用：" + e.Message; }
        };
        tree.Error += message => state.Text = message;
        path.Height = path.MinHeight = 28;
        path.FontSize = 12;
        path.Padding = new Thickness(8, 2);
        AutomationProperties.SetName(path, "本机路径");
        AutomationProperties.SetName(files, "本机文件");

        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        Grid.SetColumn(path, 0);
        toolbar.Children.Add(path);
        var parent = WorkspaceChrome.IconButton("↥", "返回上级目录", () => NavigateParent());
        parent.Classes.Add("icon");
        parent.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(parent, 1);
        toolbar.Children.Add(parent);
        var refresh = WorkspaceChrome.IconButton("刷新", "刷新本机文件", () => Run(RefreshAsync));
        refresh.Classes.Add("icon");
        refresh.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(refresh, 2);
        toolbar.Children.Add(refresh);

        var tools = new StackPanel { Spacing = 6, Children = { toolbar, state } };
        DockPanel.SetDock(tools, Dock.Top);
        Children.Add(tools);
        DockPanel.SetDock(tree, Dock.Top);
        Children.Add(tree);
        files.IsVisible = false;
        Children.Add(files);
        files.DoubleTapped += (_, _) => OpenSelected();
        SizeChanged += (_, _) =>
        {
            tree.MaxHeight = double.PositiveInfinity;
            files.Width = Bounds.Width > 160 ? double.NaN : 120;
        };
    }

    public override IRemoteFileSource SourceHandle => source;
    public string CurrentPath => currentPath;

    public override async Task RefreshAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token, source.Closed);
        await operations.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            string selected = currentPath;
            var entries = await Task.Run(() => ReadEntries(selected, linked.Token), linked.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (disposed != 0 || currentPath != selected) return;
                path.Text = selected;
                files.ItemsSource = entries;
                state.Text = $"本机文件 · {entries.Count} 项 · 不连接远程来源";
            });
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception e)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { if (disposed == 0) state.Text = "本机文件读取失败：" + e.Message; });
        }
        finally { operations.Release(); }
    }

    IReadOnlyList<RemoteEntry> ReadEntries(string selected, CancellationToken ct)
    {
        string directory = source.Resolve(selected);
        var result = new List<RemoteEntry>();
        if (!PathEquals(directory, source.Root))
        {
            string? parent = Directory.GetParent(directory)?.FullName;
            if (parent != null && source.Resolve(parent) == parent) result.Add(new(parent, "..", true, false, false, 0));
        }
        foreach (string item in Directory.EnumerateFileSystemEntries(directory))
        {
            ct.ThrowIfCancellationRequested();
            string full = source.Resolve(item);
            bool directoryEntry;
            try { directoryEntry = Directory.Exists(full); }
            catch { continue; }
            long size = 0;
            if (!directoryEntry)
            {
                try { size = new FileInfo(full).Length; }
                catch { continue; }
            }
            result.Add(new(full, Path.GetFileName(full), directoryEntry, false, !directoryEntry, size));
        }
        return result.OrderByDescending(e => e.Directory && e.Name != "..").ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    async Task<IReadOnlyList<RemoteEntry>> ListTreeEntriesAsync(string selected, CancellationToken ct)
    {
        return await Task.Run(() => ReadEntries(selected, ct).Where(entry => entry.Name != "..").ToArray(), ct).ConfigureAwait(false);
    }

    static bool PathEquals(string left, string right) => string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    public void OpenSelected()
    {
        if (files.SelectedItem is not RemoteEntry item) return;
        if (item.Directory)
        {
            try
            {
                currentPath = source.Resolve(item.Name == ".." ? Directory.GetParent(currentPath)?.FullName ?? currentPath : item.Path);
                Run(RefreshAsync);
            }
            catch (Exception e) { state.Text = "本机目录不可用：" + e.Message; }
            return;
        }
        state.Text = $"已选择本机文件：{item.Name} · 在线编辑将在本机会话内打开";
    }

    void NavigateParent()
    {
        if (PathEquals(currentPath, source.Root)) return;
        string? parent = Directory.GetParent(currentPath)?.FullName;
        if (parent == null) return;
        currentPath = source.Resolve(parent);
        Run(RefreshAsync);
    }

    async void Run(Func<CancellationToken, Task> action)
    {
        if (!await operations.WaitAsync(0)) return;
        operations.Release();
        try { await action(lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (disposed == 0) state.Text = e.Message; }
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        tree.Dispose();
        lifetime.Dispose();
        operations.Dispose();
        // The source is owned by SessionPane; a detached view must not close it.
    }
}
