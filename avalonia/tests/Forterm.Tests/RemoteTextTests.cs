using System.Text;
using Forterm.Core;
using Xunit;
namespace Forterm.Tests;

public sealed class RemoteTextTests
{
    static readonly IProgress<TransferProgress> Progress = new Sink();
    sealed class Sink : IProgress<TransferProgress> { public void Report(TransferProgress p) { } }
    static async Task Write(SftpLease source, string path, byte[] bytes, CancellationToken ct)
    { await using var file = await source.Client.OpenRawAsync(path, FileMode.Create, FileAccess.Write, ct); await file.WriteAsync(bytes, ct); await file.FlushAsync(ct); }
    [Fact] public async Task BomAndMixedNewlinesRoundTripPreservingModeAndDetectSameStampConflict()
    {
        using var fixture = new TransportTests.Fixture(posix: true); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var source = await root.OpenFilesAsync(ct.Token); var safe = new SafeTransfers();
        await source.UploadNewAsync(new MemoryStream(RemoteText.Encode("中\r\nline\n", true)), "/edit.txt", ct.Token);
        var directory = await safe.CaptureDirectoryAsync(source, "/", ct.Token); var original = await safe.OpenTextAsync(directory, "edit.txt", ct.Token);
        Assert.True(original.HasBom); Assert.Equal("混合换行", original.Newlines);
        var version = await safe.SaveTextAsync(original, "修改\r\nline\n", true, Progress, ct.Token);
        Assert.Equal(original.Version.Stamp!.Mode, version.Stamp!.Mode);
        var next = await safe.OpenTextAsync(directory, "edit.txt", ct.Token); Assert.True(next.HasBom); Assert.Equal("修改\r\nline\n", next.Text);
        var unchangedStamp = next.Version.Stamp!.Modified;
        await Write(source, "/edit.txt", RemoteText.Encode("外改\r\nline\n", true), ct.Token); source.Client.SetLastWriteTimeUtc("/edit.txt", unchangedStamp);
        await Assert.ThrowsAsync<IOException>(() => safe.SaveTextAsync(next, "我的草稿", true, Progress, ct.Token));
        Assert.Equal("外改\r\nline\n", (await safe.OpenTextAsync(directory, "edit.txt", ct.Token)).Text);
    }
    [Theory] [InlineData("binary")] [InlineData("invalid-utf8")] [InlineData("large")]
    public async Task RefusesBinaryInvalidUtf8AndLargeFileBeforeEditing(string type)
    {
        using var fixture = new TransportTests.Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var source = await root.OpenFilesAsync(ct.Token); byte[] data = type == "binary" ? [65, 0, 66] : type == "invalid-utf8" ? [0xff, 0xfe] : new byte[RemoteText.MaxBytes + 1];
        await source.UploadNewAsync(new MemoryStream(data), "/no-edit", ct.Token); var safe = new SafeTransfers(); var directory = await safe.CaptureDirectoryAsync(source, "/", ct.Token);
        await Assert.ThrowsAsync<IOException>(() => safe.OpenTextAsync(directory, "no-edit", ct.Token)); Assert.True(root.IsConnected);
    }
    [Fact] public async Task NewFileIsExclusiveAndServerWithoutAtomicReplacePreservesExisting()
    {
        using var fixture = new TransportTests.Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var source = await root.OpenFilesAsync(ct.Token); var safe = new SafeTransfers(); var directory = await safe.CaptureDirectoryAsync(source, "/", ct.Token);
        var original = new RemoteText(directory, await safe.CaptureRemoteAsync(directory, "new.txt", ct.Token), "", false);
        await safe.SaveTextAsync(original, "new file", true, Progress, ct.Token);
        await Assert.ThrowsAsync<IOException>(() => safe.SaveTextAsync(original, "must not replace", true, Progress, ct.Token));
        var opened = await safe.OpenTextAsync(directory, "new.txt", ct.Token);
        await Assert.ThrowsAsync<IOException>(() => safe.SaveTextAsync(opened, "unsupported overwrite", true, Progress, ct.Token));
        Assert.Equal("new file", (await safe.OpenTextAsync(directory, "new.txt", ct.Token)).Text);
    }
    [Fact] public async Task FourCapabilitiesRunOnOneAuthenticationAndExecDenialDoesNotBreakEditor()
    {
        foreach (string? deny in new string?[] { null, "FIXTURE_DENY_EXEC" })
        {
            using var fixture = new TransportTests.Fixture(deny, posix: true); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
            using var source = await root.OpenFilesAsync(ct.Token); using var shell = await root.OpenShellAsync(80, 24, ct.Token); using var queue = new TransferQueue(); var safe = new SafeTransfers();
            await source.UploadNewAsync(new MemoryStream(Encoding.UTF8.GetBytes("before")), "/edit.txt", ct.Token); var directory = await safe.CaptureDirectoryAsync(source, "/", ct.Token); var original = await safe.OpenTextAsync(directory, "edit.txt", ct.Token);
            var metrics = root.ReadLinuxMetricsAsync(ct.Token);
            var save = queue.Add("edit", "/", async (p, t) => { await safe.SaveTextAsync(original, "after", true, p, t); }, source.Closed);
            var upload = queue.Add("parallel", "/", (p, t) => source.UploadNewAsync(new MemoryStream(new byte[] { 4, 5, 6 }), "/parallel", t), source.Closed);
            await shell.WriterStream.WriteAsync(Encoding.UTF8.GetBytes("four-capabilities"), ct.Token); await shell.WriterStream.FlushAsync(ct.Token);
            if (deny != null) await Assert.ThrowsAnyAsync<Exception>(() => metrics); else Assert.NotNull(await metrics);
            Assert.Equal(TransferState.Completed, (await save.Completion.WaitAsync(ct.Token)).State); Assert.Equal(TransferState.Completed, (await upload.Completion.WaitAsync(ct.Token)).State);
            Assert.Equal("after", (await safe.OpenTextAsync(directory, "edit.txt", ct.Token)).Text);
            var stats = fixture.Stats(); Assert.Equal(1, stats.GetProperty("auth").GetInt32()); Assert.Equal(1, stats.GetProperty("sftp").GetInt32()); Assert.Equal(1, stats.GetProperty("shells").GetInt32());
            string input = string.Concat(stats.GetProperty("input").EnumerateArray().Select(x => x.GetString())); Assert.Equal(Convert.ToHexString(Encoding.UTF8.GetBytes("four-capabilities")).ToLowerInvariant(), input);
        }
    }
    [Fact] public async Task CancellationAtCommitDoesNotFalselyReportUnsubmitted()
    {
        using var fixture = new TransportTests.Fixture(posix: true, commitDelay: .3); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var source = await root.OpenFilesAsync(ct.Token); var safe = new SafeTransfers();
        await source.UploadNewAsync(new MemoryStream(Encoding.UTF8.GetBytes("old")), "/edit.txt", ct.Token);
        var original = await safe.OpenTextAsync(await safe.CaptureDirectoryAsync(source, "/", ct.Token), "edit.txt", ct.Token);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct.Token);
        var saved = await safe.SaveTextAsync(original, "committed", true, new CancelAtCommit(cancel), cancel.Token);
        Assert.True(cancel.IsCancellationRequested); Assert.NotEqual(original.Version.Hash, saved.Hash);
        Assert.Equal("committed", (await safe.OpenTextAsync(original.Directory, "edit.txt", ct.Token)).Text);
    }
    sealed class CancelAtCommit(CancellationTokenSource source) : IProgress<TransferProgress> { public void Report(TransferProgress p) { if (p.Phase == "提交中") source.Cancel(); } }
    [Fact] public async Task SourceClosePreventsSaveWithoutRebindingOrReauthentication()
    {
        using var fixture = new TransportTests.Fixture(posix: true); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var source = await root.OpenFilesAsync(ct.Token); using var other = await root.OpenFilesAsync(ct.Token); var safe = new SafeTransfers();
        await source.UploadNewAsync(new MemoryStream(Encoding.UTF8.GetBytes("before")), "/edit.txt", ct.Token); var original = await safe.OpenTextAsync(await safe.CaptureDirectoryAsync(source, "/", ct.Token), "edit.txt", ct.Token); source.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => safe.SaveTextAsync(original, "after", true, Progress, ct.Token)); Assert.True(other.IsAvailable); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
    }
}
