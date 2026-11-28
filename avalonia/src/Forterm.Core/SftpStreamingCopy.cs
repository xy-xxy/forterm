using Renci.SshNet.Sftp;
namespace Forterm.Core;

public sealed partial class SafeTransfers
{
    public async Task<Stream> OpenCopyReadAsync(RemoteDirectoryBinding directory, RemoteFileVersion expected, CancellationToken ct)
    {
        if (expected.Stamp is not { Regular: true, Link: false }) throw new IOException("流式复制只支持普通文件");
        await CheckRemote(directory, expected, ct); var stream = await Metadata(t => directory.Source.Client.OpenRawAsync(expected.Path, FileMode.Open, FileAccess.Read, t), ct);
        try
        {
            if (!await Is(stream, expected.Path, directory.Source, expected.Stamp, ct)) throw new IOException("流式源句柄与已选版本不一致");
            return new CopyReadStream(stream, expected.Stamp.Length, async complete =>
            {
                try
                {
                    if (!complete) return;
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(directory.Source.Closed); timeout.CancelAfter(options.MetadataTimeout);
                    if (!await Is(stream, expected.Path, directory.Source, expected.Stamp, timeout.Token)) throw new IOException("源句柄在复制期间发生变化");
                }
                finally { await stream.DisposeAsync(); }
            });
        }
        catch { await stream.DisposeAsync(); throw; }
    }
    public async Task<RemoteFileVersion> UploadCopyStreamAsync(RemoteDirectoryBinding directory, RemoteFileVersion target, Stream input, long length, string hash, bool replaceApproved,
        Func<CancellationToken, Task> verifySourceAfterRead, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        bool first = true;
        Task CheckSource(CancellationToken token) { if (first) { first = false; return Task.CompletedTask; } return verifySourceAfterRead(token); }
        await UploadContentAsync(directory, target, length, hash, replaceApproved, _ => Task.FromResult(input), CheckSource, progress, ct);
        try
        {
            using var verify = CancellationTokenSource.CreateLinkedTokenSource(directory.Source.Closed); verify.CancelAfter(options.MetadataTimeout);
            var saved = await CaptureRemoteAsync(directory, Path.GetFileName(target.Path), verify.Token, length);
            if (saved.Hash != hash) throw new IOException("复制提交后目标又发生变化"); return saved;
        }
        catch (Exception e) { throw new CommitUncertainException("复制提交回执已确认，但随后版本核验未完成；请核对目标", e); }
    }
    public Task VerifyCopySourceAsync(RemoteDirectoryBinding directory, RemoteFileVersion expected, CancellationToken ct) => CheckRemote(directory, expected, ct);
    public async Task MoveCopySourceAsync(RemoteDirectoryBinding sourceDirectory, RemoteFileVersion source, RemoteDirectoryBinding targetDirectory, RemoteFileVersion target, bool replaceApproved, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        if (sourceDirectory.SourceId != targetDirectory.SourceId) throw new IOException("原地移动要求同一SFTP来源");
        await CheckRemote(sourceDirectory, source, ct); await CheckRemote(targetDirectory, target, ct);
        if (target.Stamp != null && (!replaceApproved || !targetDirectory.Source.Client.SupportsAtomicReplacement)) throw new IOException("覆盖移动需要明确确认与原子替换能力");
        ct.ThrowIfCancellationRequested(); Available(sourceDirectory); Available(targetDirectory); progress.Report(new(source.Stamp!.Length, source.Stamp.Length, "提交中"));
        using var commit = new CancellationTokenSource(options.MetadataTimeout);
        try { await sourceDirectory.Source.Client.RenameRawAsync(source.Path, target.Path, target.Stamp != null, commit.Token); }
        catch (Renci.SshNet.Common.SftpException e) { throw new IOException("SFTP服务器明确拒绝移动，未删除目标", e); }
        catch (Exception e) { try { sourceDirectory.Source.Dispose(); } catch { } throw new CommitUncertainException("SFTP移动回执未确认，请核对两端，不自动重试", e); }
        try { if (await Stat(sourceDirectory.Source, source.Path, commit.Token) != null || await Stat(targetDirectory.Source, target.Path, commit.Token) != source.Stamp) throw new IOException("移动后的两个路径状态不符合预期"); }
        catch (Exception e) { throw new CommitUncertainException("SFTP移动回执已确认，但源移除或目标版本未确认；没有再次删除", e); }
    }
}
