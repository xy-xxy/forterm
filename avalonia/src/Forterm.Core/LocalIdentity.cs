using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Forterm.Core;

public sealed record LocalStamp(string Id, long Length, long ModifiedSeconds, long ModifiedNanos, bool Directory, uint Mode)
{
    public static LocalStamp? Read(string path)
    {
        if (OperatingSystem.IsMacOS())
        {
            byte[] stat = new byte[160]; int rc = LStat(path, stat);
            if (rc != 0) { int error = Marshal.GetLastPInvokeError(); if (error == 2) return null; throw new IOException("无法核对本地路径身份", new System.ComponentModel.Win32Exception(error)); }
            int type = BitConverter.ToUInt16(stat, 4) & 0xF000; if (type is not (0x8000 or 0x4000)) throw new IOException("本地链接或非普通文件不支持安全传输");
            return new($"{BitConverter.ToUInt32(stat, 0):x}:{BitConverter.ToUInt64(stat, 8):x}", BitConverter.ToInt64(stat, 96), BitConverter.ToInt64(stat, 48), BitConverter.ToInt64(stat, 56), type == 0x4000, (uint)(BitConverter.ToUInt16(stat, 4) & 0xFFF));
        }
        if (OperatingSystem.IsWindows())
        {
            if (path.StartsWith(@"\\")) throw new IOException("当前安全提交仅支持本机磁盘路径");
            using var h = CreateFile(path, 0x80, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (h.IsInvalid) { int error = Marshal.GetLastPInvokeError(); if (error is 2 or 3) return null; throw new IOException("无法核对本地路径身份", new System.ComponentModel.Win32Exception(error)); }
            if (!GetFileInformationByHandle(h, out var i) || GetFileType(h) != 1) throw new IOException("无法核对本地磁盘文件");
            if ((i.Attributes & 0x400) != 0) throw new IOException("本地符号链接或junction不支持安全传输");
            return new($"{i.Volume:x}:{i.IndexHigh:x8}{i.IndexLow:x8}", ((long)i.SizeHigh << 32) | i.SizeLow, ((long)i.WriteHigh << 32) | i.WriteLow, 0, (i.Attributes & 0x10) != 0, 0);
        }
        throw new PlatformNotSupportedException("本地身份核验目前面向Windows与macOS");
    }
    public static string FullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0) throw new IOException("本地路径无效"); path = Path.GetFullPath(path);
        if (OperatingSystem.IsMacOS()) foreach (string alias in new[] { "var", "tmp" })
        {
            string prefix = "/" + alias + "/"; if (!path.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string? link = new DirectoryInfo("/" + alias).LinkTarget;
            if (link != null && Path.GetFullPath(Path.Combine("/", link)) == "/private/" + alias) path = "/private" + path;
        }
        return path;
    }
    public static LocalStamp RequireParent(string path)
    {
        string? parent = Path.GetDirectoryName(FullPath(path)); if (parent == null) throw new IOException("不能以文件系统根为文件目标");
        var parents = new Stack<string>(); for (string? current = parent; current != null; current = Path.GetDirectoryName(current)) parents.Push(current);
        LocalStamp? stamp = null; foreach (string current in parents) { stamp = Read(current); if (stamp is not { Directory: true }) throw new IOException("本地父目录不存在或不是目录"); }
        return stamp!;
    }
    public static bool SameFile(LocalStamp? a, LocalStamp? b) => a == b;
    public static bool SameDirectory(LocalStamp a, LocalStamp b) => a.Directory && b.Directory && a.Id == b.Id;
    [DllImport("libSystem.B.dylib", EntryPoint = "lstat", SetLastError = true)] static extern int LStat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [Out] byte[] stat);
    [StructLayout(LayoutKind.Sequential)] struct FileInformation { public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
    [DllImport("kernel32.dll")] static extern uint GetFileType(SafeFileHandle handle);
}
