using System.Text;
namespace Forterm.Core;
public static class LocalTextFiles
{
    public static async Task<bool> WriteAsync(string path, string text, Func<LocalFileVersion, CancellationToken, Task<bool>> confirm, CancellationToken ct)
    {
        byte[] content = new UTF8Encoding(false, true).GetBytes(text); if (content.Length > 4 * 1024 * 1024) throw new IOException("配置导出超过4MiB");
        var safe = new SafeTransfers(); var target = await safe.CaptureLocalAsync(path, ct);
        if (target.Stamp != null && !await confirm(target, ct)) return false;
        string temp = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target.Path)!, ".forterm-config-" + Guid.NewGuid().ToString("N")); string? id = null;
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temp, options)) { id = LocalStamp.Read(temp)!.Id; await stream.WriteAsync(content, ct); stream.Flush(true); }
            var now = await safe.CaptureLocalAsync(target.Path, ct);
            if (now.Stamp != target.Stamp || now.Hash != target.Hash || !LocalStamp.SameDirectory(now.Parent, target.Parent) || LocalStamp.Read(temp)?.Id != id) throw new IOException("导出目标或目录已变化，未覆盖");
            ct.ThrowIfCancellationRequested(); File.Move(temp, target.Path, target.Stamp != null); id = null; return true;
        }
        finally { if (id != null && LocalStamp.Read(temp)?.Id == id) File.Delete(temp); }
    }
}
