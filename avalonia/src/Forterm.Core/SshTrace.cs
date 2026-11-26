using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
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
    /// <summary>How many lines are kept: how it began, and what came last.</summary>
    public const int MostFirst = 100, MostLast = 60;
    public const int MostBanner = 2000;
    readonly object gate = new();
    readonly long began = Stopwatch.GetTimestamp();
    readonly List<Line> first = new();
    readonly Queue<Line> last = new();
    Line? newest;
    long skipped;
    string client = "", server = "", agreed = "", banner = "", ending = "", said = "";
    bool loggedIn, asked, opened, refused, served, data, byPeer;
    long endedAt;
    // Secrets are kept only as keyed hashes, of the secret and of its words; the key is this trace's own.
    readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    readonly HashSet<string> concealed = new(StringComparer.Ordinal);
    readonly List<string> named = new();

    /// <summary>One message: <see cref="Text"/> is ours, <see cref="Said"/> what the server chose (names, its reasons), then
    /// <see cref="Tail"/> ours again.</summary>
    sealed record Line(long At, string Text, string Said, string Tail) { public int Times { get; set; } = 1; }

    /// <summary>The connection was given up or ended, whoever did it.</summary>
    public bool IsOver { get { lock (gate) return ending.Length > 0; } }
    /// <summary>The server or the network ended it (not this program, not a check of its own such as the host key): then
    /// <see cref="Explanation"/> says more than the library's exception.</summary>
    public bool EndedByPeer { get { lock (gate) return byPeer; } }
    /// <summary>Raised once, on the library's receive thread, when the server or the network has ended the connection.
    /// A server that says goodbye is not an error to the library, so nothing else tells.</summary>
    public event Action? PeerEnded;

    /// <summary>A password or ticket that must not be in the report even where the server repeats it. Only a keyed hash of
    /// it is kept, so it is found where it stands as a word of its own.</summary>
    public void ConcealSecret(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return;
        lock (gate)
        {
            concealed.Add(Mac(secret));
            foreach (Match word in Word().Matches(secret)) if (word.Length >= 4) concealed.Add(Mac(word.Value));
        }
    }
    /// <summary>A name or address of this connection's way (a jump host, its user) that must not be in the report.</summary>
    public void Conceal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (gate) if (named.Count < 64 && !named.Contains(text)) named.Add(text);
    }
    string Mac(string text) => Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text.ToLowerInvariant())));
    string upstreamWhich = "", upstreamReason = "";
    SshTrace? upstream;

    /// <summary>This connection ran through a jump host whose own connection ended first: that is what ended this one, not
    /// the server at the end of it. <paramref name="which"/>: "跳板机", or "第 2 级跳板机" in a longer chain (our words);
    /// <paramref name="jump"/>: the jump host's trace, if it had one; <paramref name="reason"/>: how its end was noticed
    /// otherwise (our words). The first one counts.</summary>
    public void UpstreamEnded(string which, SshTrace? jump, string? reason)
    {
        if (ReferenceEquals(jump, this)) jump = null;
        lock (gate)
        {
            if (upstreamWhich.Length > 0) return;
            (upstreamWhich, upstream, upstreamReason) = (which, jump, reason ?? "");
        }
    }

    /// <summary>The jump host's end, said in our words and its server's; null when no jump host ended first.</summary>
    string? Upstream(Func<string, string> safe)
    {
        string which, reason; SshTrace? jump;
        lock (gate) (which, jump, reason) = (upstreamWhich, upstream, upstreamReason);
        if (which.Length == 0) return null;
        string how = jump?.Ended(safe) is { Length: > 0 } ended ? ended : reason;
        return which + "的连接先断开了" + (how.Length > 0 ? "：" + how.TrimEnd('。', '；', '，') : "") + "。经它到这台服务器的连接也随之中断。";
    }

    /// <summary>How it ended, with what the server said through <paramref name="safe"/> and this trace's own secrets
    /// hidden; "" while alive.</summary>
    string Ended(Func<string, string> safe)
    {
        string[] hidden;
        lock (gate) hidden = named.ToArray();
        lock (gate)
        {
            if (ending.Length == 0) return "";
            return Step + ending + (said.Length > 0 ? "（服务器的说明：" + safe(Hide(said, hidden, word => concealed.Contains(Mac(word)))) + "）" : "");
        }
    }

    public bool LoggedIn { get { lock (gate) return loggedIn; } }
    /// <summary>What the server shows before the login is complete (SSH_MSG_USERAUTH_BANNER). Text from the server.</summary>
    public string Banner { get { lock (gate) return banner; } }

    /// <summary>At which step, and how it ended; "" while the connection is alive.</summary>
    public string Explanation
    {
        get
        {
            if (Upstream(said => said) is { } jump) return jump;
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
        string[] hidden;
        lock (gate) hidden = hide.Concat(named).ToArray();
        string Safe(string text) => Hide(text, hidden, word => concealed.Contains(Mac(word)));
        var text = new StringBuilder();
        foreach (string line in head) text.AppendLine(line);
        string? jump = Upstream(Safe);
        lock (gate)
        {
            if (client.Length > 0) text.AppendLine("本程序标识：" + client);
            text.AppendLine("服务器标识：" + (server.Length > 0 ? Safe(server) : "（没有收到）"));
            if (agreed.Length > 0) text.AppendLine("协商算法：" + agreed);
            // Only what the server said is gone through: a name like "服务器" must not take words out of ours.
            text.AppendLine("结果：" + (jump ?? (ending.Length == 0 ? "连接仍在" : Step + ending + (said.Length > 0 ? "（服务器的说明：" + Safe(said) + "）" : ""))));
            text.AppendLine("协议消息（毫秒，→ 发出，← 收到；通道编号是收件一方的：发出的是服务器的编号，收到的是本程序的）：");
            // Names a server chooses (requests, login methods) are its words too.
            void Show(Line line) => text.Append(line.At.ToString().PadLeft(7)).Append("  ").Append(line.Text).Append(Safe(line.Said)).Append(line.Tail).AppendLine(line.Times > 1 ? " ×" + line.Times : "");
            foreach (var line in first) Show(line);
            if (skipped > 0) text.AppendLine($"      …  中间省略 {skipped} 行");
            foreach (var line in last) Show(line);
            if (endedAt > 0) text.Append(endedAt.ToString().PadLeft(7)).Append("  ").AppendLine(ending);
        }
        text.Append("没有包含：地址、用户名、密码、票据、终端内容、文件名。");
        return text.ToString();
    }

    /// <summary>Addresses, host names, mail-like user@host, ticket-like identifiers and long tokens, the given texts, and
    /// words that <paramref name="secret"/> knows are replaced. Also where the server repeats them.</summary>
    public static string Hide(string text, IEnumerable<string> hide, Func<string, bool>? secret = null)
    {
        var given = hide.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Whole words first (a password may hold the user name), then within other text what is long enough not to be an
        // ordinary word.
        text = Word().Replace(text, word => given.Contains(word.Value, StringComparer.OrdinalIgnoreCase) || secret?.Invoke(word.Value) == true ? Hidden : word.Value);
        foreach (string item in given.Where(h => h.Length > 3 || h.Any(c => c > '\x7f')).OrderByDescending(h => h.Length)) text = text.Replace(item, Hidden, StringComparison.OrdinalIgnoreCase);
        text = Mail().Replace(text, m => Common(m.Value) ? m.Value : Hidden);
        text = HostName().Replace(text, m => Common(m.Value) ? m.Value : Hidden);
        text = Guid().Replace(text, Hidden);
        text = Address().Replace(text, Hidden);
        text = Address6().Replace(text, Hidden);
        return Token().Replace(text, Hidden);
    }
    const string Hidden = "‹已隐去›";
    /// <summary>Names of the protocol's own extensions, not of anybody's machine.</summary>
    static bool Common(string name) => name.EndsWith("@openssh.com", StringComparison.OrdinalIgnoreCase) || name.EndsWith("@libssh.org", StringComparison.OrdinalIgnoreCase)
        || name.Equals("openssh.com", StringComparison.OrdinalIgnoreCase) || name.Equals("libssh.org", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"[^\s,;:()\[\]{}<>'""=@/\\|`*!?]{1,200}")] private static partial Regex Word();
    [GeneratedRegex(@"(?<![\w.+-])[\w.+-]{1,64}@[A-Za-z0-9-]{1,63}(?:\.[A-Za-z0-9-]{1,63}){0,10}")] private static partial Regex Mail();
    [GeneratedRegex(@"(?<![\w.-])(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.){1,10}[A-Za-z][A-Za-z0-9-]{0,61}[A-Za-z0-9](?![\w-])")] private static partial Regex HostName();
    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-?(?:[0-9a-fA-F]{4}-?){3}[0-9a-fA-F]{12}\b")] private static partial Regex Guid();
    [GeneratedRegex(@"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])")] private static partial Regex Address();
    [GeneratedRegex(@"(?<![0-9A-Fa-f:])(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){7}|[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6}::(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6})?|::[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6})(?:%[0-9A-Za-z]{1,16})?(?![0-9A-Fa-f:])")] private static partial Regex Address6();
    /// <summary>A run of letters and digits as tickets and keys are written: 20 or more, with both.</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9+/=_-])(?=[A-Za-z0-9+/=_-]{0,4000}[0-9])(?=[A-Za-z0-9+/=_-]{0,4000}[A-Za-z])[A-Za-z0-9+/=_-]{20,4000}(?![A-Za-z0-9+/=_-])")] private static partial Regex Token();

    /// <summary>Text from a server, as it may be shown: no control or format characters (direction marks, invisible
    /// joiners, BOM, soft hyphen), at most <paramref name="most"/> characters. Where it is cut inside something written like
    /// a name, address or ticket, that is left out with it: a part of one is not recognised as one.</summary>
    internal static string Plain(string? text, int most, bool lines = false)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var kept = new StringBuilder();
        bool cut = false;
        foreach (Rune r in text.ReplaceLineEndings("\n").EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(r);
            if (r.Value != '\n' && (Rune.IsControl(r) || category is UnicodeCategory.Format or UnicodeCategory.PrivateUse)) continue;
            if (kept.Length + r.Utf16SequenceLength > most) { cut = true; break; }
            if (r.Value == '\n') kept.Append(lines ? '\n' : ' ');
            else kept.Append(r.ToString());
        }
        string result = kept.ToString();
        if (cut)
        {
            int end = result.Length;
            while (end > 0 && result.Length - end < 64 && Tokenish(result[end - 1])) end--;
            if (end > 0 && Tokenish(result[end - 1])) end = result.Length; // longer than any name: cut where it is
            end = Math.Min(end, most - 1); // and the "…" within the limit
            if (end > 0 && char.IsHighSurrogate(result[end - 1])) end--;
            result = result[..end].TrimEnd() + "…";
        }
        return result.Trim();
    }

    static bool Tokenish(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '+' or '/' or '=' or '.' or '@' or ':' or '%' or '~';

    void Add(string text, string fromServer = "", string tail = "")
    {
        if (newest != null && newest.Text == text && newest.Said == fromServer && newest.Tail == tail) { newest.Times++; return; }
        newest = new(Now, text, fromServer, tail);
        if (first.Count < MostFirst) { first.Add(newest); return; }
        last.Enqueue(newest);
        if (last.Count > MostLast) { last.Dequeue(); skipped++; }
    }
    long Now => (long)Stopwatch.GetElapsedTime(began).TotalMilliseconds;

    void Message(bool sent, string name, string number, string message)
    {
        if (name == "SSH_MSG_USERAUTH_BANNER") { if (!sent && message.Length > name.Length) OnBanner(message[name.Length..].TrimStart()); return; }
        // What is in the channels is many messages, and says nothing here.
        bool contents = name is "SSH_MSG_CHANNEL_DATA" or "SSH_MSG_CHANNEL_EXTENDED_DATA" or "SSH_MSG_CHANNEL_WINDOW_ADJUST";
        var detail = contents ? default : Detail(message);
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
            if (contents) Add("↔ 通道数据");
            else Add((sent ? "→ " : "← ") + name.Replace("SSH_MSG_", "") + "(" + number + ")" + (detail.Text ?? "").Replace(" 通道#", sent ? " 对方通道" : " 本方通道"), detail.Said ?? "", detail.Tail ?? "");
        }
    }

    void End(string how, string why = "", bool peer = false)
    {
        lock (gate)
        {
            if (ending.Length > 0) { if (said.Length == 0) said = why; return; }
            ending = how; said = why; endedAt = Now; byPeer = peer;
        }
        if (peer) PeerEnded?.Invoke();
    }

    /// <summary>How an exception the library raised ended the connection, and whether the server or the network did it.</summary>
    static (string How, bool Peer) How(Exception error) => error switch
    {
        SshConnectionException { InnerException: SocketException socket } => (Socket(socket), true),
        SocketException socket => (Socket(socket), true),
        SshConnectionException { DisconnectReason: DisconnectReason.ConnectionLost, InnerException: null } => ("服务器没有说明原因就关闭了连接", true),
        SshConnectionException e when SshErrors.Own(e) is { } own => (own, false),
        SshConnectionException e => ("连接因协议错误而结束（代码 " + (int)e.DisconnectReason + "）", true),
        _ => ("连接因内部错误而结束（" + error.GetType().Name + "）", false),
    };
    static string Socket(SocketException e) => SshErrors.Network(e);

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
        else if (template.StartsWith("Server version ", StringComparison.Ordinal)) { lock (gate) server = Identification(version?.ToString()); }
        else if (template.StartsWith("[{SessionId}] Disconnect received", StringComparison.Ordinal)) End("服务器断开了连接，代码 " + (reason is DisconnectReason code ? (int)code + "/" + code : "未知"), Plain(description?.ToString(), 300), peer: true);
        else if (template == "[{SessionId}] Raised exception" && error != null) { var (how, peer) = How(error); End(how, peer: peer); }
        else if (template == "[{SessionId}] Disconnecting session.") End("本程序关闭了连接");
    }

    /// <summary>The server's protocol and software version; a comment after them may name the machine and is left out.</summary>
    static string Identification(string? text)
    {
        text = Plain(text, 255);
        int space = text.IndexOf(' ');
        return space < 0 ? text : text[..space] + "（另有说明，未记录）";
    }

    /// <summary>Names a message may carry: a channel and the kind of request on it, or login methods.</summary>
    static (string Text, string Said, string Tail) Detail(string message)
    {
        if (Login().Match(message) is { Success: true } login) return (" " + login.Groups[1].Value, "", "");
        if (Refused().Match(message) is { Success: true } refused) return (" 可用方式 ", refused.Groups[1].Value, refused.Groups[2].Value == "True" ? "（部分通过）" : "");
        if (Refusal().Match(message) is { Success: true } refusal) return (" 通道#" + refusal.Groups[1].Value + " 代码" + refusal.Groups[2].Value + " ", Plain(refusal.Groups[3].Value, 200), "");
        if (Channel().Match(message) is { Success: true } channel) return (" 通道#" + channel.Groups[1].Value + (channel.Groups[2].Length > 0 ? " " : ""), channel.Groups[2].Value, "");
        return ("", "", "");
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
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information || level == LogLevel.Trace;
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
    /// <summary>A connection this program gave up on because of a check of its own, in its words; null for anything else.</summary>
    public static string? Own(SshConnectionException error)
    {
        string m = error.Message;
        if (m == "Host key could not be verified.") return "服务器的主机密钥没有通过核对（指纹没有被接受，或与记录的不一致），没有登录";
        if (m.StartsWith("No matching ", StringComparison.Ordinal))
            return "双方没有共同支持的" + (m.Contains("key exchange", StringComparison.Ordinal) ? "密钥交换算法" : m.Contains("host key", StringComparison.Ordinal) ? "主机密钥算法"
                : m.Contains("encryption", StringComparison.Ordinal) ? "加密算法" : m.Contains("MAC", StringComparison.Ordinal) ? "校验算法" : m.Contains("compression", StringComparison.Ordinal) ? "压缩算法" : "算法")
                + "，没有登录；可以在连接设置的算法偏好里查看";
        if (m.StartsWith("Server version '", StringComparison.Ordinal)) return "服务器的 SSH 协议版本不受支持（只支持 SSH-2），没有登录";
        if (m.Contains("before a valid SSH identification string was received", StringComparison.Ordinal))
            return m.StartsWith("The connection to the remote server was closed", StringComparison.Ordinal) ? "对方在说明自己是 SSH 服务器之前就关闭了连接（可能不是 SSH 端口，或被拒绝接入）" : "对方没有按 SSH 协议应答（可能不是 SSH 端口）";
        return null;
    }

    public static string Explain(Exception error) => error switch
    {
        ObjectDisposedException => "连接已断开",
        SshConnectionException own when Own(own) is { } text => text,
        SshConnectionException { InnerException: SocketException socket } => Network(socket),
        SocketException socket => Network(socket),
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

    internal static string Network(SocketException e) => e.SocketErrorCode switch
    {
        SocketError.ConnectionRefused => "对方拒绝连接（端口没有开放，或被防火墙拒绝）",
        SocketError.TimedOut => "网络没有应答，连接超时",
        SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "找不到这个主机名",
        SocketError.NetworkUnreachable or SocketError.HostUnreachable => "网络不通，到不了这个地址",
        SocketError.ConnectionReset => "连接被强行重置（服务器，或中间的防火墙、安全网关）",
        SocketError.ConnectionAborted => "连接被本机或网络设备中止",
        _ => "网络错误（" + e.SocketErrorCode + "）",
    };
}
