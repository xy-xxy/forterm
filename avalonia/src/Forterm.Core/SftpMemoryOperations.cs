using System.Security.Cryptography;
using Renci.SshNet.Common;
namespace Forterm.Core;

public sealed partial class SafeTransfers
{
    public async Task<byte[]> ReadMemoryAsync(RemoteDirectoryBinding directory, RemoteFileVersion expected, int maxBytes, CancellationToken ct)
    {
        if (maxBytes is < 0 or > 16 * 1024 * 1024 || expected.Stamp is not { Regular: true, Link: false } || expected.Stamp.Length > maxBytes) throw new IOException("内存复制限16MiB普通文件；目录和链接不复制");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, directory.Source.Closed); lifetime.CancelAfter(options.OverallTimeout); var token = lifetime.Token;
        await CheckRemote(directory, expected, token); using var output = new MemoryStream();
        await using (var input = await Metadata(t => directory.Source.Client.OpenRawAsync(expected.Path, FileMode.Open, FileAccess.Read, t), token))
        {
            if (RemoteStamp.From(await Metadata(t => input.ReadHandleAttributesAsync(t), token)) != expected.Stamp) throw new IOException("打开的源版本不一致");
            byte[] block = new byte[32768];
            while (true)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(token); idle.CancelAfter(options.IdleTimeout); int n = await input.ReadAsync(block, idle.Token); if (n == 0) break;
                if (output.Length + n > maxBytes || output.Length + n > expected.Stamp.Length) throw new IOException("源内容在读取时超过边界"); await output.WriteAsync(block.AsMemory(0, n), token);
            }
            if (RemoteStamp.From(await Metadata(t => input.ReadHandleAttributesAsync(t), token)) != expected.Stamp) throw new IOException("源文件读取时属性变化");
        }
        byte[] bytes = output.ToArray();
        if (bytes.LongLength != expected.Stamp.Length || Convert.ToHexString(SHA256.HashData(bytes)) != expected.Hash) throw new IOException("源内容在读取时变化");
        await CheckRemote(directory, expected, token); return bytes;
    }
    public async Task<RemoteFileVersion> UploadMemoryNewAsync(RemoteDirectoryBinding directory, RemoteFileVersion target, ReadOnlyMemory<byte> input, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        if (input.Length > 16 * 1024 * 1024 || target.Stamp != null) throw new IOException("内存复制限16MiB，且只创建新名称，不覆盖");
        byte[] bytes = input.ToArray(); string hash = Convert.ToHexString(SHA256.HashData(bytes));
        await UploadContentAsync(directory, target, bytes.Length, hash, false, _ => Task.FromResult<Stream>(new MemoryStream(bytes, false)), _ => Task.CompletedTask, progress, ct);
        try
        {
            using var verify = CancellationTokenSource.CreateLinkedTokenSource(directory.Source.Closed); verify.CancelAfter(options.MetadataTimeout);
            var saved = await CaptureRemoteAsync(directory, Path.GetFileName(target.Path), verify.Token, bytes.Length);
            if (saved.Hash != hash) throw new IOException("写入后的版本已变化"); return saved;
        }
        catch (Exception e) { throw new CommitUncertainException("新文件提交已确认，但后续版本核验未完成，请核对目标，不自动重试", e); }
    }
    public async Task RemoveVerifiedAsync(RemoteDirectoryBinding directory, RemoteFileVersion expected, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        if (expected.Stamp is not { Regular: true, Link: false }) throw new IOException("只删除已核对普通文件，不跟随链接或递归删除");
        await CheckRemote(directory, expected, ct); ct.ThrowIfCancellationRequested(); Available(directory);
        progress.Report(new(0, expected.Stamp.Length, "提交中"));
        using var commit = new CancellationTokenSource(options.MetadataTimeout);
        try { await directory.Source.Client.RemoveRawAsync(expected.Path, commit.Token); }
        catch (SftpException e) { throw new IOException("服务器明确拒绝删除，未重试", e); }
        catch (Exception e) { try { directory.Source.Dispose(); } catch { } throw new CommitUncertainException("删除回执未确认，原文件通道已停止；请核对源是否仍存在", e); }
    }
    public async Task RenameVerifiedAsync(RemoteDirectoryBinding directory, RemoteFileVersion source, string newName, CancellationToken ct)
    {
        if (source.Stamp is not { Regular: true, Link: false }) throw new IOException("只重命名已核对普通文件，不跟随链接");
        var target = await CaptureRemoteAsync(directory, newName, ct);
        if (target.Stamp != null || source.Path == target.Path) throw new IOException("目标已存在或与源相同，未改名");
        await CheckRemote(directory, source, ct); await CheckRemote(directory, target, ct); ct.ThrowIfCancellationRequested(); Available(directory);
        using var commit = new CancellationTokenSource(options.MetadataTimeout);
        try { await directory.Source.Client.RenameRawAsync(source.Path, target.Path, false, commit.Token); }
        catch (SftpException e) { throw new IOException("服务器明确拒绝改名，未删除目标或回退", e); }
        catch (Exception e) { try { directory.Source.Dispose(); } catch { } throw new CommitUncertainException("改名回执未确认，原文件通道已停止；请核对两个路径", e); }
    }
}
