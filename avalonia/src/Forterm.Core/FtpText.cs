using System.Text;
namespace Forterm.Core;

public sealed record FtpText(FtpDirectory Directory, FtpVersion Version, string Text, bool HasBom);

public sealed partial class FtpFileSession
{
    public async Task<FtpText> OpenTextAsync(FtpDirectory directory, string name, CancellationToken ct)
    {
        var version = await CaptureAsync(directory, name, ct, RemoteText.MaxBytes); if (version.Stamp == null) throw new FtpPolicyException("FTP文本文件不存在");
        byte[] bytes = await ReadBoundedAsync(directory, version, RemoteText.MaxBytes, ct); bool bom = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }); string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes.AsSpan(bom ? 3 : 0)); } catch (DecoderFallbackException e) { throw new IOException("FTP在线编辑仅支持有效UTF-8文本", e); }
        RemoteText.Validate(text); return new(directory, version, text, bom);
    }
    public async Task<FtpText> SaveTextNewAsync(FtpDirectory directory, string name, string text, bool bom, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        using var reservation = await FileReservations.AcquireAsync(new[] { directory.Source }, ct);
        byte[] bytes = RemoteText.Encode(text, bom); var target = await CaptureAsync(directory, name, ct, RemoteText.MaxBytes);
        if (target.Stamp != null) throw new FtpPolicyException("FTP另存名称已存在，未覆盖，请选择新名称");
        var saved = await UploadMemoryNewAsync(directory, target, bytes, progress, ct); return new(directory, saved, text, bom);
    }
}
