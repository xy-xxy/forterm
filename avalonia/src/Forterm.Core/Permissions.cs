using System.Globalization;
using Renci.SshNet.Common;
namespace Forterm.Core;

public sealed record PermissionSnapshot(string Path, bool Directory, uint Mode, long Length, DateTime Modified, object Version);
public sealed record PermissionItem(PermissionSnapshot Before, uint Mode, bool Skip);
public sealed record PermissionPlan(IRemoteFileSource Source, Guid SourceId, string Root, bool Recursive, IReadOnlyList<PermissionItem> Items, int Skipped);
public sealed class PermissionNodeSkippedException(string message) : IOException(message);
public sealed class PermissionApplyException(int done, int total, string message, Exception? inner = null) : IOException($"已确认完成{done}/{total}项；剩余停止，已完成不回滚：{message}", inner)
{ public int Done { get; } = done; public int Total { get; } = total; }

public sealed class PermissionService
{
    readonly SafeTransfers safe = new();
    static string Path(string value)
    {
        if (!value.StartsWith('/') || value == "/" || value.Length > 16384 || value.Any(c => c < 32 || c == 127) || value.Contains('\\') || value[1..].Split('/').Any(p => p is "." or ".." || p.Length == 0)) throw new IOException("权限路径必须是非根目录的明确绝对路径");
        return value;
    }
    public async Task<PermissionSnapshot> InspectAsync(IRemoteFileSource source, string path, CancellationToken ct)
    {
        path = Path(path);
        if (source is FtpFileSession ftp) return await ftp.InspectPermissionAsync(path, ct);
        if (source is not SftpLease sftp) throw new IOException("此来源不支持权限读取");
        var parent = path[..Math.Max(1, path.LastIndexOf('/'))]; await safe.CaptureDirectoryAsync(sftp, parent, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, sftp.Closed); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var stamp = RemoteStamp.From(await sftp.Client.LStatRawAsync(path, timeout.Token));
        if (stamp.Link || !stamp.Directory && !stamp.Regular) throw new PermissionNodeSkippedException("不修改链接或特殊节点");
        return new(path, stamp.Directory, stamp.Mode, stamp.Length, stamp.Modified, stamp);
    }
    public async Task<PermissionPlan> PreviewAsync(IRemoteFileSource source, string root, bool recursive, uint directoryMode, uint? fileMode, CancellationToken ct)
    {
        root = Path(root); if (directoryMode > 511 || fileMode > 511) throw new IOException("普通权限仅000–777；特殊位按各原项保留");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, source.Closed); timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var items = new List<PermissionItem>(); var visited = new HashSet<string>(StringComparer.Ordinal); int skipped = 0;
        async Task Walk(string path, int depth)
        {
            if (depth > 32 || visited.Count >= 2000 || !visited.Add(path)) throw new IOException("权限计划超过2000项/32层或出现重复路径；未执行变更");
            PermissionSnapshot before;
            try { before = await InspectAsync(source, path, timeout.Token); }
            catch (PermissionNodeSkippedException) when (path != root) { skipped++; return; }
            if (before.Directory && recursive)
            {
                IReadOnlyList<RemoteEntry> children;
                if (source is SftpLease sftp) { using var catalog = new RemoteDirectoryCatalog(sftp); children = (await catalog.ListAsync(path, true, timeout.Token)).Entries; }
                else children = (await ((FtpFileSession)source).ListAsync(path, timeout.Token)).Entries;
                foreach (var entry in children.OrderBy(e => e.Name, StringComparer.Ordinal))
                { if (entry.Link || !entry.Directory && !entry.Regular) { skipped++; continue; } await Walk(entry.Path, depth + 1); }
            }
            bool skip = !before.Directory && path != root && fileMode == null;
            uint normal = before.Directory ? directoryMode : fileMode ?? directoryMode;
            items.Add(new(before, skip ? before.Mode : before.Mode & 3584u | normal, skip));
        }
        await Walk(root, 0); return new(source, source.SourceId, root, recursive, items.ToArray(), skipped);
    }
    public static void Validate(PermissionPlan plan)
    {
        Path(plan.Root); if (plan.Source.SourceId != plan.SourceId || plan.Items.Count > 2000 || plan.Items.Count == 0) throw new IOException("权限计划来源或数量无效");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in plan.Items)
        {
            string path = Path(item.Before.Path);
            if (!seen.Add(path) || path != plan.Root && !path.StartsWith(plan.Root + "/", StringComparison.Ordinal) || item.Mode > 4095 || item.Before.Mode > 4095 || (item.Mode & 3584) != (item.Before.Mode & 3584)) throw new IOException("权限计划路径、权限或特殊位不一致，请重新预览");
        }
    }
    public async Task ApplyAsync(PermissionPlan plan, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        Validate(plan);
        if (plan.Source.SourceId != plan.SourceId || !plan.Source.IsAvailable) throw new IOException("权限计划原来源已关闭，不改投其他来源");
        using var reservation = await FileReservations.AcquireAsync(new[] { plan.Source }, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, plan.Source.Closed); timeout.CancelAfter(TimeSpan.FromMinutes(2)); int done = 0, total = plan.Items.Count(i => !i.Skip);
        try
        {
            foreach (var item in plan.Items.Where(i => !i.Skip))
            {
                timeout.Token.ThrowIfCancellationRequested(); progress.Report(new(done, total, "校验权限版本"));
                var before = await InspectAsync(plan.Source, item.Before.Path, timeout.Token);
                if (before != item.Before) throw new IOException("计划后节点类型、大小、时间或权限发生变化，请重新预览");
                if (plan.Source is SftpLease sftp) await ApplySftp(sftp, item, timeout.Token);
                else await ((FtpFileSession)plan.Source).ApplyPermissionAsync(item.Before, item.Mode, timeout.Token);
                done++; progress.Report(new(done, total, "校验权限结果"));
            }
        }
        catch (Exception e) { throw new PermissionApplyException(done, total, e.Message, e); }
    }
    static async Task ApplySftp(SftpLease source, PermissionItem item, CancellationToken ct)
    {
        var expected = (RemoteStamp)item.Before.Version;
        if (expected.Directory)
        {
            ct.ThrowIfCancellationRequested(); using var commitDirectory = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try
            {
                await source.Client.SetPermissionsRawAsync(item.Before.Path, short.Parse(Convert.ToString(item.Mode, 8), CultureInfo.InvariantCulture), commitDirectory.Token);
                var after = RemoteStamp.From(await source.Client.LStatRawAsync(item.Before.Path, commitDirectory.Token));
                if (after.Mode != item.Mode || after.User != expected.User || after.Group != expected.Group || !after.Directory || after.Link) throw new IOException("目录权限结果未符合请求");
                return;
            }
            catch (SftpException e) { throw new IOException("服务器明确拒绝目录权限修改", e); }
            catch (Exception e) { try { source.Dispose(); } catch { } throw new CommitUncertainException("目录权限结果未确认，请核对", e); }
        }
        await using var handle = await source.Client.OpenNodeHandleRawAsync(item.Before.Path, expected.Directory, ct);
        if (RemoteStamp.From(await handle.AttributesAsync(ct)) != expected) throw new IOException("权限目标句柄与预览不一致");
        ct.ThrowIfCancellationRequested();
        using var commit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            await handle.SetPermissionsAsync(short.Parse(Convert.ToString(item.Mode, 8), CultureInfo.InvariantCulture), commit.Token);
            var after = RemoteStamp.From(await handle.AttributesAsync(commit.Token));
            if (after.Mode != item.Mode || after.User != expected.User || after.Group != expected.Group || after.Directory != expected.Directory) throw new IOException("服务器未按请求确认权限结果");
        }
        catch (SftpException e) { throw new IOException("服务器明确拒绝权限修改，未自动提权", e); }
        catch (Exception e) { try { source.Dispose(); } catch { } throw new CommitUncertainException("权限变更结果未确认，原文件通道已停止；请重新读取核对", e); }
    }
}
