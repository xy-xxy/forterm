using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
public sealed class SudoPermissionTests
{
    sealed class Sink : IProgress<TransferProgress> { public void Report(TransferProgress p) { } }
    [Fact] public async Task OwnedHelperIdentityAndSplitRandomPromptUseSameSshWithoutTerminalInjection()
    {
        using var fixture = new TransportTests.Fixture("FIXTURE_PERMISSION_EXEC"); fixture.SeedText(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var files = await root.OpenFilesAsync(ct.Token); using var shell = await root.OpenShellAsync(80, 24, ct.Token);
        var permissions = new PermissionService(); var plan = await permissions.PreviewAsync(files, "/edit.txt", false, 384, null, ct.Token); var service = new SudoPermissionService();
        var approved = await service.InspectAsync(plan, ct.Token); Assert.Single(approved.Identities); int asks = 0;
        await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync(approved, false, _ => { asks++; return Task.FromResult<byte[]?>(null); }, new Sink(), ct.Token)); Assert.Equal(0, asks);
        byte[]? sent = null;
        await service.ApplyAsync(approved, true, _ => { asks++; sent = System.Text.Encoding.UTF8.GetBytes("synthetic-sudo-only"); return Task.FromResult<byte[]?>(sent); }, new Sink(), ct.Token);
        Assert.Equal(1, asks); Assert.NotNull(sent); Assert.All(sent, b => Assert.Equal(0, b)); Assert.Equal(384u, (await permissions.InspectAsync(files, "/edit.txt", ct.Token)).Mode);
        var stats = fixture.Stats(); Assert.Equal(1, stats.GetProperty("auth").GetInt32()); Assert.Empty(stats.GetProperty("input").EnumerateArray()); Assert.Equal(1, stats.GetProperty("sudoPrompts").GetInt32()); Assert.True(root.IsConnected);
    }
    [Fact] public async Task ReplacedNodeIsRefusedByNoFollowIdentityAndOtherFileKeepsMode()
    {
        using var fixture = new TransportTests.Fixture("FIXTURE_PERMISSION_EXEC"); fixture.SeedText(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token); using var files = await root.OpenFilesAsync(ct.Token);
        var permissions = new PermissionService(); var plan = await permissions.PreviewAsync(files, "/edit.txt", false, 384, null, ct.Token); var service = new SudoPermissionService(); var approved = await service.InspectAsync(plan, ct.Token); fixture.SwapPermission();
        await Assert.ThrowsAsync<PermissionApplyException>(() => service.ApplyAsync(approved, true, _ => Task.FromResult<byte[]?>(System.Text.Encoding.UTF8.GetBytes("synthetic-sudo-only")), new Sink(), ct.Token));
        Assert.Equal(420u, (await files.Client.LStatRawAsync("/protected.txt", ct.Token)).PermissionBits); Assert.True((await files.Client.LStatRawAsync("/edit.txt", ct.Token)).IsSymbolicLink); Assert.True(files.IsAvailable);
    }
    [Fact] public async Task GenericPromptNeverReceivesPasswordAndCancellationKeepsOtherCapabilities()
    {
        using var fixture = new TransportTests.Fixture("FIXTURE_PERMISSION_EXEC"); fixture.SeedText(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token); using var files = await root.OpenFilesAsync(ct.Token);
        var service = new SudoPermissionService(); var plan = await new PermissionService().PreviewAsync(files, "/edit.txt", false, 384, null, ct.Token); var approved = await service.InspectAsync(plan, ct.Token);
        // Use a dedicated fixture mode for a generic prompt; no real sudo process is launched.
        fixture.WrongSudoPrompt(); using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2)); int asks = 0;
        await Assert.ThrowsAsync<PermissionApplyException>(() => service.ApplyAsync(approved, true, _ => { asks++; return Task.FromResult<byte[]?>(System.Text.Encoding.UTF8.GetBytes("must-not-send")); }, new Sink(), cancel.Token));
        Assert.Equal(0, asks); Assert.Equal(0, fixture.Stats().GetProperty("sudoPasswordBytes").GetInt32()); Assert.True(files.IsAvailable); Assert.True(root.IsConnected);
    }
    [Fact] public async Task DeniedExecKeepsSftpAndShellAndNeverRequestsSudoPassword()
    {
        using var fixture = new TransportTests.Fixture("FIXTURE_DENY_EXEC"); fixture.SeedText(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var files = await root.OpenFilesAsync(ct.Token); using var shell = await root.OpenShellAsync(80, 24, ct.Token);
        var plan = await new PermissionService().PreviewAsync(files, "/edit.txt", false, 384, null, ct.Token);
        await Assert.ThrowsAnyAsync<Exception>(() => new SudoPermissionService().InspectAsync(plan, ct.Token)); Assert.True(files.IsAvailable); Assert.True(root.IsConnected); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
    }
}
