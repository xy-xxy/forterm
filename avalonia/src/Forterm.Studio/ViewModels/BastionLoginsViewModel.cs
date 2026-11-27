using System.Collections.ObjectModel;
using System.Windows.Input;
using Forterm.Studio.Library;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;

namespace Forterm.Studio.ViewModels;

/// <summary>"目标主机凭据" of a bastion account: logins of target hosts kept once for the portal's <c>$user</c> accounts, to
/// be chosen in a machine's account list instead of typing them each time. Name and user are kept with the account, the
/// password only in the credential store; removing a login removes its password too.</summary>
public sealed class BastionLoginsViewModel : ObservableObject
{
    readonly Func<string> accountId;
    readonly AccountStore store;
    readonly CredentialStore credentials;
    readonly IStudioInteraction ui;
    bool open, editing;
    string editName = "", editUser = "", editPassword = "", error = "";
    string? editingId;

    public BastionLoginsViewModel(Func<string> accountId, AccountStore store, CredentialStore credentials, IStudioInteraction ui)
    {
        this.accountId = accountId;
        this.store = store;
        this.credentials = credentials;
        this.ui = ui;
        ToggleCommand = new RelayCommand(() => IsOpen = !IsOpen);
        AddCommand = new RelayCommand(() => Begin(null));
        EditCommand = new RelayCommand(p => { if (p is BastionLogin login) Begin(login); });
        CancelCommand = new RelayCommand(() => { IsEditing = false; EditPassword = ""; Error = ""; });
        SaveCommand = new AsyncCommand(SaveAsync, e => Error = WorkspaceViewModel.Short(e.Message));
        DeleteCommand = new AsyncCommand<BastionLogin>(DeleteAsync, e => Error = WorkspaceViewModel.Short(e.Message));
        Reload();
    }

    public ObservableCollection<BastionLogin> Items { get; } = new();
    public bool IsEmpty => Items.Count == 0;
    public bool IsOpen { get => open; set { if (Set(ref open, value) && !value) { IsEditing = false; EditPassword = ""; } } }
    public bool IsEditing { get => editing; private set { if (Set(ref editing, value)) Raise(nameof(IsListing)); } }
    public bool IsListing => !editing;
    public string EditTitle => editingId == null ? "新建凭据" : "编辑凭据";
    public string EditName { get => editName; set => Set(ref editName, value ?? ""); }
    public string EditUser { get => editUser; set => Set(ref editUser, value ?? ""); }
    public string EditPassword { get => editPassword; set => Set(ref editPassword, value ?? ""); }
    public string PasswordHint => editingId == null ? "目标主机的密码" : "留空保持不变";
    public string Error { get => error; private set { if (Set(ref error, value)) Raise(nameof(HasError)); } }
    public bool HasError => error.Length > 0;
    public ICommand ToggleCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }

    internal void Reload()
    {
        var logins = store.Find(accountId())?.Logins ?? [];
        Items.Clear();
        foreach (var login in logins) Items.Add(login);
        Raise(nameof(IsEmpty));
    }

    void Begin(BastionLogin? login)
    {
        editingId = login?.Id;
        EditName = login?.Name ?? "";
        EditUser = login?.User ?? "";
        EditPassword = "";
        Error = "";
        Raise(nameof(EditTitle)); Raise(nameof(PasswordHint));
        IsEditing = true;
    }

    /// <summary>The password goes to the credential store first; the login is written after.</summary>
    async Task SaveAsync()
    {
        Error = "";
        if (store.Find(accountId()) is not { } account) { Error = "这个堡垒机账户已经不在了"; return; }
        var existing = editingId == null ? null : account.Logins.FirstOrDefault(l => l.Id == editingId);
        if (editingId != null && existing == null) { Error = "这个凭据已经被删除"; IsEditing = false; return; }
        if (existing == null && EditPassword.Length == 0) { Error = "请填写密码"; return; }
        var login = (existing ?? new BastionLogin()) with { Name = EditName.Trim(), User = EditUser.Trim() };
        var logins = account.Logins.Where(l => l.Id != login.Id).ToList();
        int at = existing == null ? logins.Count : account.Logins.ToList().FindIndex(l => l.Id == login.Id);
        logins.Insert(Math.Min(at, logins.Count), login);
        var next = account with { Logins = logins };
        try { AccountStore.Validate(next); }
        catch (InvalidDataException e) { Error = e.Message; return; }
        bool wrote = EditPassword.Length > 0;
        if (wrote) await credentials.SaveAccountSecretAsync(login.Id, BastionLogin.SecretKind, EditPassword, CancellationToken.None);
        // The account as it is now (a choice may have been kept while the password was written); removed meanwhile, it
        // stays removed, and the password just written goes with it.
        if (store.Find(account.Id) is not { } latest)
        {
            if (wrote) await credentials.ForgetAccountSecretAsync(login.Id, BastionLogin.SecretKind, CancellationToken.None);
            Error = "这个堡垒机账户已经不在了";
            return;
        }
        var logins2 = latest.Logins.Where(l => l.Id != login.Id).ToList();
        int at2 = latest.Logins.ToList().FindIndex(l => l.Id == login.Id);
        logins2.Insert(at2 < 0 ? logins2.Count : Math.Min(at2, logins2.Count), login);
        store.Upsert(latest with { Logins = logins2 });
        EditPassword = "";
        IsEditing = false;
        Reload();
    }

    async Task DeleteAsync(BastionLogin? login)
    {
        if (login == null || store.Find(accountId()) is not { } account) return;
        if (!await ui.ConfirmAsync("删除凭据", $"删除“{login.Label}”？它的密码也会从系统凭据中移除；选了它的机器改回默认账号。", "删除", danger: true)) return;
        await credentials.ForgetAccountSecretAsync(login.Id, BastionLogin.SecretKind, CancellationToken.None);
        if (store.Find(account.Id) is not { } latest) return; // removed meanwhile: stays removed
        string key = "login:" + login.Id;
        store.Upsert(latest with
        {
            Logins = latest.Logins.Where(l => l.Id != login.Id).ToList(),
            Choices = latest.Choices.Where(c => c.Value != key).ToDictionary(c => c.Key, c => c.Value),
        });
        if (editingId == login.Id) IsEditing = false;
        Reload();
    }
}
