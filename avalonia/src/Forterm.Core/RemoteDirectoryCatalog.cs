namespace Forterm.Core;

public sealed record RemoteEntry(string Path, string Name, bool Directory, bool Link, bool Regular, long Size)
{
    /// <summary>Last change as the server tells it; null when it does not. Not part of the identity: what was equal stays equal.</summary>
    public DateTimeOffset? Modified { get; init; }
    public bool Equals(RemoteEntry? other) => other is not null && Path == other.Path && Name == other.Name && Directory == other.Directory && Link == other.Link && Regular == other.Regular && Size == other.Size;
    public override int GetHashCode() => HashCode.Combine(Path, Name, Directory, Link, Regular, Size);
    public override string ToString() => $"{(Link ? "↗ " : Directory ? "▸ " : "  ")}{Name}  {(Directory ? "" : Size + " B")}";
}
public sealed record RemoteListing(RemoteDirectoryBinding Binding, IReadOnlyList<RemoteEntry> Entries);

/// <summary>Source-bound lazy listings. One request per path, bounded cache and metadata deadline.</summary>
public sealed class RemoteDirectoryCatalog(SftpLease source) : IDisposable
{
    readonly SafeTransfers safe = new(); readonly object gate = new(); readonly CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(source.Closed);
    readonly Dictionary<string, CacheEntry> cache = new(StringComparer.Ordinal); long sequence; bool disposed;
    sealed class CacheEntry(Task<RemoteListing> task, long age) { public Task<RemoteListing> Task = task; public long Age = age; }
    public Guid SourceId => source.SourceId;
    public async Task<RemoteListing> ListAsync(string path, bool refresh, CancellationToken ct)
    {
        if (path.Length > 16384) throw new IOException("目录路径过长");
        Task<RemoteListing> task;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this); source.Closed.ThrowIfCancellationRequested();
            if (cache.TryGetValue(path, out var old) && (old.Task.IsFaulted || old.Task.IsCanceled || refresh && old.Task.IsCompleted)) cache.Remove(path);
            if (cache.TryGetValue(path, out var present)) { present.Age = ++sequence; task = present.Task; }
            else
            {
                // Never evict an outstanding request: repeated clicks cannot multiply it.
                if (cache.Count >= 128) { var oldest = cache.Where(p => p.Value.Task.IsCompleted).MinBy(p => p.Value.Age); if (oldest.Key == null) throw new IOException("目录请求过多，请等待当前读取"); cache.Remove(oldest.Key); }
                task = ReadAsync(path); cache.Add(path, new(task, ++sequence));
                _ = task.ContinueWith(t => { if (t.IsFaulted) _ = t.Exception; Trim(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        return await task.WaitAsync(ct).ConfigureAwait(false);
    }
    void Trim()
    {
        lock (gate)
        {
            int count = cache.Values.Where(v => v.Task.IsCompletedSuccessfully).Sum(v => v.Task.Result.Entries.Count);
            while (count > 50000)
            {
                var oldest = cache.Where(p => p.Value.Task.IsCompletedSuccessfully).MinBy(p => p.Value.Age); if (oldest.Key == null) break;
                count -= oldest.Value.Task.Result.Entries.Count; cache.Remove(oldest.Key);
            }
        }
    }
    async Task<RemoteListing> ReadAsync(string path)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(8)); var ct = timeout.Token;
        var binding = await safe.CaptureDirectoryAsync(source, path, ct).ConfigureAwait(false); var entries = new List<RemoteEntry>();
        await foreach (var f in source.Client.ListDirectoryAsync(binding.Path, ct).ConfigureAwait(false))
        {
            if (f.Name is "." or "..") continue;
            if (entries.Count >= 20000) throw new IOException("单目录超过20000项，请使用更具体的路径");
            if (string.IsNullOrWhiteSpace(f.Name) || f.Name.IndexOfAny(['/', '\\', '\0', '\r', '\n']) >= 0) throw new IOException("目录包含不安全的名称，未将服务器返回路径用于操作");
            entries.Add(new(binding.Path.TrimEnd('/') + "/" + f.Name, f.Name, f.IsDirectory, f.IsSymbolicLink, f.IsRegularFile, f.Length) { Modified = f.LastWriteTimeUtc > DateTime.UnixEpoch ? new DateTimeOffset(DateTime.SpecifyKind(f.LastWriteTimeUtc, DateTimeKind.Utc)) : null });
        }
        var after = await safe.CaptureDirectoryAsync(source, binding.Path, ct).ConfigureAwait(false);
        if (after.SourceId != binding.SourceId || after.Stamp.User != binding.Stamp.User || after.Stamp.Group != binding.Stamp.Group || after.Stamp.Mode != binding.Stamp.Mode) throw new IOException("目录属性在读取期间发生变化");
        return new(binding, entries.OrderByDescending(f => f.Directory && !f.Link).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; lifetime.Cancel(); lifetime.Dispose(); cache.Clear(); } }
}
