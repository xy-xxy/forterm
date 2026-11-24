using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
namespace Forterm.Core;

public sealed record PrivilegeIdentity(ulong Device, ulong Inode, long Size, uint Mode, long ModifiedNS, bool Directory);
public sealed record SudoPermissionPlan(PermissionPlan Plan, IReadOnlyDictionary<string, PrivilegeIdentity> Identities, IReadOnlyList<string> Notes);
public sealed class SudoPermissionService
{
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    static readonly string Helper = ReadHelper();
    static string ReadHelper() { using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Forterm.PermissionHelper")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    internal static string Literal(string value) => "'" + value.Replace("'", "'\\''") + "'";
    internal static string Script(PermissionItem item, string action, PrivilegeIdentity? expected)
    {
        if (!item.Before.Path.StartsWith('/') || item.Before.Path == "/" || item.Before.Path.Any(c => c < 32 || c == 127) || item.Mode > 4095) throw new IOException("sudo路径或权限无效");
        var payload = JsonSerializer.Serialize(new { action, path = item.Before.Path, mode = item.Mode, expected }, Json);
        return "/usr/bin/python3 -c " + Literal(Helper) + " " + Literal(payload);
    }
    static SftpLease Source(PermissionPlan plan) => plan.Source is SftpLease { Owner: not null } source && source.SourceId == plan.SourceId && source.IsAvailable ? source : throw new IOException("此来源不支持同一已认证SSH安全sudo");
    public async Task<SudoPermissionPlan> InspectAsync(PermissionPlan plan, CancellationToken ct)
    {
        PermissionService.Validate(plan); var source = Source(plan); var identities = new Dictionary<string, PrivilegeIdentity>(StringComparer.Ordinal); var notes = new List<string>();
        foreach (var item in plan.Items.Where(i => !i.Skip))
        {
            var reply = await source.Owner!.PermissionExecAsync("LC_ALL=C " + Script(item, "inspect", null), null, null, ct);
            if (reply.ExitCode != 0) throw new IOException("安全身份读取不可用；需要已安装/usr/bin/python3、no-follow接口与独立exec权限，不会安装或降级");
            PrivilegeIdentity identity;
            try { identity = JsonSerializer.Deserialize<PrivilegeIdentity>(reply.Output.Trim(), Json) ?? throw new IOException(); } catch { throw new IOException("安全身份返回无效，未提权"); }
            if (identity.Mode > 4095 || identity.Size != item.Before.Length || identity.Mode != item.Before.Mode || identity.Directory != item.Before.Directory || identity.ModifiedNS / 1_000_000_000 != new DateTimeOffset(DateTime.SpecifyKind(item.Before.Modified, DateTimeKind.Utc)).ToUnixTimeSeconds()) throw new IOException("exec与文件通道路径元数据不一致，拒绝对未知目标提权");
            var policy = await source.Owner.PermissionExecAsync("LC_ALL=C /usr/bin/sudo -n -u root -l -- " + Script(item, "apply", identity), null, null, ct);
            string error = policy.Error.ToLowerInvariant();
            if (policy.ExitCode == 0) notes.Add(item.Before.Path + " · sudo策略允许，身份固定root");
            else if (error.Contains("tty") || error.Contains("terminal is required")) throw new IOException("sudo策略要求TTY，独立安全通道不支持，不写入交互终端");
            else if (error.Contains("password is required") || error.Contains("a password is required")) notes.Add(item.Before.Path + " · sudo需要本次密码，仅在随机明确提示后请求");
            else throw new IOException("sudo策略未确认此安全命令权限，未执行变更");
            notes.Add(item.Before.Path + " · device=" + identity.Device + " inode=" + identity.Inode); identities.Add(item.Before.Path, identity);
        }
        return new(plan, identities, notes);
    }
    public async Task ApplyAsync(SudoPermissionPlan approved, bool confirmedSameFilesystem, Func<CancellationToken, Task<byte[]?>> password, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        if (!confirmedSameFilesystem) throw new IOException("需明确确认此SSH真实目标与文件路径空间一致");
        PermissionService.Validate(approved.Plan); var source = Source(approved.Plan); using var reservation = await FileReservations.AcquireAsync(new[] { source }, ct); int done = 0, total = approved.Plan.Items.Count(i => !i.Skip);
        var permissions = new PermissionService(); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, source.Closed); timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            foreach (var item in approved.Plan.Items.Where(i => !i.Skip))
            {
                timeout.Token.ThrowIfCancellationRequested(); progress.Report(new(done, total, "校验sudo身份"));
                if (!approved.Identities.TryGetValue(item.Before.Path, out var identity) || await permissions.InspectAsync(source, item.Before.Path, timeout.Token) != item.Before) throw new IOException("权限或身份计划已变化，请重新预览");
                string marker = "[FORTERM-SUDO-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() + "]";
                var command = "LC_ALL=C /usr/bin/sudo -S -p " + Literal(marker) + " -u root -- " + Script(item, "apply", identity);
                var result = await source.Owner!.PermissionExecAsync(command, marker, password, timeout.Token);
                if (result.ExitCode != 0) throw new IOException(result.Error.Contains("tty", StringComparison.OrdinalIgnoreCase) ? "sudo要求TTY，未降级" : "sudo变更未确认，可能策略拒绝/认证取消/目标变化；请重新读取，不自动重试");
                var after = await permissions.InspectAsync(source, item.Before.Path, timeout.Token);
                if (after.Mode != item.Mode || after.Directory != item.Before.Directory) throw new IOException("sudo后文件通道权限核验不一致");
                done++; progress.Report(new(done, total, "校验sudo结果"));
            }
        }
        catch (Exception e) { throw new PermissionApplyException(done, total, e.Message, e); }
    }
}
