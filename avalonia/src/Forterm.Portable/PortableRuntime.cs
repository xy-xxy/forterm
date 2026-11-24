using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
namespace Forterm.Portable;

public static class PortableRuntime
{
    public static ProcessStartInfo StartInfo(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (string argument in arguments) start.ArgumentList.Add(argument); return start;
    }
    static void SafePath(string relative)
    {
        if (relative.Length is < 1 or > 2048 || relative.Contains('\\') || relative.StartsWith('/') || relative.Any(char.IsControl)) throw new IOException("便携载荷路径无效");
        foreach (string segment in relative.Split('/'))
        {
            string basename = segment.Split('.')[0].ToUpperInvariant();
            if (segment.Length is < 1 or > 255 || segment is "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.') || segment.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0 || basename is "CON" or "PRN" or "AUX" or "NUL" || basename.Length == 4 && (basename.StartsWith("COM") || basename.StartsWith("LPT")) && basename[3] is >= '1' and <= '9') throw new IOException("便携载荷路径无效");
        }
    }
    public static Dictionary<string, string> Extract(Stream payload, string expectedSHA256, string directory)
    {
        if (!payload.CanSeek || payload.Length > 256L * 1024 * 1024) throw new IOException("便携载荷边界无效");
        payload.Position = 0; string hash = Convert.ToHexString(SHA256.HashData(payload)); if (!hash.Equals(expectedSHA256, StringComparison.OrdinalIgnoreCase)) throw new IOException("便携载荷完整性不符"); payload.Position = 0;
        if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 || Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("便携载荷需要独立空目录");
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, true); var entries = new List<(ZipArchiveEntry Entry, string Name)>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); string? prefix = null; long length = 0;
        if (archive.Entries.Count > 4096) throw new IOException("便携载荷条目超限");
        foreach (var entry in archive.Entries)
        {
            int slash = entry.FullName.IndexOf('/'); if (slash < 1) throw new IOException("便携载荷缺少统一根目录"); string root = entry.FullName[..slash]; SafePath(root); prefix ??= root;
            if (prefix != root) throw new IOException("便携载荷根目录不一致");
            if (entry.FullName.EndsWith('/')) { SafePath(entry.FullName.TrimEnd('/')); continue; }
            string relative = entry.FullName[(slash + 1)..]; SafePath(relative); int kind = (entry.ExternalAttributes >> 16) & 0xF000;
            if (kind is not (0 or 0x8000) || !names.Add(relative) || entry.Length < 0 || entry.Length > 256L * 1024 * 1024 || entry.Length > 512L * 1024 * 1024 - length) throw new IOException("便携载荷类型、重复或大小无效");
            length += entry.Length; entries.Add((entry, relative));
        }
        foreach (string required in new[] { "Forterm.exe", "xshell.exe", "xftp.exe", "forterm-diagnose.exe" }) if (!names.Contains(required)) throw new IOException("便携载荷四入口不完整");
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (entry, name) in entries)
        {
            string path = Path.Combine(directory, name.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var input = entry.Open(); using var output = new FileStream(path, options); using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[32768]; long written = 0; int count;
            while ((count = input.Read(buffer)) > 0)
            {
                if (count > entry.Length - written) throw new IOException("便携条目解压超出声明边界");
                output.Write(buffer, 0, count); digest.AppendData(buffer, 0, count); written += count;
            }
            if (written != entry.Length) throw new IOException("便携条目长度不符"); hashes[name] = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
        }
        return hashes;
    }
    public static string CreateDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "Forterm-Avalonia-portable-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(path) || File.Exists(path)) throw new IOException("便携临时目录已存在");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path); else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
