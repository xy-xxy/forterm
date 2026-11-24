using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace Forterm.Core;

public sealed record TotpParameters(string Algorithm = "SHA1", int Digits = 6, int Period = 30)
{
    public void Validate() { if (Algorithm is not ("SHA1" or "SHA256" or "SHA512") || Digits is not (6 or 8) || Period is < 5 or > 300) throw new IOException("验证器算法须SHA1/SHA256/SHA512，位数6或8，周期5–300秒"); }
}
public sealed record TotpConfiguration(byte[] Secret, TotpParameters Parameters);
public sealed record TotpCode(string Code, int RemainingSeconds);
public static class Totp
{
    const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    static IOException Invalid() => new("验证器key必须为规范的10–128字节Base32密钥");
    public static byte[] DecodeBase32(string input)
    {
        if (input.Length > 1024) throw Invalid();
        string clean = input.Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "").Replace("-", "").ToUpperInvariant();
        string raw = clean.TrimEnd('='); int padding = clean.Length - raw.Length;
        int expected = raw.Length % 8 switch { 0 => 0, 2 => 6, 4 => 4, 5 => 3, 7 => 1, _ => -1 };
        if (raw.Length == 0 || expected < 0 || padding > 6 || padding > 0 && padding != expected) throw Invalid();
        var result = new List<byte>(); int bits = 0, buffer = 0;
        foreach (char c in raw)
        {
            int value = Alphabet.IndexOf(c); if (value < 0) throw Invalid();
            buffer = (buffer << 5) | value; bits += 5;
            if (bits >= 8) { bits -= 8; result.Add((byte)(buffer >> bits)); buffer &= (1 << bits) - 1; }
        }
        byte[] bytes = result.ToArray();
        if (buffer != 0 || bytes.Length is < 10 or > 128 || EncodeBase32(bytes) != raw) { CryptographicOperations.ZeroMemory(bytes); throw Invalid(); }
        return bytes;
    }
    public static string EncodeBase32(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder(); int buffer = 0, bits = 0;
        foreach (byte b in bytes) { buffer = (buffer << 8) | b; bits += 8; while (bits >= 5) { bits -= 5; text.Append(Alphabet[(buffer >> bits) & 31]); } buffer &= (1 << bits) - 1; }
        if (bits > 0) text.Append(Alphabet[(buffer << (5 - bits)) & 31]); return text.ToString();
    }
    static string Decode(string value)
    {
        for (int i = 0; i < value.Length; i++) if (value[i] == '%' && (i + 2 >= value.Length || !Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2]))) throw new IOException("验证器URI转义无效");
        return Uri.UnescapeDataString(value.Replace("+", " "));
    }
    public static TotpConfiguration Normalize(string input, TotpParameters? parameters = null)
    {
        if (input.Length > 4096) throw new IOException("验证器配置超出4096字符边界");
        parameters ??= new(); string secret = input.Trim();
        if (secret.StartsWith("otpauth:", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < secret.Length; i++) if (secret[i] == '%' && (i + 2 >= secret.Length || !Uri.IsHexDigit(secret[i + 1]) || !Uri.IsHexDigit(secret[i + 2]))) throw new IOException("验证器URI转义无效");
            if (!Uri.TryCreate(secret, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("otpauth", StringComparison.OrdinalIgnoreCase) || !uri.Host.Equals("totp", StringComparison.OrdinalIgnoreCase) || uri.Port != -1 || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0) throw new IOException("仅支持不含凭据或片段的otpauth TOTP");
            string label = Uri.UnescapeDataString(uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped));
            if (label.Length is < 1 or > 256 || label.Any(c => c < 32)) throw new IOException("验证器URI标签无效");
            var query = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string piece in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = piece.IndexOf('='); string key = Decode(equals < 0 ? piece : piece[..equals]), value = Decode(equals < 0 ? "" : piece[(equals + 1)..]);
                if (key is not ("secret" or "issuer" or "algorithm" or "digits" or "period") || !query.TryAdd(key, value)) throw new IOException("验证器URI含不支持或重复参数");
            }
            // An otpauth URI is a standalone configuration, not an override on old form fields.
            parameters = new(); string algorithm = query.GetValueOrDefault("algorithm", "SHA1"); int digits = 6, period = 30;
            if (query.TryGetValue("digits", out var d) && d.Length > 0) { if (d is not ("6" or "8")) throw new IOException("验证器位数无效"); digits = int.Parse(d, CultureInfo.InvariantCulture); }
            if (query.TryGetValue("period", out var p) && p.Length > 0) { if (p.Length > 3 || p.Any(c => c is < '0' or > '9')) throw new IOException("验证器周期无效"); period = int.Parse(p, CultureInfo.InvariantCulture); }
            parameters = new(algorithm.Length == 0 ? "SHA1" : algorithm, digits, period); secret = query.GetValueOrDefault("secret", "");
        }
        parameters.Validate(); return new(DecodeBase32(secret), parameters);
    }
    public static TotpCode Generate(ReadOnlySpan<byte> secret, TotpParameters parameters, DateTimeOffset time)
    {
        parameters.Validate(); long unix = time.ToUnixTimeSeconds(); if (unix < 0 || secret.Length is < 10 or > 128) throw new IOException("验证器key或时间无效");
        Span<byte> counter = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(counter, (ulong)(unix / parameters.Period));
        byte[] digest = parameters.Algorithm switch { "SHA1" => HMACSHA1.HashData(secret, counter), "SHA256" => HMACSHA256.HashData(secret, counter), _ => HMACSHA512.HashData(secret, counter) };
        try
        {
            int offset = digest[^1] & 15; uint value = BinaryPrimitives.ReadUInt32BigEndian(digest.AsSpan(offset, 4)) & 0x7fffffff;
            uint modulo = parameters.Digits == 8 ? 100_000_000u : 1_000_000u;
            return new((value % modulo).ToString("D" + parameters.Digits, CultureInfo.InvariantCulture), parameters.Period - (int)(unix % parameters.Period));
        }
        finally { CryptographicOperations.ZeroMemory(digest); CryptographicOperations.ZeroMemory(counter); }
    }
}
