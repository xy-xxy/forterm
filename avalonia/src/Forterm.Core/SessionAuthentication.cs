using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
namespace Forterm.Core;

public sealed record CredentialQuestion(string Text, bool Echo);
public sealed record CredentialChallenge(int Round, bool PasswordAlreadySubmitted, bool Temporary, string Instruction, IReadOnlyList<CredentialQuestion> Questions);
public sealed class SessionAuthentication : IDisposable
{
    readonly List<AuthenticationMethod> methods = new(); readonly List<IDisposable> resources = new();
    readonly Func<CredentialChallenge, CancellationToken, Task<string[]?>>? prompt;
    readonly CancellationToken ct; readonly bool temporary; string initialPassword; bool passwordSubmitted, usedInteractivePassword; int rounds;
    public IReadOnlyList<AuthenticationMethod> Methods => methods;
    public SessionAuthentication(string user, string password, bool temporary, Func<CredentialChallenge, CancellationToken, Task<string[]?>>? prompt, CancellationToken ct, string mode = "password", string keyPath = "", string keyPassphrase = "", SessionConnectOptions? options = null)
    {
        this.prompt = prompt; this.ct = ct; this.temporary = temporary; initialPassword = mode is "key" or "agent" ? "" : password;
        if (mode == "key")
        {
            if (new FileInfo(keyPath).Length > 2 * 1024 * 1024) throw new IOException("私钥文件超过2MiB边界");
            var key = new PrivateKeyFile(keyPath, keyPassphrase); resources.Add(key); methods.Add(new PrivateKeyAuthenticationMethod(user, key));
        }
        else if (mode == "agent")
        {
            var agent = (options?.AgentProvider ?? OpenSshAgent.ConnectDefaultAsync)(ct).GetAwaiter().GetResult();
            try { var sources = agent.SourcesAsync(options?.AgentFingerprint ?? "", ct).GetAwaiter().GetResult(); methods.Add(new PrivateKeyAuthenticationMethod(user, sources.ToArray())); resources.Add(agent); }
            catch { agent.Dispose(); throw; }
        }
        else if (mode == "password") methods.Add(new FreshPasswordMethod(user, this));
        else if (mode != "keyboard") throw new NotSupportedException("该认证方式尚未迁移");
        var interactive = new KeyboardInteractiveAuthenticationMethod(user); interactive.AuthenticationPrompt += (_, e) =>
        {
            ct.ThrowIfCancellationRequested(); if (++rounds > 16 || e.Prompts.Count > 16) throw new SshAuthenticationException("服务器认证问题过多");
            if (e.Prompts.Count == 0) return;
            if (initialPassword.Length > 0 && !temporary && !passwordSubmitted && !usedInteractivePassword && rounds == 1 && e.Prompts.Count == 1 && !e.Prompts[0].IsEchoed && IsPasswordQuestion(e.Prompts[0].Request))
            { e.Prompts[0].Response = initialPassword; usedInteractivePassword = true; passwordSubmitted = true; initialPassword = ""; return; }
            var questions = e.Prompts.OrderBy(p => p.Id).Select(p => new CredentialQuestion(Limit(p.Request), p.IsEchoed)).ToArray();
            var answers = Ask(new(rounds, passwordSubmitted, temporary, Limit(e.Instruction), questions));
            foreach (var pair in e.Prompts.OrderBy(p => p.Id).Zip(answers)) pair.First.Response = pair.Second;
            if (e.Prompts.Count == 1 && !e.Prompts[0].IsEchoed && IsPasswordQuestion(e.Prompts[0].Request)) { passwordSubmitted = true; initialPassword = ""; }
            Array.Clear(answers);
        };
        methods.Add(interactive);
    }
    string[] Ask(CredentialChallenge challenge)
    {
        ct.ThrowIfCancellationRequested(); if (challenge.Round > 16 || challenge.Questions.Count > 16) throw new SshAuthenticationException("服务器认证问题过多"); if (prompt == null) throw new SshAuthenticationException("服务器要求追加验证，请手动连接以继续");
        var result = prompt(challenge, ct).WaitAsync(ct).GetAwaiter().GetResult(); ct.ThrowIfCancellationRequested();
        if (result == null) throw new OperationCanceledException("用户取消认证", ct);
        if (result.Length != challenge.Questions.Count) throw new SshAuthenticationException("认证响应数不匹配"); return result;
    }
    static bool IsPasswordQuestion(string text) => text.Trim().TrimEnd(':', '：', ' ').ToLowerInvariant() is "password" or "密码" or "登录密码" or "login password";
    static string Limit(string text) => text.Length <= 1024 ? text : text[..1024] + "…";
    sealed class FreshPasswordMethod(string user, SessionAuthentication owner) : AuthenticationMethod(user)
    {
        bool submitted;
        public override string Name => "password";
        public override AuthenticationResult Authenticate(Session session)
        {
            owner.ct.ThrowIfCancellationRequested(); string password;
            if (!submitted && !owner.passwordSubmitted) { password = owner.initialPassword; owner.initialPassword = ""; submitted = true; }
            else { submitted = true; var answer = owner.Ask(new(++owner.rounds, true, owner.temporary, "服务器要求新的密码验证，不自动重用之前的内容。", new[] { new CredentialQuestion("本轮密码", false) })); password = answer[0]; Array.Clear(answer); }
            owner.passwordSubmitted = true; byte[] bytes = Encoding.UTF8.GetBytes(password); password = "";
            try { using var method = new PasswordAuthenticationMethod(Username, bytes); var result = method.Authenticate(session); AllowedAuthentications = method.AllowedAuthentications; return result; }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
    }
    public void Dispose() { initialPassword = ""; foreach (var method in methods) method.Dispose(); foreach (var resource in resources) resource.Dispose(); }
}
