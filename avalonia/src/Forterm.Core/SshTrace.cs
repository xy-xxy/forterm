using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Renci.SshNet.Common;
using Renci.SshNet.Messages.Transport;

namespace Forterm.Core;

/// <summary>What a connection and its server said to each other while it was opened: the names of the messages, the
/// channel they were for and when, and how the connection ended. Never contents, user names, addresses or secrets. It is
/// only kept in memory. Used to tell the user, and whoever runs the server, at which step a server hung up.</summary>
public sealed partial class SshTrace : ILoggerFactory
{
    /// <summary>How many lines are kept; after that only the end of the connection is.</summary>
    public const int Most = 160;
    public const int MostBanner = 2000;
    readonly object gate = new();
    readonly long began = Stopwatch.GetTimestamp();
    readonly List<Line> lines = new();
    string client = "", server = "", agreed = "", banner = "", ending = "", said = "";
    bool loggedIn, asked, opened, refused, served, data, full;
    long endedAt;

    sealed record Line(long At, string Text, string Said) { public int Times { get; set; } = 1; }

    /// <summary>The connection was given up or ended, whoever did it.</summary>
    public bool IsOver { get { lock (gate) return ending.Length > 0; } }
    public bool LoggedIn { get { lock (gate) return loggedIn; } }
    /// <summary>What the server shows before the login is complete (SSH_MSG_USERAUTH_BANNER). Text from the server.</summary>
    public string Banner { get { lock (gate) return banner; } }

    /// <summary>At which step, and how it ended; "" while the connection is alive.</summary>
    public string Explanation
    {
        get
        {
            lock (gate)
            {
                if (ending.Length == 0) return "";
                return Step + ending + (said.Length > 0 ? "（服务器的说明：" + said + "）" : "");
            }
        }
    }

    string Step => !loggedIn ? "登录完成之前，"
        : !asked ? "登录已通过，还没有请求打开通道，"
        : !opened && refused ? "登录已通过，服务器拒绝打开通道后，"
        : !opened ? "登录已通过，请求打开第一条通道时，"
        : !served ? "登录已通过，通道已打开，请求终端或文件服务时，"
        : data ? "" : "登录已通过，终端或文件服务刚打开时，";

    void OnBanner(string text)
    {
        text = Plain(text, MostBanner, lines: true);
        lock (gate)
        {
            if (banner.Length < MostBanner) banner = Plain(banner.Length == 0 ? text : banner + "\n" + text, MostBanner, lines: true);
            Add("← 登录提示（" + text.Length + " 字，内容不在诊断里）");
        }
    }
    internal void OnConnection(Renci.SshNet.ConnectionInfo info)
    {
        lock (gate)
        {
            client = info.ClientVersion;
            agreed = string.Join("，", new[] { info.CurrentKeyExchangeAlgorithm, info.CurrentHostKeyAlgorithm, info.CurrentClientEncryption, info.CurrentServerEncryption, info.CurrentClientHmacAlgorithm }.Where(n => !string.IsNullOrEmpty(n)).Distinct());
        }
    }

    /// <summary>For the clipboard. <paramref name="hide"/>: what must not be in it even where the server repeats it.</summary>
    public string Report(IEnumerable<string> head, IEnumerable<string> hide)
    {
        var hidden = hide.ToArray();
        string Safe(string fromServer) => Hide(fromServer, hidden);
        var text = new StringBuilder();
        foreach (string line in head) text.AppendLine(line);
        lock (gate)
        {
            if (client.Length > 0) text.AppendLine("本程序标识：" + client);
            text.AppendLine("服务器标识：" + (server.Length > 0 ? Safe(server) : "（没有收到）"));
            if (agreed.Length > 0) text.AppendLine("协商算法：" + agreed);
            text.AppendLine("结果：" + (ending.Length == 0 ? "连接仍在" : Safe(Explanation)));
            text.AppendLine("协议消息（毫秒，→ 发出，← 收到）：");
            foreach (var line in lines) text.Append(line.At.ToString().PadLeft(7)).Append("  ").Append(line.Text).Append(Safe(line.Said)).AppendLine(line.Times > 1 ? " ×" + line.Times : "");
            if (full) text.AppendLine("      …  之后的消息不再记录");
            if (endedAt > 0) text.Append(endedAt.ToString().PadLeft(7)).Append("  ").AppendLine(ending);
        }
        text.Append("没有包含：地址、用户名、密码、票据、终端内容、文件名。");
        return text.ToString();
    }

    /// <summary>Addresses, ticket-like identifiers and the given texts are replaced. Also where the server repeats them.</summary>
    public static string Hide(string text, IEnumerable<string> hide)
    {
        foreach (string secret in hide.Where(h => h.Length > 3).OrderByDescending(h => h.Length)) text = text.Replace(secret, Hidden, StringComparison.OrdinalIgnoreCase);
        text = Guid().Replace(text, Hidden);
        return Address().Replace(text, Hidden);
    }
    const string Hidden = "‹已隐去›";

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-?(?:[0-9a-fA-F]{4}-?){3}[0-9a-fA-F]{12}\b")] private static partial Regex Guid();
    [GeneratedRegex(@"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])")] private static partial Regex Address();

    internal static string Plain(string? text, int most, bool lines = false)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var kept = new StringBuilder();
        foreach (char c in text.ReplaceLineEndings("\n"))
        {
            if (kept.Length == most) break;
            if (c == '\n') kept.Append(lines ? '\n' : ' ');
            else if (c is '\u202a' or '\u202b' or '\u202c' or '\u202d' or '\u202e' or '\u2066' or '\u2067' or '\u2068' or '\u2069' or '\u200e' or '\u200f') continue;
            else if (!char.IsControl(c)) kept.Append(c);
        }
        return kept.ToString().Trim();
    }

    void Add(string text, string fromServer = "")
    {
        if (full) return;
        if (lines.Count > 0 && lines[^1].Text == text && lines[^1].Said == fromServer) { lines[^1].Times++; return; }
        if (lines.Count == Most) { full = true; return; }
        lines.Add(new(Now, text, fromServer));
    }
    long Now => (long)Stopwatch.GetElapsedTime(began).TotalMilliseconds;

    void Message(bool sent, string name, string number, string message)
    {
        if (name == "SSH_MSG_USERAUTH_BANNER") { if (!sent && message.Length > name.Length) OnBanner(message[name.Length..].TrimStart()); return; }
        var detail = Detail(message);
        lock (gate)
        {
            switch (name)
            {
                case "SSH_MSG_USERAUTH_SUCCESS": loggedIn |= !sent; break;
                case "SSH_MSG_CHANNEL_OPEN": asked |= sent; break;
                case "SSH_MSG_CHANNEL_OPEN_CONFIRMATION": opened |= !sent; break;
                case "SSH_MSG_CHANNEL_OPEN_FAILURE": refused |= !sent; break;
                case "SSH_MSG_CHANNEL_SUCCESS": served |= !sent; break;
                case "SSH_MSG_CHANNEL_DATA": data |= !sent; break;
            }
            Add((sent ? "→ " : "← ") + name.Replace("SSH_MSG_", "") + "(" + number + ")" + detail.Text, detail.Said);
        }
    }

    void End(string how, string why = "")
    {
        lock (gate)
        {
            if (ending.Length > 0) { if (said.Length == 0) said = why; return; }
            ending = how; said = why; endedAt = Now;
        }
    }

    static string How(Exception error) => error switch
    {
        SshConnectionException { InnerException: SocketException socket } => Socket(socket),
        SocketException socket => Socket(socket),
        SshConnectionException { DisconnectReason: DisconnectReason.ConnectionLost } => "服务器没有说明原因就关闭了连接",
        SshConnectionException e => "连接因协议错误而结束（代码 " + (int)e.DisconnectReason + "）",
        _ => "连接因内部错误而结束（" + error.GetType().Name + "）",
    };
    static string Socket(SocketException e) => e.SocketErrorCode switch
    {
        SocketError.ConnectionReset => "连接被强行重置（服务器，或中间的防火墙、安全网关）",
        SocketError.ConnectionAborted => "连接被本机或网络设备中止",
        SocketError.TimedOut => "网络没有应答，连接超时",
        _ => "网络错误（" + e.SocketErrorCode + "）",
    };

    void Log(IReadOnlyList<KeyValuePair<string, object?>> values, Exception? error)
    {
        string template = "", message = "", name = "", number = "";
        object? version = null, reason = null, description = null;
        foreach (var (key, value) in values)
            switch (key)
            {
                case "{OriginalFormat}": template = value as string ?? ""; break;
                case "MessageName": name = value as string ?? ""; break;
                case "MessageNumber": number = value?.ToString() ?? ""; break;
                case "Message": message = value as string ?? ""; break;
                case "ServerIdentification": version = value; break;
                case "ReasonCode": reason = value; break;
                case "MessageDescription": description = value; break;
            }
        // Only what is known here; everything else the library says (addresses, for example) is not kept.
        if (template.StartsWith("[{SessionId}] Sending message ", StringComparison.Ordinal)) Message(true, name, number, message);
        else if (template.StartsWith("[{SessionId}] Received message ", StringComparison.Ordinal)) Message(false, name, number, message);
        else if (template.StartsWith("Failure sending message ", StringComparison.Ordinal)) { lock (gate) Add("→ " + name.Replace("SSH_MSG_", "") + " 没有发出"); }
        else if (template.StartsWith("Server version ", StringComparison.Ordinal)) { lock (gate) server = Plain(version?.ToString(), 255); }
        else if (template.StartsWith("[{SessionId}] Disconnect received", StringComparison.Ordinal)) End("服务器断开了连接，代码 " + (reason is DisconnectReason code ? (int)code + "/" + code : "未知"), Plain(description?.ToString(), 300));
        else if (template == "[{SessionId}] Raised exception" && error != null) End(How(error));
        else if (template == "[{SessionId}] Disconnecting session.") End("本程序关闭了连接");
    }

    /// <summary>Names a message may carry: a channel and the kind of request on it, or login methods.</summary>
    static (string Text, string Said) Detail(string message)
    {
        if (Login().Match(message) is { Success: true } login) return (" " + login.Groups[1].Value, "");
        if (Refused().Match(message) is { Success: true } refused) return (" 可用方式 " + refused.Groups[1].Value + (refused.Groups[2].Value == "True" ? "（部分通过）" : ""), "");
        if (Refusal().Match(message) is { Success: true } refusal) return (" 通道" + refusal.Groups[1].Value + " 代码" + refusal.Groups[2].Value + " ", Plain(refusal.Groups[3].Value, 200));
        if (Channel().Match(message) is { Success: true } channel) return (" 通道" + channel.Groups[1].Value + (channel.Groups[2].Length > 0 ? " " + channel.Groups[2].Value : ""), "");
        return ("", "");
    }
    [GeneratedRegex(@"^SSH_MSG_USERAUTH_REQUEST \(([a-z-]{1,40})\)$")] private static partial Regex Login();
    [GeneratedRegex(@"^SSH_MSG_USERAUTH_FAILURE ([a-z0-9,@.-]{0,200}) \(PartialSuccess:(True|False)\)$")] private static partial Regex Refused();
    [GeneratedRegex(@"^SSH_MSG_CHANNEL_OPEN_FAILURE : #(\d{1,10}) (\d{1,10}) (.*)$", RegexOptions.Singleline)] private static partial Regex Refusal();
    [GeneratedRegex(@"^SSH_MSG_CHANNEL_[A-Z_]{1,40} : #(\d{1,10})(?: ([a-z0-9@.-]{1,60}))?$")] private static partial Regex Channel();

    ILogger ILoggerFactory.CreateLogger(string categoryName) => categoryName == "Renci.SshNet.Session" ? new Sink(this) : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    void ILoggerFactory.AddProvider(ILoggerProvider provider) { }
    void IDisposable.Dispose() { }

    sealed class Sink(SshTrace trace) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        // Messages are only looked at while there is room for them.
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information || level == LogLevel.Trace && !Volatile.Read(ref trace.full);
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            // A trace must never be what ends a connection.
            try { if (IsEnabled(level) && state is IReadOnlyList<KeyValuePair<string, object?>> values) trace.Log(values, error); }
            catch (Exception) { }
        }
    }
}

/// <summary>The library's messages that a user meets, in words of ours.</summary>
public static class SshErrors
{
    public static string Explain(Exception error) => error switch
    {
        ObjectDisposedException => "连接已断开",
        SshOperationTimeoutException => "服务器在限定时间内没有答复",
        SshChannelOpenException open => open.ReasonCode switch
        {
            1 => "服务器不允许打开这条通道",
            2 => "服务器没能连上目标",
            3 => "服务器不支持这种通道",
            4 => "服务器资源不足，不能再打开通道",
            _ => "服务器拒绝打开通道（代码 " + open.ReasonCode + "）",
        } + (SshTrace.Plain(open.Description, 300) is { Length: > 0 } said ? "：" + said : ""),
        SshConnectionException { DisconnectReason: DisconnectReason.ConnectionLost, InnerException: null } => "服务器没有说明原因就关闭了连接",
        SshConnectionException { Message: "Client not connected." } => "连接已断开",
        SshException e when e.Message.StartsWith("The pseudo-terminal request was not accepted", StringComparison.Ordinal) => "服务器不允许分配终端",
        SshException e when e.Message.StartsWith("The request to start a shell was not accepted", StringComparison.Ordinal) => "服务器不允许启动终端",
        SshException e when e.Message.StartsWith("Subsystem 'sftp' could not be executed", StringComparison.Ordinal) => "服务器不提供 SFTP 服务",
        _ => error.Message,
    };
}
