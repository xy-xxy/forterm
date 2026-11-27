using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Windows.Input;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.Library;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;

namespace Forterm.Studio.ViewModels;

/// <summary>堡垒机账户 with a local authenticator. The TOTP seed is read from the OS credential
/// store only while its account is selected, kept in memory, and zeroed when deselected or closed.
/// Codes are masked by default; copying recomputes the current code and clears the clipboard after
/// the period if it still holds that code. Studio never contacts the portal or logs in.</summary>
public sealed class AccountsViewModel : ObservableObject, IDisposable
{
    public static readonly Choice[] Algorithms = [new("SHA1", "SHA1"), new("SHA256", "SHA256"), new("SHA512", "SHA512")];
    public static readonly Choice[] DigitChoices = [new("6", "6 位"), new("8", "8 位")];
    const string PasswordKind = "password", SeedKind = "seed";

    readonly AccountStore store;
    readonly CredentialStore credentials;
    readonly IStudioInteraction ui;
    readonly Func<DateTimeOffset> clock;
    readonly DispatcherTimer? timer;
    BastionAccount? selected;
    byte[]? seed;
    int loadVersion;
    bool reveal, editing;
    string code = "", remaining = "", error = "", editName = "", editUrl = "", editUser = "", editPassword = "", editSeed = "", editNote = "", editPeriod = "30", editTenant = "";
    double progress;
    bool clearPassword, clearSeed;
    Choice editAlgorithm = Algorithms[0], editDigits = DigitChoices[0];
    string? editingId;

    readonly Action<BastionAccount>? logIn;

    /// <param name="logIn">"登录堡垒机": shows the account in the library's "堡垒机" tab and logs in there.</param>
    public AccountsViewModel(AccountStore store, CredentialStore credentials, IStudioInteraction ui, Func<DateTimeOffset>? clock = null, bool startTimer = true,
        Action<BastionAccount>? logIn = null)
    {
        this.logIn = logIn;
        this.store = store;
        this.credentials = credentials;
        this.ui = ui;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        Reload();
        store.Changed += Reload;
        AddCommand = new RelayCommand(() => BeginEdit(null));
        EditCommand = new RelayCommand(() => { if (selected != null) BeginEdit(selected); });
        CancelEditCommand = new RelayCommand(() => { IsEditing = false; ClearEditSecrets(); });
        SaveCommand = new AsyncCommand(SaveAsync, e => Error = WorkspaceViewModel.Short(e.Message));
        DeleteCommand = new AsyncCommand(DeleteAsync, e => Error = WorkspaceViewModel.Short(e.Message));
        CopyUserCommand = new AsyncCommand(() => ui.CopyTextAsync(selected?.Username ?? ""));
        CopyPasswordCommand = new AsyncCommand(CopyPasswordAsync, e => Error = WorkspaceViewModel.Short(e.Message));
        CopyCodeCommand = new AsyncCommand(CopyCodeAsync, e => Error = WorkspaceViewModel.Short(e.Message));
        OpenPortalCommand = new AsyncCommand(() => selected is { PortalUrl.Length: > 0 } a ? ui.OpenUrlAsync(a.PortalUrl) : Task.CompletedTask);
        LoginCommand = new RelayCommand(() =>
        {
            if (selected is not { } a || this.logIn == null) return;
            if (a.TenantName.Length == 0 || !a.PortalUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) { Error = "登录堡垒机需要 https:// 门户地址和租户名称，请先编辑账户"; return; }
            this.logIn(a);
        });
        ToggleRevealCommand = new RelayCommand(() => { Reveal = !Reveal; Tick(); });
        if (startTimer)
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => Tick();
            timer.Start();
        }
    }

    public ObservableCollection<BastionAccount> Accounts { get; } = new();
    public BastionAccount? Selected
    {
        get => selected;
        set
        {
            if (!Set(ref selected, value)) return;
            Forget();
            Reveal = false;
            Raise(nameof(HasSelection)); Raise(nameof(ShowDetails));
            _ = LoadSeedAsync(value);
        }
    }
    public bool HasSelection => selected != null;
    public bool ShowDetails => selected != null && !editing;
    public bool IsEmpty => Accounts.Count == 0 && !editing;
    public bool HasSeed => seed != null;
    public bool Reveal { get => reveal; set => Set(ref reveal, value); }
    public string Code { get => code; private set => Set(ref code, value); }
    public string Remaining { get => remaining; private set => Set(ref remaining, value); }
    public double Progress { get => progress; private set => Set(ref progress, value); }
    public string Error { get => error; private set { if (Set(ref error, value)) Raise(nameof(HasError)); } }
    public bool HasError => Error.Length > 0;

    public bool IsEditing { get => editing; private set { if (Set(ref editing, value)) { Raise(nameof(ShowDetails)); Raise(nameof(IsEmpty)); } } }
    public string EditTitle => editingId == null ? "添加账户" : "编辑账户";
    public string EditName { get => editName; set => Set(ref editName, value); }
    public string EditUrl { get => editUrl; set => Set(ref editUrl, value); }
    public string EditTenant { get => editTenant; set => Set(ref editTenant, value); }
    public string EditUser { get => editUser; set => Set(ref editUser, value); }
    public string EditPassword { get => editPassword; set => Set(ref editPassword, value); }
    public string EditSeed { get => editSeed; set => Set(ref editSeed, value); }
    public string EditNote { get => editNote; set => Set(ref editNote, value); }
    public bool ClearPassword { get => clearPassword; set => Set(ref clearPassword, value); }
    public bool ClearSeed { get => clearSeed; set => Set(ref clearSeed, value); }
    public Choice EditAlgorithm { get => editAlgorithm; set => Set(ref editAlgorithm, value ?? Algorithms[0]); }
    public Choice EditDigits { get => editDigits; set => Set(ref editDigits, value ?? DigitChoices[0]); }
    public string EditPeriod { get => editPeriod; set => Set(ref editPeriod, value); }
    public bool EditingHasPassword => editingId != null && store.Find(editingId)?.HasPassword == true;
    public bool EditingHasSeed => editingId != null && store.Find(editingId)?.HasSeed == true;
    public Choice[] AlgorithmChoices => Algorithms;
    public Choice[] Digits => DigitChoices;

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand CopyUserCommand { get; }
    public ICommand CopyPasswordCommand { get; }
    public ICommand CopyCodeCommand { get; }
    public ICommand OpenPortalCommand { get; }
    /// <summary>"登录堡垒机": the account's fort portal, without a browser, in the library's "堡垒机" tab.</summary>
    public ICommand LoginCommand { get; }
    public bool CanLogIn => logIn != null;
    public ICommand ToggleRevealCommand { get; }

    void Reload()
    {
        var next = store.Accounts.OrderBy(a => a.Name, StringComparer.CurrentCulture).ToList();
        // Only what the "堡垒机" tab keeps changed (a machine's choice, a target login): nothing here shows it, and a
        // revealed code stays revealed.
        if (next.Count == Accounts.Count && next.Zip(Accounts).All(p => p.First.Id == p.Second.Id && (p.First with { Choices = p.Second.Choices, Logins = p.Second.Logins }) == p.Second))
            return;
        string? keep = selected?.Id;
        Accounts.Clear();
        foreach (var a in store.Accounts.OrderBy(a => a.Name, StringComparer.CurrentCulture)) Accounts.Add(a);
        var again = keep == null ? null : Accounts.FirstOrDefault(a => a.Id == keep);
        selected = null;
        Selected = again ?? Accounts.FirstOrDefault();
        Raise(nameof(IsEmpty));
    }

    async Task LoadSeedAsync(BastionAccount? account)
    {
        int version = ++loadVersion;
        if (account is not { HasSeed: true }) { Tick(); return; }
        try
        {
            string? text = await credentials.AccountSecretAsync(account.Id, SeedKind, CancellationToken.None);
            if (version != loadVersion) return; // another account was selected, or the window closed
            if (text == null) { Error = "系统凭据中找不到验证器密钥，请编辑账户重新保存"; return; }
            if (seed != null) CryptographicOperations.ZeroMemory(seed);
            seed = Totp.DecodeBase32(text);
            Raise(nameof(HasSeed));
        }
        // Fire-and-forget: every failure (vault errors, a stored secret that is not valid Base32) is shown.
        catch (Exception e) { if (version == loadVersion) Error = "读取验证器密钥失败：" + WorkspaceViewModel.Short(e.Message); }
        Tick();
    }

    /// <summary>Recomputes the visible code and the period countdown.</summary>
    public void Tick()
    {
        if (seed == null || selected == null) { Code = ""; Remaining = ""; Progress = 0; return; }
        var current = Totp.Generate(seed, selected.Parameters, clock());
        Code = reveal ? current.Code : new string('•', selected.Digits);
        Remaining = $"{current.RemainingSeconds} 秒后刷新";
        Progress = 100.0 * current.RemainingSeconds / selected.Period;
    }

    async Task CopyCodeAsync()
    {
        if (seed == null || selected == null) return;
        var current = Totp.Generate(seed, selected.Parameters, clock()); // recomputed at the moment of copying
        await ui.CopyTextAsync(current.Code);
        ui.ClearClipboardLater(current.Code, TimeSpan.FromSeconds(Math.Max(1, current.RemainingSeconds)));
    }

    async Task CopyPasswordAsync()
    {
        if (selected is not { HasPassword: true } a) return;
        string? password = await credentials.AccountSecretAsync(a.Id, PasswordKind, CancellationToken.None);
        if (password == null) { Error = "系统凭据中找不到密码"; return; }
        await ui.CopyTextAsync(password);
        ui.ClearClipboardLater(password, TimeSpan.FromSeconds(30));
    }

    void BeginEdit(BastionAccount? account)
    {
        editingId = account?.Id;
        EditName = account?.Name ?? "";
        EditUrl = account?.PortalUrl ?? "";
        EditTenant = account?.TenantName ?? "";
        EditUser = account?.Username ?? "";
        EditNote = account?.Note ?? "";
        EditAlgorithm = Algorithms.FirstOrDefault(c => c.Value == account?.Algorithm) ?? Algorithms[0];
        EditDigits = DigitChoices.FirstOrDefault(c => c.Value == (account?.Digits ?? 6).ToString(CultureInfo.InvariantCulture)) ?? DigitChoices[0];
        EditPeriod = (account?.Period ?? 30).ToString(CultureInfo.InvariantCulture);
        ClearEditSecrets();
        ClearPassword = false;
        ClearSeed = false;
        Error = "";
        IsEditing = true;
        Raise(nameof(EditTitle)); Raise(nameof(EditingHasPassword)); Raise(nameof(EditingHasSeed));
    }

    void ClearEditSecrets() { EditPassword = ""; EditSeed = ""; }

    static bool SamePortal(string a, string b) =>
        Uri.TryCreate(a, UriKind.Absolute, out var x) && Uri.TryCreate(b, UriKind.Absolute, out var y) && x.Scheme == y.Scheme && string.Equals(x.Host, y.Host, StringComparison.OrdinalIgnoreCase) && x.Port == y.Port;

    /// <summary>Secrets go to the OS credential store first; the metadata is written after.</summary>
    async Task SaveAsync()
    {
        Error = "";
        var existing = editingId == null ? null : store.Find(editingId);
        if (!int.TryParse(EditPeriod, NumberStyles.None, CultureInfo.InvariantCulture, out int period)) { Error = "周期必须是 5–300 的整数秒"; return; }
        var parameters = new TotpParameters(EditAlgorithm.Value, int.Parse(EditDigits.Value, CultureInfo.InvariantCulture), period);
        byte[]? newSeed = null;
        try
        {
            if (EditSeed.Trim().Length > 0)
            {
                var configuration = Totp.Normalize(EditSeed.Trim(), parameters);
                newSeed = configuration.Secret;
                parameters = configuration.Parameters;
            }
            var account = (existing ?? new BastionAccount()) with
            {
                Name = EditName.Trim(),
                PortalUrl = EditUrl.Trim(),
                TenantName = EditTenant.Trim(),
                // Another portal: the certificate the user trusted was that one's.
                CertificatePin = existing != null && !SamePortal(existing.PortalUrl, EditUrl.Trim()) ? "" : existing?.CertificatePin ?? "",
                Username = EditUser.Trim(),
                Note = EditNote.Trim(),
                Algorithm = parameters.Algorithm,
                Digits = parameters.Digits,
                Period = parameters.Period,
                HasPassword = EditPassword.Length > 0 || existing?.HasPassword == true && !ClearPassword,
                HasSeed = newSeed != null || existing?.HasSeed == true && !ClearSeed,
            };
            AccountStore.Validate(account);
            if (EditPassword.Length > 0) await credentials.SaveAccountSecretAsync(account.Id, PasswordKind, EditPassword, CancellationToken.None);
            else if (existing?.HasPassword == true && ClearPassword) await credentials.ForgetAccountSecretAsync(account.Id, PasswordKind, CancellationToken.None);
            if (newSeed != null) await credentials.SaveAccountSecretAsync(account.Id, SeedKind, Totp.EncodeBase32(newSeed), CancellationToken.None);
            else if (existing?.HasSeed == true && ClearSeed) await credentials.ForgetAccountSecretAsync(account.Id, SeedKind, CancellationToken.None);
            // What the "堡垒机" tab kept meanwhile (target logins, choices, a certificate just trusted) stays.
            if (existing != null && store.Find(account.Id) is { } latest)
            {
                bool same = SamePortal(latest.PortalUrl, account.PortalUrl);
                // Another portal: its machines are others; what was chosen for the old ones goes (the logins stay, chosen anew).
                account = account with { Logins = latest.Logins, Choices = same ? latest.Choices : new Dictionary<string, string>(), CertificatePin = same ? latest.CertificatePin : "" };
            }
            store.Upsert(account);
            IsEditing = false;
            ClearEditSecrets();
            Selected = Accounts.FirstOrDefault(a => a.Id == account.Id);
            _ = LoadSeedAsync(selected);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or FormatException or ArgumentException)
        {
            Error = WorkspaceViewModel.Short(e.Message);
        }
        finally { if (newSeed != null) CryptographicOperations.ZeroMemory(newSeed); }
    }

    async Task DeleteAsync()
    {
        if (selected is not { } a) return;
        if (!await ui.ConfirmAsync("删除账户", $"删除“{a.Name}”？已保存的密码、验证器密钥和目标主机凭据也会从系统凭据中移除。", "删除", danger: true)) return;
        await credentials.ForgetAccountSecretAsync(a.Id, PasswordKind, CancellationToken.None);
        await credentials.ForgetAccountSecretAsync(a.Id, SeedKind, CancellationToken.None);
        foreach (var login in store.Find(a.Id)?.Logins ?? a.Logins) await credentials.ForgetAccountSecretAsync(login.Id, BastionLogin.SecretKind, CancellationToken.None);
        store.Remove(a.Id);
    }

    void Forget()
    {
        if (seed != null) CryptographicOperations.ZeroMemory(seed);
        seed = null;
        Raise(nameof(HasSeed));
        Tick();
    }

    public void Dispose()
    {
        loadVersion++; // a seed read still in flight is dropped instead of stored after closing
        timer?.Stop();
        store.Changed -= Reload;
        Forget();
        ClearEditSecrets();
    }
}
