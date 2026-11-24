using FluentFTP;
namespace Forterm.Core;

public sealed partial class FtpFileSession
{
    public Task<FtpVersion> CaptureNodeAsync(FtpDirectory directory, string name, CancellationToken ct) => Run(async (client, token) =>
    {
        Bound(directory); name = Name(name); var item = (await ListChecked(client, directory.Path, token)).SingleOrDefault(f => f.Name == name);
        if (item == null || item.Type is not (FtpObjectType.File or FtpObjectType.Directory) || !string.IsNullOrEmpty(item.LinkTarget)) throw new FtpPolicyException("FTP节点不存在、是链接或类型不支持");
        return item.Type == FtpObjectType.File ? await Capture(client, directory, name, token) : new FtpVersion(directory.Path.TrimEnd('/') + "/" + name, Stamp(item), null);
    }, ct, true);
    public Task CreateDirectoryAsync(FtpDirectory directory, string name, CancellationToken ct) => Run(async (client, token) =>
    {
        Bound(directory); name = Name(name); if ((await ListChecked(client, directory.Path, token)).Any(f => f.Name == name)) throw new FtpPolicyException("FTP目录名称已存在");
        token.ThrowIfCancellationRequested(); using var commit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try { if (!(await client.Execute("MKD " + directory.Path.TrimEnd('/') + "/" + name, commit.Token)).Success) throw new FtpPolicyException("FTP拒绝创建目录"); }
        catch (FtpPolicyException) { throw; }
        catch (Exception e) { transport.Stop("FTP创建目录结果未确认"); throw new CommitUncertainException("FTP创建目录回执未确认，请核对", e); }
        return true;
    }, ct);
    /// <summary>Additive (Studio recursive delete): DELE/RMD of one listed file or directory verified by
    /// its listing metadata instead of a content hash. Links and special entries are refused.</summary>
    internal Task RemoveNodeByMetadataAsync(FtpDirectory directory, string name, Func<RemoteNodeStat, string?> verify, IProgress<TransferProgress> progress, CancellationToken ct) => Run(async (client, token) =>
    {
        Bound(directory); name = Name(name); string path = directory.Path.TrimEnd('/') + "/" + name;
        var item = (await ListChecked(client, directory.Path, token)).SingleOrDefault(f => f.Name == name) ?? throw new FtpPolicyException("FTP节点不存在，未删除");
        if (item.Type is not (FtpObjectType.File or FtpObjectType.Directory) || !string.IsNullOrEmpty(item.LinkTarget)) throw new FtpPolicyException("FTP不删除链接或特殊节点");
        bool isDirectory = item.Type == FtpObjectType.Directory;
        if (verify(new(isDirectory, false, !isDirectory, item.Size, item.Modified)) is { } refused) throw new FtpPolicyException(refused);
        token.ThrowIfCancellationRequested(); progress.Report(new(0, null, "提交中")); using var commit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try { if (!(await client.Execute((isDirectory ? "RMD " : "DELE ") + path, commit.Token)).Success) throw new FtpPolicyException(isDirectory ? "FTP目录不为空或服务器拒绝" : "FTP服务器拒绝删除"); }
        catch (FtpPolicyException) { throw; }
        catch (Exception e) { transport.Stop("FTP删除结果未确认"); throw new CommitUncertainException("FTP删除回执未确认，请核对", e); }
        return true;
    }, ct, true); // listing + DELE over a slow link must not hit the 8-second metadata budget
    public Task RemoveNodeAsync(FtpDirectory directory, FtpVersion expected, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        if (expected.Stamp?.Type != FtpObjectType.Directory) return RemoveVerifiedAsync(directory, expected, progress, ct);
        return Run(async (client, token) =>
        {
            Bound(directory); string name = Name(Path.GetFileName(expected.Path)); if (expected.Path != directory.Path.TrimEnd('/') + "/" + name) throw new FtpPolicyException("FTP目录节点路径不匹配");
            var item = (await ListChecked(client, directory.Path, token)).SingleOrDefault(f => f.Name == name); if (item == null || Stamp(item) != expected.Stamp) throw new FtpPolicyException("FTP目录节点已变化");
            token.ThrowIfCancellationRequested(); progress.Report(new(0, null, "提交中")); using var commit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try { if (!(await client.Execute("RMD " + expected.Path, commit.Token)).Success) throw new FtpPolicyException("FTP目录不为空或服务器拒绝；没有递归删除"); }
            catch (FtpPolicyException) { throw; }
            catch (Exception e) { transport.Stop("FTP目录删除结果未确认"); throw new CommitUncertainException("FTP空目录删除回执未确认，请核对", e); }
            return true;
        }, ct);
    }
    public Task RenameNodeAsync(FtpDirectory directory, FtpVersion expected, string name, CancellationToken ct)
    {
        if (expected.Stamp?.Type != FtpObjectType.Directory) return RenameVerifiedAsync(directory, expected, name, ct);
        return Run(async (client, token) =>
        {
            Bound(directory); name = Name(name); var items = await ListChecked(client, directory.Path, token); var old = items.SingleOrDefault(f => directory.Path.TrimEnd('/') + "/" + f.Name == expected.Path);
            if (old == null || Stamp(old) != expected.Stamp || items.Any(f => f.Name == name)) throw new FtpPolicyException("FTP节点已变化或新名称已存在");
            token.ThrowIfCancellationRequested(); using var commit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try
            {
                if ((await client.Execute("RNFR " + expected.Path, commit.Token)).Code != "350" || !(await client.Execute("RNTO " + directory.Path.TrimEnd('/') + "/" + name, commit.Token)).Success) throw new FtpPolicyException("FTP明确拒绝目录改名");
            }
            catch (FtpPolicyException) { throw; }
            catch (Exception e) { transport.Stop("FTP目录改名结果未确认"); throw new CommitUncertainException("FTP目录改名回执未确认，请核对两个路径", e); }
            return true;
        }, ct);
    }
}
