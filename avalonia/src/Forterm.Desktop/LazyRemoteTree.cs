using Avalonia.Controls;
using Forterm.Core;
namespace Forterm.Desktop;

/// <summary>Bound to one catalog/source for life. Collapsed branches release UI nodes.</summary>
public sealed class LazyRemoteTree : TreeView, IDisposable
{
    protected override Type StyleKeyOverride => typeof(TreeView);
    readonly Func<string, CancellationToken, Task<IReadOnlyList<RemoteEntry>>> listing; readonly bool includeFiles; readonly CancellationTokenSource lifetime = new();
    readonly Dictionary<TreeViewItem, Node> nodes = new(); bool disposed; long rootVersion;
    sealed class Node(string path, bool directory) { public string Path = path; public bool Directory = directory; public long Version; public bool Loading; }
    public event Action<string>? Navigate, Error;
    public LazyRemoteTree(RemoteDirectoryCatalog catalog) : this(async (path, ct) => (await catalog.ListAsync(path, false, ct)).Entries) { }
    public LazyRemoteTree(Func<string, CancellationToken, Task<IReadOnlyList<RemoteEntry>>> listing, bool includeFiles = false)
    {
        this.listing = listing; this.includeFiles = includeFiles; MaxHeight = 150;
        SelectionChanged += (_, _) => { if (SelectedItem is TreeViewItem item && nodes.TryGetValue(item, out var node) && node.Directory) Navigate?.Invoke(node.Path); };
    }
    public void SetRoot(string path)
    {
        if (Items.Count == 1 && Items[0] is TreeViewItem item && nodes.TryGetValue(item, out var current) && current.Path == path) return;
        rootVersion++; nodes.Clear(); Items.Clear(); Items.Add(Create(path, path, true));
    }
    TreeViewItem Create(string path, string label, bool directory)
    {
        var item = new TreeViewItem { Header = label, Tag = path }; var node = new Node(path, directory); nodes.Add(item, node);
        if (directory)
        {
            item.Items.Add(new TreeViewItem { Header = "展开载入…", IsEnabled = false });
            item.Expanded += async (_, e) => { if (ReferenceEquals(e.Source, item)) await Expand(item, node); };
            item.Collapsed += (_, e) => { if (ReferenceEquals(e.Source, item)) { node.Version++; DropChildren(item); item.Items.Add(new TreeViewItem { Header = "展开载入…", IsEnabled = false }); } };
        }
        return item;
    }
    void DropChildren(TreeViewItem parent)
    {
        foreach (var child in parent.Items.OfType<TreeViewItem>().ToArray()) { DropChildren(child); nodes.Remove(child); }
        parent.Items.Clear();
    }
    async Task Expand(TreeViewItem item, Node node)
    {
        if (disposed || node.Loading) return;
        if (node.Path.Count(c => c == '/') > 128) { Error?.Invoke("目录树最多128层，请用路径栏导航"); return; }
        node.Loading = true; long epoch = ++node.Version, root = rootVersion;
        try
        {
            var children = await listing(node.Path, lifetime.Token);
            if (disposed || root != rootVersion || !item.IsExpanded || !nodes.TryGetValue(item, out var current) || current != node || epoch != node.Version) return;
            var entries = children.Where(e => !e.Link && (includeFiles || e.Directory)).ToArray();
            DropChildren(item);
            if (nodes.Count + entries.Length > 4096) throw new IOException("目录树超过4096节点，请收起其他分支或使用文件列表");
            foreach (var entry in entries) item.Items.Add(Create(entry.Path, entry.Name, entry.Directory));
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (!disposed && nodes.ContainsKey(item) && epoch == node.Version) { DropChildren(item); item.Items.Add(new TreeViewItem { Header = "读取失败，收起再展开重试", IsEnabled = false }); Error?.Invoke(e.Message); }
        }
        finally { node.Loading = false; if (!disposed && node.Version != epoch && nodes.ContainsKey(item) && item.IsExpanded) _ = Expand(item, node); }
    }
    public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); rootVersion++; nodes.Clear(); Items.Clear(); }
}
