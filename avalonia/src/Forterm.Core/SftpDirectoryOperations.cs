using Renci.SshNet.Common;
namespace Forterm.Core;

public sealed partial class SafeTransfers
{
    public async Task<RemoteFileVersion> CaptureNodeAsync(RemoteDirectoryBinding directory, string name, CancellationToken ct)
    {
        await CheckDirectory(directory, ct); string path = directory.Path.TrimEnd('/') + "/" + Name(name); var stamp = await Stat(directory.Source, path, ct);
        if (stamp == null) throw new IOException("节点不存在");
        if (stamp.Link || !stamp.Directory && !stamp.Regular) throw new IOException("不跟随链接或操作特殊节点");
        return stamp.Directory ? new(path, stamp, null) : await CaptureRemoteAsync(directory, name, ct);
    }
    async Task CheckNode(RemoteDirectoryBinding directory, RemoteFileVersion expected, CancellationToken ct)
    {
        if (expected.Stamp is not { Directory: true, Link: false }) { await CheckRemote(directory, expected, ct); return; }
        string path = directory.Path.TrimEnd('/') + "/" + Name(Path.GetFileName(expected.Path)); if (path != expected.Path) throw new IOException("节点不属于原目录");
        await CheckDirectory(directory, ct); if (await Stat(directory.Source, path, ct) != expected.Stamp) throw new IOException("目录节点在操作前发生变化");
    }
    public async Task CreateDirectoryAsync(RemoteDirectoryBinding parent, string name, CancellationToken ct)
    {
        await CheckDirectory(parent, ct); string path = parent.Path.TrimEnd('/') + "/" + Name(name);
        if (await Stat(parent.Source, path, ct) != null) throw new IOException("目录名称已存在，未覆盖"); ct.ThrowIfCancellationRequested(); Available(parent);
        using var commit = new CancellationTokenSource(options.MetadataTimeout);
        try { await parent.Source.Client.MakeDirectoryRawAsync(path, commit.Token); }
        catch (SftpException e) { throw new IOException("服务器明确拒绝创建目录", e); }
        catch (Exception e) { try { parent.Source.Dispose(); } catch { } throw new CommitUncertainException("创建目录回执未确认，原文件通道已停止；请核对路径", e); }
        try { var stamp = await Stat(parent.Source, path, commit.Token); if (stamp is not { Directory: true, Link: false, Mode: 448 }) throw new IOException(); }
        catch (Exception e) { throw new CommitUncertainException("目录创建回执已确认，但0700权限/目录核验未完成，请核对", e); }
    }
    /// <summary>Additive (Studio recursive delete): removes one node verified by its lstat metadata
    /// instead of a content hash. The parent is re-checked component by component (no symlinked
    /// component), the node's lstat must satisfy <paramref name="verify"/>, then it is removed without
    /// following: a link is unlinked, a directory must be empty. Commit outcome follows RemoveVerifiedAsync.</summary>
    internal async Task RemoveNodeByMetadataAsync(RemoteDirectoryBinding directory, string name, Func<RemoteNodeStat, string?> verify, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        await CheckDirectory(directory, ct); string path = directory.Path.TrimEnd('/') + "/" + Name(name);
        // The read-only keepalive probes the last verified directory; point it at this (surviving)
        // parent so it never probes a directory this delete is about to remove.
        directory.Source.VerifiedDirectory(directory.Path);
        var stamp = await Stat(directory.Source, path, ct) ?? throw new IOException("节点不存在，未删除");
        bool isDirectory = stamp is { Directory: true, Link: false };
        if (verify(new(isDirectory, stamp.Link, stamp.Regular && !stamp.Link, stamp.Length, stamp.Modified)) is { } refused) throw new IOException(refused);
        ct.ThrowIfCancellationRequested(); Available(directory); progress.Report(new(0, null, "提交中"));
        using var commit = new CancellationTokenSource(options.MetadataTimeout);
        try { if (isDirectory) await directory.Source.Client.RemoveDirectoryRawAsync(path, commit.Token); else await directory.Source.Client.RemoveRawAsync(path, commit.Token); }
        catch (SftpException e) { throw new IOException(isDirectory ? "目录不为空或服务器拒绝删除" : "服务器明确拒绝删除，未重试", e); }
        catch (Exception e) { try { directory.Source.Dispose(); } catch { } throw new CommitUncertainException("删除回执未确认，原文件通道已停止；请核对路径", e); }
    }
    public async Task RemoveNodeAsync(RemoteDirectoryBinding directory, RemoteFileVersion expected, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        if (expected.Stamp is not { Directory: true, Link: false }) { await RemoveVerifiedAsync(directory, expected, progress, ct); return; }
        await CheckNode(directory, expected, ct); ct.ThrowIfCancellationRequested(); Available(directory); progress.Report(new(0, null, "提交中"));
        using var commit = new CancellationTokenSource(options.MetadataTimeout);
        try { await directory.Source.Client.RemoveDirectoryRawAsync(expected.Path, commit.Token); }
        catch (SftpException e) { throw new IOException("目录不为空或服务器拒绝删除；没有递归删除", e); }
        catch (Exception e) { try { directory.Source.Dispose(); } catch { } throw new CommitUncertainException("空目录删除回执未确认，请核对路径", e); }
    }
    public async Task RenameNodeAsync(RemoteDirectoryBinding directory, RemoteFileVersion expected, string name, CancellationToken ct)
    {
        if (expected.Stamp is not { Directory: true, Link: false }) { await RenameVerifiedAsync(directory, expected, name, ct); return; }
        await CheckNode(directory, expected, ct); string target = directory.Path.TrimEnd('/') + "/" + Name(name);
        if (target == expected.Path || await Stat(directory.Source, target, ct) != null) throw new IOException("新名称已存在或等于源，未改名"); ct.ThrowIfCancellationRequested(); Available(directory);
        using var commit = new CancellationTokenSource(options.MetadataTimeout);
        try { await directory.Source.Client.RenameRawAsync(expected.Path, target, false, commit.Token); }
        catch (SftpException e) { throw new IOException("服务器明确拒绝目录改名，未覆盖目标", e); }
        catch (Exception e) { try { directory.Source.Dispose(); } catch { } throw new CommitUncertainException("目录改名回执未确认，请核对两个路径", e); }
    }
}
