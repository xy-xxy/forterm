using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Forterm.Core;

public sealed class LaunchConnection
{
    public required string Protocol { get; set; }
    public required string Host { get; init; }
    public required string User { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public string Password { get; set; } = "";
    public bool HasPassword { get; init; }
    public int Port { get; init; }
    public string Title { get; set; } = "";
    public string KeyPath { get; set; } = "";
    public bool Temporary => true;
    public override string ToString() => "[temporary connection; redacted]";
}
public sealed class LaunchPlan
{
    public string Entry { get; set; } = "forterm";
    public int ArgumentCount { get; set; }
    public bool Diagnose { get; set; }
    public bool NewWindow { get; set; }
    public string Error { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public LaunchConnection? Connection { get; set; }
    // This is the only representation diagnostics can serialize.
    public object RedactedReport() => new { entry = Entry, argumentCount = ArgumentCount, diagnose = Diagnose, newWindow = NewWindow, recognized = Error.Length == 0, message = Error, networkRequested = Connection != null && !Diagnose && Error.Length == 0 };
    public override string ToString() => JsonSerializer.Serialize(RedactedReport());
    public static LaunchPlan Parse(IReadOnlyList<string> args, string entry = "forterm")
    {
        var p = new LaunchPlan { Entry = Path.GetFileNameWithoutExtension(entry).ToLowerInvariant(), ArgumentCount = args.Count }; p.Diagnose = p.Entry == "forterm-diagnose";
        if (args.Count > 128) { p.Error = "启动参数过多"; return p; }
        if (args.Any(a => a.Length > 32768)) { p.Error = "启动参数过长"; return p; }
        string? raw = null; string title = "", key = "";
        static string Unwrap(string a) => a.Length > 1 && a[0] == '"' && a[^1] == '"' ? a[1..^1] : a;
        static bool Known(string a) => a.ToLowerInvariant() is "-url" or "-newtab" or "-newwin" or "-i" or "--diagnose" or "--entry=xshell" or "--entry=xftp" or "--entry=diagnose";
        for (int i = 0; i < args.Count; i++)
        {
            string arg = Unwrap(args[i]), low = arg.ToLowerInvariant();
            switch (low)
            {
                case "--diagnose": case "--entry=diagnose": p.Diagnose = true; break;
                case "--entry=xshell": p.Entry = "xshell"; break;
                case "--entry=xftp": p.Entry = "xftp"; break;
                case "-url":
                case "-newtab":
                case "-newwin":
                case "-i":
                    if (low == "-newwin") { p.NewWindow = true; if (i + 1 < args.Count && Unwrap(args[i + 1]).Equals("-url", StringComparison.OrdinalIgnoreCase)) break; }
                    if (i + 1 >= args.Count || Known(Unwrap(args[i + 1]))) { p.Error = "启动选项缺少参数"; break; }
                    string value = Unwrap(args[++i]); if (low == "-newtab") title = value; else if (low == "-i") key = value; else { if (raw != null) p.Error = "一次只接受一个连接"; raw = value; }
                    break;
                default:
                    if (Regex.IsMatch(arg, @"^(ssh|sftp|ftp)://", RegexOptions.IgnoreCase)) { if (raw != null) p.Error = "一次只接受一个连接"; raw = arg; } else p.Error = "包含不支持的启动参数"; break;
            }
        }
        if (p.Error.Length > 0) return p;
        if (raw == null) { if (title.Length > 0 || key.Length > 0 || p.NewWindow) p.Error = "没有提供连接URL"; return p; }
        try { p.Connection = ParseUrl(raw); p.Connection.Title = title; p.Connection.KeyPath = key; if (p.Entry == "xftp" && p.Connection.Protocol != "ftp") p.Connection.Protocol = "sftp"; }
        catch (FormatException) { p.Connection = null; p.Error = "连接URL格式或端口无效"; }
        return p;
    }
    public static LaunchConnection ParseUrl(string raw)
    {
        if (raw.IndexOfAny(['\r', '\n', '\0', '?', '#']) >= 0) throw new FormatException();
        var match = Regex.Match(raw, @"^(?<scheme>ssh|sftp|ftp)://(?<info>.*)@(?<host>[^/?#]+)(/?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) throw new FormatException();
        string info = match.Groups["info"].Value, authority = match.Groups["host"].Value, scheme = match.Groups["scheme"].Value.ToLowerInvariant();
        if (info.IndexOfAny(['/', '\\']) >= 0) throw new FormatException();
        int colon = info.IndexOf(':'); string username = Decode(colon < 0 ? info : info[..colon]), password = colon < 0 ? "" : Decode(info[(colon + 1)..]); if (username.Length == 0) throw new FormatException();
        string host, portText = "";
        if (authority.StartsWith('[')) { int end = authority.IndexOf(']'); if (end < 2) throw new FormatException(); host = authority[1..end]; if (end + 1 < authority.Length) { if (authority[end + 1] != ':') throw new FormatException(); portText = authority[(end + 2)..]; } }
        else { int index = authority.IndexOf(':'); host = index < 0 ? authority : authority[..index]; if (index >= 0) portText = authority[(index + 1)..]; }
        if (host.Length == 0 || host.IndexOfAny([' ', '\t', '\r', '\n', '/', '\\', '@']) >= 0) throw new FormatException();
        int port = scheme == "ftp" ? 21 : 22;
        if (portText.Length > 0 && (!int.TryParse(portText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)) throw new FormatException();
        return new() { Protocol = scheme, Host = host, Port = port, User = username, Password = password, HasPassword = colon >= 0 };
    }
    static string Decode(string text) { for (int i = 0; i < text.Length; i++) if (text[i] == '%') { if (i + 2 >= text.Length || !Uri.IsHexDigit(text[i + 1]) || !Uri.IsHexDigit(text[i + 2])) throw new FormatException(); i += 2; } return Uri.UnescapeDataString(text); }
}
public sealed class LaunchGate : IDisposable
{
    readonly byte[] key = RandomNumberGenerator.GetBytes(32); readonly Dictionary<string, TimeSpan> seen = new(); readonly object gate = new();
    public bool Accept(LaunchPlan plan, TimeSpan now)
    {
        if (plan.NewWindow || plan.Error.Length > 0 || plan.Connection is not { } c) return true;
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(new object[] { plan.Diagnose, plan.Entry, c.Protocol, c.Host, c.Port, c.User, c.Password, c.HasPassword, c.KeyPath });
        string id; try { id = Convert.ToHexString(HMACSHA256.HashData(key, data)); } finally { CryptographicOperations.ZeroMemory(data); }
        lock (gate) { foreach (var old in seen.Where(x => now - x.Value > TimeSpan.FromSeconds(3)).Select(x => x.Key).ToArray()) seen.Remove(old); if (seen.ContainsKey(id)) return false; if (seen.Count >= 1024) seen.Clear(); seen[id] = now; return true; }
    }
    public void Dispose() { CryptographicOperations.ZeroMemory(key); lock (gate) seen.Clear(); }
}
