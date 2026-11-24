namespace Forterm.Core;

public sealed partial class FtpFileSession
{
    public Task RemoveVerifiedAsync(FtpDirectory directory, FtpVersion expected, IProgress<TransferProgress> progress, CancellationToken ct) => Run(async (client, token) =>
    {
        if (expected.Stamp == null) throw new FtpPolicyException("FTP删除源不存在");
        await Check(client, directory, expected, token); token.ThrowIfCancellationRequested(); Ensure(); progress.Report(new(0, expected.Stamp.Length, "提交中"));
        using var commit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try { var result = await client.Execute("DELE " + expected.Path, commit.Token); if (!result.Success) throw new FtpPolicyException("FTP服务器明确拒绝删除"); }
        catch (FtpPolicyException) { throw; }
        catch (Exception e) { transport.Stop("FTP删除结果未确认，请核对源文件"); throw new CommitUncertainException("FTP删除回执未确认；不重试，请核对原文件", e); }
        return true;
    }, ct, true);
    public Task RenameVerifiedAsync(FtpDirectory directory, FtpVersion expected, string newName, CancellationToken ct) => Run(async (client, token) =>
    {
        var target = await Capture(client, directory, Name(newName), token); if (target.Stamp != null || target.Path == expected.Path) throw new FtpPolicyException("FTP新名称已存在或等于源，未主动覆盖");
        await Check(client, directory, expected, token); token.ThrowIfCancellationRequested(); Ensure();
        using var commit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            var from = await client.Execute("RNFR " + expected.Path, commit.Token); if (from.Code != "350") throw new FtpPolicyException("FTP拒绝源文件改名");
            var to = await client.Execute("RNTO " + target.Path, commit.Token); if (!to.Success) throw new FtpPolicyException("FTP拒绝新名称，未主动删除目标");
        }
        catch (FtpPolicyException) { throw; }
        catch (Exception e) { transport.Stop("FTP改名结果未确认，请核对两个路径"); throw new CommitUncertainException("FTP改名回执未确认，不自动重试", e); }
        return true;
    }, ct, true);
}
