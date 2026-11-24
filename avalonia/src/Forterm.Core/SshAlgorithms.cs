using System.Text.RegularExpressions;
using Renci.SshNet;

namespace Forterm.Core;

/// <summary>Algorithm preferences for a connection. A preferred name is moved to the front of the
/// client's offer; nothing is removed, so a server without it negotiates the default order.
/// There is deliberately no host key preference: the trust store keeps one fingerprint per
/// host:port, so switching key types would raise "fingerprint changed" warnings that users learn
/// to click through.</summary>
public static class SshAlgorithms
{
    static readonly Lazy<(string[] KeyExchange, string[] Cipher)> names = new(() =>
    {
        using var none = new NoneAuthenticationMethod("algorithm-list");
        var info = new ConnectionInfo("unused", 22, "algorithm-list", none);
        return (info.KeyExchangeAlgorithms.Keys.ToArray(), info.Encryptions.Keys.ToArray());
    });

    /// <summary>Key exchange names in the library's default order.</summary>
    public static IReadOnlyList<string> KeyExchange => names.Value.KeyExchange;
    /// <summary>Cipher names (both directions) in the library's default order.</summary>
    public static IReadOnlyList<string> Ciphers => names.Value.Cipher;

    public static void Validate(string keyExchange, string cipher)
    {
        if (keyExchange.Length > 0 && !KeyExchange.Contains(keyExchange)) throw new ArgumentException("不支持的密钥交换算法：" + Shown(keyExchange));
        if (cipher.Length > 0 && !Ciphers.Contains(cipher)) throw new ArgumentException("不支持的 Cipher 算法：" + Shown(cipher));
    }

    internal static void Apply(ConnectionInfo info, SessionConnectOptions options)
    {
        if (options.PreferredKeyExchange is { Length: > 0 } kex) info.KeyExchangeAlgorithms.SetPosition(kex, 0);
        if (options.PreferredCipher is { Length: > 0 } cipher) info.Encryptions.SetPosition(cipher, 0);
    }

    static string Shown(string value) => value.Length > 64 ? value[..64] + "…" : value;
}

/// <summary>Names the algorithms a connected transport actually negotiated.</summary>
public sealed record SshNegotiated(string KeyExchange, string HostKey, string Cipher);

/// <summary>Session environment variables ("会话环境变量"), sent as SSH "env" requests on each
/// interactive shell channel before the shell starts. Never typed into the terminal.</summary>
public static partial class ShellEnvironment
{
    public const int MaxEntries = 32;
    public const int MaxValueLength = 1024;

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$")]
    private static partial Regex NamePattern();

    public static void Validate(IReadOnlyList<KeyValuePair<string, string>> variables)
    {
        if (variables.Count > MaxEntries) throw new ArgumentException($"环境变量最多 {MaxEntries} 个");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, value) in variables)
        {
            if (name is null || !NamePattern().IsMatch(name)) throw new ArgumentException("环境变量名只能包含字母、数字和下划线，不能以数字开头，最长64个字符");
            if (!seen.Add(name)) throw new ArgumentException("环境变量重复：" + name);
            if (value is null || value.Length > MaxValueLength) throw new ArgumentException($"环境变量 {name} 的值最长 {MaxValueLength} 个字符");
            if (value.Any(char.IsControl)) throw new ArgumentException($"环境变量 {name} 的值不能包含换行或控制字符");
        }
    }
}
