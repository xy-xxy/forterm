using Forterm.Core;
using Forterm.Studio.Library;
using Renci.SshNet.Common;

namespace Forterm.Studio.Services;

/// <summary>Secrets typed into the editor for "测试连接" or a one-off connection; never stored.</summary>
public sealed class OneTimeSecrets
{
    public string Password { get; set; } = "";
    public string Passphrase { get; set; } = "";
    public void Clear() { Password = ""; Passphrase = ""; }
}

/// <summary>Connects saved profiles: saved secrets from the OS credential store, the explicit
/// jump chain (first entry connects first), and the outbound proxy for the first hop. Each hop is
/// authenticated exactly once; nothing reconnects or retries with stored secrets on failure.</summary>
public sealed class ProfileConnector(LibraryStore library, CredentialStore credentials, KnownHosts knownHosts, IStudioInteraction interaction)
{
    public LibraryStore Library => library;

    /// <summary>Jumps in order, then the target. A jump's own chain is not followed: the
    /// target's list is the whole path, as in the editor.</summary>
    public IReadOnlyList<StudioProfile> ResolveChain(StudioProfile target)
    {
        var chain = new List<StudioProfile>();
        foreach (string id in target.JumpChain)
        {
            var jump = library.Find(id) ?? throw new IOException("跳板连接已不存在，请编辑此连接的跳板机链路");
            if (!jump.IsSsh) throw new IOException("跳板必须是 SSH 连接");
            if (chain.Any(c => c.Id == id) || id == target.Id) throw new IOException("跳板链包含重复连接或连接自身");
            chain.Add(jump);
        }
        if (chain.Count > LibraryStore.MaxJumps) throw new IOException($"跳板最多 {LibraryStore.MaxJumps} 级");
        chain.Add(target);
        return chain;
    }

    /// <summary>More than any server allows by default: one too many is refused by the server, with a reason, instead of
    /// waiting for ever.</summary>
    public const int MaxChannels = 200;

    public static SessionConnectOptions OptionsFor(StudioProfile p) => new()
    {
        MaxChannels = MaxChannels,
        CommonChannels = true,
        KeepAliveEnabled = p.HeartbeatMs > 0,
        KeepAliveSeconds = p.HeartbeatMs > 0 ? Math.Clamp(p.HeartbeatMs / 1000, 5, 600) : 15,
        ConnectTimeoutSeconds = p.TimeoutMs > 0 ? Math.Clamp(p.TimeoutMs / 1000, 5, 300) : 45,
        Proxy = !p.ProxyDisabled && p.Proxy.Trim().Length > 0 ? ProxyParser.Parse(p.Proxy) : null,
        PreferredCipher = p.IsSsh ? p.Cipher : "",
        PreferredKeyExchange = p.IsSsh ? p.KeyExchange : "",
    };

    public async Task<SharedSshSession> ConnectSshAsync(StudioProfile target, CancellationToken ct, OneTimeSecrets? targetSecrets = null, bool persistHostKeys = true, SshTrace? trace = null)
    {
        if (!target.IsSsh) throw new IOException("不是 SSH 连接");
        var chain = ResolveChain(target);
        // The proxy configured on the target applies to the first hop ("代理优先于跳板机链路").
        var firstHopProxy = OptionsFor(target).Proxy ?? OptionsFor(chain[0]).Proxy;
        SharedSshSession? previous = null;
        try
        {
            for (int i = 0; i < chain.Count; i++)
            {
                var hop = chain[i];
                bool isTarget = i == chain.Count - 1;
                var options = OptionsFor(hop) with { Jump = previous, Proxy = i == 0 ? firstHopProxy : null, Trace = isTarget ? trace : null };
                var next = await ConnectOneAsync(hop, options, isTarget ? targetSecrets : null, !isTarget, persistHostKeys, ct);
                // The next hop holds its own lease on the jump transport; drop ours.
                previous?.Dispose();
                previous = next;
            }
            var result = previous!;
            previous = null;
            return result;
        }
        finally { previous?.Dispose(); }
    }

    async Task<SharedSshSession> ConnectOneAsync(StudioProfile p, SessionConnectOptions options, OneTimeSecrets? secrets, bool isJump, bool persistHostKeys, CancellationToken ct)
    {
        string password = secrets?.Password ?? "", phrase = secrets?.Passphrase ?? "";
        string who = (isJump ? "跳板：" : "") + Display(p);
        try
        {
            if (p.AuthMode is "password" or "keyboard" && password.Length == 0 && p.HasPassword)
                password = await credentials.PasswordAsync(p.Id, ct) ?? throw new IOException(who + " 的已保存密码不存在，请编辑连接重新保存");
            if (p.AuthMode == "key" && phrase.Length == 0 && p.HasPassphrase)
                phrase = await credentials.PassphraseAsync(p.Id, ct) ?? throw new IOException(who + " 的已保存私钥口令不存在，请编辑连接重新保存");
            if (p.AuthMode == "password" && password.Length == 0)
                password = await AskOnce(who + "：未保存密码，输入内容仅用于本次连接。", "登录密码", ct);
            Task<bool> Verify(string fingerprint, CancellationToken token) =>
                knownHosts.VerifyAsync(p.Host, p.Port, fingerprint, persistHostKeys, interaction.ConfirmHostKeyAsync, token);
            Task<SharedSshSession> Open() => SharedSshSession.ConnectAsync(p.Host, p.Port, p.User, password, Verify, ct,
                temporary: false, challenge: interaction.AskCredentialsAsync, authMode: p.AuthMode, keyPath: p.KeyPath, keyPassphrase: phrase, options: options);
            try { return await Open(); }
            catch (SshPassPhraseNullOrEmptyException)
            {
                phrase = await AskOnce(who + "：私钥已加密，口令仅用于本次连接，不会自动使用登录密码。", "私钥口令", ct);
                return await Open();
            }
        }
        finally { password = ""; phrase = ""; }
    }

    public async Task<FtpFileSession> ConnectFtpAsync(StudioProfile p, CancellationToken ct, OneTimeSecrets? secrets = null)
    {
        string password = secrets?.Password ?? "";
        try
        {
            if (password.Length == 0 && p.HasPassword) password = await credentials.PasswordAsync(p.Id, ct) ?? throw new IOException("已保存的 FTP 密码不存在，请编辑连接重新保存");
            if (password.Length == 0) password = await AskOnce(Display(p) + "：未保存密码，输入内容仅用于本次连接。", "登录密码", ct);
            return await FtpFileSession.ConnectAsync(p.Host, p.Port, p.User, password, ct, OptionsFor(p) with { Proxy = null });
        }
        finally { password = ""; }
    }

    public static Task<TelnetPtyConnection> ConnectTelnetAsync(StudioProfile p, CancellationToken ct) => TelnetPtyConnection.ConnectAsync(p.Host, p.Port, ct, flowControl: true);

    /// <summary>Authenticates and disconnects. Host keys confirmed here are NOT added to the trust store.</summary>
    public async Task<string> TestAsync(StudioProfile p, OneTimeSecrets secrets, CancellationToken ct)
    {
        switch (p.Protocol)
        {
            case "ftp":
                using (await ConnectFtpAsync(p, ct, secrets)) return "FTP 登录成功（测试连接已断开）";
            case "telnet":
                using (await ConnectTelnetAsync(p, ct)) return "Telnet 端口可连接（测试连接已断开）";
            default:
                using (await ConnectSshAsync(p, ct, secrets, persistHostKeys: false))
                    return p.JumpChain.Count > 0 ? $"经 {p.JumpChain.Count} 级跳板认证成功（测试连接已断开）" : "SSH 认证成功（测试连接已断开）";
        }
    }

    async Task<string> AskOnce(string instruction, string prompt, CancellationToken ct)
    {
        var answer = await interaction.AskCredentialsAsync(new CredentialChallenge(1, false, false, instruction, new[] { new CredentialQuestion(prompt, false) }), ct);
        if (answer == null || answer.Length == 0) throw new OperationCanceledException("已取消");
        try { return answer[0]; }
        finally { Array.Clear(answer); }
    }

    static string Display(StudioProfile p) => (p.Name.Length > 0 ? p.Name + " " : "") + $"({p.User}@{p.Host}:{p.Port})";
}
