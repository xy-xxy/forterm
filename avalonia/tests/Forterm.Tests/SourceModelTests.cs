using Forterm.Core;
using Xunit;
namespace Forterm.Tests;
public sealed class SourceModelTests
{
    [Fact] public void OnlyExplicitlyReferencedFileProtocolTabsHideAndRestoreAfterLastOwnerCloses()
    {
        var model = new WorkspaceAssociations(); Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), ftp = Guid.NewGuid();
        model.Add(new(a, "ssh-a", WorkspaceProtocol.Ssh, true)); model.Add(new(b, "sftp-b", WorkspaceProtocol.Sftp, true)); model.Add(new(c, "ssh-c", WorkspaceProtocol.Ssh, true)); model.Add(new(ftp, "ftp", WorkspaceProtocol.Ftp, true));
        model.Attach(a, b); model.Attach(a, ftp); model.Select(a, b);
        Assert.Equal(new[] { a, c }, model.Visible().Select(e => e.Id)); Assert.Equal(a, model.VisibleOwner(ftp));
        model.Attach(c, b); model.Detach(a, b); Assert.DoesNotContain(model.Visible(), e => e.Id == b);
        model.Close(c); Assert.Contains(model.Visible(), e => e.Id == b); Assert.DoesNotContain(model.Visible(), e => e.Id == ftp);
        model.Close(a); Assert.Equal(new[] { b, ftp }, model.Visible().Select(e => e.Id));
    }
    [Fact] public void NewTabsAppendAfterClosingEarlierWorkspace()
    {
        var model = new WorkspaceAssociations(); var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in ids.Take(3)) model.Add(new(id, "test", WorkspaceProtocol.Ssh, true));
        model.Close(ids[0]); model.Add(new(ids[3], "new", WorkspaceProtocol.Ssh, true));
        Assert.Equal(new[] { ids[1], ids[2], ids[3] }, model.Visible().Select(e => e.Id));
    }
    [Fact] public void CyclesRetainReachableRootAndSshNeverHides()
    {
        var model = new WorkspaceAssociations(); Guid a = Guid.NewGuid(), b = Guid.NewGuid(), ssh = Guid.NewGuid();
        model.Add(new(a, "a", WorkspaceProtocol.Sftp, true)); model.Add(new(b, "b", WorkspaceProtocol.Ftp, true)); model.Add(new(ssh, "ssh", WorkspaceProtocol.Ssh, true));
        model.Attach(a, b); model.Attach(b, a); Assert.NotEmpty(model.Visible()); Assert.NotNull(model.VisibleOwner(a)); Assert.NotNull(model.VisibleOwner(b));
        model.Attach(a, ssh); Assert.Contains(model.Visible(), e => e.Id == ssh);
    }
    [Fact] public void OfflineLocalAndUnknownSourcesCannotTriggerImplicitConnections()
    {
        var model = new WorkspaceAssociations(); Guid a = Guid.NewGuid(), b = Guid.NewGuid(), local = Guid.NewGuid();
        model.Add(new(a, "a", WorkspaceProtocol.Ssh, true)); model.Add(new(b, "b", WorkspaceProtocol.Sftp, false)); model.Add(new(local, "local", WorkspaceProtocol.Local, true));
        Assert.Throws<IOException>(() => model.Attach(a, b)); Assert.Throws<IOException>(() => model.Attach(a, local)); Assert.Throws<InvalidOperationException>(() => model.Attach(a, Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => model.Select(a, b)); Assert.Equal(new[] { a }, model.Sources(a).Sources);
    }
    [Fact] public async Task DirectoryCatalogIsLazySingleFlightAndBoundToOriginalSource()
    {
        using var fixture = new TransportTests.Fixture(listDelay: .2); fixture.SeedTree(); using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var root = await SharedSshSession.ConnectAsync("127.0.0.1", fixture.Port, "synthetic", "synthetic-only", (_, _) => Task.FromResult(true), ct.Token);
        using var source = await root.OpenFilesAsync(ct.Token); using var catalog = new RemoteDirectoryCatalog(source);
        Assert.Empty(fixture.Stats().GetProperty("lists").EnumerateArray());
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct.Token);
        var one = catalog.ListAsync("/", false, waiting.Token); var two = catalog.ListAsync("/", false, ct.Token); waiting.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => one); var listing = await two; Assert.Equal(source.SourceId, listing.Binding.SourceId);
        Assert.Equal(1, fixture.Stats().GetProperty("lists").GetArrayLength()); Assert.Contains(listing.Entries, e => e.Path == "/a" && e.Directory);
        await catalog.ListAsync("/", false, ct.Token); Assert.Equal(1, fixture.Stats().GetProperty("lists").GetArrayLength());
        await catalog.ListAsync("/a", false, ct.Token); Assert.Equal(new[] { "/", "/a" }, fixture.Stats().GetProperty("lists").EnumerateArray().Select(x => x.GetString()));
        source.Dispose(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.ListAsync("/", false, ct.Token)); Assert.Equal(1, fixture.Stats().GetProperty("auth").GetInt32());
    }
}
