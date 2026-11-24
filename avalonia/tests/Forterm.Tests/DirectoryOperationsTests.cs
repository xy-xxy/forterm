using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
public sealed class DirectoryOperationsTests
{
    sealed class Sink : IProgress<TransferProgress> { public void Report(TransferProgress p) { } }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task DirectoryCreateRenameAndEmptyDeleteRejectExistingAndNonempty(bool ftpMode)
    {
        using var sshFixture = new TransportTests.Fixture(posix: true); using var ftpFixture = new FtpTests.Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", sshFixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var sftp = await root.OpenFilesAsync(ct.Token); using var ftp = await FtpFileSession.ConnectAsync("127.0.0.1", ftpFixture.Port, "synthetic", "synthetic-only", ct.Token, new() { KeepAliveEnabled = false });
        IRemoteFileSource source = ftpMode ? ftp : sftp; var operations = new RemoteFileOperations(); var parent = await operations.DirectoryAsync(source, "/", ct.Token);
        await operations.CreateDirectoryAsync(parent, "created", ct.Token); if (!ftpMode) Assert.Equal(448u, (await sftp.Client.LStatRawAsync("/created", ct.Token)).PermissionBits);
        await Assert.ThrowsAnyAsync<IOException>(() => operations.CreateDirectoryAsync(parent, "created", ct.Token));
        var directory = await operations.CaptureNodeAsync(parent, "created", ct.Token); Assert.True(directory.IsDirectory); await operations.RenameAsync(directory, "renamed", ct.Token);
        var target = await operations.DirectoryAsync(source, "/renamed", ct.Token); var child = await operations.WriteNewAsync(target, "child.txt", new byte[] { 1, 2, 3 }, new Sink(), ct.Token);
        directory = await operations.CaptureNodeAsync(parent, "renamed", ct.Token);
        await Assert.ThrowsAnyAsync<IOException>(() => operations.RemoveAsync(directory, new Sink(), ct.Token)); Assert.True(source.IsAvailable);
        Assert.Equal(new byte[] { 1, 2, 3 }, await operations.ReadAsync(child, ct.Token));
        await operations.RemoveAsync(child, new Sink(), ct.Token); directory = await operations.CaptureNodeAsync(parent, "renamed", ct.Token);
        await operations.RemoveAsync(directory, new Sink(), ct.Token); await Assert.ThrowsAnyAsync<IOException>(() => operations.CaptureNodeAsync(parent, "renamed", ct.Token)); Assert.True(root.IsConnected);
        Assert.Equal(1, sshFixture.Stats().GetProperty("auth").GetInt32()); Assert.Equal(1, ftpFixture.Stats().GetProperty("auth").GetInt32());
    }
}
