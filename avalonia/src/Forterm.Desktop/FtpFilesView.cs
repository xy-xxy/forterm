using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Forterm.Core;
namespace Forterm.Desktop;

public sealed class FtpFilesView : FileBrowserPanel
{
    readonly FileActionController? fileActions; bool refreshPending;
    readonly Window owner; readonly string title; readonly FtpFileSession source; readonly TransferQueue queue; readonly IFilePickers pickers;
    readonly Func<FtpDirectory, string, CancellationToken, Task> open; readonly Func<FtpDirectory, CancellationToken, Task> create;
    readonly SafeTransfers local = new(); readonly ListBox files = new() { Focusable = true, MinWidth = 80 }; readonly TextBox path = new() { Text = "/" }; readonly TextBlock state = UI.Label("FTP · 不主动覆盖同名目标；不提供SSH终端或exec", true);
    readonly TransferPanel transfers; readonly LazyRemoteTree tree; readonly SemaphoreSlim operations = new(1, 1); readonly CancellationTokenSource lifetime = new();
    readonly CancellationTokenRegistration sourceNotice; CancellationTokenSource? active; long epoch; string observed = "/"; bool disposed;
    public override IRemoteFileSource SourceHandle => source;
    public FtpFilesView(Window owner, string title, FtpFileSession source, TransferQueue queue, IFilePickers pickers,
        Func<FtpDirectory, string, CancellationToken, Task> open, Func<FtpDirectory, CancellationToken, Task> create, FileActionController? fileActions = null)
    {
        this.fileActions = fileActions; this.owner = owner; this.title = title; this.source = source; this.queue = queue; this.pickers = pickers; this.open = open; this.create = create;
        Margin = new Thickness(8, 4); state.MaxHeight = 28; transfers = new(owner, queue);
        tree = new(async (directory, ct) => (await source.ListAsync(directory, ct)).Entries) { IsVisible = true }; tree.SetRoot("/"); Avalonia.Automation.AutomationProperties.SetName(tree, "远程目录树"); tree.Navigate += target => { path.Text = target; Run(RefreshAsync); }; tree.Error += message => state.Text = message;
        files.ItemTemplate = WorkspaceChrome.FileEntries;
        files.Classes.Add("file-entry-list");
        tree.Classes.Add("file-directory-tree");
        path.Height = path.MinHeight = 26; path.FontSize = 12; path.Padding = new Thickness(6, 2);
        var tools = new StackPanel { Spacing = 6, Children = { path } }; var buttons = new WrapPanel();
        foreach (var action in new (string Text, Func<CancellationToken, Task> Work)[] { ("刷新", RefreshAsync), ("上传", Upload), ("下载", Download), ("编辑", Edit) })
        { var button = UI.Button(action.Text, () => Run(action.Work)); button.MinWidth = button.Width = 32; button.Height = 26; button.Padding = new Thickness(2, 0); button.FontSize = 11; button.ContentTemplate = new WorkspaceIconTemplate(); ToolTip.SetTip(button, action.Text); buttons.Children.Add(button); }
        var more = UI.Button("⋯", () => { }); more.Classes.Add("icon"); more.Width = more.MinWidth = more.Height = more.MinHeight = 26;
        var menu = new ContextMenu
        {
            ItemsSource = new[] {
            new MenuItem { Header = "显示 / 收起目录树", Command = new ActionCommand(() => tree.IsVisible = !tree.IsVisible) },
            new MenuItem { Header = "新建文本", Command = new ActionCommand(() => Run(NewText)) },
            new MenuItem { Header = "取消当前操作", Command = new ActionCommand(() => active?.Cancel()) }
        }
        }; more.Click += (_, _) => menu.Open(more); buttons.Children.Add(more); tools.Children.Add(buttons); tools.Children.Add(state); SetDock(tools, Dock.Top); Children.Add(tools);
        SetDock(transfers, Dock.Bottom); Children.Add(transfers); SetDock(tree, Dock.Top); Children.Add(tree); Children.Add(files);
        SizeChanged += (_, _) => { tree.MaxHeight = Math.Clamp(Bounds.Height * .2, 32, 100); transfers.ViewportHeight = Bounds.Height < 500 ? 76 : 170; transfers.SetCompact(Bounds.Height < 300); if (files.Bounds.Width < 1) files.Width = 80; else if (files.Width == 80 && Bounds.Width > 160) files.Width = double.NaN; };
        path.TextChanged += (_, _) => { if ((path.Text ?? "/") != observed) { observed = path.Text ?? "/"; Interlocked.Increment(ref epoch); files.ItemsSource = null; state.Text = "路径已变化，请刷新；任务仍绑定原来源"; } };
        files.DoubleTapped += (_, _) => { if (files.SelectedItem is RemoteEntry e) { if (e.Directory && !e.Link) { path.Text = e.Path; Run(RefreshAsync); } else if (e.Regular && !e.Link) Run(Edit); } };
        AutomationProperties.SetName(path, "远程路径"); AutomationProperties.SetName(files, "远程文件");
        if (fileActions != null)
        {
            var menuItems = fileActions.Menu(source, title, () => files.SelectedItem as RemoteEntry, () => path.Text ?? "/", Run, message => state.Text = message);
            files.ContextMenu = new ContextMenu { ItemsSource = menuItems };
            fileActions.Keyboard(files, source, title, () => files.SelectedItem as RemoteEntry, () => path.Text ?? "/", Run, message => state.Text = message);
            fileActions.Changed += FilesChanged;
        }
        AttachedToVisualTree += (_, _) => { if (refreshPending && !disposed) { refreshPending = false; _ = RefreshAfterChange(); } };
        sourceNotice = source.Closed.Register(() => Dispatcher.UIThread.Post(() => { if (!disposed) state.Text = source.CloseReason ?? "FTP来源已关闭，不自动重新认证"; }));
    }
    async void Run(Func<CancellationToken, Task> action)
    {
        if (!await operations.WaitAsync(0)) { state.Text = "已有文件操作进行中"; return; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, source.Closed); timeout.CancelAfter(TimeSpan.FromMinutes(30)); active = timeout;
        try { await action(timeout.Token); } catch (OperationCanceledException) { state.Text = "操作已取消或来源已停止，请核对未完成项"; } catch (Exception e) { state.Text = e.Message; } finally { active = null; operations.Release(); }
    }
    public override async Task RefreshAsync(CancellationToken ct)
    {
        string selected = path.Text ?? "/"; observed = selected; long request = Interlocked.Increment(ref epoch); var listing = await source.ListAsync(selected, ct);
        if (disposed || request != Volatile.Read(ref epoch) || path.Text != selected) return;
        observed = listing.Directory.Path; path.Text = observed; files.ItemsSource = listing.Entries; state.Text = "FTP已载入；仅操作经核对的新名称，不主动覆盖";
    }
    async Task Upload(CancellationToken ct)
    {
        var directory = await source.CaptureDirectoryAsync(path.Text ?? "/", ct); var picked = await pickers.UploadAsync(owner); ct.ThrowIfCancellationRequested(); if (picked == null) { state.Text = "已取消选择"; return; }
        var input = await local.CaptureLocalAsync(picked.Path, ct); var target = await source.CaptureAsync(directory, picked.Name, ct);
        if (target.Stamp != null) throw new FtpPolicyException("FTP同名目标已存在，未主动覆盖；请选择新名称");
        queue.Add(picked.Name, title + " · " + directory.Path, async (progress, token) => { await source.UploadAsync(new(directory, target, input), progress, token); await Dispatcher.UIThread.InvokeAsync(() => { if (!disposed && path.Text == directory.Path) Run(RefreshAsync); }); }, source.Closed);
        state.Text = "已排队；固定原FTP来源与目标，标准FTP不保证原子排他";
    }
    async Task Download(CancellationToken ct)
    {
        if (files.SelectedItem is not RemoteEntry item || !item.Regular || item.Link) throw new IOException("请选择普通文件");
        var directory = await source.CaptureDirectoryAsync(item.Path[..Math.Max(1, item.Path.LastIndexOf('/'))], ct); var input = await source.CaptureAsync(directory, item.Name, ct);
        var picked = await pickers.DownloadAsync(owner, item.Name); ct.ThrowIfCancellationRequested(); if (picked == null) return; var target = await local.CaptureLocalAsync(picked, ct);
        bool replace = target.Stamp != null && await UI.Confirm(owner, "确认替换本地文件", target.Path + "\n完成校验前保留旧文件。", ct); if (target.Stamp != null && !replace) return;
        queue.Add(item.Name, title + " · " + directory.Path, (progress, token) => source.DownloadAsync(new(directory, input, target, replace), progress, token), source.Closed); state.Text = "已加入串行下载队列";
    }
    async Task Edit(CancellationToken ct)
    {
        if (files.SelectedItem is not RemoteEntry item || !item.Regular || item.Link) throw new IOException("请选择普通文本文件");
        var directory = await source.CaptureDirectoryAsync(item.Path[..Math.Max(1, item.Path.LastIndexOf('/'))], ct); await open(directory, item.Name, ct);
    }
    async Task NewText(CancellationToken ct) => await create(await source.CaptureDirectoryAsync(path.Text ?? "/", ct), ct);
    async Task RefreshAfterChange()
    {
        try { await RefreshAsync(lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!disposed) state.Text = e.Message; }
    }
    void FilesChanged(Guid sourceId, string directory)
    {
        if (disposed || sourceId != source.SourceId || (path.Text ?? "/") != directory) return;
        if (IsEffectivelyVisible) _ = RefreshAfterChange(); else refreshPending = true;
    }
    public override void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); sourceNotice.Dispose(); if (fileActions != null) fileActions.Changed -= FilesChanged; tree.Dispose(); transfers.Dispose(); }
}
