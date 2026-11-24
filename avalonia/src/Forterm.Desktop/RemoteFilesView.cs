using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Forterm.Core;
namespace Forterm.Desktop;

public sealed class RemoteFilesView : FileBrowserPanel
{
    readonly FileActionController? fileActions; bool refreshPending;
    readonly Window owner; readonly string title; readonly IFilePickers pickers; readonly TransferQueue transfers;
    readonly SafeTransfers safe = new(); readonly RemoteDirectoryCatalog catalog;
    readonly Func<RemoteDirectoryBinding, string, CancellationToken, Task> openEditor;
    readonly Func<RemoteDirectoryBinding, CancellationToken, Task> newText;
    readonly ListBox files = new() { Focusable = true, MinWidth = 80 }; readonly TextBox path = new() { Text = "." };
    readonly TextBlock state = new() { Classes = { "note" }, MaxHeight = 28 };
    readonly SemaphoreSlim operations = new(1, 1); readonly CancellationTokenSource lifetime = new();
    readonly LazyRemoteTree tree; readonly TransferPanel transferPanel; readonly CancellationTokenRegistration sourceNotice;
    public override IRemoteFileSource SourceHandle => Source;
    public SftpLease Source { get; }
    int disposed;
    public RemoteFilesView(Window owner, string title, SftpLease source, TransferQueue transfers, IFilePickers pickers,
        Func<RemoteDirectoryBinding, string, CancellationToken, Task> openEditor,
        Func<RemoteDirectoryBinding, CancellationToken, Task> newText, FileActionController? fileActions = null)
    {
        this.fileActions = fileActions; this.owner = owner; this.title = title; Source = source; this.transfers = transfers; this.pickers = pickers; this.openEditor = openEditor; this.newText = newText;
        catalog = new(source); tree = new(catalog) { IsVisible = true }; tree.SetRoot("/"); Avalonia.Automation.AutomationProperties.SetName(tree, "远程目录树"); tree.Navigate += target => { if (target != shownDirectory) { path.Text = target; Run(RefreshAsync); } }; tree.Error += message => state.Text = message; transferPanel = new(owner, transfers); Margin = new Thickness(8, 4);
        files.ItemTemplate = WorkspaceChrome.FileEntries;
        files.Classes.Add("file-entry-list");
        tree.Classes.Add("file-directory-tree");
        path.Height = path.MinHeight = 26; path.FontSize = 12; path.Padding = new Thickness(6, 2);
        var tools = new StackPanel { Spacing = 6 }; tools.Children.Add(path);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var action in new (string Label, Func<CancellationToken, Task> Work)[] { ("刷新", RefreshAsync), ("上传", Upload), ("下载", Download), ("编辑", OpenEditor) })
        { var button = UI.Button(action.Label, () => Run(action.Work)); button.MinWidth = button.Width = 32; button.Height = 26; button.Padding = new Thickness(2, 0); button.FontSize = 11; button.ContentTemplate = new WorkspaceIconTemplate(); ToolTip.SetTip(button, action.Label); buttons.Children.Add(button); }
        var more = UI.Button("⋯", () => { }); more.Classes.Add("icon"); more.Width = more.MinWidth = more.Height = more.MinHeight = 26;
        var treeMenu = new MenuItem { Header = "收起目录树" }; treeMenu.Click += (_, _) => { tree.IsVisible = !tree.IsVisible; treeMenu.Header = tree.IsVisible ? "收起目录树" : "显示目录树"; };
        var actions = new ContextMenu
        {
            ItemsSource = new[] { treeMenu,
            new MenuItem { Header = "新建文本", Command = new ActionCommand(() => Run(NewText)) },
            new MenuItem { Header = "上级目录", Command = new ActionCommand(ParentDirectory) },
            new MenuItem { Header = "取消当前操作", Command = new ActionCommand(() => operationCancel?.Cancel()) }
        }
        };
        more.Click += (_, _) => actions.Open(more); buttons.Children.Add(more); tools.Children.Add(buttons); tools.Children.Add(state); SetDock(tools, Dock.Top); Children.Add(tools);
        SetDock(transferPanel, Dock.Bottom); Children.Add(transferPanel); SetDock(tree, Dock.Top); Children.Add(tree); Children.Add(files);
        SizeChanged += (_, _) => { tree.MaxHeight = Math.Clamp(Bounds.Height * .2, 32, 100); transferPanel.ViewportHeight = Bounds.Height < 500 ? 76 : 170; transferPanel.SetCompact(Bounds.Height < 300); if (files.Bounds.Width < 1) files.Width = 80; else if (files.Width == 80 && Bounds.Width > 160) files.Width = double.NaN; };
        path.TextChanged += (_, _) => { if (!updatingPath && path.Text != observedPath) { observedPath = path.Text; Interlocked.Increment(ref listingEpoch); files.ItemsSource = null; state.Text = "路径已变化，请刷新载入；排队任务保持原目标"; } };
        AutomationProperties.SetName(files, "远程文件"); AutomationProperties.SetName(path, "远程路径");
        files.DoubleTapped += (_, _) => { if (files.SelectedItem is RemoteEntry item) { if (item.Directory && !item.Link) { path.Text = item.Path; Run(RefreshAsync); } else if (item.Regular && !item.Link) Run(OpenEditor); } };
        if (fileActions != null)
        {
            var menuItems = fileActions.Menu(Source, title, () => files.SelectedItem as RemoteEntry, () => path.Text ?? "/", Run, message => state.Text = message);
            files.ContextMenu = new ContextMenu { ItemsSource = menuItems };
            fileActions.Keyboard(files, Source, title, () => files.SelectedItem as RemoteEntry, () => path.Text ?? "/", Run, message => state.Text = message);
            fileActions.Changed += FilesChanged;
        }
        AttachedToVisualTree += (_, _) => { if (refreshPending && disposed == 0) { refreshPending = false; _ = RefreshAfterChange(); } };
        sourceNotice = Source.Closed.Register(() => Dispatcher.UIThread.Post(() => { if (disposed == 0) { state.Text = Source.CloseReason ?? "原文件来源已关闭，不自动改投或重新认证。"; } }));
    }
    void ParentDirectory() { string current = shownDirectory ?? path.Text ?? "/"; path.Text = current[..Math.Max(1, current.TrimEnd('/').LastIndexOf('/'))]; Run(RefreshAsync); }
    async Task OpenEditor(CancellationToken ct)
    {
        if (files.SelectedItem is not RemoteEntry item || !item.Regular || item.Link) throw new IOException("请选择普通文本文件");
        var directory = await safe.CaptureDirectoryAsync(Source, item.Path[..Math.Max(1, item.Path.LastIndexOf('/'))], ct);
        await openEditor(directory, item.Name, ct);
    }
    async Task NewText(CancellationToken ct) => await newText(await safe.CaptureDirectoryAsync(Source, path.Text ?? ".", ct), ct);
    CancellationTokenSource? operationCancel;
    async void Run(Func<CancellationToken, Task> action)
    {
        if (!await operations.WaitAsync(0)) { state.Text = "已有操作进行中"; return; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, Source.Closed); timeout.CancelAfter(TimeSpan.FromMinutes(30)); operationCancel = timeout;
        try { await action(timeout.Token); } catch (OperationCanceledException) { state.Text = "操作已取消/超时；未完成文件可能保留，请核对目标"; } catch (Exception e) { state.Text = e.Message; } finally { operationCancel = null; operations.Release(); }
    }
    long listingEpoch; string? shownDirectory; string? observedPath = "."; bool updatingPath;
    public override async Task RefreshAsync(CancellationToken ct)
    {
        var source = Source; string selected = path.Text ?? "."; observedPath = selected; long epoch = Interlocked.Increment(ref listingEpoch);
        if (!source.HasWorkingDirectory && !source.IsDirectoryAuthorized(selected))
        {
            if (!selected.StartsWith('/')) { state.Text = "服务端拒绝或不支持REALPATH，当前目录未知；输入已知绝对目录后刷新并明确确认。"; return; }
            bool confirmed = await UI.Confirm(owner, "确认直接读取绝对目录", selected + "\n服务器未提供当前目录。仅按此绝对目录做LSTAT/LIST核对，不猜路径、不重新登录。", ct);
            if (!confirmed) { state.Text = "已取消绝对目录读取"; return; }
            source.AuthorizeAbsoluteDirectory(selected);
        }
        var listing = await catalog.ListAsync(selected, true, ct); var binding = listing.Binding; var list = listing.Entries;
        if (disposed != 0 || epoch != Volatile.Read(ref listingEpoch) || source != Source || !source.IsAvailable || path.Text != selected) return;
        if (shownDirectory == null && !source.HasWorkingDirectory) tree.SetRoot(binding.Path); shownDirectory = binding.Path; updatingPath = true; try { files.ItemsSource = list; state.Text = source.HasWorkingDirectory ? "目录已载入" : "已按明确绝对目录读取；未改变认证"; observedPath = binding.Path; path.Text = binding.Path; } finally { updatingPath = false; }
    }
    async Task Upload(CancellationToken ct)
    {
        var source = Source; string selectedDirectory = path.Text ?? "."; if (!source.HasWorkingDirectory && shownDirectory != selectedDirectory) throw new IOException("请先刷新并确认这个绝对目录，成功读取后再上传");
        state.Text = "正在核对原来源目录…";
        var directory = await safe.CaptureDirectoryAsync(source, selectedDirectory, ct);
        var picked = await pickers.UploadAsync(owner); ct.ThrowIfCancellationRequested();
        if (picked == null) { state.Text = "已取消选择"; return; }
        string localPath = picked.Path;
        state.Text = "正在核对上传源与目标版本…"; var input = await safe.CaptureLocalAsync(localPath, ct); var target = await safe.CaptureRemoteAsync(directory, picked.Name, ct);
        bool replace = false; if (target.Stamp != null) { if (!source.Client.SupportsAtomicReplacement) throw new IOException("服务器未提供原子覆盖能力；旧文件保持，请使用新名称"); replace = await UI.Confirm(owner, "确认覆盖远程版本", $"原来源：{title}\n目录：{directory.Path}\n文件：{picked.Name}\n大小：{target.Stamp.Length} B\n修改：{target.Stamp.Modified:O}\n提交前会再次核对版本；发现变化即停止。", ct); if (!replace) { state.Text = "已取消覆盖"; return; } }
        ct.ThrowIfCancellationRequested(); if (!source.IsAvailable) throw new OperationCanceledException("原文件来源已关闭");
        var plan = new UploadPlan(directory, target, input, replace); transfers.Add(picked.Name, $"{title} · {directory.Path}", async (progress, token) => { await safe.UploadAsync(plan, progress, token); await Dispatcher.UIThread.InvokeAsync(() => { if (disposed == 0 && Source == source && path.Text == directory.Path) Run(RefreshAsync); }); }, source.Closed); state.Text = "已加入串行队列；目标已固定到原来源目录";
    }
    async Task Download(CancellationToken ct)
    {
        var source = Source; if (files.SelectedItem is not RemoteEntry item || item.Directory || item.Link) throw new InvalidOperationException("请选择普通文件");
        string parent = item.Path[..Math.Max(1, item.Path.LastIndexOf('/'))]; var directory = await safe.CaptureDirectoryAsync(source, parent, ct); var input = await safe.CaptureRemoteAsync(directory, item.Name, ct);
        var picked = await pickers.DownloadAsync(owner, item.Name); ct.ThrowIfCancellationRequested(); if (picked == null) { state.Text = "已取消选择"; return; }
        string localPath = picked;
        var target = await safe.CaptureLocalAsync(localPath, ct); bool replace = false;
        if (target.Stamp != null) { replace = await UI.Confirm(owner, "确认替换本地版本", $"{target.Path}\n当前大小：{target.Stamp.Length} B\n只替换已核对的这一版本；下载完成前旧文件保持不动。", ct); if (!replace) { state.Text = "已取消覆盖"; return; } }
        ct.ThrowIfCancellationRequested(); if (!source.IsAvailable) throw new OperationCanceledException("原文件来源已关闭");
        var plan = new DownloadPlan(directory, input, target, replace); transfers.Add(item.Name, $"{title} · {directory.Path}", (progress, token) => safe.DownloadAsync(plan, progress, token), source.Closed); state.Text = "已加入串行队列；下载将校验后提交";
    }

    async Task RefreshAfterChange()
    {
        try { await RefreshAsync(lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (disposed == 0) state.Text = e.Message; }
    }
    void FilesChanged(Guid sourceId, string directory)
    {
        if (disposed != 0 || sourceId != Source.SourceId || (path.Text ?? "/") != directory) return;
        if (IsEffectivelyVisible) _ = RefreshAfterChange(); else refreshPending = true;
    }
    public override void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel(); sourceNotice.Dispose(); if (fileActions != null) fileActions.Changed -= FilesChanged; tree.Dispose(); catalog.Dispose(); transferPanel.Dispose();
        // This view borrows Source. Closing or detaching it never closes the source connection.
    }
}
