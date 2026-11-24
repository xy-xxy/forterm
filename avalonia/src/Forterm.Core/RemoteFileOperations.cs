namespace Forterm.Core;

public sealed class RemoteFileSnapshot
{
    public IRemoteFileSource Source { get; }
    public Guid SourceId => Source.SourceId;
    public string Directory { get; }
    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path); public long Length { get; }
    public string Hash { get; }
    public bool IsDirectory { get; }
    internal object Version { get; }
    internal RemoteFileSnapshot(IRemoteFileSource source, string directory, string path, long length, string hash, object version, bool isDirectory = false) { Source = source; Directory = directory; Path = path; Length = length; Hash = hash; Version = version; IsDirectory = isDirectory; }
}
public sealed class RemoteDirectorySnapshot
{
    public IRemoteFileSource Source { get; }
    public Guid SourceId => Source.SourceId; public string Path { get; }
    internal object Binding { get; }
    /// <summary>Additive: the directory's modification time when captured (SFTP); null for FTP.</summary>
    public DateTime? Modified => Binding is RemoteDirectoryBinding sftp ? sftp.Stamp.Modified : null;
    internal RemoteDirectorySnapshot(IRemoteFileSource source, string path, object binding) { Source = source; Path = path; Binding = binding; }
}
public sealed class PartialMoveException(string message, Exception inner) : IOException(message, inner);
/// <summary>No-follow metadata of one node, as seen just before a metadata-verified remove.</summary>
public sealed record RemoteNodeStat(bool Directory, bool Link, bool Regular, long Length, DateTime Modified);
public sealed class RemoteFileOperations
{
    public const int MaxCopyBytes = 16 * 1024 * 1024;
    readonly SafeTransfers safe = new();
    sealed record SftpBound(RemoteDirectoryBinding Directory, RemoteFileVersion File);
    sealed record FtpBound(FtpDirectory Directory, FtpVersion File);
    public async Task<RemoteDirectorySnapshot> DirectoryAsync(IRemoteFileSource source, string path, CancellationToken ct)
    {
        if (source is SftpLease sftp) { var d = await safe.CaptureDirectoryAsync(sftp, path, ct); return new(source, d.Path, d); }
        if (source is FtpFileSession ftp) { var d = await ftp.CaptureDirectoryAsync(path, ct); return new(source, d.Path, d); }
        throw new NotSupportedException("来源协议未支持");
    }
    public async Task<RemoteFileSnapshot> CaptureAsync(RemoteDirectorySnapshot directory, string name, CancellationToken ct, long maxBytes = long.MaxValue)
    {
        if (directory.Binding is RemoteDirectoryBinding sftp)
        {
            var f = await safe.CaptureRemoteAsync(sftp, name, ct, maxBytes); if (f.Stamp == null) throw new IOException("源文件不存在");
            return new(directory.Source, directory.Path, f.Path, f.Stamp.Length, f.Hash!, new SftpBound(sftp, f));
        }
        if (directory.Binding is FtpDirectory ftp)
        {
            var f = await ftp.Source.CaptureAsync(ftp, name, ct, maxBytes); if (f.Stamp == null) throw new IOException("源文件不存在");
            return new(directory.Source, directory.Path, f.Path, f.Stamp.Length, f.Hash!, new FtpBound(ftp, f));
        }
        throw new NotSupportedException();
    }
    public async Task<RemoteFileSnapshot> CaptureNodeAsync(RemoteDirectorySnapshot directory, string name, CancellationToken ct)
    {
        if (directory.Binding is RemoteDirectoryBinding sftp)
        {
            var node = await safe.CaptureNodeAsync(sftp, name, ct); return new(directory.Source, directory.Path, node.Path, node.Stamp!.Length, node.Hash ?? "", new SftpBound(sftp, node), node.Stamp.Directory);
        }
        if (directory.Binding is FtpDirectory ftp)
        {
            var node = await ftp.Source.CaptureNodeAsync(ftp, name, ct); return new(directory.Source, directory.Path, node.Path, node.Stamp!.Length, node.Hash ?? "", new FtpBound(ftp, node), node.Stamp.Type == FluentFTP.FtpObjectType.Directory);
        }
        throw new NotSupportedException();
    }
    Task CreateDirectoryCoreAsync(RemoteDirectorySnapshot parent, string name, CancellationToken ct) => parent.Binding switch
    {
        RemoteDirectoryBinding sftp => safe.CreateDirectoryAsync(sftp, name, ct),
        FtpDirectory ftp => ftp.Source.CreateDirectoryAsync(ftp, name, ct),
        _ => throw new NotSupportedException()
    };
    public Task<byte[]> ReadAsync(RemoteFileSnapshot file, CancellationToken ct) => file.Version switch
    {
        SftpBound value => safe.ReadMemoryAsync(value.Directory, value.File, MaxCopyBytes, ct),
        FtpBound value => value.Directory.Source.ReadBoundedAsync(value.Directory, value.File, MaxCopyBytes, ct),
        _ => throw new NotSupportedException()
    };
    async Task<RemoteFileSnapshot> WriteNewCoreAsync(RemoteDirectorySnapshot directory, string name, ReadOnlyMemory<byte> bytes, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        if (bytes.Length > MaxCopyBytes) throw new IOException("跨来源内存复制限16MiB");
        if (directory.Binding is RemoteDirectoryBinding sftp)
        {
            var target = await safe.CaptureRemoteAsync(sftp, name, ct, MaxCopyBytes); if (target.Stamp != null) throw new IOException("目标已存在，未覆盖");
            var saved = await safe.UploadMemoryNewAsync(sftp, target, bytes, progress, ct); return new(directory.Source, directory.Path, saved.Path, saved.Stamp!.Length, saved.Hash!, new SftpBound(sftp, saved));
        }
        if (directory.Binding is FtpDirectory ftp)
        {
            var target = await ftp.Source.CaptureAsync(ftp, name, ct, MaxCopyBytes); if (target.Stamp != null) throw new IOException("FTP目标已存在，未主动覆盖");
            var saved = await ftp.Source.UploadMemoryNewAsync(ftp, target, bytes, progress, ct); return new(directory.Source, directory.Path, saved.Path, saved.Stamp!.Length, saved.Hash!, new FtpBound(ftp, saved));
        }
        throw new NotSupportedException();
    }
    Task RemoveCoreAsync(RemoteFileSnapshot file, IProgress<TransferProgress> progress, CancellationToken ct) => file.Version switch
    {
        SftpBound v => safe.RemoveNodeAsync(v.Directory, v.File, progress, ct),
        FtpBound v => v.Directory.Source.RemoveNodeAsync(v.Directory, v.File, progress, ct),
        _ => throw new NotSupportedException()
    };
    Task RenameCoreAsync(RemoteFileSnapshot file, string name, CancellationToken ct) => file.Version switch
    {
        SftpBound v => safe.RenameNodeAsync(v.Directory, v.File, name, ct),
        FtpBound v => v.Directory.Source.RenameNodeAsync(v.Directory, v.File, name, ct),
        _ => throw new NotSupportedException()
    };
    sealed class CopyProgress(long bytes, IProgress<TransferProgress> progress) : IProgress<TransferProgress>
    { public void Report(TransferProgress p) => progress.Report(p with { Bytes = bytes, Total = bytes }); }
    public async Task CreateDirectoryAsync(RemoteDirectorySnapshot parent, string name, CancellationToken ct)
    { using var reservation = await FileReservations.AcquireAsync(new[] { parent.Source }, ct); await CreateDirectoryCoreAsync(parent, name, ct); }
    public async Task<RemoteFileSnapshot> WriteNewAsync(RemoteDirectorySnapshot directory, string name, ReadOnlyMemory<byte> bytes, IProgress<TransferProgress> progress, CancellationToken ct)
    { using var reservation = await FileReservations.AcquireAsync(new[] { directory.Source }, ct); return await WriteNewCoreAsync(directory, name, bytes, progress, ct); }
    public async Task RemoveAsync(RemoteFileSnapshot file, IProgress<TransferProgress> progress, CancellationToken ct)
    { using var reservation = await FileReservations.AcquireAsync(new[] { file.Source }, ct); await RemoveCoreAsync(file, progress, ct); }
    /// <summary>Additive: removes <paramref name="name"/> in <paramref name="directory"/> after its no-follow
    /// metadata passes <paramref name="verify"/> (null = proceed, otherwise the refusal message). Unlike
    /// <see cref="RemoveAsync"/> it never reads file content, and on SFTP it unlinks links and special
    /// nodes without following them. Directories must be empty.</summary>
    public async Task RemoveNodeByMetadataAsync(RemoteDirectorySnapshot directory, string name, Func<RemoteNodeStat, string?> verify, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        using var reservation = await FileReservations.AcquireAsync(new[] { directory.Source }, ct);
        if (directory.Binding is RemoteDirectoryBinding sftp) { await safe.RemoveNodeByMetadataAsync(sftp, name, verify, progress, ct); return; }
        if (directory.Binding is FtpDirectory ftp) { await ftp.Source.RemoveNodeByMetadataAsync(ftp, name, verify, progress, ct); return; }
        throw new NotSupportedException("来源协议未支持");
    }
    public async Task RenameAsync(RemoteFileSnapshot file, string name, CancellationToken ct)
    { using var reservation = await FileReservations.AcquireAsync(new[] { file.Source }, ct); await RenameCoreAsync(file, name, ct); }
    public async Task<RemoteCopyTarget> PlanTargetAsync(RemoteDirectorySnapshot directory, string name, CancellationToken ct)
    {
        if (directory.Binding is RemoteDirectoryBinding sftp)
        {
            var target = await safe.CaptureRemoteAsync(sftp, name, ct); return new(directory, name, target.Path, target.Stamp != null, target.Stamp?.Length, target.Stamp?.Modified, sftp.Source.Client.SupportsAtomicReplacement, new SftpBound(sftp, target));
        }
        if (directory.Binding is FtpDirectory ftp)
        {
            var target = await ftp.Source.CaptureAsync(ftp, name, ct); return new(directory, name, target.Path, target.Stamp != null, target.Stamp?.Length, target.Stamp?.Modified, false, new FtpBound(ftp, target));
        }
        throw new NotSupportedException();
    }
    Task<Stream> OpenReadStreamAsync(RemoteFileSnapshot source, CancellationToken ct) => source.Version switch
    {
        SftpBound v => safe.OpenCopyReadAsync(v.Directory, v.File, ct),
        FtpBound v => v.Directory.Source.OpenCopyReadAsync(v.Directory, v.File, ct),
        _ => throw new NotSupportedException()
    };
    Task VerifySourceAsync(RemoteFileSnapshot source, CancellationToken ct) => source.Version switch
    {
        SftpBound v => safe.VerifyCopySourceAsync(v.Directory, v.File, ct),
        FtpBound v => v.Directory.Source.VerifyCopySourceAsync(v.Directory, v.File, ct),
        _ => throw new NotSupportedException()
    };
    async Task<RemoteFileSnapshot> WriteStreamAsync(RemoteFileSnapshot source, RemoteCopyTarget target, Stream input, bool replaceApproved, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        if (target.Version is SftpBound sftp)
        {
            var saved = await safe.UploadCopyStreamAsync(sftp.Directory, sftp.File, input, source.Length, source.Hash, replaceApproved, token => VerifySourceAsync(source, token), progress, ct);
            return new(target.Directory.Source, target.Directory.Path, saved.Path, saved.Stamp!.Length, saved.Hash!, new SftpBound(sftp.Directory, saved));
        }
        if (target.Version is FtpBound ftp)
        {
            var saved = await ftp.Directory.Source.UploadCopyStreamAsync(ftp.Directory, ftp.File, input, source.Length, source.Hash, token => VerifySourceAsync(source, token), progress, ct);
            return new(target.Directory.Source, target.Directory.Path, saved.Path, saved.Stamp!.Length, saved.Hash!, new FtpBound(ftp.Directory, saved));
        }
        throw new NotSupportedException();
    }
    public async Task<RemoteFileSnapshot> CopyAsync(RemoteFileSnapshot source, RemoteDirectorySnapshot destination, string name, bool move, IProgress<TransferProgress> progress, CancellationToken ct)
        => await CopyPlannedAsync(source, await PlanTargetAsync(destination, name, ct), move, false, progress, ct);
    public async Task<RemoteFileSnapshot> CopyPlannedAsync(RemoteFileSnapshot source, RemoteCopyTarget target, bool move, bool replaceApproved, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        if (source.IsDirectory) throw new IOException("目录不递归复制或移动");
        if (source.SourceId == target.Directory.SourceId && source.Path == target.Path) throw new IOException("源与目标相同，未复制或移动");
        if (target.Exists && (!replaceApproved || !target.CanReplace)) throw new IOException("目标已存在；需要明确确认与SFTP原子替换能力，未覆盖");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, source.Source.Closed, target.Directory.Source.Closed);
        using var reservation = await FileReservations.AcquireAsync(new[] { source.Source, target.Directory.Source }, linked.Token);
        bool same = source.Source.Resource.Id == target.Directory.Source.Resource.Id;
        if (move && same)
        {
            progress.Report(new(0, source.Length, "校验源版本"));
            if (source.Version is SftpBound src && target.Version is SftpBound dst)
            {
                await safe.MoveCopySourceAsync(src.Directory, src.File, dst.Directory, dst.File, replaceApproved, progress, linked.Token);
                return new(target.Directory.Source, target.Directory.Path, target.Path, source.Length, source.Hash, new SftpBound(dst.Directory, new(target.Path, src.File.Stamp, src.File.Hash)));
            }
            if (source.Version is FtpBound from && target.Version is FtpBound to)
            {
                var moved = await from.Directory.Source.MoveCopySourceAsync(from.Directory, from.File, to.Directory, to.File, progress, linked.Token);
                return new(target.Directory.Source, target.Directory.Path, target.Path, source.Length, source.Hash, new FtpBound(to.Directory, moved));
            }
        }
        RemoteFileSnapshot result;
        if (same && source.Source.Protocol == WorkspaceProtocol.Ftp)
        {
            if (source.Length > MaxCopyBytes) throw new IOException("同一FTP控制连接复制限16MiB；请明确选择另一条已有认证来源，不会自动登录");
            byte[] bytes = await ReadAsync(source, linked.Token);
            try { result = await WriteNewCoreAsync(target.Directory, target.Name, bytes, progress, linked.Token); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        }
        else
        {
            await using var read = await OpenReadStreamAsync(source, linked.Token);
            result = await WriteStreamAsync(source, target, read, replaceApproved, progress, linked.Token);
        }
        if (result.Hash != source.Hash) throw new CommitUncertainException("目标提交已确认，但与源版本不一致；未删除源，请核对");
        if (!move) return result;
        try { progress.Report(new(result.Length, result.Length, "校验源版本")); linked.Token.ThrowIfCancellationRequested(); await RemoveCoreAsync(source, new CopyProgress(result.Length, progress), linked.Token); }
        catch (Exception e) { throw new PartialMoveException("目标副本已提交；源删除未完成或未确认，请核对两个路径，不自动重试移动", e); }
        return result;
    }
}
public sealed class RemoteCopyTarget
{
    public RemoteDirectorySnapshot Directory { get; }
    public string Name { get; }
    public string Path { get; }
    public bool Exists { get; }
    public long? Length { get; }
    public DateTime? Modified { get; }
    public bool CanReplace { get; }
    internal object Version { get; }
    internal RemoteCopyTarget(RemoteDirectorySnapshot directory, string name, string path, bool exists, long? length, DateTime? modified, bool canReplace, object version)
    { Directory = directory; Name = name; Path = path; Exists = exists; Length = length; Modified = modified; CanReplace = canReplace; Version = version; }
}
