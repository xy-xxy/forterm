using System.Diagnostics;
using System.Text.Json;
using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
public sealed class FtpTests
{
    [Fact] public async Task FtpListsLiteralPercentUnicodeAndReadsWithOneAuthentication()
    {
        using var fixture = new Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var source = await FtpFileSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", ct.Token, new() { KeepAliveEnabled = false });
        var listing = await source.ListAsync("/", ct.Token); Assert.Contains(listing.Entries, f => f.Name == "edit.txt" && f.Regular); Assert.Contains(listing.Entries, f => f.Name == "link.txt" && f.Link);
        var version = await source.CaptureAsync(listing.Directory, "edit.txt", ct.Token, RemoteText.MaxBytes); var bytes = await source.ReadBoundedAsync(listing.Directory, version, RemoteText.MaxBytes, ct.Token);
        Assert.Equal("合成FTP文件\r\nline\n", System.Text.Encoding.UTF8.GetString(bytes));
        using var sibling = source.CreateSibling(); source.Dispose(); Assert.True(sibling.IsAvailable); await sibling.ListAsync("/a", ct.Token);
        var stats = fixture.Stats(); Assert.Equal(1, stats.GetProperty("connections").GetInt32()); Assert.Equal(1, stats.GetProperty("auth").GetInt32());
        await Assert.ThrowsAsync<FtpPolicyException>(() => sibling.CaptureAsync(new(sibling, sibling.SourceId, "/"), "link.txt", ct.Token));
    }
    sealed class ProgressSink : IProgress<TransferProgress> { public void Report(TransferProgress value) { } }
    [Fact] public async Task UploadDownloadPercentPathAndExistingDestinationProtection()
    {
        using var fixture = new Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var source = await FtpFileSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", ct.Token, new() { KeepAliveEnabled = false });
        string folder = Path.Combine(Path.GetTempPath(), "forterm-ftp-files-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string input = Path.Combine(folder, "input"), output = Path.Combine(folder, "download"); byte[] bytes = new byte[1024 * 1024 + 13]; Random.Shared.NextBytes(bytes); await File.WriteAllBytesAsync(input, bytes, ct.Token);
            var local = new SafeTransfers(); var directory = await source.CaptureDirectoryAsync("/a", ct.Token); var target = await source.CaptureAsync(directory, "中文 空格%20.bin", ct.Token);
            var uploaded = await source.UploadAsync(new(directory, target, await local.CaptureLocalAsync(input, ct.Token)), new ProgressSink(), ct.Token);
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), uploaded.Hash);
            await source.DownloadAsync(new(directory, uploaded, await local.CaptureLocalAsync(output, ct.Token), false), new ProgressSink(), ct.Token);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(output, ct.Token)); Assert.True(fixture.Files().TryGetProperty("a/中文 空格%20.bin", out _)); Assert.False(fixture.Files().TryGetProperty("a/中文 空格 .bin", out _));
            var inputVersion = await local.CaptureLocalAsync(input, ct.Token); await Assert.ThrowsAsync<FtpPolicyException>(() => source.UploadAsync(new(directory, uploaded, inputVersion), new ProgressSink(), ct.Token));
            Assert.True(source.IsAvailable); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
        }
        finally { Directory.Delete(folder, true); }
    }
    [Fact] public async Task FtpTextPreservesBomAndRequiresNewNameForSave()
    {
        using var fixture = new Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var source = await FtpFileSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", ct.Token, new() { KeepAliveEnabled = false });
        var directory = await source.CaptureDirectoryAsync("/", ct.Token); var original = await source.OpenTextAsync(directory, "edit.txt", ct.Token); Assert.Equal("合成FTP文件\r\nline\n", original.Text);
        var saved = await source.SaveTextNewAsync(directory, "副本 %20.txt", "UTF-8\r\n新文件\n", true, new ProgressSink(), ct.Token);
        var loaded = await source.OpenTextAsync(directory, "副本 %20.txt", ct.Token); Assert.True(loaded.HasBom); Assert.Equal(saved.Text, loaded.Text);
        await Assert.ThrowsAsync<FtpPolicyException>(() => source.SaveTextNewAsync(directory, "edit.txt", "must keep old", false, new ProgressSink(), ct.Token));
        Assert.Equal(original.Text, (await source.OpenTextAsync(directory, "edit.txt", ct.Token)).Text);
    }
    [Fact] public async Task MissingRenameReplyReportsUncertainWithoutReplayingCredentials()
    {
        using var fixture = new Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var source = await FtpFileSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", ct.Token, new() { KeepAliveEnabled = false });
        var directory = await source.CaptureDirectoryAsync("/", ct.Token); var target = await source.CaptureAsync(directory, "ack-unknown.txt", ct.Token); fixture.DropReply("RNTO");
        await Assert.ThrowsAsync<CommitUncertainException>(() => source.UploadMemoryNewAsync(directory, target, new byte[] { 1, 2, 3 }, new ProgressSink(), ct.Token));
        Assert.True(fixture.Files().TryGetProperty("ack-unknown.txt", out _)); Assert.False(source.IsAvailable); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
    }
    [Fact] public async Task MissingCompletionReplyNeverBecomesEmptySuccessfulListingOrReconnect()
    {
        using var fixture = new Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var source = await FtpFileSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", ct.Token, new() { KeepAliveEnabled = false });
        fixture.DropReply("LIST"); await Assert.ThrowsAnyAsync<Exception>(() => source.ListAsync("/", ct.Token)); Assert.False(source.IsAvailable);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ListAsync("/", ct.Token)); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32()); Assert.Equal(1, fixture.Stats().GetProperty("connections").GetInt32());
    }
    [Fact] public async Task ParentLinksAreRefusedForListingCaptureAndTextWithoutAnotherLogin()
    {
        using var fixture = new Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var source = await FtpFileSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", ct.Token, new() { KeepAliveEnabled = false });
        await Assert.ThrowsAsync<FtpPolicyException>(() => source.ListAsync("/link-dir", ct.Token));
        await Assert.ThrowsAsync<FtpPolicyException>(() => source.CaptureAsync(new(source, source.SourceId, "/link-dir"), "new.txt", ct.Token));
        Assert.True(source.IsAvailable); await source.ListAsync("/a", ct.Token); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
    }
    [Fact] public async Task FtpHeartbeatIsOnlyProtocolNoopAndStopsWithLastReference()
    {
        using var fixture = new Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var source = await FtpFileSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", ct.Token, new() { KeepAliveSeconds = 5 });
        await Task.Delay(5300, ct.Token); var stats = fixture.Stats();
        Assert.Contains(stats.GetProperty("commands").EnumerateArray(), c => c.GetProperty("command").GetString() == "NOOP");
        Assert.DoesNotContain(stats.GetProperty("commands").EnumerateArray(), c => c.GetProperty("command").GetString() is "LIST" or "RETR" or "STOR");
        Assert.Equal(1, stats.GetProperty("auth").GetInt32()); source.Dispose();
    }
    [Fact] public async Task CancellationAtFtpCommitWaitsForAcknowledgementInsteadOfFalseUnsubmitted()
    {
        using var fixture = new Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var source = await FtpFileSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", ct.Token, new() { KeepAliveEnabled = false });
        var directory = await source.CaptureDirectoryAsync("/", ct.Token); var target = await source.CaptureAsync(directory, "commit-cancelled.txt", ct.Token);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct.Token);
        var saved = await source.UploadMemoryNewAsync(directory, target, new byte[] { 1, 2, 3 }, new CancelAtCommit(cancel), cancel.Token);
        Assert.True(cancel.IsCancellationRequested); Assert.NotNull(saved.Stamp); Assert.True(fixture.Files().TryGetProperty("commit-cancelled.txt", out _)); Assert.True(source.IsAvailable);
    }
    sealed class CancelAtCommit(CancellationTokenSource source) : IProgress<TransferProgress> { public void Report(TransferProgress value) { if (value.Phase == "提交中") source.Cancel(); } }
    [Theory] [InlineData("/a/../b")] [InlineData("/a\r\nDELE /x")] [InlineData("/a\\b")]
    public void RejectsControlAndTraversalWithoutSilentlyChangingPath(string path) => Assert.Throws<FtpPolicyException>(() => FtpFileSession.Absolute(path, "/"));
    internal sealed class Fixture : IDisposable
    {
        readonly Process process; public int Port { get; }
        public Fixture()
        {
            string root = TransportTests.FindRoot(); var start = new ProcessStartInfo(Path.Combine(root, "tooling/fixture-venv/bin/python3")) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(root, "tooling/ftp_fixture.py")); process = Process.Start(start)!; string? line = process.StandardOutput.ReadLine();
            if (line == null) throw new Exception(process.StandardError.ReadToEnd()); Port = JsonDocument.Parse(line).RootElement.GetProperty("port").GetInt32();
        }
        JsonElement Command(string command) { process.StandardInput.WriteLine(command); process.StandardInput.Flush(); return JsonDocument.Parse(process.StandardOutput.ReadLine()!).RootElement.Clone(); }
        public JsonElement Stats() => Command("stats"); public JsonElement Files() => Command("files"); public void Large() => Command("large"); public void DropReply(string command) => Command("drop-reply " + command);
        public void Dispose() { if (!process.HasExited) { process.StandardInput.WriteLine("stop"); process.StandardInput.Flush(); if (!process.WaitForExit(2000)) process.Kill(true); } process.Dispose(); }
    }
}
