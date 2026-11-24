using Avalonia.Controls;
using Forterm.Core;
namespace Forterm.Desktop;

public abstract class FileBrowserPanel : DockPanel, IDisposable
{
    public abstract IRemoteFileSource SourceHandle { get; }
    public abstract Task RefreshAsync(CancellationToken ct);
    public abstract void Dispose();
}
