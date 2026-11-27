using FluentFTP;
using Forterm.Core;
using Forterm.Studio.ViewModels;

namespace Forterm.Studio.Services;

public enum TransferKind { Upload, Download, Operation }

/// <summary>File actions of one workspace's file source (SFTP or FTP). Everything that changes a
/// remote or local file goes through the connection's serial <see cref="TransferQueue"/>. Transfers
/// and saves are re-verified before commit by Core's SafeTransfers / RemoteFileOperations; deletes
/// re-check each item's type and size against what the user confirmed. Remote names are validated
/// with <see cref="LocalName"/> before they become local paths.
/// Folder upload, folder download and recursive delete are planned first (at most
/// <see cref="MaxItems"/> items, <see cref="MaxDepth"/> levels), confirmed, then executed in order;
/// a failure or cancel stops the rest and reports how far it got. Symbolic links are never followed.</summary>
public sealed class FileActions(IRemoteFileSource source, TransferQueue queue, IStudioInteraction ui, DirectoryLister list, Action<string> refresh, string title, SharedSshSession? shell = null)
{
    public const int MaxItems = 2000, MaxDepth = 32;
    readonly SafeTransfers safe = new();
    readonly RemoteFileOperations ops = new();
    readonly PermissionService permissions = new();
    readonly SudoPermissionService privileged = new();

    public event Action<Guid, TransferKind>? Queued;
    /// <summary>Target of "下载到桌面" (tests point it at a temporary folder).</summary>
    public string DesktopDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public IRemoteFileSource Source => source;
    /// <summary>Asked before a command channel is opened on the connection (see <see cref="CommandChannels"/>).</summary>
    public CommandChannels? Commands { get; set; }
    /// <summary>sudo needs the file view and the exec channel on the same authenticated SSH transport.</summary>
    public bool SudoAvailable => source is SftpLease { SupportsSameSshExec: true };

    SftpLease? Sftp => source as SftpLease;
    FtpFileSession? Ftp => source as FtpFileSession;

    TransferTicket Enqueue(TransferKind kind, string name, string directory, Func<IProgress<TransferProgress>, CancellationToken, Task> run, string? refreshPath)
    {
        var ticket = queue.Add(name, $"{title} · {directory}", async (progress, ct) =>
        {
            try { await run(progress, ct); }
            finally { if (refreshPath != null) refresh(refreshPath); }
        }, source.Closed);
        Queued?.Invoke(ticket.Id, kind);
        return ticket;
    }

    public static string Join(string directory, string name) => directory == "/" ? "/" + name : directory.TrimEnd('/') + "/" + name;

    static string Parent(string path) => path.LastIndexOf('/') is var i and > 0 ? path[..i] : "/";

    /// <summary>Remote names typed by the user: one path segment, no control characters.</summary>
    public static string ValidName(string? name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 255 || name is "." or ".." || name.IndexOfAny(['/', '\\', '\0']) >= 0 || name.Any(char.IsControl))
            throw new InvalidDataException("名称不能为空，不能是 . 或 ..，不能包含 / \\ 或控制字符，最长 255 个字符");
        return name;
    }

    static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0.0} KB",
        _ => bytes + " B",
    };

    async Task<bool> ExistsAsync(string directory, string name, CancellationToken ct) =>
        (await list(directory, true, ct)).Any(e => e.Name == name);

    // ---------- upload ----------

    /// <summary>Queues one upload per file. Existing targets need explicit confirmation and an
    /// atomic-replace capable SFTP server; FTP never overwrites. Returns how many were queued.</summary>
    public async Task<int> UploadFilesAsync(string remoteDirectory, IReadOnlyList<string> localFiles, CancellationToken ct)
    {
        int queued = 0;
        foreach (string local in localFiles)
        {
            string name = ValidName(Path.GetFileName(local));
            var input = await safe.CaptureLocalAsync(local, ct);
            if (input.Stamp is not { Directory: false }) throw new IOException($"“{name}”不是普通文件，未上传");
            if (Sftp is { } sftp)
            {
                var directory = await safe.CaptureDirectoryAsync(sftp, remoteDirectory, ct);
                var target = await safe.CaptureRemoteAsync(directory, name, ct);
                bool replace = false;
                if (target.Stamp is { } existing)
                {
                    if (existing.Directory || existing.Link || !existing.Regular) throw new IOException($"“{name}”在远程是目录或链接，未覆盖");
                    if (!sftp.Client.SupportsAtomicReplacement) throw new IOException($"“{name}”已存在，服务器不支持安全覆盖；请改名后上传");
                    if (!await ui.ConfirmAsync("覆盖远程文件", $"{Join(directory.Path, name)} 已存在（{Size(existing.Length)}）。\n提交前会再次核对版本，发现变化即停止。", "覆盖", danger: true)) continue;
                    replace = true;
                }
                var plan = new UploadPlan(directory, target, input, replace);
                Enqueue(TransferKind.Upload, name, directory.Path, (p, t) => safe.UploadAsync(plan, p, t), remoteDirectory);
            }
            else if (Ftp is { } ftp)
            {
                var directory = await ftp.CaptureDirectoryAsync(remoteDirectory, ct);
                var target = await ftp.CaptureAsync(directory, name, ct);
                if (target.Stamp != null) throw new IOException($"“{name}”已存在：FTP 没有安全覆盖，请改名后上传");
                Enqueue(TransferKind.Upload, name, directory.Path, async (p, t) => await ftp.UploadAsync(new FtpUploadPlan(directory, target, input), p, t), remoteDirectory);
            }
            else throw new NotSupportedException("来源协议不支持上传");
            queued++;
        }
        return queued;
    }

    sealed record LocalTree(List<string> Directories, List<(string Relative, string Local, long Length)> Files, int Skipped);

    static LocalTree PlanLocal(string root)
    {
        var tree = new LocalTree(new(), new(), 0);
        int skipped = 0;
        void Walk(string dir, string relative, int depth)
        {
            if (depth > MaxDepth) throw new IOException($"本地目录超过 {MaxDepth} 层，未上传");
            foreach (string entry in Directory.EnumerateFileSystemEntries(dir).Order(StringComparer.Ordinal))
            {
                if (tree.Directories.Count + tree.Files.Count >= MaxItems) throw new IOException($"本地目录超过 {MaxItems} 项，未上传");
                var info = new FileInfo(entry);
                string rel = relative.Length == 0 ? info.Name : relative + "/" + info.Name;
                ValidName(info.Name);
                if (info.LinkTarget != null) { skipped++; continue; }
                if (info.Attributes.HasFlag(FileAttributes.Directory)) { tree.Directories.Add(rel); Walk(entry, rel, depth + 1); }
                else tree.Files.Add((rel, entry, info.Length));
            }
        }
        Walk(root, "", 1);
        return tree with { Skipped = skipped };
    }

    /// <summary>Creates <c>remoteDirectory/folderName</c> (must not exist) and uploads the tree into it.</summary>
    public async Task<bool> UploadFolderAsync(string remoteDirectory, string localFolder, CancellationToken ct)
    {
        string name = ValidName(Path.GetFileName(Path.TrimEndingDirectorySeparator(localFolder)));
        var tree = await Task.Run(() => PlanLocal(localFolder), ct);
        if (await ExistsAsync(remoteDirectory, name, ct)) throw new IOException($"{Join(remoteDirectory, name)} 已存在；为避免覆盖，请先改名或删除后再上传");
        long total = tree.Files.Sum(f => f.Length);
        string skipped = tree.Skipped > 0 ? $"\n跳过 {tree.Skipped} 个符号链接（不跟随）。" : "";
        if (!await ui.ConfirmAsync("上传文件夹", $"上传“{name}”到 {remoteDirectory}：{tree.Files.Count} 个文件（{Size(total)}）、{tree.Directories.Count} 个子目录。{skipped}\n中途失败或取消时，已上传的部分保留在远程。", "上传", danger: false)) return false;
        string root = Join(remoteDirectory, name);
        Enqueue(TransferKind.Upload, name + "/", remoteDirectory, async (progress, ct2) =>
        {
            int done = 0;
            try
            {
                await MkdirCoreAsync(remoteDirectory, name, ct2);
                foreach (string dir in tree.Directories) await MkdirCoreAsync(Parent(Join(root, dir)), Path.GetFileName(dir), ct2);
                long before = 0;
                foreach (var file in tree.Files)
                {
                    ct2.ThrowIfCancellationRequested();
                    var step = new Offset(progress, before, total, $"{done + 1}/{tree.Files.Count}");
                    await UploadNewCoreAsync(Parent(Join(root, file.Relative)), file.Local, step, ct2);
                    before += file.Length;
                    done++;
                }
            }
            catch (CommitUncertainException e) { throw new CommitUncertainException($"已上传 {done}/{tree.Files.Count} 个文件；下一个结果未确认：{e.Message}", e); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new IOException($"已上传 {done}/{tree.Files.Count} 个文件后停止：{e.Message}", e);
            }
        }, remoteDirectory);
        return true;
    }

    async Task UploadNewCoreAsync(string remoteDirectory, string local, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        string name = Path.GetFileName(local);
        var input = await safe.CaptureLocalAsync(local, ct);
        if (Sftp is { } sftp)
        {
            var directory = await safe.CaptureDirectoryAsync(sftp, remoteDirectory, ct);
            var target = await safe.CaptureRemoteAsync(directory, name, ct);
            if (target.Stamp != null) throw new IOException($"{Join(directory.Path, name)} 已存在，未覆盖");
            await safe.UploadAsync(new UploadPlan(directory, target, input, false), progress, ct);
        }
        else if (Ftp is { } ftp)
        {
            var directory = await ftp.CaptureDirectoryAsync(remoteDirectory, ct);
            var target = await ftp.CaptureAsync(directory, name, ct);
            if (target.Stamp != null) throw new IOException($"{Join(directory.Path, name)} 已存在，未覆盖");
            await ftp.UploadAsync(new FtpUploadPlan(directory, target, input), progress, ct);
        }
    }

    // ---------- download ----------

    public async Task<bool> DownloadFileAsync(FileNodeViewModel node, string localPath, CancellationToken ct)
    {
        var target = await safe.CaptureLocalAsync(localPath, ct);
        bool replace = false;
        if (target.Stamp is { } existing)
        {
            if (existing.Directory) throw new IOException("本地目标是目录，未下载");
            if (!await ui.ConfirmAsync("替换本地文件", $"{localPath} 已存在（{Size(existing.Length)}）。\n下载完成并校验前，旧文件保持不动。", "替换", danger: true)) return false;
            replace = true;
        }
        if (Sftp is { } sftp)
        {
            var directory = await safe.CaptureDirectoryAsync(sftp, node.ParentPath, ct);
            var input = await safe.CaptureRemoteAsync(directory, node.Name, ct);
            if (input.Stamp is not { Regular: true, Link: false }) throw new IOException("只能下载普通文件；链接和特殊文件不跟随");
            var plan = new DownloadPlan(directory, input, target, replace);
            Enqueue(TransferKind.Download, node.Name, directory.Path, (p, t) => safe.DownloadAsync(plan, p, t), null);
        }
        else if (Ftp is { } ftp)
        {
            var directory = await ftp.CaptureDirectoryAsync(node.ParentPath, ct);
            var input = await ftp.CaptureAsync(directory, node.Name, ct);
            if (input.Stamp is not { Type: FtpObjectType.File }) throw new IOException("只能下载普通文件；链接和特殊文件不跟随");
            var plan = new FtpDownloadPlan(directory, input, target, replace);
            Enqueue(TransferKind.Download, node.Name, directory.Path, (p, t) => ftp.DownloadAsync(plan, p, t), null);
        }
        return true;
    }

    sealed record RemoteTree(List<string> Directories, List<(string Relative, string Remote, long Length)> Files, List<RemoteEntry> PostOrder, int Skipped,
        Dictionary<string, RemoteDirectorySnapshot> Snapshots);

    /// <summary>Lists a remote directory tree without following links. With <paramref name="snapshots"/>
    /// each directory is also captured (before it is listed) so a later job can tell whether it is
    /// still the directory the user confirmed.</summary>
    async Task<RemoteTree> PlanRemoteAsync(string root, CancellationToken ct, bool snapshots = false)
    {
        var tree = new RemoteTree(new(), new(), new(), 0, new(StringComparer.Ordinal));
        int skipped = 0, items = 0;
        async Task Walk(string dir, string relative, int depth)
        {
            if (depth > MaxDepth) throw new IOException($"目录超过 {MaxDepth} 层，已停止规划，未做任何修改");
            if (snapshots) tree.Snapshots[dir] = await ops.DirectoryAsync(source, dir, ct);
            foreach (var entry in FileNodeViewModel.Sort(await list(dir, true, ct)))
            {
                if (++items > MaxItems) throw new IOException($"目录超过 {MaxItems} 项，已停止规划，未做任何修改");
                string rel = relative.Length == 0 ? entry.Name : relative + "/" + entry.Name;
                if (entry.Directory && !entry.Link)
                {
                    tree.Directories.Add(rel);
                    await Walk(entry.Path, rel, depth + 1);
                }
                else if (entry.Regular && !entry.Link) tree.Files.Add((rel, entry.Path, entry.Size));
                else skipped++;
                tree.PostOrder.Add(entry);
            }
        }
        await Walk(root, "", 1);
        return tree with { Skipped = skipped };
    }

    static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM\u00B9", "COM\u00B2", "COM\u00B3",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT\u00B9", "LPT\u00B2", "LPT\u00B3",
    };
    static readonly char[] WindowsInvalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>A remote name is never trusted as a local path. Refuses names that Windows would
    /// treat as rooted or drive-relative (<c>C:x</c>), alternate data streams (<c>:</c>), characters
    /// invalid in Windows names, control characters, <c>.</c>/<c>..</c>, reserved device names and a
    /// trailing dot or space. Applied on every platform so behaviour matches the Windows target.</summary>
    public static string LocalName(string name)
    {
        string stem = name.Split('.')[0].TrimEnd(' ');
        if (name.Length == 0 || name.Length > 255 || name is "." or ".." || Path.IsPathRooted(name) ||
            name.IndexOfAny(WindowsInvalid) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Any(char.IsControl) ||
            name.EndsWith('.') || name.EndsWith(' ') || ReservedDeviceNames.Contains(stem))
            throw new IOException($"远程名称“{WorkspaceViewModel.Short(new string(name.Select(c => char.IsControl(c) ? '?' : c).ToArray()))}”不能安全地用作本机文件名；已停止，没有写入本机。请先在服务器上改名。");
        return name;
    }

    /// <summary><paramref name="relative"/> ("a/b/c", remote names) under <paramref name="root"/>;
    /// each part passes <see cref="LocalName"/> and the full path must stay inside the root.</summary>
    public static string LocalChild(string root, string relative)
    {
        var parts = relative.Split('/');
        foreach (string part in parts) LocalName(part);
        string baseFull = Path.GetFullPath(root);
        // A filesystem root ("D:\\", "/", a share root) already ends with a separator.
        string prefix = Path.EndsInDirectorySeparator(baseFull) ? baseFull : baseFull + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine([baseFull, .. parts]));
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(prefix, comparison) || full.Length == prefix.Length) throw new IOException("本机目标路径越出所选目录，已停止，没有写入。");
        return full;
    }

    /// <summary>Downloads a remote folder into <c>localParent/name</c> (must not exist).</summary>
    public async Task<bool> DownloadFolderAsync(FileNodeViewModel node, string localParent, CancellationToken ct)
    {
        string localRoot = LocalChild(localParent, node.Name);
        if (Path.Exists(localRoot)) throw new IOException($"{localRoot} 已存在；为避免覆盖，请选择其他位置");
        var tree = await PlanRemoteAsync(node.Path, ct);
        // Every local path is computed (and refused) before anything is confirmed or written.
        var localDirectories = tree.Directories.Select(d => LocalChild(localRoot, d)).ToList();
        var localFiles = tree.Files.Select(f => (f.Remote, f.Length, Local: LocalChild(localRoot, f.Relative))).ToList();
        long total = tree.Files.Sum(f => f.Length);
        string skipped = tree.Skipped > 0 ? $"\n跳过 {tree.Skipped} 个链接或特殊文件（不跟随）。" : "";
        if (!await ui.ConfirmAsync("下载文件夹", $"下载 {node.Path} 到 {localRoot}：{tree.Files.Count} 个文件（{Size(total)}）、{tree.Directories.Count} 个子目录。{skipped}", "下载", danger: false)) return false;
        Enqueue(TransferKind.Download, node.Name + "/", node.ParentPath, async (progress, ct2) =>
        {
            int done = 0;
            try
            {
                // Queued jobs run later: a folder that appeared meanwhile is never merged into.
                if (Path.Exists(localRoot)) throw new IOException($"{localRoot} 已存在，未合并写入");
                Directory.CreateDirectory(localRoot);
                foreach (string dir in localDirectories) Directory.CreateDirectory(dir);
                long before = 0;
                foreach (var file in localFiles)
                {
                    ct2.ThrowIfCancellationRequested();
                    var step = new Offset(progress, before, total, $"{done + 1}/{tree.Files.Count}");
                    await DownloadNewCoreAsync(file.Remote, file.Local, step, ct2);
                    before += file.Length;
                    done++;
                }
            }
            catch (CommitUncertainException e) { throw new CommitUncertainException($"已下载 {done}/{tree.Files.Count} 个文件；下一个结果未确认：{e.Message}", e); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new IOException($"已下载 {done}/{tree.Files.Count} 个文件后停止：{e.Message}", e);
            }
        }, null);
        return true;
    }

    async Task DownloadNewCoreAsync(string remote, string local, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        var target = await safe.CaptureLocalAsync(local, ct);
        if (target.Stamp != null) throw new IOException(local + " 已存在，未覆盖");
        string name = Path.GetFileName(remote);
        if (Sftp is { } sftp)
        {
            var directory = await safe.CaptureDirectoryAsync(sftp, Parent(remote), ct);
            var input = await safe.CaptureRemoteAsync(directory, name, ct);
            await safe.DownloadAsync(new DownloadPlan(directory, input, target, false), progress, ct);
        }
        else if (Ftp is { } ftp)
        {
            var directory = await ftp.CaptureDirectoryAsync(Parent(remote), ct);
            var input = await ftp.CaptureAsync(directory, name, ct);
            await ftp.DownloadAsync(new FtpDownloadPlan(directory, input, target, false), progress, ct);
        }
    }

    // ---------- create / rename / delete ----------

    async Task MkdirCoreAsync(string parent, string name, CancellationToken ct)
    {
        var directory = await ops.DirectoryAsync(source, parent, ct);
        await ops.CreateDirectoryAsync(directory, name, ct);
    }

    public void CreateFolder(string parent, string name)
    {
        name = ValidName(name);
        Enqueue(TransferKind.Operation, name + " · 新建目录", parent, (_, ct) => MkdirCoreAsync(parent, name, ct), parent);
    }

    /// <summary>Empty file, created exclusively: an existing name is never replaced.</summary>
    public void CreateFile(string parent, string name)
    {
        name = ValidName(name);
        Enqueue(TransferKind.Operation, name + " · 新建文件", parent, async (progress, ct) =>
        {
            var directory = await ops.DirectoryAsync(source, parent, ct);
            await ops.WriteNewAsync(directory, name, ReadOnlyMemory<byte>.Empty, progress, ct);
        }, parent);
    }

    public void Rename(FileNodeViewModel node, string newName)
    {
        newName = ValidName(newName);
        if (newName == node.Name) return;
        Enqueue(TransferKind.Operation, node.Name + " → " + newName, node.ParentPath, async (_, ct) =>
        {
            var directory = await ops.DirectoryAsync(source, node.ParentPath, ct);
            var file = await ops.CaptureNodeAsync(directory, node.Name, ct);
            await ops.RenameAsync(file, newName, ct);
        }, node.ParentPath);
    }

    /// <summary>Files and links: one confirmed removal. Directories: planned post-order removal
    /// of everything inside, then the directory itself; each item is re-captured before removal.</summary>
    public async Task<bool> DeleteAsync(FileNodeViewModel node, CancellationToken ct)
    {
        bool ftp = Ftp != null;
        if (!node.IsDirectory || node.IsLink)
        {
            if (ftp && node.IsLink) throw new NotSupportedException("FTP 不删除链接；请在服务器上处理");
            if (!await ui.ConfirmAsync("删除", $"删除 {node.Path}？{(node.IsLink ? "\n只删除链接本身，不影响链接目标。" : "")}\n删除不能撤销。", "删除", danger: true)) return false;
            var expect = Expect(node.Path, directory: false, link: node.IsLink, regular: node.IsRegular, size: node.IsRegular ? node.Size : null);
            Enqueue(TransferKind.Operation, node.Name + " · 删除", node.ParentPath, async (progress, ct2) =>
            {
                try
                {
                    var directory = await ops.DirectoryAsync(source, node.ParentPath, ct2);
                    await ops.RemoveNodeByMetadataAsync(directory, node.Name, expect, progress, ct2);
                }
                catch (OperationCanceledException e) when (!ct2.IsCancellationRequested) { throw new IOException("删除未完成（超时或连接已停止）：" + e.Message, e); }
            }, node.ParentPath);
            return true;
        }
        var tree = await PlanRemoteAsync(node.Path, ct, snapshots: !ftp);
        int total = tree.PostOrder.Count + 1;
        int links = tree.PostOrder.Count(e => !e.Directory && !e.Regular || e.Link);
        // FTP listings do not reliably mark links (DOS/IIS formats, servers hiding symlinks), so a
        // recursive FTP delete could follow one: only an empty FTP directory is removed.
        if (ftp && tree.PostOrder.Count > 0) throw new NotSupportedException("FTP 只删除空目录：FTP 列表不能可靠区分链接，不做递归删除。请先删除其中内容，或改用 SFTP；未做任何修改");
        if (!await ui.ConfirmAsync("删除目录",
            $"删除 {node.Path} 及其中全部内容：{tree.Files.Count} 个文件、{tree.Directories.Count} 个子目录" + (links > 0 ? $"、{links} 个链接或特殊项" : "") + $"，共 {total} 项。\n" +
            "链接只删除链接本身，不跟随。删除不能撤销；中途失败或取消时，已删除的项不会恢复。",
            "全部删除", danger: true)) return false;
        // What the user confirmed: each entry's type (no-follow) and, for regular files, size as listed.
        // The serial queue may run this much later; an entry that differs is left alone and the rest stops.
        var order = tree.PostOrder.Select(e => (Parent: Parent(e.Path), e.Name, Check: Expect(e.Path, e.Directory && !e.Link, e.Link, e.Regular && !e.Link, e.Regular && !e.Link ? e.Size : null)))
            .Append((Parent: node.ParentPath, node.Name, Check: Expect(node.Path, true, false, false, null))).ToList();
        var planned = tree.Snapshots;
        Enqueue(TransferKind.Operation, node.Name + "/ · 删除", node.ParentPath, async (progress, ct2) =>
        {
            int done = 0;
            var verified = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var (parent, name, check) in order)
                {
                    ct2.ThrowIfCancellationRequested();
                    RemoteDirectorySnapshot directory;
                    if (planned.TryGetValue(parent, out var snapshot))
                    {
                        // A directory of the confirmed tree: on first use it must still have the
                        // modification time captured at planning. Its mtime only changes once its own
                        // entries change, and nothing has been removed from it before this point, so a
                        // difference means it was replaced or its contents changed after confirmation.
                        if (verified.Add(parent))
                        {
                            var now = await ops.DirectoryAsync(source, parent, ct2);
                            if (snapshot.Modified is { } then && now.Modified != then) throw new IOException($"{parent} 在确认后已变化（目录被替换或内容有增减），已停止");
                        }
                        directory = snapshot;
                    }
                    else directory = await ops.DirectoryAsync(source, parent, ct2); // the root's parent: only the root is removed there
                    // Core re-checks the parent for every item: owner/group/mode as captured, and no
                    // symlinked path component.
                    await ops.RemoveNodeByMetadataAsync(directory, name, check, new Progress<TransferProgress>(), ct2);
                    progress.Report(new(++done, total, $"已删除 {done}/{total}", "项"));
                }
            }
            catch (OperationCanceledException) when (ct2.IsCancellationRequested) { throw; }
            catch (CommitUncertainException e) { throw new CommitUncertainException($"已删除 {done}/{total} 项；下一项结果未确认：{e.Message}", e); }
            catch (Exception e) { throw new IOException($"已删除 {done}/{total} 项后停止：{e.Message}", e); }
        }, node.ParentPath);
        return true;
    }

    // ---------- editor, permissions, archive, desktop ----------

    /// <summary>Opens a remote UTF-8 text file (≤ 1 MiB, regular file, no links) for the built-in editor.</summary>
    public async Task<IRemoteTextDocument> OpenTextAsync(FileNodeViewModel node, CancellationToken ct)
    {
        if (Sftp is { } sftp)
        {
            var directory = await safe.CaptureDirectoryAsync(sftp, node.ParentPath, ct);
            return new SftpTextDocument(await safe.OpenTextAsync(directory, node.Name, ct));
        }
        if (Ftp is { } ftp)
        {
            var directory = await ftp.CaptureDirectoryAsync(node.ParentPath, ct);
            return new FtpTextDocument(await ftp.OpenTextAsync(directory, node.Name, ct));
        }
        throw new NotSupportedException("来源协议不支持在线编辑");
    }

    public Task<PermissionSnapshot> InspectPermissionsAsync(string path, CancellationToken ct) => permissions.InspectAsync(source, path, ct);

    /// <summary>Plans (≤ 2000 items / 32 levels), confirms, then applies item by item. Special bits
    /// (setuid/setgid/sticky) and owners are kept; a changed item stops the rest without rollback.</summary>
    public async Task<bool> ChangePermissionsAsync(FileNodeViewModel node, uint mode, bool recursive, uint? fileMode, CancellationToken ct, bool useSudo = false)
    {
        var plan = await permissions.PreviewAsync(source, node.Path, recursive && node.IsDirectory, mode, fileMode, ct);
        if (useSudo) return await ChangePermissionsWithSudoAsync(node, plan, mode, ct);
        int changing = plan.Items.Count(i => !i.Skip && i.Mode != i.Before.Mode);
        string scope = recursive && node.IsDirectory
            ? $"目录设为 {Octal(mode)}，文件{(fileMode is { } f ? "设为 " + Octal(f) : "保持不变")}；共 {plan.Items.Count} 项，实际变化 {changing} 项，跳过 {plan.Skipped} 个链接或特殊节点。"
            : $"设为 {Octal(mode)}（原为 {Octal(plan.Items[0].Before.Mode & 511u)}）。";
        if (!await ui.ConfirmAsync("修改权限", $"{node.Path}\n{scope}\n保留特殊位、不改所有者；逐项复核，发现变化即停止，已完成的不回滚。", "修改", danger: recursive)) return false;
        Enqueue(TransferKind.Operation, node.Name + " · 权限 " + Octal(mode), node.ParentPath, (p, t) => permissions.ApplyAsync(plan, p, t), node.ParentPath);
        return true;
    }

    /// <summary>sudo path (alpha.36 parity): the Core service first reads each item's device/inode
    /// identity over exec (no-follow) and checks it matches the file view, asks sudo's policy, and at
    /// apply time re-checks every item. The password is requested only after sudo prints this run's
    /// random prompt, written to that exec's stdin and never stored.</summary>
    async Task<bool> ChangePermissionsWithSudoAsync(FileNodeViewModel node, PermissionPlan plan, uint mode, CancellationToken ct)
    {
        if (!SudoAvailable) throw new NotSupportedException("sudo 只用于经 SSH 的 SFTP 文件区");
        // Reading the items' identities is already a command.
        if (Commands is { } commands && !await commands.AllowAsync("sudo 修改权限", "核对文件和 sudo 的命令")) return false;
        var approved = await privileged.InspectAsync(plan, ct);
        int count = plan.Items.Count(i => !i.Skip);
        var notes = approved.Notes.Take(6).Select(n => "· " + n).ToList();
        if (approved.Notes.Count > 6) notes.Add($"· 另有 {approved.Notes.Count - 6} 条");
        string message = $"{node.Path}\n以 root 身份设为 {Octal(mode)}，共 {count} 项。\n{string.Join("\n", notes)}\n\n" +
                         "确认这台 SSH 服务器上的路径就是文件区看到的同一文件系统。逐项复核身份与权限，发现变化即停止，已完成的不回滚。";
        if (!await ui.ConfirmAsync("sudo 修改权限", message, "以 sudo 修改", danger: true)) return false;
        Enqueue(TransferKind.Operation, node.Name + " · sudo 权限 " + Octal(mode), node.ParentPath, (p, t) => { Commands?.Opening(); return privileged.ApplyAsync(approved, true, AskSudoPasswordAsync, p, t); }, node.ParentPath);
        return true;
    }

    async Task<byte[]?> AskSudoPasswordAsync(CancellationToken ct)
    {
        var answers = await ui.AskCredentialsAsync(new CredentialChallenge(1, false, true,
            "sudo 在独立命令通道里出现了本次随机提示。密码只写入这条命令的输入，不保存，也不使用登录密码。",
            [new CredentialQuestion("本次 sudo 密码", false)]), ct);
        if (answers is not { Length: > 0 }) return null;
        try { return System.Text.Encoding.UTF8.GetBytes(answers[0]); }
        finally { Array.Clear(answers); }
    }

    /// <summary>Type (no-follow, link or not) and, for regular files, size must still match what was
    /// listed when the user confirmed. The listing has no modification time, so a same-size
    /// replacement of a regular file is not detected; content is never read for a delete.</summary>
    static Func<RemoteNodeStat, string?> Expect(string path, bool directory, bool link, bool regular, long? size) => now =>
        now.Directory != directory || now.Link != link || now.Regular != regular || size is { } expected && now.Length != expected
            ? $"{path} 在确认后已变化（类型或大小不同），未删除它" : null;

    public static string Octal(uint mode) => Convert.ToString(mode & 511u, 8).PadLeft(3, '0');

    static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    /// <summary>"压缩": tar.gz next to the item, on a separate exec channel (never the terminal).
    /// The archive is opened with noclobber first, so an existing file is never replaced or removed;
    /// a failed tar removes only the archive this command created.</summary>
    public async Task<bool> CompressAsync(FileNodeViewModel node, CancellationToken ct)
    {
        if (shell == null) throw new NotSupportedException("压缩需要 SSH 会话；FTP 不能在服务器上执行命令");
        string archive = ValidName(node.Name + ".tar.gz");
        if (await ExistsAsync(node.ParentPath, archive, ct)) throw new IOException($"{Join(node.ParentPath, archive)} 已存在；为避免覆盖，请先改名或删除");
        if (!await ui.ConfirmAsync("压缩", $"在服务器上用 tar 把 {node.Path} 打包为 {Join(node.ParentPath, archive)}。\n使用独立的命令通道执行，不会在终端里输入；符号链接按链接本身打包。", "压缩", danger: false)) return false;
        if (Commands is { } commands && !await commands.AllowAsync("压缩", "tar 打包命令")) return false;
        string command = $"cd -- {Quote(node.ParentPath)} && set -C && exec 3> {Quote(archive)} && {{ tar -czf - -- {Quote(node.Name)} >&3 || {{ s=$?; rm -f -- {Quote(archive)}; exit $s; }}; }}";
        var session = shell;
        Enqueue(TransferKind.Operation, archive + " · 压缩", node.ParentPath, async (progress, t) =>
        {
            progress.Report(new(0, null, "服务器打包中"));
            Commands?.Opening();
            var result = await session.ExecAsync(command, TimeSpan.FromMinutes(10), t, 256 * 1024);
            if (result.ExitStatus != 0) throw new IOException("tar 失败：" + WorkspaceViewModel.Short(result.Error.Trim().Length > 0 ? result.Error.Trim() : "退出码 " + result.ExitStatus));
        }, node.ParentPath);
        return true;
    }

    /// <summary>"下载到桌面": no picker; a free name is chosen so nothing on the desktop is replaced.</summary>
    public async Task<string?> DownloadToDesktopAsync(FileNodeViewModel node, CancellationToken ct)
    {
        string chosen = Appearance.Current.DownloadFolder, folder = chosen.Length > 0 ? chosen : DesktopDirectory;
        if (folder.Length == 0 || !Directory.Exists(folder)) throw new IOException(chosen.Length > 0 ? $"找不到默认下载目录 {chosen}，请在系统配置中修改" : "找不到本机桌面目录");
        if (node.IsDirectory && !node.IsLink) return await DownloadFolderAsync(node, folder, ct) ? LocalChild(folder, node.Name) : null;
        string target = FreeName(folder, LocalName(node.Name));
        return await DownloadFileAsync(node, target, ct) ? target : null;
    }

    static string FreeName(string folder, string name)
    {
        string candidate = LocalChild(folder, name);
        string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
        for (int i = 1; Path.Exists(candidate) && i < 1000; i++) candidate = LocalChild(folder, $"{stem} ({i}){ext}");
        if (Path.Exists(candidate)) throw new IOException("同名文件过多，未下载");
        return candidate;
    }

    /// <summary>Per-file progress folded into the whole folder transfer.</summary>
    sealed class Offset(IProgress<TransferProgress> inner, long before, long total, string step) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => inner.Report(new(before + value.Bytes, total, step + " · " + value.Phase));
    }
}
