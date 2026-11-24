using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
public sealed class PermissionTests
{
    sealed class Sink(Action<TransferProgress>? callback = null) : IProgress<TransferProgress> { public void Report(TransferProgress p) => callback?.Invoke(p); }
    [Fact] public async Task RecursivePlanIsReadOnlyPostOrderSkipsLinksAndPreservesSpecialBits()
    {
        using var fixture = new TransportTests.Fixture(); fixture.SeedTree(); fixture.SeedText(); fixture.MakeLinks(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var files = await root.OpenFilesAsync(ct.Token); await files.UploadNewAsync(new MemoryStream("owned"u8.ToArray()), "/a/file.txt", ct.Token); files.Client.ChangePermissions("/a/file.txt", 4755);
        var service = new PermissionService(); var before = (await files.Client.LStatRawAsync("/a/file.txt", ct.Token)).PermissionBits; var plan = await service.PreviewAsync(files, "/a", true, 448, 384, ct.Token);
        Assert.Equal(before, (await files.Client.LStatRawAsync("/a/file.txt", ct.Token)).PermissionBits); Assert.Equal("/a", plan.Items.Last().Before.Path);
        await service.ApplyAsync(plan, new Sink(), ct.Token); Assert.Equal(2432u, (await files.Client.LStatRawAsync("/a/file.txt", ct.Token)).PermissionBits); Assert.Equal(448u, (await files.Client.LStatRawAsync("/a/nested", ct.Token)).PermissionBits);
        await Assert.ThrowsAnyAsync<IOException>(() => service.PreviewAsync(files, "/link-file", false, 384, null, ct.Token)); Assert.True(root.IsConnected); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
    }
    [Fact] public async Task ChangedPreviewAndCancellationDoNotPretendWholePlanApplied()
    {
        using var fixture = new TransportTests.Fixture(); fixture.SeedTree(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var files = await root.OpenFilesAsync(ct.Token); var service = new PermissionService();
        files.Client.ChangePermissions("/a", 700); var one = await service.PreviewAsync(files, "/a", false, 448, null, ct.Token); files.Client.ChangePermissions("/a", 755);
        var changed = await Assert.ThrowsAsync<PermissionApplyException>(() => service.ApplyAsync(one, new Sink(), ct.Token)); Assert.Equal(0, changed.Done); Assert.Equal(493u, (await files.Client.LStatRawAsync("/a", ct.Token)).PermissionBits);
        var plan = await service.PreviewAsync(files, "/a", true, 448, null, ct.Token); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct.Token);
        var partial = await Assert.ThrowsAsync<PermissionApplyException>(() => service.ApplyAsync(plan, new Sink(p => { if (p.Bytes == 1) cancel.Cancel(); }), cancel.Token)); Assert.Equal(1, partial.Done); Assert.True(files.IsAvailable);
    }
    [Fact] public async Task FtpRequiresKnownSiteChmodAndVerifiesResult()
    {
        using var fixture = new FtpTests.Fixture(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var source = await FtpFileSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", ct.Token, new() { KeepAliveEnabled = false }); var service = new PermissionService();
        var plan = await service.PreviewAsync(source, "/edit.txt", false, 384, null, ct.Token); await service.ApplyAsync(plan, new Sink(), ct.Token);
        Assert.Equal(384u, (await service.InspectAsync(source, "/edit.txt", ct.Token)).Mode); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
    }
}
