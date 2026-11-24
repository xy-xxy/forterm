using System.Security.Cryptography;
using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
public sealed class LargeCopyTests
{
    sealed class Sink(Action<TransferProgress>? action = null) : IProgress<TransferProgress> { public void Report(TransferProgress p) => action?.Invoke(p); }
    static string ExpectedHash()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] block = Enumerable.Range(0, 32768).Select(i => (byte)i).ToArray();
        for (int i = 0; i < 544; i++) hash.AppendData(block); hash.AppendData("large-copy-end"u8); return Convert.ToHexString(hash.GetHashAndReset());
    }
    [Theory] [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task MoreThan16MiBStreamsAcrossIndependentAuthenticatedSources(bool fromFtp, bool toFtp)
    {
        using var sshFixture = new TransportTests.Fixture(posix: true); using var firstFtp = new FtpTests.Fixture(); using var secondFtp = new FtpTests.Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        sshFixture.Large(); firstFtp.Large();
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", sshFixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var sourceSftp = await root.OpenFilesAsync(ct.Token); using var targetSftp = await root.OpenFilesAsync(ct.Token);
        using var ftpA = await FtpFileSession.ConnectAsync("127.0.0.1", firstFtp.Port, "synthetic", "synthetic-only", ct.Token, new() { KeepAliveEnabled = false });
        using var ftpB = await FtpFileSession.ConnectAsync("127.0.0.1", secondFtp.Port, "synthetic", "synthetic-only", ct.Token, new() { KeepAliveEnabled = false });
        IRemoteFileSource source = fromFtp ? ftpA : sourceSftp, target = toFtp ? ftpB : targetSftp; var operations = new RemoteFileOperations();
        var inputDir = await operations.DirectoryAsync(source, "/a", ct.Token); var outputDir = await operations.DirectoryAsync(target, "/b", ct.Token);
        var file = await operations.CaptureAsync(inputDir, "large.bin", ct.Token); Assert.True(file.Length > RemoteFileOperations.MaxCopyBytes); Assert.Equal(ExpectedHash(), file.Hash);
        long maximum = 0; var result = await operations.CopyAsync(file, outputDir, "streamed.bin", false, new Sink(p => maximum = Math.Max(maximum, p.Bytes)), ct.Token);
        Assert.Equal(file.Hash, result.Hash); Assert.Equal(file.Length, result.Length); Assert.Equal(file.Length, maximum);
        var current = await operations.CaptureAsync(outputDir, "streamed.bin", ct.Token); Assert.Equal(ExpectedHash(), current.Hash);
        Assert.Equal(1, sshFixture.Stats().GetProperty("auth").GetInt32()); Assert.Equal(1, firstFtp.Stats().GetProperty("auth").GetInt32()); Assert.Equal(1, secondFtp.Stats().GetProperty("auth").GetInt32());
    }
    [Fact] public async Task SameSftpLargeMoveUsesConfirmedAtomicReplaceAndPreservesSourceMode()
    {
        using var fixture = new TransportTests.Fixture(posix: true); fixture.Large(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token); using var files = await root.OpenFilesAsync(ct.Token); var operations = new RemoteFileOperations();
        var a = await operations.DirectoryAsync(files, "/a", ct.Token); var b = await operations.DirectoryAsync(files, "/b", ct.Token); files.Client.ChangePermissions("/a/large.bin", 640);
        var source = await operations.CaptureAsync(a, "large.bin", ct.Token); await operations.WriteNewAsync(b, "replace.bin", "old"u8.ToArray(), new Sink(), ct.Token); files.Client.ChangePermissions("/b/replace.bin", 600); var target = await operations.PlanTargetAsync(b, "replace.bin", ct.Token);
        await Assert.ThrowsAsync<IOException>(() => operations.CopyPlannedAsync(source, target, true, false, new Sink(), ct.Token));
        var moved = await operations.CopyPlannedAsync(source, target, true, true, new Sink(), ct.Token); Assert.Equal(ExpectedHash(), moved.Hash); Assert.False(files.Client.Exists("/a/large.bin")); Assert.Equal(416u, (await files.Client.LStatRawAsync("/b/replace.bin", ct.Token)).PermissionBits);
        Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
    }
    [Fact] public async Task SameFtpLargeCopyRefusesButLargeMoveRenamesWithoutAnotherLogin()
    {
        using var fixture = new FtpTests.Fixture(); fixture.Large(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var source = await FtpFileSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", ct.Token, new() { KeepAliveEnabled = false }); using var sibling = source.CreateSibling(); var operations = new RemoteFileOperations();
        Assert.Equal(source.Resource.Id, sibling.Resource.Id);
        var a = await operations.DirectoryAsync(source, "/a", ct.Token); var b = await operations.DirectoryAsync(sibling, "/b", ct.Token); var file = await operations.CaptureAsync(a, "large.bin", ct.Token);
        await Assert.ThrowsAsync<IOException>(() => operations.CopyAsync(file, b, "must-not-copy", false, new Sink(), ct.Token));
        var moved = await operations.CopyAsync(file, b, "moved.bin", true, new Sink(), ct.Token); Assert.Equal(file.Hash, moved.Hash);
        Assert.False(fixture.Files().TryGetProperty("a/large.bin", out _)); Assert.True(fixture.Files().TryGetProperty("b/moved.bin", out _)); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
    }
}
