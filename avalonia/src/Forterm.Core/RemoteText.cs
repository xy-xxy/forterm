using System.Security.Cryptography;
using System.Text;
namespace Forterm.Core;

public sealed record RemoteText(RemoteDirectoryBinding Directory, RemoteFileVersion Version, string Text, bool HasBom)
{
    public const int MaxBytes = 1024 * 1024;
    public string Newlines => DescribeNewlines(Text);
    public static string DescribeNewlines(string text) => text.Contains("\r\n") ? text.Replace("\r\n", "").Contains('\n') || text.Replace("\r\n", "").Contains('\r') ? "混合换行" : "CRLF" : text.Contains('\r') ? "CR / 混合换行" : "LF";
    public static byte[] Encode(string text, bool bom)
    {
        Validate(text); var utf8 = new UTF8Encoding(false, true); int count = utf8.GetByteCount(text);
        if (count + (bom ? 3 : 0) > MaxBytes) throw new IOException("在线编辑限1MiB，请使用文件传输");
        byte[] bytes = new byte[count + (bom ? 3 : 0)]; if (bom) { bytes[0] = 0xef; bytes[1] = 0xbb; bytes[2] = 0xbf; }
        utf8.GetBytes(text, bytes.AsSpan(bom ? 3 : 0)); return bytes;
    }
    internal static void Validate(string text) { if (text.Any(c => c < 32 && c is not ('\t' or '\r' or '\n') || c == 127)) throw new IOException("文件包含二进制控制字节，不支持在线文本编辑"); }
}

public sealed partial class SafeTransfers
{
    public async Task<RemoteText> OpenTextAsync(RemoteDirectoryBinding d, string name, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, d.Source.Closed); timeout.CancelAfter(TimeSpan.FromSeconds(15)); var token = timeout.Token;
        await CheckDirectory(d, token); string path = d.Path.TrimEnd('/') + "/" + Name(name);
        var stamp = await Stat(d.Source, path, token);
        if (stamp is not { Regular: true, Link: false, Length: >= 0 and <= RemoteText.MaxBytes }) throw new IOException("在线编辑只支持不超过1MiB的普通文件；不跟随符号链接");
        await using var input = await Metadata(t => d.Source.Client.OpenRawAsync(path, FileMode.Open, FileAccess.Read, t), token);
        if (!await Is(input, path, d.Source, stamp, token)) throw new IOException("文件在打开时发生变化");
        using var output = new MemoryStream(); byte[] block = new byte[32768];
        while (true) { int n = await input.ReadAsync(block, token); if (n == 0) break; if (output.Length + n > RemoteText.MaxBytes) throw new IOException("文件读取时超过1MiB边界"); await output.WriteAsync(block.AsMemory(0, n), token); }
        byte[] bytes = output.ToArray();
        if (bytes.LongLength != stamp.Length || !await Is(input, path, d.Source, stamp, token) || await Stat(d.Source, path, token) != stamp) throw new IOException("文件在读取期间发生变化");
        await CheckDirectory(d, token);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        // Second full bounded read detects same-size/same-mtime edits during the first read.
        if (hash != await HashRemote(d.Source, path, token, RemoteText.MaxBytes) || await Stat(d.Source, path, token) != stamp) throw new IOException("文件内容在读取期间发生变化");
        bool bom = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }); string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes.AsSpan(bom ? 3 : 0)); } catch (DecoderFallbackException e) { throw new IOException("在线编辑仅接受有效UTF-8文本；请通过文件传输处理其他编码", e); }
        RemoteText.Validate(text); return new(d, new(path, stamp, hash), text, bom);
    }
    public async Task<RemoteFileVersion> SaveTextAsync(RemoteText original, string text, bool replaceApproved, IProgress<TransferProgress> progress, CancellationToken ct)
    {
        using var reservation = await FileReservations.AcquireAsync(new[] { original.Directory.Source }, ct);
        // Owned immutable snapshot; no plaintext local staging file is created.
        byte[] content = RemoteText.Encode(text, original.HasBom); string hash = Convert.ToHexString(SHA256.HashData(content));
        await UploadContentAsync(original.Directory, original.Version, content.Length, hash, replaceApproved,
            _ => Task.FromResult<Stream>(new MemoryStream(content, false)), _ => Task.CompletedTask, progress, ct);
        try
        {
            // Cancellation after the commit point must never be reported as "not submitted".
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(original.Directory.Source.Closed); timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var saved = await CaptureRemoteAsync(original.Directory, original.Version.Path[(original.Version.Path.LastIndexOf('/') + 1)..], timeout.Token, RemoteText.MaxBytes);
            if (saved.Hash != hash) throw new IOException("远程文件在保存之后又发生变化");
            return saved;
        }
        catch (Exception e) { throw new CommitUncertainException("保存改名提交已确认，但随后版本核验未完成；请核对远端，草稿保持，不能当作未提交或自动重试", e); }
    }
}
