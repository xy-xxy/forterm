using FluentFTP;
namespace Forterm.Core;

public sealed partial class FtpFileSession
{
    public Task<PermissionSnapshot> InspectPermissionAsync(string path, CancellationToken ct) => Run(async (client, token) =>
    {
        path = Absolute(path, transport.Cwd); if (path == "/") throw new FtpPolicyException("不对根目录生成权限计划");
        string parent = path[..Math.Max(1, path.LastIndexOf('/'))], name = Path.GetFileName(path);
        var item = (await ListChecked(client, parent, token)).SingleOrDefault(f => f.Name == name);
        if (item == null || item.Type is not (FtpObjectType.File or FtpObjectType.Directory) || !string.IsNullOrEmpty(item.LinkTarget)) throw new FtpPolicyException("FTP权限目标不存在、为链接或特殊节点");
        if (string.IsNullOrEmpty(item.RawPermissions) || item.RawPermissions.Length < 9 || item.RawPermissions.Contains('?')) throw new FtpPolicyException("FTP服务器未提供已知权限，不能当作000处理");
        var help = await client.Execute("SITE HELP", token);
        if (!help.Success || !(help.Message + " " + help.InfoMessages).Contains("CHMOD", StringComparison.OrdinalIgnoreCase)) throw new FtpPolicyException("FTP未确认SITE CHMOD能力；不使用其他命令冒充");
        var stamp = Stamp(item); return new PermissionSnapshot(path, item.Type == FtpObjectType.Directory, Convert.ToUInt32(item.Chmod.ToString(System.Globalization.CultureInfo.InvariantCulture), 8), item.Size, item.Modified, stamp);
    }, ct);
    public Task ApplyPermissionAsync(PermissionSnapshot expected, uint mode, CancellationToken ct) => Run(async (client, token) =>
    {
        if (mode > 4095) throw new FtpPolicyException("权限模式无效");
        string path = Absolute(expected.Path, transport.Cwd), parent = path[..Math.Max(1, path.LastIndexOf('/'))], name = Name(Path.GetFileName(path));
        var item = (await ListChecked(client, parent, token)).SingleOrDefault(f => f.Name == name);
        if (item == null || Stamp(item) != (FtpStamp)expected.Version) throw new FtpPolicyException("FTP权限计划后节点已变化");
        token.ThrowIfCancellationRequested(); using var commit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            var reply = await client.Execute("SITE CHMOD " + Convert.ToString(mode, 8) + " " + path, commit.Token);
            if (!reply.Success) throw new FtpPolicyException("FTP服务器明确拒绝SITE CHMOD");
            var after = (await ListChecked(client, parent, commit.Token)).SingleOrDefault(f => f.Name == name);
            if (after == null || Convert.ToUInt32(after.Chmod.ToString(System.Globalization.CultureInfo.InvariantCulture), 8) != mode || after.Type != item.Type) throw new IOException("FTP权限结果未符合请求");
        }
        catch (FtpPolicyException) { throw; }
        catch (Exception e) { transport.Stop("FTP权限变更结果未确认"); throw new CommitUncertainException("FTP权限变更回执或结果未确认，请重新读取", e); }
        return true;
    }, ct);
}
