using System.Windows.Input;
using Forterm.Studio.Launch;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;

namespace Forterm.Studio.ViewModels;

/// <summary>File tree commands (toolbar, path bar and the right-click menu).</summary>
public sealed partial class WorkspaceViewModel
{
    public ICommand RefreshDirectoryCommand { get; private set; } = null!;
    public ICommand UploadFilesCommand { get; private set; } = null!;
    public ICommand UploadFolderCommand { get; private set; } = null!;
    public ICommand NewFileCommand { get; private set; } = null!;
    public ICommand NewFolderCommand { get; private set; } = null!;
    public ICommand DownloadCommand { get; private set; } = null!;
    public ICommand CopyPathCommand { get; private set; } = null!;
    public ICommand CopyNameCommand { get; private set; } = null!;
    public ICommand CdToTerminalCommand { get; private set; } = null!;
    public ICommand NewTerminalHereCommand { get; private set; } = null!;
    public ICommand RenameCommand { get; private set; } = null!;
    public ICommand DeleteCommand { get; private set; } = null!;
    public ICommand NavigateCommand { get; private set; } = null!;
    public ICommand NavigateRootCommand { get; private set; } = null!;
    public ICommand EnterDirectoryCommand { get; private set; } = null!;
    public ICommand OpenEditorCommand { get; private set; } = null!;
    public ICommand PermissionsCommand { get; private set; } = null!;
    public ICommand CompressCommand { get; private set; } = null!;
    public ICommand DownloadToDesktopCommand { get; private set; } = null!;
    public bool CanCompress => session != null;

    void InitFileCommands()
    {
        AsyncCommand Command(Func<Task> run) => new(async () =>
        {
            if (actions == null) { FileNotice = "文件通道不可用"; return; }
            await run();
        }, Report);
        RefreshDirectoryCommand = Command(() => ReloadDirectoryAsync(CurrentDirectory));
        UploadFilesCommand = Command(UploadFilesAsync);
        UploadFolderCommand = Command(UploadFolderAsync);
        NewFileCommand = Command(() => CreateAsync(folder: false));
        NewFolderCommand = Command(() => CreateAsync(folder: true));
        DownloadCommand = Command(DownloadAsync);
        CopyPathCommand = new AsyncCommand(() => CopyAsync(selectedNode?.Path ?? CurrentDirectory, "路径"), Report);
        CopyNameCommand = new AsyncCommand(() => CopyAsync(selectedNode?.Name ?? "", "文件名"), Report);
        CdToTerminalCommand = new AsyncCommand(CdToTerminalAsync, Report);
        NewTerminalHereCommand = new AsyncCommand(NewTerminalHereAsync, Report);
        RenameCommand = Command(RenameAsync);
        DeleteCommand = Command(DeleteAsync);
        NavigateCommand = new RelayCommand(p => { if (p is PathSegment segment) _ = NavigateAsync(segment.FullPath); });
        NavigateRootCommand = new RelayCommand(() => _ = NavigateAsync(rootPath));
        EnterDirectoryCommand = new AsyncCommand(EnterDirectoryAsync, Report);
        OpenEditorCommand = Command(async () => { if (selectedNode is { IsFile: true } node) await OpenEditorAsync(node); else FileNotice = "请选择一个文件"; });
        PermissionsCommand = Command(ChangePermissionsAsync);
        CompressCommand = Command(async () => { if (selectedNode is { } node && await actions!.CompressAsync(node, lifetime.Token)) FileNotice = "压缩已排队（服务器端 tar）"; });
        DownloadToDesktopCommand = Command(async () =>
        {
            if (selectedNode is not { } node) return;
            if (node.IsLink) { FileNotice = "链接不跟随；请在链接目标所在目录下载"; return; }
            if (await actions!.DownloadToDesktopAsync(node, lifetime.Token) is { } target) { downloadedTo = Path.GetDirectoryName(target) ?? ""; FileNotice = "已加入下载队列 → " + target; }
        });
    }

    void Report(Exception e) => FileNotice = e is OperationCanceledException ? "已取消" : Short(e.Message);

    async Task CopyAsync(string text, string what)
    {
        await interaction.CopyTextAsync(text);
        interaction.Toast(new ToastMessage(ToastKind.Success, $"已复制{what}：{Short(text)}"));
    }

    async Task UploadFilesAsync()
    {
        string target = CurrentDirectory;
        var files = await interaction.PickOpenFilesAsync("上传到 " + target);
        if (files.Length > 0) await UploadFilesAsync(target, files);
    }

    Task UploadFilesAsync(string target, IReadOnlyList<string> files) => UploadAsync(target, files, 0);

    /// <summary>Where files dropped on this row (or on empty space) go. A row that only says something ("空目录")
    /// stands for the directory it is in.</summary>
    public string DropDirectory(FileNodeViewModel? node) => node switch
    {
        null => CurrentDirectory,
        { IsPlaceholder: true } => node.Parent is { } parent ? DropDirectory(parent) : CurrentDirectory,
        { IsDirectory: true, IsLink: false } => node.Path,
        _ => node.ParentPath,
    };

    /// <summary>Files and folders dropped on the file column: the same plans, limits and questions as uploading
    /// them from the menu. <paramref name="unreachable"/> items had no local path.</summary>
    public async Task DropAsync(string target, IReadOnlyList<string> paths, int unreachable = 0)
    {
        if (actions == null) { FileNotice = "文件通道不可用"; return; }
        try { await UploadAsync(target, paths, unreachable); }
        catch (Exception e) { Report(e); }
    }

    /// <summary>One after the other; one that is refused does not keep the others back, and is named.</summary>
    async Task UploadAsync(string target, IReadOnlyList<string> paths, int unreachable)
    {
        int files = 0, folders = 0, left = unreachable;
        var refused = new List<string>();
        // Files first: their questions are short.
        var items = paths.Where(p => !Directory.Exists(p)).Concat(paths.Where(Directory.Exists)).ToList();
        for (int i = 0; i < items.Count; i++)
        {
            string path = items[i], name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            try
            {
                if (Directory.Exists(path)) { if (await actions!.UploadFolderAsync(target, path, lifetime.Token)) folders++; }
                else if (File.Exists(path)) files += await actions!.UploadFilesAsync(target, [path], lifetime.Token);
                else { left++; refused.Add($"“{name}”已不存在"); }
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
            {
                // Closed, or the connection is gone: none of the rest can be uploaded either.
                left += items.Count - i;
                refused.Insert(0, "已取消，或连接已断开");
                break;
            }
            catch (Exception e)
            {
                string reason = Short(e.Message);
                left++;
                refused.Add(reason.Contains(name, StringComparison.Ordinal) ? reason : $"“{name}”：{reason}");
            }
        }
        if (disposed != 0) return;
        if (unreachable > 0) refused.Add($"{unreachable} 项不是本机文件");
        string queued = string.Join("、", new[] { files > 0 ? $"{files} 个文件" : "", folders > 0 ? $"{folders} 个文件夹" : "" }.Where(x => x.Length > 0));
        FileNotice = (queued.Length > 0 ? $"已加入传输队列：{queued} → {target}" : "未上传任何文件")
            + (left > 0 ? $"。{left} 项没有上传：{string.Join("；", refused.Take(3))}{(refused.Count > 3 ? "…" : "")}" : "");
        if (left > 0 && queued.Length > 0) interaction.Toast(new ToastMessage(ToastKind.Warning, $"{Title}：{left} 项没有上传。{Short(refused[0])}"));
    }

    string downloadedTo = "";
    /// <summary>The menu entry for downloading without asking where to.</summary>
    public string DownloadHereHeader => Appearance.Current.DownloadFolder is { Length: > 0 } folder ? "下载到“" + Short(Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) is { Length: > 0 } name ? name : folder) + "”" : "下载到桌面";

    void OnTransfersEnded(TransferBatch batch)
    {
        if (disposed != 0) return;
        if (batch.Failures.Count > 0)
        {
            interaction.Toast(new ToastMessage(ToastKind.Error, $"{Title}：{batch.Failures.Count} 项没有完成。{Short(batch.Failures[0])}"));
            return;
        }
        if (!Appearance.Current.NotifyTransfers) return; // what went wrong is always said
        var parts = new List<string>();
        if (batch.Uploads > 0) parts.Add($"上传 {batch.Uploads} 项");
        if (batch.Downloads > 0) parts.Add($"下载 {batch.Downloads} 项");
        if (batch.Operations > 0) parts.Add($"文件操作 {batch.Operations} 项");
        if (parts.Count == 0) return; // everything was cancelled
        string folder = downloadedTo;
        interaction.Toast(batch.Downloads > 0 && folder.Length > 0
            ? new ToastMessage(ToastKind.Success, $"{Title}：已完成{string.Join("，", parts)}", "打开文件夹", () => _ = interaction.OpenFolderAsync(folder))
            : new ToastMessage(ToastKind.Success, $"{Title}：已完成{string.Join("，", parts)}"));
    }

    async Task UploadFolderAsync()
    {
        string target = CurrentDirectory;
        var folder = await interaction.PickFolderAsync("选择要上传到 " + target + " 的文件夹");
        if (folder == null) return;
        if (await actions!.UploadFolderAsync(target, folder, lifetime.Token)) FileNotice = $"已加入传输队列：文件夹 {Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))} → {target}";
    }

    async Task CreateAsync(bool folder)
    {
        string parent = CurrentDirectory;
        string? name = await interaction.PromptTextAsync(folder ? "新建文件夹" : "新建文件", "名称（在 " + parent + " 下）", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (folder) actions!.CreateFolder(parent, name); else actions!.CreateFile(parent, name);
        FileNotice = folder ? "新建文件夹已排队" : "新建文件已排队（同名时不会覆盖）";
    }

    async Task DownloadAsync()
    {
        if (selectedNode is not { } node) { FileNotice = "请先选择要下载的文件或目录"; return; }
        if (node.IsDirectory && !node.IsLink)
        {
            var folder = await interaction.PickFolderAsync("选择保存“" + node.Name + "”的本地位置");
            if (folder != null && await actions!.DownloadFolderAsync(node, folder, lifetime.Token)) { downloadedTo = folder; FileNotice = "下载文件夹已加入传输队列"; }
            return;
        }
        if (node.IsLink) { FileNotice = "链接不跟随；请在链接目标所在目录下载"; return; }
        var path = await interaction.PickSaveFileAsync("下载 " + node.Name, node.Name);
        if (path != null && await actions!.DownloadFileAsync(node, path, lifetime.Token)) { downloadedTo = Path.GetDirectoryName(path) ?? ""; FileNotice = "下载已加入传输队列"; }
    }

    async Task RenameAsync()
    {
        if (selectedNode is not { } node) return;
        string? name = await interaction.PromptTextAsync("重命名", "新名称", node.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == node.Name) return;
        actions!.Rename(node, name);
        FileNotice = "重命名已排队；提交前会核对原版本";
    }

    async Task DeleteAsync()
    {
        if (selectedNode is not { } node) return;
        if (await actions!.DeleteAsync(node, lifetime.Token)) FileNotice = "删除已排队；每一项删除前都会核对类型和大小，变化则停止";
    }

    bool launchTypingConfirmed;

    /// <summary>A session opened by the controller (xshell/xftp), not a saved connection or the local shell.</summary>
    internal bool IsLaunchedSession => profile == null && Kind != WorkspaceKind.Local;

    /// <summary>Another column is maximised over the terminals.</summary>
    internal bool IsCentreCovered { get; set; }

    /// <summary>Controller-launched sessions get nothing written automatically. A user-initiated cd is
    /// typed only after an explicit confirmation (once per workspace), because bastions may audit it
    /// or present a menu rather than a shell.</summary>
    async Task<bool> MayTypeIntoTerminalAsync()
    {
        if (!IsLaunchedSession || launchTypingConfirmed) return true;
        if (!await interaction.ConfirmAsync("向终端输入命令",
                "这是控制器唤醒的临时会话，Studio 不会自动向它写入任何内容" + (IsIdleOn ? "（你打开的空闲保活除外）" : "") + "。\n现在将按你的操作在终端中输入一条 cd 命令（堡垒机可能会审计；若终端是堡垒机菜单而不是 shell，请取消）。\n本会话中不再询问。",
                "输入", danger: false)) return false;
        launchTypingConfirmed = true;
        return true;
    }

    /// <summary>"执行 CD 命令到终端": types <c>cd &lt;dir&gt;</c> into the active terminal (user action).</summary>
    async Task CdToTerminalAsync()
    {
        // Linked files are used next to the terminal of the session that shows them.
        var desk = Desk;
        if (desk.session == null) { FileNotice = "只有 SSH 会话可以联动终端"; return; }
        string directory = CurrentDirectory;
        if (CdCommand(directory) is not { } command) { FileNotice = "路径含控制字符，不输入到终端"; return; }
        if (!await desk.MayTypeIntoTerminalAsync()) return;
        if (desk.disposed != 0 || !ReferenceEquals(desk, Desk)) return;
        desk.ShowTerminal = true;
        var tab = await desk.EnsureShellAsync();
        if (tab == null) { FileNotice = "终端不可用"; return; }
        desk.ActiveTerminal = tab;
        await desk.WriteTerminalAsync(tab, command, "CD 命令", byUser: true);
    }

    /// <summary>"新建终端到当前目录": a new shell tab on the same transport, then <c>cd</c>.</summary>
    async Task NewTerminalHereAsync()
    {
        var desk = Desk;
        if (desk.session == null) { FileNotice = "只有 SSH 会话可以新建终端"; return; }
        string directory = CurrentDirectory;
        if (CdCommand(directory) is not { } command) { FileNotice = "路径含控制字符，不输入到终端"; return; }
        if (!await desk.MayTypeIntoTerminalAsync()) return;
        if (desk.disposed != 0 || !ReferenceEquals(desk, Desk)) return;
        var tab = await desk.AddTerminalAsync();
        if (tab == null) { FileNotice = "终端不可用"; return; }
        await desk.WriteTerminalAsync(tab, command, "CD 命令", byUser: true);
    }

    int navigation;

    /// <param name="follow">The shell went there: that yields to what the user does meanwhile.</param>
    async Task NavigateAsync(string path, bool follow = false)
    {
        if (follow && fileMenuOpen) { deferredFollow = path; return; }
        if (path == rootPath) { SelectNothing(); return; }
        int mine = ++navigation;
        var before = selectedNode;
        var node = await ExpandToAsync(path);
        if (node == null || disposed != 0 || mine != navigation) return;
        if (follow && !ReferenceEquals(before, selectedNode) && selectedNode != null) return;
        if (follow && fileMenuOpen) { deferredFollow = path; return; }
        SelectedNode = node;
    }

    async Task ChangePermissionsAsync()
    {
        if (selectedNode is not { } node) return;
        if (node.IsLink) { FileNotice = "不修改链接的权限"; return; }
        var current = await actions!.InspectPermissionsAsync(node.Path, lifetime.Token);
        var request = await interaction.EditPermissionsAsync(current, actions.SudoAvailable);
        if (request == null) return;
        if (await actions.ChangePermissionsAsync(node, request.Mode, request.Recursive, request.FileMode, lifetime.Token, request.UseSudo))
            FileNotice = request.UseSudo ? "sudo 权限修改已排队；每一项都会再次核对身份，需要密码时会单独询问" : "权限修改已排队；每一项都会再次核对";
    }

    /// <summary>The server refused REALPATH: open a directory only after the user names it.</summary>
    async Task EnterDirectoryAsync()
    {
        if (sftp == null || lister == null) return;
        string? path = await interaction.PromptTextAsync("打开目录", "绝对路径（以 / 开头）", "/");
        if (string.IsNullOrWhiteSpace(path)) return;
        path = path.Trim();
        if (!path.StartsWith('/')) { FileNotice = "必须是以 / 开头的绝对路径"; return; }
        sftp.AuthorizeAbsoluteDirectory(path);
        NeedsDirectory = false;
        await LoadTreeAsync(path, lifetime.Token);
    }
}
