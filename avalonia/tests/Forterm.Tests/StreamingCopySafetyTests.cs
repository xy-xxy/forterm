using System.Text;
using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
public sealed class StreamingCopySafetyTests
{
    sealed class Sink(Action<TransferProgress>? action = null) : IProgress<TransferProgress> { public void Report(TransferProgress p) => action?.Invoke(p); }
    [Fact] public async Task ConfirmedSftpOverwritePreservesOldModeAndStaleTargetCannotBeReplaced()
    {
        using var fixture = new TransportTests.Fixture(posix: true); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var files = await root.OpenFilesAsync(ct.Token); var operations = new RemoteFileOperations(); var a = await operations.DirectoryAsync(files, "/a", ct.Token); var b = await operations.DirectoryAsync(files, "/b", ct.Token);
        var source = await operations.WriteNewAsync(a, "source", "new-content"u8.ToArray(), new Sink(), ct.Token); await operations.WriteNewAsync(b, "target", "old"u8.ToArray(), new Sink(), ct.Token); files.Client.ChangePermissions("/b/target", 640);
        var plan = await operations.PlanTargetAsync(b, "target", ct.Token); Assert.True(plan.CanReplace);
        await Assert.ThrowsAsync<IOException>(() => operations.CopyPlannedAsync(source, plan, false, false, new Sink(), ct.Token));
        var saved = await operations.CopyPlannedAsync(source, plan, false, true, new Sink(), ct.Token); Assert.Equal(source.Hash, saved.Hash); Assert.Equal(416u, (await files.Client.LStatRawAsync("/b/target", ct.Token)).PermissionBits);
        var stale = await operations.PlanTargetAsync(b, "target", ct.Token); using (var output = files.Client.OpenWrite("/b/target")) { output.SetLength(0); output.Write("external-change"u8); output.Flush(); }
        await Assert.ThrowsAsync<IOException>(() => operations.CopyPlannedAsync(source, stale, false, true, new Sink(), ct.Token)); Assert.True(files.IsAvailable);
        using var value = new MemoryStream(); await files.Client.DownloadFileAsync("/b/target", value, ct.Token); Assert.StartsWith("external-change", Encoding.UTF8.GetString(value.ToArray()));
    }
    [Fact] public async Task CancelledLargeOverwriteKeepsOldTargetAndSource()
    {
        using var fixture = new TransportTests.Fixture(posix: true); fixture.Large(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var files = await root.OpenFilesAsync(ct.Token); var operations = new RemoteFileOperations(); var a = await operations.DirectoryAsync(files, "/a", ct.Token); var b = await operations.DirectoryAsync(files, "/b", ct.Token);
        var source = await operations.CaptureAsync(a, "large.bin", ct.Token); var old = await operations.WriteNewAsync(b, "target", "old"u8.ToArray(), new Sink(), ct.Token); var plan = await operations.PlanTargetAsync(b, "target", ct.Token);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operations.CopyPlannedAsync(source, plan, false, true, new Sink(p => { if (p.Phase == "传输中" && p.Bytes >= 65536) cancel.Cancel(); }), cancel.Token));
        Assert.Equal(old.Hash, (await operations.CaptureAsync(b, "target", ct.Token)).Hash); Assert.True(files.Client.Exists("/a/large.bin")); Assert.DoesNotContain(fixture.Files().EnumerateObject(), p => p.Name.StartsWith("b/.forterm-upload-"));
    }
    [Fact] public async Task SourceChangesAfterReadBeforeCommitKeepOldDestination()
    {
        using var fixture = new TransportTests.Fixture(posix: true); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var source = await root.OpenFilesAsync(ct.Token); using var destination = await root.OpenFilesAsync(ct.Token); var operations = new RemoteFileOperations();
        var a = await operations.DirectoryAsync(source, "/a", ct.Token); var b = await operations.DirectoryAsync(destination, "/b", ct.Token);
        var original = await operations.WriteNewAsync(a, "source", "original-data"u8.ToArray(), new Sink(), ct.Token); var old = await operations.WriteNewAsync(b, "old", "preserve-old"u8.ToArray(), new Sink(), ct.Token); var target = await operations.PlanTargetAsync(b, "old", ct.Token);
        bool changed = false;
        await Assert.ThrowsAsync<IOException>(() => operations.CopyPlannedAsync(original, target, false, true, new Sink(p =>
        {
            if (p.Phase.Contains("校验") && !changed) { changed = true; using var write = source.Client.OpenWrite("/a/source"); write.Write("modified-data"u8); write.Flush(); }
        }), ct.Token));
        Assert.True(changed); Assert.Equal(old.Hash, (await operations.CaptureAsync(b, "old", ct.Token)).Hash); Assert.True(source.IsAvailable); Assert.True(destination.IsAvailable);
    }
    [Fact] public async Task OppositeCopiesAcquireResourcesInOneOrderAndBothComplete()
    {
        using var first = new FtpTests.Fixture(); using var second = new FtpTests.Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var aSource = await FtpFileSession.ConnectAsync("127.0.0.1", first.Port, "synthetic", "synthetic-only", ct.Token, new() { KeepAliveEnabled = false });
        using var bSource = await FtpFileSession.ConnectAsync("127.0.0.1", second.Port, "synthetic", "synthetic-only", ct.Token, new() { KeepAliveEnabled = false });
        var operations = new RemoteFileOperations(); var a = await operations.DirectoryAsync(aSource, "/", ct.Token); var b = await operations.DirectoryAsync(bSource, "/", ct.Token);
        var one = await operations.CaptureAsync(a, "edit.txt", ct.Token); var two = await operations.CaptureAsync(b, "edit.txt", ct.Token);
        var result = await Task.WhenAll(operations.CopyAsync(one, b, "from-a.txt", false, new Sink(), ct.Token), operations.CopyAsync(two, a, "from-b.txt", false, new Sink(), ct.Token));
        Assert.Equal(one.Hash, result[0].Hash); Assert.Equal(two.Hash, result[1].Hash); Assert.Equal(1, first.Stats().GetProperty("auth").GetInt32()); Assert.Equal(1, second.Stats().GetProperty("auth").GetInt32());
    }
}
