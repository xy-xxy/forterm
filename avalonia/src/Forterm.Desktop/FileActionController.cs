using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Forterm.Core;
namespace Forterm.Desktop;

/// <summary>Private in-app file clipboard. Plans retain the original source/version and destination.</summary>
public sealed class FileActionController(Window owner, TransferQueue queue)
{
    readonly RemoteFileOperations operations = new();
    sealed record ClipboardFile(RemoteFileSnapshot File, string Title, bool Move);
    ClipboardFile? clipboard;
    public event Action<Guid, string>? Changed;
    void Notify(Guid source, string directory) => Dispatcher.UIThread.Post(() => Changed?.Invoke(source, directory));
    static RemoteEntry File(RemoteEntry? item) => item is { Regular: true, Link: false } ? item : throw new IOException("此操作仅支持普通文件；目录和链接不递归操作");
    static RemoteEntry Node(RemoteEntry? item) => item is { Link: false } && (item.Regular || item.Directory) ? item : throw new IOException("请选择普通文件或目录，不操作链接或特殊节点");
    public MenuItem[] Menu(IRemoteFileSource source, string title, Func<RemoteEntry?> selected, Func<string> directory, Action<Func<CancellationToken, Task>> run, Action<string> status)
    {
        MenuItem Item(string label, Func<CancellationToken, Task> work) => new() { Header = label, Command = new ActionCommand(() => run(work)) };
        return [
            Item("复制文件", ct => Copy(source, title, File(selected()), false, status, ct)),
            Item("剪切文件", ct => Copy(source, title, File(selected()), true, status, ct)),
            Item("粘贴文件", ct => Paste(source, title, directory(), status, ct)),
            Item("重命名节点", ct => Rename(source, title, Node(selected()), status, ct)),
            Item("删除节点", ct => Remove(source, title, Node(selected()), status, ct)),
            Item("新建目录", ct => MakeDirectory(source, title, directory(), status, ct)),
            Item("权限计划", async ct => { var node = Node(selected()); ct.ThrowIfCancellationRequested(); await new PermissionWindow(source, title, node.Path, queue).ShowDialog(owner); })
        ];
    }
    public void Keyboard(ListBox files, IRemoteFileSource source, string title, Func<RemoteEntry?> selected, Func<string> directory, Action<Func<CancellationToken, Task>> run, Action<string> status)
    {
        files.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (!files.IsKeyboardFocusWithin || e.KeyModifiers is not (KeyModifiers.Control or KeyModifiers.Meta)) return;
            if (e.Key == Key.C) { e.Handled = true; run(ct => Copy(source, title, File(selected()), false, status, ct)); }
            else if (e.Key == Key.X) { e.Handled = true; run(ct => Copy(source, title, File(selected()), true, status, ct)); }
            else if (e.Key == Key.V) { e.Handled = true; run(ct => Paste(source, title, directory(), status, ct)); }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }
    async Task Copy(IRemoteFileSource source, string title, RemoteEntry item, bool move, Action<string> status, CancellationToken ct)
    {
        var directory = await operations.DirectoryAsync(source, item.Path[..Math.Max(1, item.Path.LastIndexOf('/'))], ct);
        var snapshot = await operations.CaptureAsync(directory, item.Name, ct); ct.ThrowIfCancellationRequested(); clipboard = new(snapshot, title, move);
        status((move ? "已标记剪切：" : "已复制：") + title + " · " + item.Name + "；源版本固定，粘贴时复核；独立来源采用32KiB流式，同FTP控制连接复制限16MiB");
    }
    async Task Paste(IRemoteFileSource destination, string title, string path, Action<string> status, CancellationToken ct)
    {
        var captured = clipboard ?? throw new IOException("应用内文件剪贴板为空，请先复制或剪切普通文件");
        if (!captured.File.Source.IsAvailable) throw new IOException("剪贴板原来源已关闭，不自动重新登录或改投");
        var directory = await operations.DirectoryAsync(destination, path, ct);
        string? name = await UI.AskText(owner, captured.Move ? "移动文件到原目标目录" : "复制文件到原目标目录", captured.Title + " · " + captured.File.Path + "\n→ " + title + " · " + directory.Path + "\n目标名称（已有SFTP目标另行确认，FTP不覆盖）", captured.File.Name, ct);
        if (name == null) { status("已取消粘贴"); return; }
        ct.ThrowIfCancellationRequested();
        var target = await operations.PlanTargetAsync(directory, name, ct); if (captured.File.SourceId == directory.SourceId && captured.File.Path == target.Path) throw new IOException("源与目标相同，请使用不同名称"); bool replace = false;
        if (target.Exists)
        {
            if (!target.CanReplace) throw new IOException("该目标未确认原子替换能力；已有文件保留，请选择新名称");
            replace = await UI.Confirm(owner, "确认覆盖已核对SFTP目标？", title + " · " + target.Path + "\n" + target.Length + " B · " + target.Modified + "\n仅替换这一版本，远端变化即停止；复制保留目标权限，原地移动保留源权限。", ct);
            if (!replace) { status("已取消覆盖"); return; }
        }
        ct.ThrowIfCancellationRequested();
        var bound = CancellationTokenSource.CreateLinkedTokenSource(captured.File.Source.Closed, destination.Closed);
        TransferTicket ticket;
        try
        {
            ticket = queue.Add(name + (captured.Move ? " · 移动" : " · 复制"), captured.Title + " → " + title + " · " + directory.Path,
                async (progress, token) =>
                {
                    await operations.CopyPlannedAsync(captured.File, target, captured.Move, replace, progress, token);
                    if (captured.Move) { Interlocked.CompareExchange(ref clipboard, null, captured); Notify(captured.File.SourceId, captured.File.Directory); }
                    Notify(directory.SourceId, directory.Path);
                }, bound.Token);
        }
        catch { bound.Dispose(); throw; }
        _ = ticket.Completion.ContinueWith(_ => bound.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        status("已加入后台队列；固定原来源和目标目录，源/目标版本变化即停止，覆盖限已明确确认的SFTP版本");
    }
    async Task Rename(IRemoteFileSource source, string title, RemoteEntry item, Action<string> status, CancellationToken ct)
    {
        var directory = await operations.DirectoryAsync(source, item.Path[..Math.Max(1, item.Path.LastIndexOf('/'))], ct); var file = await operations.CaptureNodeAsync(directory, item.Name, ct);
        string? name = await UI.AskText(owner, "重命名远程节点", title + " · " + item.Path + "\n只提交经核对的版本，不覆盖已存在目标", item.Name, ct); if (name == null) return; ct.ThrowIfCancellationRequested();
        queue.Add(item.Name + " · 改名", title + " · " + directory.Path, async (_, token) => { await operations.RenameAsync(file, name, token); Notify(source.SourceId, directory.Path); }, source.Closed); status("改名已排队，目标已固定到原来源目录");
    }
    async Task MakeDirectory(IRemoteFileSource source, string title, string path, Action<string> status, CancellationToken ct)
    {
        var parent = await operations.DirectoryAsync(source, path, ct);
        string? name = await UI.AskText(owner, "新建远程目录", title + " · " + parent.Path + "\nSFTP以0700创建；FTP使用服务器目录权限策略。名称已存在即拒绝。", "", ct);
        if (name == null) return; ct.ThrowIfCancellationRequested();
        queue.Add(name + " · 新目录", title + " · " + parent.Path, async (_, token) => { await operations.CreateDirectoryAsync(parent, name, token); Notify(source.SourceId, parent.Path); }, source.Closed);
        status("新建目录已排队，绑定原来源目录");
    }
    async Task Remove(IRemoteFileSource source, string title, RemoteEntry item, Action<string> status, CancellationToken ct)
    {
        var directory = await operations.DirectoryAsync(source, item.Path[..Math.Max(1, item.Path.LastIndexOf('/'))], ct); var file = await operations.CaptureNodeAsync(directory, item.Name, ct);
        if (!await UI.Confirm(owner, "删除远程节点？", title + " · " + file.Path + "\n" + (file.IsDirectory ? "仅删除空目录" : file.Length + " B") + "\n无回收站；仅删除已核对的这一版本，不递归。", ct)) return; ct.ThrowIfCancellationRequested();
        queue.Add(item.Name + " · 删除", title + " · " + directory.Path, async (progress, token) => { await operations.RemoveAsync(file, progress, token); Notify(source.SourceId, directory.Path); }, source.Closed); status("删除已排队；提交前仍会核对原版本");
    }
}
public static partial class UI
{
    public static async Task<string?> AskText(Window owner, string title, string message, string initial, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var window = new Window { Title = title, Width = 580, Height = 320, MinWidth = 360, MinHeight = 220, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var input = new TextBox { Text = initial, MaxLength = 1024 }; var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        buttons.Children.Add(Button("取消", () => window.Close())); buttons.Children.Add(Button("确认", () => window.Close(input.Text), true));
        var body = new DockPanel { Margin = new Avalonia.Thickness(20) }; DockPanel.SetDock(buttons, Dock.Bottom); body.Children.Add(buttons);
        var content = new StackPanel { Spacing = 14, Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, input } }; body.Children.Add(new ScrollViewer { Content = content }); window.Content = body;
        window.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        using var cancel = ct.Register(() => Dispatcher.UIThread.Post(() => window.Close())); return await window.ShowDialog<string?>(owner);
    }
}
