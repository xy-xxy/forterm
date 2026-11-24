using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Forterm.Core;
using Renci.SshNet.Common;
namespace Forterm.Desktop;

/// <summary>Resolves immutable saved-profile jump chains. Hidden jump roots
/// are retained by descendants, cached weakly and never reauthenticated by retry.</summary>
public sealed class ConnectionCoordinator(Window owner, ProfileStore store, ProfileRepository credentials, KnownHosts knownHosts, Func<UserPreferences> preferences, Func<HostKeyQuestion, CancellationToken, Task<bool>>? confirm = null)
{
    static readonly ConcurrentDictionary<string, WeakReference<SharedSshSession>> cache = new(); static readonly ConcurrentDictionary<string, SemaphoreSlim> slots = new();
    public static IReadOnlyList<ConnectionProfile> ResolveChain(ConnectionProfile target, IReadOnlyList<ConnectionProfile> profiles)
    {
        if (target.Protocol is "ftp" or "telnet") { if (target.JumpId.Length > 0) throw new IOException("当前协议不支持SSH跳板，未绕过配置直连"); return new[] { target }; }
        var byId = profiles.ToDictionary(p => p.Id); var visited = new HashSet<string>(); var chain = new List<ConnectionProfile>(); var current = target;
        while (true) { if (current.Protocol is not ("ssh" or "sftp")) throw new IOException("SSH跳板链包含非SSH协议"); if (!visited.Add(current.Id) || chain.Count >= 5) throw new IOException("跳板链循环或超过4级跳板，未读取凭据或发起连接"); chain.Add(current); if (current.JumpId.Length == 0) break; if (!byId.TryGetValue(current.JumpId, out current!)) throw new IOException("保存的跳板连接不存在"); }
        chain.Reverse(); return chain;
    }
    string Key(IReadOnlyList<ConnectionProfile> prefix) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((store.DirectoryPath ?? "memory") + JsonSerializer.Serialize(prefix))));
    sealed class JumpUse(SharedSshSession root, IDisposable? lease) : IDisposable { public SharedSshSession Root => root; public void Dispose() { if (lease == null) root.Dispose(); else lease.Dispose(); } }
    public async Task<SharedSshSession> ConnectAsync(ConnectionProfile target, string password, string phrase, bool temporary, CancellationToken ct, string keyOverride = "", bool rememberHostKeys = true)
    {
        if (target.Protocol == "ftp") throw new IOException("FTP认证使用独立FTP连接，不向FTP端口发送SSH握手");
        var chain = ResolveChain(target, store.Load()); JumpUse? jump = null;
        try
        {
            for (int i = 0; i < chain.Count; i++)
            {
                var p = chain[i]; string key = Key(chain.Take(i + 1).ToArray());
                if (i == chain.Count - 1) { var root = await Create(p, password, phrase, temporary, jump?.Root, ct, keyOverride, false, rememberHostKeys); if (rememberHostKeys) cache[key] = new(root); return root; }
                var slot = slots.GetOrAdd(key, _ => new(1, 1)); await slot.WaitAsync(ct); JumpUse? next = null;
                try
                {
                    if (cache.TryGetValue(key, out var weak) && weak.TryGetTarget(out var existing) && existing.IsConnected) { try { next = new(existing, existing.Retain()); } catch (ObjectDisposedException) { } }
                    if (next == null) { var root = await Create(p, "", "", false, jump?.Root, ct, "", true, rememberHostKeys); cache[key] = new(root); next = new(root, null); }
                }
                finally { slot.Release(); }
                jump?.Dispose(); jump = next;
            }
            throw new IOException("连接链为空");
        }
        finally { jump?.Dispose(); password = ""; phrase = ""; }
    }
    async Task<SharedSshSession> Create(ConnectionProfile p, string password, string phrase, bool temporary, SharedSshSession? jump, CancellationToken ct, string keyOverride, bool isJump, bool rememberHostKeys)
    {
        string mode = keyOverride.Length > 0 ? "key" : p.AuthMode; string key = keyOverride.Length > 0 ? keyOverride : p.KeyPath;
        try
        {
            if (!temporary && (mode is "password" or "keyboard") && password.Length == 0 && p.HasPassword) password = await credentials.PasswordAsync(p, ct) ?? throw new IOException("系统密码不存在，请重新输入");
            if (!temporary && mode == "key" && phrase.Length == 0 && p.HasPassphrase) phrase = await credentials.PassphraseAsync(p, ct) ?? throw new IOException("系统私钥口令不存在，请重新输入");
            if (isJump && (mode is "password" or "keyboard") && password.Length == 0 && !p.HasPassword) { var answer = await Dispatcher.UIThread.InvokeAsync(() => UI.AskCredentials(owner, new(1, false, false, "跳板：" + p.Name + "（" + p.Host + "）", new[] { new CredentialQuestion("本次登录密码", false) }), ct)); if (answer == null) throw new OperationCanceledException(ct); password = answer[0]; Array.Clear(answer); }
            var options = preferences().SessionOptions() with { Jump = jump, AgentFingerprint = p.AgentFingerprint };
            Task<bool> Verify(string fp, CancellationToken token) => knownHosts.VerifyAsync(p.Host, p.Port, fp, rememberHostKeys, confirm ?? ((q, t) => Dispatcher.UIThread.InvokeAsync(() => UI.Confirm(owner, q.Changed ? "主机指纹已变化，请重新核对" : "确认主机指纹", q.Changed ? $"{q.Endpoint}\n旧指纹：{q.Previous}\n新指纹：{q.Current}" : $"{q.Endpoint}\n{q.Current}", t))), token);
            async Task<SharedSshSession> Open() => await SharedSshSession.ConnectAsync(p.Host, p.Port, p.User, password, Verify, ct, temporary, (q, t) => Dispatcher.UIThread.InvokeAsync(() => UI.AskCredentials(owner, q, t)), mode, key, phrase, options);
            try { return await Open(); } catch (SshPassPhraseNullOrEmptyException) { var answer = await Dispatcher.UIThread.InvokeAsync(() => UI.AskCredentials(owner, new(1, false, temporary, "私钥已加密；口令仅本次使用，不自动使用登录密码或临时票据。", new[] { new CredentialQuestion("私钥口令", false) }), ct)); if (answer == null) throw new OperationCanceledException(ct); phrase = answer[0]; Array.Clear(answer); return await Open(); }
        }
        finally { password = ""; phrase = ""; }
    }
}
