using Forterm.Core;

namespace Forterm.Studio.Library;

public static class ProxyParser
{
    /// <summary>socks5://host:port, socks://host:port or http://host:port. Credentials in the URL
    /// are rejected: they would end up in plain metadata.</summary>
    public static ProxyEndpoint Parse(string text)
    {
        text = text.Trim();
        int scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme <= 0) throw new InvalidDataException("代理格式应为 socks5://地址:端口 或 http://地址:端口");
        var kind = text[..scheme].ToLowerInvariant() switch
        {
            "socks5" or "socks" or "socks5h" => ProxyKind.Socks5,
            "http" => ProxyKind.Http,
            _ => throw new InvalidDataException("代理只支持 socks5 或 http"),
        };
        string rest = text[(scheme + 3)..].TrimEnd('/');
        if (rest.Contains('@')) throw new InvalidDataException("代理地址中不能包含账号密码");
        string host; string portText;
        if (rest.StartsWith('['))
        {
            int end = rest.IndexOf(']');
            if (end < 2 || end + 1 >= rest.Length || rest[end + 1] != ':') throw new InvalidDataException("代理地址无效");
            host = rest[1..end]; portText = rest[(end + 2)..];
        }
        else
        {
            int colon = rest.LastIndexOf(':');
            if (colon <= 0) throw new InvalidDataException("代理需要端口");
            host = rest[..colon]; portText = rest[(colon + 1)..];
        }
        if (!int.TryParse(portText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int port))
            throw new InvalidDataException("代理端口无效");
        var endpoint = new ProxyEndpoint(kind, host, port);
        try { endpoint.Validate(); }
        catch (ArgumentException e) { throw new InvalidDataException(e.Message); }
        return endpoint;
    }
}

public static class Encodings
{
    public static readonly string[] Names = ["UTF-8", "GBK", "GB18030", "Big5", "Shift_JIS", "EUC-KR", "ISO-8859-1"];

    static Encodings() => System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    public static bool IsSupported(string name) => Names.Contains(name);

    public static System.Text.Encoding Get(string name) => name switch
    {
        "GBK" => System.Text.Encoding.GetEncoding(936),
        _ => System.Text.Encoding.GetEncoding(name),
    };
}
