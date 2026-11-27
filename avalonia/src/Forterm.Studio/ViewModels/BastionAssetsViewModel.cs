using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Windows.Input;
using Forterm.Core;
using Forterm.Studio.Library;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;
using Forterm.Studio.Services.Bastion;

namespace Forterm.Studio.ViewModels;

/// <summary>One resource row of "堡垒机资产": the account to open it with, and SSH / SFTP.</summary>
public sealed class BastionAssetRow : ObservableObject
{
    FortAccount? account;

    public BastionAssetRow(FortAsset asset, BastionAssetsViewModel owner)
    {
        Asset = asset;
        Owner = owner;
        account = asset.Accounts.FirstOrDefault(a => !a.AsksForCredentials) ?? asset.Accounts.FirstOrDefault();
        SshCommand = new AsyncCommand(() => owner.OpenAsync(this, false), owner.Fail);
        SftpCommand = new AsyncCommand(() => owner.OpenAsync(this, true), owner.Fail);
    }

    public FortAsset Asset { get; }
    /// <summary>The window's model: its buttons are off while it is busy.</summary>
    public BastionAssetsViewModel Owner { get; }
    public string Name => Asset.Name;
    public string Ip => Asset.Ip;
    public string TypeName => Asset.TypeName;
    public IReadOnlyList<FortAccount> Accounts => Asset.Accounts;
    public FortAccount? Account { get => account; set => Set(ref account, value); }
    public ICommand SshCommand { get; }
    public ICommand SftpCommand { get; }
}

/// <summary>"堡垒机资产": logs into the account's fort portal without a browser (user name, password from the credential
/// store, the code from its authenticator seed), lists the resources, and opens SSH or SFTP sessions in Studio with the
/// one-time sid the portal hands out, as the vendor's own client would start Xshell or Xftp. The portal's certificate is
/// confirmed by the user once and pinned. One thing at a time (the portal's session is one); nothing of the login is kept
/// on disk, and closing the window leaves the portal.</summary>
public sealed class BastionAssetsViewModel : ObservableObject, IDisposable
{
    public const int PageSize = 20;
    const string PasswordKind = "password", SeedKind = "seed";

    BastionAccount account;
    readonly AccountStore store;
    readonly CredentialStore credentials;
    readonly IStudioInteraction ui;
    readonly Func<FortTicket, bool, string, Task> open;
    readonly Func<Uri, string?, FortPortal> connect;
    readonly CancellationTokenSource lifetime = new();
    FortPortal? portal;
    string status = "", query = "";
    bool busy, loggedIn, disposed;
    int page = 1, pages = 1, total;

    /// <param name="open">Opens a session with a ticket: SFTP when the flag is set; the text is the title.</param>
    public BastionAssetsViewModel(BastionAccount account, AccountStore store, CredentialStore credentials, IStudioInteraction ui,
        Func<FortTicket, bool, string, Task> open, Func<Uri, string?, FortPortal>? connect = null)
    {
        this.account = account;
        this.store = store;
        this.credentials = credentials;
        this.ui = ui;
        this.open = open;
        this.connect = connect ?? ((uri, pin) => new FortPortal(uri, pin));
        LoginCommand = new AsyncCommand(LoginAsync, Fail);
        SearchCommand = new AsyncCommand(() => ShowAsync(1), Fail);
        PreviousCommand = new AsyncCommand(() => ShowAsync(page - 1), Fail);
        NextCommand = new AsyncCommand(() => ShowAsync(page + 1), Fail);
        LogoutCommand = new AsyncCommand(LogoutAsync, Fail);
    }

    public string Title => "堡垒机资产 · " + account.Name;
    public string Status { get => status; private set => Set(ref status, value); }
    public bool IsBusy
    {
        get => busy;
        private set { if (Set(ref busy, value)) { Raise(nameof(IsIdle)); Raise(nameof(CanGoBack)); Raise(nameof(CanGoOn)); Raise(nameof(CanSearch)); } }
    }
    public bool IsIdle => !busy;
    public bool IsLoggedIn { get => loggedIn; private set { if (Set(ref loggedIn, value)) { Raise(nameof(IsLoggedOut)); Raise(nameof(CanSearch)); } } }
    public bool IsLoggedOut => !loggedIn;
    public bool CanSearch => loggedIn && !busy;
    public string Query { get => query; set => Set(ref query, value ?? ""); }
    public ObservableCollection<BastionAssetRow> Assets { get; } = new();
    public string PageText => total == 0 ? "没有资产" : $"第 {page} / {pages} 页，共 {total} 台";
    public bool CanGoBack => page > 1 && !busy;
    public bool CanGoOn => page < pages && !busy;
    public ICommand LoginCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand LogoutCommand { get; }

    internal void Fail(Exception e)
    {
        IsBusy = false;
        if (disposed) return;
        if (e is FortPortalException { SignedOut: true }) LoggedOut();
        Status = e is OperationCanceledException ? "已取消" : WorkspaceViewModel.Short(e.Message);
    }

    /// <summary>The window may have closed while a question or a call was waiting: then nothing more is done.</summary>
    void StillOpen() { if (disposed) throw new OperationCanceledException(lifetime.Token); }

    async Task LoginAsync()
    {
        if (IsBusy || IsLoggedIn) return;
        // The account as it is now: a certificate trusted at an earlier login of this window, or an edit in between.
        account = store.Find(account.Id) ?? account;
        if (!Uri.TryCreate(account.PortalUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) { Status = "账户里的门户地址必须是 https:// 开头"; return; }
        if (account.TenantName.Trim().Length == 0 || account.Username.Length == 0) { Status = "请先在账户里填写租户和用户名"; return; }
        IsBusy = true;
        Status = "正在登录…";
        byte[]? seed = null;
        try
        {
            string? password = account.HasPassword ? await credentials.AccountSecretAsync(account.Id, PasswordKind, lifetime.Token) : null;
            if (password == null)
            {
                var asked = await ui.AskCredentialsAsync(new CredentialChallenge(1, false, false, $"登录堡垒机 {account.Name}（{account.Username}）", [new CredentialQuestion("堡垒机密码", false)]), lifetime.Token);
                StillOpen();
                if (asked is not [{ Length: > 0 } typed]) { Status = "没有登录"; return; }
                password = typed;
                Array.Clear(asked);
            }
            seed = account.HasSeed && await credentials.AccountSecretAsync(account.Id, SeedKind, lifetime.Token) is { } s ? Totp.DecodeBase32(s) : null;
            var parameters = account.Parameters;
            // The code is asked when the portal has been told to expect it: after the certificate, never before.
            async Task<string?> Code(CancellationToken ct)
            {
                if (seed != null) return Totp.Generate(seed, parameters, DateTimeOffset.UtcNow).Code;
                var asked = await ui.AskCredentialsAsync(new CredentialChallenge(1, true, false, "手机 APP 上的 6 位动态口令", [new CredentialQuestion("动态口令", true)]), ct);
                StillOpen();
                return asked is [{ Length: > 0 } typed] ? typed : null;
            }
            string? pin = account.CertificatePin.Length > 0 ? account.CertificatePin : null;
            for (int attempt = 0; ; attempt++)
            {
                StillOpen();
                portal?.Dispose();
                portal = connect(uri, pin);
                try { await portal.LoginAsync(account.TenantName.Trim(), account.Username, password, Code, lifetime.Token); break; }
                catch (FortCertificateException e) when (attempt == 0)
                {
                    bool trusted = await TrustAsync(e);
                    StillOpen();
                    if (!trusted) { Status = "没有信任堡垒机网页的证书，没有登录"; portal.Dispose(); portal = null; return; }
                    pin = e.Fingerprint;
                }
            }
            StillOpen();
            IsLoggedIn = true;
            Status = "已登录";
            await LoadAsync(1);
        }
        finally
        {
            if (seed != null) CryptographicOperations.ZeroMemory(seed);
            IsBusy = false;
        }
    }

    /// <summary>The portal's certificate, shown before anything is sent: the user compares the fingerprint with the one the
    /// administrator gives, and Studio keeps it with the account. A changed one is said plainly.</summary>
    async Task<bool> TrustAsync(FortCertificateException e)
    {
        string fingerprint = string.Join(":", Enumerable.Range(0, e.Fingerprint.Length / 2).Select(i => e.Fingerprint.Substring(i * 2, 2).ToUpperInvariant()));
        string message = (e.Changed
                ? "堡垒机网页的证书和上次确认的不同。如果不是管理员更换了证书，可能有人在冒充堡垒机，请不要继续。\n\n"
                : "第一次连接这个堡垒机网页。请和管理员给的证书指纹核对，一致再信任；之后证书变了会拒绝连接。\n\n")
            + $"SHA-256 指纹：\n{fingerprint}\n\n签发给：{WorkspaceViewModel.Short(e.Subject)}\n有效期至：{e.NotAfter:yyyy-MM-dd}"
            + (e.Errors != System.Net.Security.SslPolicyErrors.None ? "\n（系统不认识签发它的机构，自签证书常见这样）" : "");
        if (!await ui.ConfirmAsync(e.Changed ? "证书变了" : "确认堡垒机网页的证书", message, e.Changed ? "信任新证书" : "信任这个证书", danger: e.Changed)) return false;
        if (disposed) return false;
        // Kept only for the portal it was shown for: an account edited meanwhile to another address does not get it.
        if (store.Find(account.Id) is { } saved && saved.PortalUrl == account.PortalUrl)
        {
            store.Upsert(saved with { CertificatePin = e.Fingerprint });
            account = saved with { CertificatePin = e.Fingerprint };
        }
        return true;
    }

    async Task ShowAsync(int which)
    {
        if (IsBusy) return;
        if (portal == null || !IsLoggedIn) { Status = "请先登录"; return; }
        IsBusy = true;
        try { await LoadAsync(which); }
        finally { IsBusy = false; }
    }

    async Task LoadAsync(int which)
    {
        var result = await portal!.ListAsync(Query, Math.Max(1, which), PageSize, lifetime.Token);
        page = Math.Clamp(which, 1, result.Pages);
        pages = result.Pages;
        total = result.Total;
        Assets.Clear();
        foreach (var asset in result.Assets) Assets.Add(new BastionAssetRow(asset, this));
        Raise(nameof(PageText)); Raise(nameof(CanGoBack)); Raise(nameof(CanGoOn));
        Status = total == 0 && Query.Length > 0 ? "没有找到符合的资产" : "";
    }

    /// <summary>Asks the portal for a one-time sid and opens the session in Studio. The target's own account and password,
    /// where the portal wants them, are asked here and not kept. One at a time: a second click while one opens is ignored.</summary>
    internal async Task OpenAsync(BastionAssetRow row, bool files)
    {
        if (IsBusy) return;
        if (portal == null || !IsLoggedIn) { Status = "请先登录"; return; }
        var asset = row.Asset;
        var chosen = row.Account ?? asset.Accounts.FirstOrDefault();
        if (chosen == null) { Status = $"{asset.Name} 没有可用的账号"; return; }
        IsBusy = true;
        Status = $"正在打开 {asset.Name}…";
        try
        {
            var ticket = await portal.OpenAsync(asset, chosen, files, AskTargetAsync, lifetime.Token);
            StillOpen();
            await open(ticket, files, asset.Name);
            Status = $"已打开 {asset.Name}（{(files ? "SFTP" : "SSH")}）";
        }
        finally { IsBusy = false; }

        async Task<FortCredentials?> AskTargetAsync(FortAccount account, bool fixedName, CancellationToken ct)
        {
            string target = $"{asset.Name}（{asset.Ip}）";
            var asked = fixedName
                ? await ui.AskCredentialsAsync(new CredentialChallenge(1, false, false, $"{target}的账号 {account.Name} 需要填写密码", [new CredentialQuestion("密码", false)]), ct)
                : await ui.AskCredentialsAsync(new CredentialChallenge(1, false, false, $"{target}的账号和密码", [new CredentialQuestion("账号", true), new CredentialQuestion("密码", false)]), ct);
            StillOpen();
            try
            {
                return (fixedName, asked) switch
                {
                    (true, [{ Length: > 0 } p]) => new FortCredentials(account.Name, p),
                    (false, [{ Length: > 0 } u, { Length: > 0 } p]) => new FortCredentials(u.Trim(), p),
                    (_, null) => null,
                    _ => throw new FortPortalException(fixedName ? "密码不能为空" : "账号和密码都不能为空"),
                };
            }
            finally { if (asked != null) Array.Clear(asked); }
        }
    }

    async Task LogoutAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (portal is { } p) await p.LogoutAsync(lifetime.Token);
            portal?.Dispose();
            portal = null;
            LoggedOut();
            Status = "已退出登录";
        }
        finally { IsBusy = false; }
    }

    void LoggedOut()
    {
        IsLoggedIn = false;
        Assets.Clear();
        total = 0; page = pages = 1;
        Raise(nameof(PageText)); Raise(nameof(CanGoBack)); Raise(nameof(CanGoOn));
    }

    /// <summary>The window closed: what is running stops, and a login that exists (or comes about while it stops) is left.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        if (portal is { } p)
        {
            portal = null;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await p.LogoutAsync(limit.Token);
                }
                catch (OperationCanceledException) { }
                finally { p.Dispose(); }
            });
        }
    }
}
