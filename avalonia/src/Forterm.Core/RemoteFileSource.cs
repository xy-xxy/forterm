namespace Forterm.Core;

public interface IRemoteFileSource : IDisposable
{
    Guid SourceId { get; }
    FileResource Resource { get; }
    CancellationToken Closed { get; }
    bool IsAvailable { get; }
    WorkspaceProtocol Protocol { get; }
    string? CloseReason { get; }
}
