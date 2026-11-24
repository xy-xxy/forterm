using System.Text;
using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
public sealed class FileOperationsTests
{
    sealed class ProgressSink(Action<TransferProgress>? callback = null) : IProgress<TransferProgress> { public void Report(TransferProgress p) => callback?.Invoke(p); }
    [Theory] [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task CopyAndMoveAcrossSftpAndFtpShareOriginalAuthenticatedSources(bool fromFtp, bool toFtp)
    {
        using var sshFixture = new TransportTests.Fixture(posix: true); using var ftpFixture = new FtpTests.Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", sshFixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var files = await root.OpenFilesAsync(ct.Token); using var ftp = await FtpFileSession.ConnectAsync("127.0.0.1", ftpFixture.Port, "synthetic", "synthetic-only", ct.Token, new() { KeepAliveEnabled = false });
        using var shell = await root.OpenShellAsync(80, 24, ct.Token); var operations = new RemoteFileOperations(); byte[] bytes = new byte[1024 * 1024 + 7]; Random.Shared.NextBytes(bytes);
        IRemoteFileSource source = fromFtp ? ftp : files, target = toFtp ? ftp : files;
        var a = await operations.DirectoryAsync(source, "/a", ct.Token); var b = await operations.DirectoryAsync(target, "/b", ct.Token);
        var original = await operations.WriteNewAsync(a, "source.bin", bytes, new ProgressSink(), ct.Token);
        var copied = await operations.CopyAsync(original, b, "copy.bin", false, new ProgressSink(), ct.Token); Assert.Equal(original.Hash, copied.Hash);
        Assert.Equal(bytes, await operations.ReadAsync(copied, ct.Token)); Assert.Equal(original.Hash, (await operations.CaptureAsync(a, "source.bin", ct.Token)).Hash);
        var moved = await operations.CopyAsync(original, b, "moved.bin", true, new ProgressSink(), ct.Token); Assert.Equal(original.Hash, moved.Hash);
        await Assert.ThrowsAnyAsync<IOException>(() => operations.CaptureAsync(a, "source.bin", ct.Token));
        Assert.Equal(1, sshFixture.Stats().GetProperty("auth").GetInt32()); Assert.Equal(1, ftpFixture.Stats().GetProperty("auth").GetInt32()); Assert.True(root.IsConnected);
    }
    [Fact] public async Task ChangedSourceAfterCopyIsPreservedAndMoveReportsPartial()
    {
        using var fixture = new TransportTests.Fixture(posix: true); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var source = await root.OpenFilesAsync(ct.Token); using var independent = await root.OpenFilesAsync(ct.Token); var operations = new RemoteFileOperations();
        var a = await operations.DirectoryAsync(source, "/a", ct.Token); var b = await operations.DirectoryAsync(independent, "/b", ct.Token); var original = await operations.WriteNewAsync(a, "source.txt", Encoding.UTF8.GetBytes("old"), new ProgressSink(), ct.Token);
        bool changed = false;
        var progress = new ProgressSink(p => { if (p.Phase == "校验源版本" && !changed) { changed = true; using var file = source.Client.OpenWrite("/a/source.txt"); file.SetLength(0); file.Write(Encoding.UTF8.GetBytes("external change")); file.Flush(); } });
        var error = await Assert.ThrowsAsync<PartialMoveException>(() => operations.CopyAsync(original, b, "copied.txt", true, progress, ct.Token)); Assert.Contains("目标副本已提交", error.Message);
        var now = await operations.CaptureAsync(a, "source.txt", ct.Token); Assert.Equal("external change", Encoding.UTF8.GetString(await operations.ReadAsync(now, ct.Token)));
        Assert.Equal("old", Encoding.UTF8.GetString(await operations.ReadAsync(await operations.CaptureAsync(b, "copied.txt", ct.Token), ct.Token))); Assert.True(source.IsAvailable);
    }
    [Fact] public async Task ExistingTargetAndClosedSourceCannotOverwriteOrRebind()
    {
        using var fixture = new TransportTests.Fixture(posix: true); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var source = await root.OpenFilesAsync(ct.Token); using var other = await root.OpenFilesAsync(ct.Token); var operations = new RemoteFileOperations();
        var a = await operations.DirectoryAsync(source, "/a", ct.Token); var b = await operations.DirectoryAsync(other, "/b", ct.Token);
        var file = await operations.WriteNewAsync(a, "source.txt", Encoding.UTF8.GetBytes("source"), new ProgressSink(), ct.Token); var old = await operations.WriteNewAsync(b, "old.txt", Encoding.UTF8.GetBytes("keep"), new ProgressSink(), ct.Token);
        await Assert.ThrowsAsync<IOException>(() => operations.CopyAsync(file, b, "old.txt", false, new ProgressSink(), ct.Token)); Assert.Equal("keep", Encoding.UTF8.GetString(await operations.ReadAsync(old, ct.Token)));
        source.Dispose(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operations.CopyAsync(file, b, "must-not-create", false, new ProgressSink(), ct.Token));
        Assert.True(other.IsAvailable); Assert.False(other.Client.Exists("/b/must-not-create")); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
    }
}
