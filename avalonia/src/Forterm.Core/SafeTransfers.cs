using System.Security.Cryptography;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
namespace Forterm.Core;

public sealed record TransferPolicy(TimeSpan MetadataTimeout, TimeSpan IdleTimeout, TimeSpan OverallTimeout)
{ public static TransferPolicy Default { get; } = new(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(30)); }
public sealed record RemoteStamp(long Length, DateTime Modified, int User, int Group, bool Directory, bool Regular, bool Link, uint Mode)
{ public static RemoteStamp From(SftpFileAttributes a) => new(a.Size, a.LastWriteTimeUtc, a.UserId, a.GroupId, a.IsDirectory, a.IsRegularFile, a.IsSymbolicLink, a.PermissionBits); }
public sealed record RemoteDirectoryBinding(SftpLease Source, Guid SourceId, string Path, RemoteStamp Stamp);
public sealed record RemoteFileVersion(string Path, RemoteStamp? Stamp, string? Hash);
public sealed record LocalFileVersion(string Path, LocalStamp? Stamp, LocalStamp Parent, string? Hash);
public sealed record UploadPlan(RemoteDirectoryBinding Directory, RemoteFileVersion Target, LocalFileVersion Input, bool ReplaceApproved);
public sealed record DownloadPlan(RemoteDirectoryBinding Directory, RemoteFileVersion Input, LocalFileVersion Target, bool ReplaceApproved);

/// <summary>Snapshot paths before async pickers/prompts; stage and verify data
/// before a single commit point. No delete-then-rename overwrite fallback.</summary>
public sealed partial class SafeTransfers(TransferPolicy? policy = null)
{
    readonly TransferPolicy options = policy ?? TransferPolicy.Default;
    static void Available(RemoteDirectoryBinding d) { if (d.Source.SourceId != d.SourceId || !d.Source.IsAvailable) throw new OperationCanceledException("原文件来源已关闭，不改投其他来源"); d.Source.Closed.ThrowIfCancellationRequested(); }
    async Task<T> Metadata<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(options.MetadataTimeout);
        try { return await action(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false); } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("远程元数据读取超时；没有将未知状态当成不存在"); }
    }
    static string Absolute(string path, string cwd)
    {
        if (path.IndexOfAny(['\0', '\r', '\n', '\\']) >= 0) throw new IOException("远程路径无效");
        if (!path.StartsWith('/')) { if (string.IsNullOrEmpty(cwd)) throw new IOException("服务端未提供当前目录，请明确输入并确认已知绝对目录"); path = cwd.TrimEnd('/') + "/" + path; }
        var pieces = path.Split('/', StringSplitOptions.RemoveEmptyEntries); if (pieces.Contains("..")) throw new IOException("请使用明确的绝对目录，不接受父跳转路径"); return "/" + string.Join('/', pieces.Where(s => s != "."));
    }
    static string Name(string name) { if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', '\0', '\r', '\n']) >= 0 || System.Text.Encoding.UTF8.GetByteCount(name) > 1024) throw new IOException("文件名无效"); return name; }
    /// <summary>The open file is the version expected: by its handle, or (<see cref="SftpLease.PathStampWhenHandleFails"/>, a
    /// server that cannot say what an open file is) by its path.</summary>
    async Task<bool> Is(SftpFileStream open, string path, SftpLease source, RemoteStamp? expected, CancellationToken token)
    {
        try { return RemoteStamp.From(await Metadata(t => open.ReadHandleAttributesAsync(t), token)) == expected; }
        catch (SftpException e) when (source.PathStampWhenHandleFails && e is not SftpPathNotFoundException) { return await Stat(source, path, token) == expected; }
    }
    async Task<RemoteStamp?> Stat(SftpLease source, string path, CancellationToken ct)
    { try { return RemoteStamp.From(await Metadata(t => source.Client.LStatRawAsync(path, t), ct)); } catch (SftpPathNotFoundException) { return null; } }
    async Task<RemoteStamp> Directory(SftpLease source, string path, CancellationToken ct)
    {
        RemoteStamp? result = null; string current = "";
        foreach (string part in new[] { "" }.Concat(path.Split('/', StringSplitOptions.RemoveEmptyEntries))) { current = part == "" ? "/" : current.TrimEnd('/') + "/" + part; result = await Stat(source, current, ct); if (result is not { Directory: true, Link: false }) throw new IOException("远程父目录不存在、不是目录或包含符号链接"); }
        return result!;
    }
    public async Task<RemoteDirectoryBinding> CaptureDirectoryAsync(SftpLease source, string selectedPath, CancellationToken ct)
    {
        if (!source.IsAvailable) throw new OperationCanceledException("文件来源已关闭"); string absolute = Absolute(selectedPath, source.Client.WorkingDirectory);
        if (!source.IsDirectoryAuthorized(absolute)) throw new IOException("当前目录未知；必须由用户明确确认绝对目录，未静默猜路径");
        var stamp = await Directory(source, absolute, ct); source.VerifiedDirectory(absolute); return new(source, source.SourceId, absolute, stamp);
    }
    async Task CheckDirectory(RemoteDirectoryBinding d, CancellationToken ct)
    { Available(d); var now = await Directory(d.Source, d.Path, ct); if (now.User != d.Stamp.User || now.Group != d.Stamp.Group || now.Mode != d.Stamp.Mode) throw new IOException("远程目录身份属性已变化"); Available(d); }
    public async Task<RemoteFileVersion> CaptureRemoteAsync(RemoteDirectoryBinding d, string name, CancellationToken ct, long maxBytes = long.MaxValue)
    {
        await CheckDirectory(d, ct); string path = d.Path.TrimEnd('/') + "/" + Name(name); var stamp = await Stat(d.Source, path, ct); if (stamp == null) return new(path, null, null);
        if (!stamp.Regular || stamp.Link || stamp.Length < 0 || stamp.Length > maxBytes) throw new IOException("只传输普通文件，不跟随链接或特殊文件");
        string hash = await HashRemote(d.Source, path, ct, Math.Min(stamp.Length, maxBytes)); if (stamp != await Stat(d.Source, path, ct)) throw new IOException("远程文件在核对时变化"); return new(path, stamp, hash);
    }
    public async Task<LocalFileVersion> CaptureLocalAsync(string path, CancellationToken ct)
    {
        path = LocalStamp.FullPath(path); var parent = LocalStamp.RequireParent(path); var stamp = LocalStamp.Read(path); if (stamp == null) return new(path, null, parent, null); if (stamp.Directory) throw new IOException("请选择本地普通文件");
        string hash = await HashLocal(path, ct); if (stamp != LocalStamp.Read(path)) throw new IOException("本地文件在核对时变化"); return new(path, stamp, parent, hash);
    }
    async Task CheckLocal(LocalFileVersion v, CancellationToken ct)
    {
        if (!LocalStamp.SameDirectory(v.Parent, LocalStamp.RequireParent(v.Path)) || v.Stamp != LocalStamp.Read(v.Path)) throw new IOException("本地文件或父目录已变化，未覆盖");
        if (v.Stamp != null && v.Hash != await HashLocal(v.Path, ct)) throw new IOException("本地文件内容已变化，未覆盖");
    }
    async Task CheckRemote(RemoteDirectoryBinding d, RemoteFileVersion v, CancellationToken ct)
    {
        string expected = d.Path.TrimEnd('/') + "/" + Name(v.Path[(v.Path.LastIndexOf('/') + 1)..]);
        if (!StringComparer.Ordinal.Equals(expected, v.Path)) throw new IOException("文件路径不属于已绑定的原目录，拒绝改投目标");
        await CheckDirectory(d, ct); if (v.Stamp != await Stat(d.Source, v.Path, ct)) throw new IOException("远程目标属性已变化，未覆盖");
        if (v.Stamp != null && v.Hash != await HashRemote(d.Source, v.Path, ct, v.Stamp.Length)) throw new IOException("远程文件内容已变化，未覆盖");
    }
    async Task<string> HashRemote(SftpLease s, string path, CancellationToken ct, long maxBytes = long.MaxValue)
    {
        await using var stream = await Metadata(t => s.Client.OpenRawAsync(path, FileMode.Open, FileAccess.Read, t), ct);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] block = new byte[32768]; long count = 0;
        while (true) { int n = await stream.ReadAsync(block, ct); if (n == 0) break; if (count > maxBytes - n) throw new IOException("远程文件超过读取边界"); count += n; hash.AppendData(block, 0, n); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    static async Task<string> HashLocal(string path, CancellationToken ct) { await using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32768, true); return await Hash(s, ct); }
    static async Task<string> Hash(Stream stream, CancellationToken ct) => Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    async Task<(long Bytes, string Hash)> Copy(Stream input, Stream output, long length, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[32768]; long bytes = 0;
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct); idle.CancelAfter(options.IdleTimeout); int n;
            try { n = await input.ReadAsync(buffer, idle.Token); if (n == 0) break; if (bytes > length - n) throw new IOException("内容在传输时超过预先核对的长度"); await output.WriteAsync(buffer.AsMemory(0, n), idle.Token); } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("传输长时间无进展，已停止当前任务"); }
            hash.AppendData(buffer, 0, n); bytes += n; progress.Report(new(bytes, length, "传输中"));
        }
        await output.FlushAsync(ct); if (bytes != length) throw new IOException("内容长度在传输期间变化"); return (bytes, Convert.ToHexString(hash.GetHashAndReset()));
    }
    public async Task UploadAsync(UploadPlan plan, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        using var reservation = await FileReservations.AcquireAsync(new[] { plan.Directory.Source }, ct);
        if (plan.Input.Stamp is not { Directory: false } stamp) throw new IOException("上传源不是普通文件");
        await UploadContentAsync(plan.Directory, plan.Target, stamp.Length, plan.Input.Hash!, plan.ReplaceApproved,
            _ => Task.FromResult<Stream>(new FileStream(plan.Input.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 32768, true)),
            token => CheckLocal(plan.Input, token), progress, ct);
    }
    async Task UploadContentAsync(RemoteDirectoryBinding d, RemoteFileVersion target, long length, string hash, bool replaceApproved,
        Func<CancellationToken, Task<Stream>> openInput, Func<CancellationToken, Task> checkInput, IProgress<TransferProgress> progress, CancellationToken ct)
    {

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, d.Source.Closed); lifetime.CancelAfter(options.OverallTimeout); var token = lifetime.Token;
        string temp = d.Path.TrimEnd('/') + "/.forterm-upload-" + Guid.NewGuid().ToString("N"); bool created = false, committing = false; SftpFileStream? staging = null;
        bool byPath = d.Source.PathStampWhenHandleFails; RemoteStamp? pathCreated = null;
        try
        {
            await checkInput(token); await CheckRemote(d, target, token);
            if (target.Stamp != null && (!replaceApproved || !d.Source.Client.SupportsAtomicReplacement)) throw new IOException("目标已存在；需要明确确认且服务器支持原子替换，否则保留旧文件");
            progress.Report(new(0, length, "传输中"));
            (long Bytes, string Hash) copied;
            await using (var input = await openInput(token))
            {
                staging = await Metadata(t => d.Source.Client.OpenRawAsync(temp, FileMode.CreateNew, FileAccess.Write, t), token);
                created = true;
                // The owner, group and mode the temporary file got: what is checked and committed must still have them.
                if (byPath) pathCreated = await Stat(d.Source, temp, token);
                copied = await Copy(input, staging, length, progress, token);
            }
            progress.Report(new(copied.Bytes, length, "校验内容"));
            // The path as it is before its content is read back: what is committed is what was checked (when the server
            // cannot say what an open file is, PathStampWhenHandleFails).
            var pathChecked = byPath ? await Stat(d.Source, temp, token) : null;
            if (byPath && (pathCreated == null || pathChecked == null || pathChecked.User != pathCreated.User || pathChecked.Group != pathCreated.Group || pathChecked.Mode != pathCreated.Mode))
                throw new IOException("临时上传路径已被替换，未提交");
            if (copied.Hash != hash || await HashRemote(d.Source, temp, token, length) != copied.Hash) throw new IOException("上传内容校验失败，未提交目标");
            if (target.Stamp != null && byPath)
            {
                // The same as below, by the path where the server cannot do it by the open file: the old owner and group, and
                // its mode, set by the open file where the server can and by the path where it cannot, read back by the path.
                if (pathChecked!.User != target.Stamp.User || pathChecked.Group != target.Stamp.Group) throw new IOException("无法保留旧目标的所有者或组，未提交覆盖");
                short mode = short.Parse(Convert.ToString((int)target.Stamp.Mode, 8), System.Globalization.CultureInfo.InvariantCulture);
                try
                {
                    try { await Metadata(async t => { await staging!.SetHandlePermissionsAsync(mode, t); return true; }, token); }
                    catch (SftpException e) when (e is not SftpPathNotFoundException) { await Metadata(t => Task.Run(() => { d.Source.Client.ChangePermissions(temp, mode); return true; }, t), token); }
                }
                catch (TimeoutException) { d.Source.Dispose(); throw; }
                catch (OperationCanceledException) { d.Source.Dispose(); throw; }
                catch (SftpException e) when (e is not SftpPathNotFoundException) { throw new IOException("服务器不能设置临时文件的权限（" + e.Message + "），不能保证覆盖后保留原文件的权限，未覆盖", e); }
                var modeSet = await Stat(d.Source, temp, token);
                if (modeSet == null || modeSet.Mode != target.Stamp.Mode) throw new IOException("服务器未保留目标权限，未提交覆盖");
                // Nothing but the mode changed since the content was checked.
                if (modeSet with { Mode = pathChecked.Mode } != pathChecked) throw new IOException("临时上传路径已被替换，未提交");
                pathChecked = modeSet; pathCreated = modeSet;
            }
            else if (target.Stamp != null)
            {
                var temporaryStamp = RemoteStamp.From(await Metadata(t => staging!.ReadHandleAttributesAsync(t), token));
                if (temporaryStamp == null || temporaryStamp.User != target.Stamp.User || temporaryStamp.Group != target.Stamp.Group) throw new IOException("无法保留旧目标的所有者或组，未提交覆盖");
                try { await Metadata(async t => { await staging!.SetHandlePermissionsAsync(short.Parse(Convert.ToString((int)target.Stamp.Mode, 8), System.Globalization.CultureInfo.InvariantCulture), t); return true; }, token); }
                catch (TimeoutException) { d.Source.Dispose(); throw; }
                catch (OperationCanceledException) { d.Source.Dispose(); throw; }
                if ((await Metadata(t => staging!.ReadHandleAttributesAsync(t), token)).PermissionBits != target.Stamp.Mode) throw new IOException("服务器未保留目标权限，未提交覆盖");
            }
            RemoteStamp? handleVersion;
            try { handleVersion = RemoteStamp.From(await Metadata(t => staging!.ReadHandleAttributesAsync(t), token)); }
            catch (SftpException) when (pathChecked != null) { handleVersion = null; }
            var pathNow = await Stat(d.Source, temp, token);
            if (handleVersion != null ? handleVersion != pathNow
                : pathNow != pathChecked || pathNow is not { Regular: true, Link: false } || pathNow.Length != copied.Bytes)
                throw new IOException("临时上传路径已被替换，未提交");
            await checkInput(token); await CheckRemote(d, target, token); token.ThrowIfCancellationRequested(); Available(d);
            await staging!.DisposeAsync(); staging = null;
            progress.Report(new(copied.Bytes, length, "提交中")); committing = true;
            using var commit = new CancellationTokenSource(options.MetadataTimeout);
            try
            {
                await d.Source.Client.RenameRawAsync(temp, target.Path, target.Stamp != null, commit.Token);
            }
            catch (SftpException e) { committing = false; throw new IOException("服务器明确拒绝提交，未删除旧目标；不会以删除再改名回退", e); }
            catch (Exception e) { try { d.Source.Dispose(); } catch { } throw new CommitUncertainException("提交回执未确认；原来源已停止文件操作，请核对目标与临时文件，不能当作成功或自动重试", e); }
            created = false;
        }
        finally { if (staging != null) { try { await staging.DisposeAsync(); } catch { } } if (created && !committing) { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5)); try { await CheckDirectory(d, cleanup.Token); await d.Source.Client.RemoveRawAsync(temp, cleanup.Token); } catch (Exception e) { throw new IOException("未提交目标；临时上传项清理未确认：" + temp, e); } } }
    }
    public async Task DownloadAsync(DownloadPlan plan, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        using var reservation = await FileReservations.AcquireAsync(new[] { plan.Directory.Source }, ct);
        var d = plan.Directory; if (plan.Input.Stamp is not { Regular: true, Link: false }) throw new IOException("下载源不是普通文件");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, d.Source.Closed); lifetime.CancelAfter(options.OverallTimeout); var token = lifetime.Token;
        string temp = Path.Combine(Path.GetDirectoryName(plan.Target.Path)!, ".forterm-download-" + Guid.NewGuid().ToString("N")); bool created = false, committing = false; string? temporaryId = null;
        try
        {
            await CheckLocal(plan.Target, token); await CheckRemote(d, plan.Input, token); if (plan.Target.Stamp != null && !plan.ReplaceApproved) throw new IOException("未明确确认覆盖本地文件");
            var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 32768, Options = FileOptions.Asynchronous }; if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            (long Bytes, string Hash) copied;
            await using (var input = await Metadata(t => d.Source.Client.OpenRawAsync(plan.Input.Path, FileMode.Open, FileAccess.Read, t), token))
            await using (var output = new FileStream(temp, fileOptions)) { created = true; temporaryId = LocalStamp.Read(temp)!.Id; copied = await Copy(input, output, plan.Input.Stamp.Length, progress, token); output.Flush(true); }
            progress.Report(new(copied.Bytes, plan.Input.Stamp.Length, "校验内容"));
            if (LocalStamp.Read(temp)?.Id != temporaryId || copied.Hash != plan.Input.Hash || await HashLocal(temp, token) != copied.Hash) throw new IOException("下载内容校验失败，未提交目标");
            if (!OperatingSystem.IsWindows() && plan.Target.Stamp != null) File.SetUnixFileMode(temp, (UnixFileMode)plan.Target.Stamp.Mode);
            await CheckRemote(d, plan.Input, token); await CheckLocal(plan.Target, token); token.ThrowIfCancellationRequested(); Available(d);
            progress.Report(new(copied.Bytes, plan.Input.Stamp.Length, "提交中")); committing = true;
            try { File.Move(temp, plan.Target.Path, plan.Target.Stamp != null); } catch (Exception e) { if (File.Exists(temp)) { committing = false; throw new IOException("本地提交被拒绝，未删除旧目标", e); } throw new CommitUncertainException("本地提交结果未确认；请核对原文件和临时文件，不自动重试", e); }
            created = false;
        }
        finally { if (created && !committing) { try { var remaining = LocalStamp.Read(temp); if (remaining != null && remaining.Id != temporaryId) throw new IOException("临时路径归属已变化，不删除替换项"); File.Delete(temp); } catch (Exception e) { throw new IOException("未提交目标；临时下载项清理未确认：" + temp, e); } } }
    }
}
