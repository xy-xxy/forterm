using System.IO.Compression;
using System.Security.Cryptography;
using Forterm.Portable;
using Xunit;
namespace Forterm.Tests;
public sealed class PortableTests
{
    static MemoryStream Zip(string? extra = null, bool symlink = false)
    {
        var stream = new MemoryStream(); using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (string name in new[] { "Forterm.exe", "xshell.exe", "xftp.exe", "forterm-diagnose.exe" }.Concat(extra == null ? [] : new[] { extra }))
            { var entry = archive.CreateEntry("Forterm/" + name); if (symlink && name == extra) entry.ExternalAttributes = 0xA000 << 16; using var output = entry.Open(); output.Write(new byte[] { 1, 2, 3 }); }
        }
        stream.Position = 0; return stream;
    }
    static string Hash(Stream stream) { stream.Position = 0; string hash = Convert.ToHexString(SHA256.HashData(stream)); stream.Position = 0; return hash; }
    [Fact] public void PortableArgumentsPreserveEmptyUnicodeQuotesAndMoreThanEightKiBWithoutShell()
    {
        string[] args = ["", "-newtab", "合成 空格", "-url", "sftp://synthetic:" + new string('a', 12000) + "@127.0.0.1:1", "a\"b\\c"];
        var start = PortableRuntime.StartInfo(Path.Combine(Path.GetTempPath(), "Forterm.exe"), args); Assert.False(start.UseShellExecute); Assert.Equal(args, start.ArgumentList); Assert.Empty(start.Arguments);
    }
    [Fact] public void OwnedRuntimeExtractsEveryEntryAndRequiresCompleteHashAndEmptyDirectory()
    {
        using var zip = Zip("licenses/许可.txt"); string dir = PortableRuntime.CreateDirectory();
        try
        {
            var hashes = PortableRuntime.Extract(zip, Hash(zip), dir); Assert.Equal(5, hashes.Count); Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(dir, "licenses", "许可.txt")));
            Assert.Throws<IOException>(() => PortableRuntime.Extract(zip, Hash(zip), dir));
            string empty = Path.Combine(dir, "empty"); Directory.CreateDirectory(empty); Assert.Throws<IOException>(() => PortableRuntime.Extract(zip, new string('0', 64), empty)); Assert.Empty(Directory.GetFileSystemEntries(empty));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Theory] [InlineData("../escape")] [InlineData("C:/escape")] [InlineData("sub/../../escape")] [InlineData("CON.txt")] [InlineData("file. ")] [InlineData("sub\\escape")] [InlineData("FORTERM.exe")]
    public void UnsafeOrDuplicateWindowsPathsRefusedBeforeWriting(string name)
    {
        using var zip = Zip(name); string dir = PortableRuntime.CreateDirectory(); try { Assert.Throws<IOException>(() => PortableRuntime.Extract(zip, Hash(zip), dir)); Assert.Empty(Directory.GetFileSystemEntries(dir)); } finally { Directory.Delete(dir); }
    }
    [Fact] public void ArchiveSymlinkIsRefusedBeforeWriting()
    {
        using var zip = Zip("link", true); string dir = PortableRuntime.CreateDirectory(); try { Assert.Throws<IOException>(() => PortableRuntime.Extract(zip, Hash(zip), dir)); Assert.Empty(Directory.GetFileSystemEntries(dir)); } finally { Directory.Delete(dir); }
    }
}
