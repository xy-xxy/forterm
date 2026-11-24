using FluentFTP;
namespace Forterm.Core;

public sealed partial class FtpFileSession
{
    public async Task<Stream> OpenCopyReadAsync(FtpDirectory directory, FtpVersion expected, CancellationToken ct)
    {
        Ensure(); var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, Closed); timeout.CancelAfter(TimeSpan.FromMinutes(30)); bool acquired = false; Stream? stream = null;
        try
        {
            await transport.Gate.WaitAsync(timeout.Token); acquired = true;
            if (expected.Stamp?.Type != FtpObjectType.File) throw new FtpPolicyException("FTP流式复制只支持普通文件");
            await Check(transport.Client, directory, expected, timeout.Token);
            stream = await transport.Client.OpenRead(expected.Path, FtpDataType.Binary, 0, expected.Stamp.Length == 0 ? -1L : expected.Stamp.Length, timeout.Token);
            var data = stream;
            return new CopyReadStream(data, expected.Stamp.Length, async complete =>
            {
                try
                {
                    await data.DisposeAsync();
                    if (!complete) { transport.Stop("FTP源流未完整读取，已停止原控制连接，不重放凭据"); return; }
                    using var ack = CancellationTokenSource.CreateLinkedTokenSource(Closed); ack.CancelAfter(TimeSpan.FromSeconds(8));
                    await transport.Client.CompleteData(ack.Token);
                }
                catch { transport.Stop("FTP源流完成回执未确认，已停止连接"); throw; }
                finally { timeout.Dispose(); transport.Gate.Release(); }
            });
        }
        catch (Exception error)
        {
            if (stream != null) { try { await stream.DisposeAsync(); } catch { } transport.Stop("FTP源流开通失败或未完成"); }
            else if (error is not (FtpPolicyException or FluentFTP.Exceptions.FtpCommandException)) transport.Stop("FTP源开通前协议状态未确认，已停止连接");
            else if (!transport.Client.IsConnected) transport.Stop("FTP连接已关闭，不自动重新登录");
            if (acquired) transport.Gate.Release(); timeout.Dispose(); throw;
        }
    }
    public Task<FtpVersion> UploadCopyStreamAsync(FtpDirectory directory, FtpVersion target, Stream input, long length, string hash,
        Func<CancellationToken, Task> verifySourceAfterRead, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        bool first = true;
        Task CheckSource(CancellationToken token) { if (first) { first = false; return Task.CompletedTask; } return verifySourceAfterRead(token); }
        return UploadContent(directory, target, length, hash, _ => Task.FromResult(input), CheckSource, progress, ct);
    }
    public Task VerifyCopySourceAsync(FtpDirectory directory, FtpVersion expected, CancellationToken ct) => Run(async (client, token) => { await Check(client, directory, expected, token); return true; }, ct, true);
    public Task<FtpVersion> MoveCopySourceAsync(FtpDirectory sourceDirectory, FtpVersion source, FtpDirectory targetDirectory, FtpVersion target, IProgress<TransferProgress> progress, CancellationToken ct) => Run(async (client, token) =>
    {
        if (!ReferenceEquals(sourceDirectory.Source.transport, targetDirectory.Source.transport) || !targetDirectory.Source.IsAvailable) throw new FtpPolicyException("FTP原地移动要求同一已认证控制连接");
        if (target.Stamp != null) throw new FtpPolicyException("FTP不支持安全覆盖移动，目标保留");
        var translatedTarget = new FtpDirectory(this, SourceId, targetDirectory.Path);
        await Check(client, sourceDirectory, source, token); await Check(client, translatedTarget, target, token); token.ThrowIfCancellationRequested(); progress.Report(new(source.Stamp!.Length, source.Stamp.Length, "提交中"));
        using var commit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            var from = await client.Execute("RNFR " + source.Path, commit.Token); if (from.Code != "350") throw new FtpPolicyException("FTP明确拒绝移动源");
            if (!(await client.Execute("RNTO " + target.Path, commit.Token)).Success) throw new FtpPolicyException("FTP明确拒绝移动目标");
        }
        catch (FtpPolicyException) { throw; }
        catch (Exception e) { transport.Stop("FTP移动回执未确认，请核对两端"); throw new CommitUncertainException("FTP移动回执未确认，不自动重试", e); }
        try
        {
            if ((await ListChecked(client, sourceDirectory.Path, commit.Token)).Any(f => f.Name == Path.GetFileName(source.Path))) throw new IOException("源路径仍存在或被重新创建");
            return new FtpVersion(target.Path, source.Stamp, source.Hash);
        }
        catch (Exception e) { throw new CommitUncertainException("FTP移动回执已确认，但源路径移除状态未确认；没有再次删除", e); }
    }, ct, true);
}
