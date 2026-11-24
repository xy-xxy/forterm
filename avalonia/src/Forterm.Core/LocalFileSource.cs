namespace Forterm.Core;

/// <summary>Read-only identity for the local file workspace. It never opens a
/// network channel and only lives for the local terminal session.</summary>
public sealed class LocalFileSource : IRemoteFileSource
{
    readonly CancellationTokenSource closed = new();
    int disposed;

    public LocalFileSource(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("本机目录不能为空", nameof(root));
        Root = Path.GetFullPath(root);
        if (!Directory.Exists(Root)) throw new DirectoryNotFoundException(Root);
        Resource = new FileResource();
    }

    public string Root { get; }
    public Guid SourceId { get; } = Guid.NewGuid();
    public FileResource Resource { get; }
    public CancellationToken Closed => closed.Token;
    public bool IsAvailable => Volatile.Read(ref disposed) == 0;
    public WorkspaceProtocol Protocol => WorkspaceProtocol.Local;
    public string? CloseReason => IsAvailable ? null : "本机文件来源已关闭";

    public string Resolve(string path)
    {
        if (!IsAvailable) throw new OperationCanceledException(CloseReason, null, Closed);
        string candidate = Path.GetFullPath(path);
        string root = Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.Equals(Root, StringComparison.OrdinalIgnoreCase) && !candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("本机路径超出当前文件工作区");
        return candidate;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) closed.Cancel();
        closed.Dispose();
    }
}
