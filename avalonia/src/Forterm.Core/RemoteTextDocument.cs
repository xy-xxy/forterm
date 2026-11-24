namespace Forterm.Core;

/// <summary>Editor baseline retains its original live source, path and version across UI switches.</summary>
public interface IRemoteTextDocument
{
    IRemoteFileSource Source { get; }
    string Path { get; }
    string Text { get; }
    bool HasBom { get; }
    bool Exists { get; }
    bool CanReplace { get; }
    Task<IRemoteTextDocument> SaveAsync(string text, IProgress<TransferProgress> progress, CancellationToken ct);
    Task<IRemoteTextDocument> SaveAsNewAsync(string name, string text, IProgress<TransferProgress> progress, CancellationToken ct);
}
public sealed record SftpTextDocument(RemoteText Snapshot) : IRemoteTextDocument
{
    public IRemoteFileSource Source => Snapshot.Directory.Source;
    public string Path => Snapshot.Version.Path; public string Text => Snapshot.Text; public bool HasBom => Snapshot.HasBom;
    public bool Exists => Snapshot.Version.Stamp != null; public bool CanReplace => Snapshot.Directory.Source.Client.SupportsAtomicReplacement;
    public async Task<IRemoteTextDocument> SaveAsync(string text, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        var saved = await new SafeTransfers().SaveTextAsync(Snapshot, text, true, progress, ct);
        return new SftpTextDocument(Snapshot with { Version = saved, Text = text });
    }
    public async Task<IRemoteTextDocument> SaveAsNewAsync(string name, string text, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        var safe = new SafeTransfers(); var target = await safe.CaptureRemoteAsync(Snapshot.Directory, name, ct, RemoteText.MaxBytes);
        if (target.Stamp != null) throw new IOException("另存名称已存在，未覆盖");
        var saved = await safe.SaveTextAsync(Snapshot with { Version = target }, text, true, progress, ct);
        return new SftpTextDocument(Snapshot with { Version = saved, Text = text });
    }
}
public sealed record FtpTextDocument(FtpText Snapshot) : IRemoteTextDocument
{
    public IRemoteFileSource Source => Snapshot.Directory.Source;
    public string Path => Snapshot.Version.Path; public string Text => Snapshot.Text; public bool HasBom => Snapshot.HasBom;
    public bool Exists => Snapshot.Version.Stamp != null; public bool CanReplace => false;
    public Task<IRemoteTextDocument> SaveAsync(string text, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        if (Exists) throw new FtpPolicyException("FTP未确认安全原子覆盖，请另存为新名称");
        return SaveAsNewAsync(System.IO.Path.GetFileName(Path), text, progress, ct);
    }
    public async Task<IRemoteTextDocument> SaveAsNewAsync(string name, string text, IProgress<TransferProgress> progress, CancellationToken ct)
        => new FtpTextDocument(await Snapshot.Directory.Source.SaveTextNewAsync(Snapshot.Directory, name, text, HasBom, progress, ct));
}
