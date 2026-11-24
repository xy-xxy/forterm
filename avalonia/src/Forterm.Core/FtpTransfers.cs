using System.Security.Cryptography;
using FluentFTP;
using FluentFTP.Exceptions;

namespace Forterm.Core;

public sealed record FtpUploadPlan(FtpDirectory Directory, FtpVersion Target, LocalFileVersion Input);
public sealed record FtpDownloadPlan(FtpDirectory Directory, FtpVersion Input, LocalFileVersion Target, bool ReplaceApproved);

public sealed partial class FtpFileSession
{
    readonly SafeTransfers localFiles = new();
    async Task CheckLocal(LocalFileVersion expected, CancellationToken ct)
    {
        try { var now = await localFiles.CaptureLocalAsync(expected.Path, ct); if (expected.Path != now.Path || expected.Stamp != now.Stamp || expected.Hash != now.Hash || !LocalStamp.SameDirectory(expected.Parent, now.Parent)) throw new FtpPolicyException("本地文件或父目录已变化，未继续"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException && e is not FtpPolicyException) { throw new FtpPolicyException("本地文件无法按原版本读取，未继续"); }
    }

    public async Task<FtpVersion> UploadAsync(FtpUploadPlan plan, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        using var reservation = await FileReservations.AcquireAsync(new[] { plan.Directory.Source }, ct);
        if (plan.Input.Stamp is not { Directory: false } stamp) throw new FtpPolicyException("FTP上传源不是普通文件");
        await CheckLocal(plan.Input, ct);
        return await UploadContent(plan.Directory, plan.Target, stamp.Length, plan.Input.Hash!,
            _ => Task.FromResult<Stream>(new FileStream(plan.Input.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 32768, true)),
            token => CheckLocal(plan.Input, token), progress, ct);
    }
    public Task<FtpVersion> UploadMemoryNewAsync(FtpDirectory directory, FtpVersion target, ReadOnlyMemory<byte> input, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        if (input.Length > 16 * 1024 * 1024) throw new FtpPolicyException("FTP内存复制限16MiB，请使用文件传输");
        byte[] snapshot = input.ToArray(); string hash = Convert.ToHexString(SHA256.HashData(snapshot));
        return UploadContent(directory, target, snapshot.Length, hash, _ => Task.FromResult<Stream>(new MemoryStream(snapshot, false)), _ => Task.CompletedTask, progress, ct);
    }
    Task<FtpVersion> UploadContent(FtpDirectory directory, FtpVersion target, long length, string hash,
        Func<CancellationToken, Task<Stream>> open, Func<CancellationToken, Task> checkInput, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        if (target.Stamp != null) throw new FtpPolicyException("标准FTP未确认安全原子覆盖能力；保留旧目标，请选择新名称");
        return Run(async (client, token) =>
        {
            Bound(directory); string temp = directory.Path.TrimEnd('/') + "/.forterm-upload-" + Guid.NewGuid().ToString("N");
            bool created = false, committing = false; FtpVersion? staged = null;
            try
            {
                await checkInput(token); await Check(client, directory, target, token);
                await using (var input = await open(token))
                {
                    created = true;
                    (long Bytes, string Hash) copied;
                    await using (var output = await client.OpenWrite(temp, FtpDataType.Binary, -1L, token))
                    { copied = await CopyData(input, output, length, progress, token); }
                    await client.CompleteData(token);
                    if (copied.Hash != hash) throw new FtpPolicyException("FTP上传源内容变化，未提交目标");
                }
                progress.Report(new(length, length, "校验内容"));
                staged = await Capture(client, directory, Path.GetFileName(temp), token, length);
                if (staged.Hash != hash) throw new FtpPolicyException("FTP临时内容校验不一致，未提交目标");
                await checkInput(token); await Check(client, directory, target, token); token.ThrowIfCancellationRequested(); Ensure();
                progress.Report(new(length, length, "提交中")); committing = true;
                using var commit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                try
                {
                    var from = await client.Execute("RNFR " + temp, commit.Token);
                    if (from.Code != "350") { committing = false; throw new FtpPolicyException("FTP服务器明确拒绝改名，未主动覆盖旧文件"); }
                    var to = await client.Execute("RNTO " + target.Path, commit.Token);
                    if (!to.Success) { committing = false; throw new FtpPolicyException("FTP服务器明确拒绝提交，未删除旧目标"); }
                }
                catch (FtpPolicyException) { throw; }
                catch (Exception e) { transport.Stop("FTP提交回执未确认，请核对远程结果，不自动重试。"); throw new CommitUncertainException("FTP改名提交结果未确认，可能已生效；原连接已停止，请核对目标和临时项", e); }
                created = false;
                try
                {
                    using var verify = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    var saved = await Capture(client, directory, Path.GetFileName(target.Path), verify.Token, length);
                    if (saved.Hash != hash) throw new FtpPolicyException("FTP目标在保存后又发生变化");
                    return saved;
                }
                catch (Exception e) { throw new CommitUncertainException("FTP提交回执已确认，但随后版本核验未完成；草稿保留，请核对远端", e); }
            }
            finally
            {
                if (created && !committing)
                {
                    if (staged?.Hash != hash || !IsAvailable) throw new CommitUncertainException("FTP目标未提交；随机临时项归属/清理未确认，请核对：" + temp);
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    try { await Check(client, directory, staged, cleanup.Token); var reply = await client.Execute("DELE " + temp, cleanup.Token); if (!reply.Success) throw new IOException(); }
                    catch (Exception e) { throw new CommitUncertainException("FTP目标未提交，临时项清理未确认：" + temp, e); }
                }
            }
        }, ct, true);
    }
    static async Task<(long Bytes, string Hash)> CopyData(Stream input, Stream output, long length, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] block = new byte[32768]; long count = 0;
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct); idle.CancelAfter(TimeSpan.FromSeconds(30));
            int n = await input.ReadAsync(block, idle.Token); if (n == 0) break;
            if (count > length - n) throw new IOException("FTP输入超过预先核对长度");
            await output.WriteAsync(block.AsMemory(0, n), idle.Token); hash.AppendData(block, 0, n); count += n; progress.Report(new(count, length, "传输中"));
        }
        await output.FlushAsync(ct); if (count != length) throw new IOException("FTP长度在传输期间变化"); return (count, Convert.ToHexString(hash.GetHashAndReset()));
    }
    public async Task DownloadAsync(FtpDownloadPlan plan, IProgress<TransferProgress> progress, CancellationToken ct)
    { using var reservation = await FileReservations.AcquireAsync(new[] { plan.Directory.Source }, ct); await DownloadCoreAsync(plan, progress, ct); }
    Task DownloadCoreAsync(FtpDownloadPlan plan, IProgress<TransferProgress> progress, CancellationToken ct) => Run(async (client, token) =>
    {
        if (plan.Input.Stamp == null) throw new FtpPolicyException("FTP下载源不存在");
        if (plan.Target.Stamp != null && !plan.ReplaceApproved) throw new FtpPolicyException("未明确确认替换本地文件");
        string temp = Path.Combine(Path.GetDirectoryName(plan.Target.Path)!, ".forterm-download-" + Guid.NewGuid().ToString("N")); string? identity = null; bool created = false, committed = false;
        try
        {
            await CheckLocal(plan.Target, token); await Check(client, plan.Directory, plan.Input, token);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 32768, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            (long Bytes, string Hash) copied;
            await using (var input = await client.OpenRead(plan.Input.Path, FtpDataType.Binary, 0, plan.Input.Stamp.Length == 0 ? -1L : plan.Input.Stamp.Length, token))
            await using (var output = new FileStream(temp, options)) { created = true; identity = LocalStamp.Read(temp)!.Id; copied = await CopyData(input, output, plan.Input.Stamp.Length, progress, token); output.Flush(true); }
            await client.CompleteData(token); progress.Report(new(copied.Bytes, plan.Input.Stamp.Length, "校验内容"));
            if (copied.Hash != plan.Input.Hash || LocalStamp.Read(temp)?.Id != identity) throw new FtpPolicyException("FTP下载内容或临时项身份不一致");
            await using (var verify = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 32768, true)) if (Convert.ToHexString(await SHA256.HashDataAsync(verify, token)) != copied.Hash) throw new FtpPolicyException("本地下载临时内容校验不一致");
            if (!OperatingSystem.IsWindows() && plan.Target.Stamp != null) File.SetUnixFileMode(temp, (UnixFileMode)plan.Target.Stamp.Mode);
            await Check(client, plan.Directory, plan.Input, token); await CheckLocal(plan.Target, token); token.ThrowIfCancellationRequested(); Ensure();
            progress.Report(new(copied.Bytes, plan.Input.Stamp.Length, "提交中"));
            try { File.Move(temp, plan.Target.Path, plan.Target.Stamp != null); committed = true; }
            catch (Exception e) { if (!File.Exists(temp)) throw new CommitUncertainException("FTP下载本地提交结果未确认，请核对目标", e); throw new FtpPolicyException("本地提交被拒绝，旧目标保留"); }
            return true;
        }
        finally
        {
            if (created && !committed)
            {
                try { var stamp = LocalStamp.Read(temp); if (stamp != null && stamp.Id != identity) throw new IOException(); File.Delete(temp); }
                catch (Exception e) { throw new CommitUncertainException("本地临时下载项清理未确认，请核对：" + temp, e); }
            }
        }
    }, ct, true);
}
