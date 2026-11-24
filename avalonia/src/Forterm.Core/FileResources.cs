namespace Forterm.Core;

public sealed class FileResource
{
    public Guid Id { get; } = Guid.NewGuid(); internal SemaphoreSlim Gate { get; } = new(1, 1);
}
public sealed class FileReservations : IDisposable
{
    readonly List<FileResource> held = new(); int disposed;
    FileReservations() { }
    public static async Task<FileReservations> AcquireAsync(IEnumerable<IRemoteFileSource> sources, CancellationToken ct)
    {
        var endpoints = sources.ToArray();
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(endpoints.Select(s => s.Closed).Prepend(ct).ToArray());
        var lease = new FileReservations();
        try
        {
            foreach (var resource in endpoints.Select(s => s.Resource).DistinctBy(r => r.Id).OrderBy(r => r.Id))
            { await resource.Gate.WaitAsync(wait.Token).ConfigureAwait(false); lease.held.Add(resource); }
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) != 0) return; for (int i = held.Count - 1; i >= 0; i--) held[i].Gate.Release(); }
}
