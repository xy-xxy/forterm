using System.Collections.ObjectModel;
using System.Windows.Input;
using Forterm.Studio.Library;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;
using Forterm.Studio.Services.Bastion;

namespace Forterm.Studio.ViewModels;

/// <summary>One bastion account in the left column of the "堡垒机" tab. Its login (<see cref="Session"/>) is made the first
/// time the account is chosen and lives as long as the account and the program.</summary>
public sealed class BastionAccountItem : ObservableObject
{
    BastionAccount account;
    BastionAssetsViewModel? session;

    public BastionAccountItem(BastionAccount account) => this.account = account;

    public BastionAccount Account
    {
        get => account;
        internal set { account = value; Raise(); Raise(nameof(Name)); Raise(nameof(Detail)); Raise(nameof(CanLogIn)); Raise(nameof(Portal)); }
    }
    public string Id => account.Id;
    public string Name => account.Name;
    /// <summary>User name and tenant, as the login page asks for them.</summary>
    public string Detail => account.Username + (account.TenantName.Length > 0 ? " · " + account.TenantName : "");
    public string Portal => Uri.TryCreate(account.PortalUrl, UriKind.Absolute, out var uri) ? uri.Authority : account.PortalUrl;
    /// <summary>Logging in needs an https address and a tenant.</summary>
    public bool CanLogIn => account.TenantName.Trim().Length > 0 && account.Username.Length > 0 && account.PortalUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    public BastionAssetsViewModel? Session { get => session; internal set { if (Set(ref session, value)) Raise(nameof(IsLoggedIn)); } }
    public bool IsLoggedIn => session?.IsLoggedIn == true;
    internal void RaiseLogin() => Raise(nameof(IsLoggedIn));
}

/// <summary>The "堡垒机" tab of the connection library: the bastion accounts on the left, and on the right the machines of the
/// one chosen, as its portal lists them, to open in Studio (see <see cref="BastionAssetsViewModel"/>). Nothing is sent
/// to a portal until the user logs in; logins stay while the program runs and are left when it closes or the account
/// goes.</summary>
public sealed class BastionHubViewModel : ObservableObject, IDisposable
{
    readonly AccountStore store;
    readonly CredentialStore credentials;
    readonly IStudioInteraction ui;
    readonly Func<FortTicket, bool, string, Task<string>> open;
    readonly Func<Uri, string?, FortPortal>? connect;
    readonly Action onChanged;
    readonly List<Task> leaving = new();
    BastionAccountItem? selected;
    bool disposed;

    /// <param name="open">Opens a session with a ticket (SFTP when the flag is set; the text is the title) and says what
    /// came of it.</param>
    public BastionHubViewModel(AccountStore store, CredentialStore credentials, IStudioInteraction ui, Func<FortTicket, bool, string, Task<string>> open,
        Func<Uri, string?, FortPortal>? connect = null)
    {
        this.store = store;
        this.credentials = credentials;
        this.ui = ui;
        this.open = open;
        this.connect = connect;
        onChanged = () => Avalonia.Threading.Dispatcher.UIThread.Post(Reload);
        store.Changed += onChanged;
        Reload();
    }

    public ObservableCollection<BastionAccountItem> Items { get; } = new();

    /// <summary>The account shown on the right. A null from the list is no choice while accounts exist.</summary>
    public BastionAccountItem? Selected
    {
        get => selected;
        set
        {
            if (value == null && Items.Count > 0 || value != null && !Items.Contains(value)) { Raise(); return; }
            if (!Set(ref selected, value)) return;
            if (value != null && value.Session == null) value.Session = Create(value);
            Raise(nameof(Current)); Raise(nameof(HasSelection));
        }
    }

    public BastionAssetsViewModel? Current => selected?.Session;
    public bool HasSelection => selected != null;
    public bool IsEmpty => Items.Count == 0;
    public bool HasAccounts => Items.Count > 0;
    /// <summary>Some portal is logged in: closing the program leaves it first.</summary>
    public bool HasLogins => Items.Any(i => i.IsLoggedIn);
    /// <summary>"堡垒机账户": add, edit, remove accounts (set by the main view model).</summary>
    public ICommand? ManageCommand { get; set; }
    /// <summary>The same, starting a new account.</summary>
    public ICommand? AddCommand { get; set; }

    BastionAssetsViewModel Create(BastionAccountItem item)
    {
        var session = new BastionAssetsViewModel(item.Account, store, credentials, ui, open, connect);
        session.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(BastionAssetsViewModel.IsLoggedIn)) item.RaiseLogin(); };
        return session;
    }

    /// <summary>"登录堡垒机" of an account (from "堡垒机账户"): shown here, and logged in.</summary>
    public void LogIn(string accountId)
    {
        Reload();
        if (Items.FirstOrDefault(i => i.Id == accountId) is not { } item) return;
        Selected = item;
        if (item.Session is { IsLoggedOut: true } session && session.LoginCommand.CanExecute(null)) session.LoginCommand.Execute(null);
    }

    void Reload()
    {
        if (disposed) return;
        var accounts = store.Accounts;
        foreach (var gone in Items.Where(i => accounts.All(a => a.Id != i.Id)).ToList())
        {
            Items.Remove(gone);
            if (gone.Session is { } session) Leave(session);
        }
        for (int i = 0; i < accounts.Count; i++)
        {
            var item = Items.FirstOrDefault(x => x.Id == accounts[i].Id);
            if (item == null) Items.Insert(Math.Min(i, Items.Count), item = new BastionAccountItem(accounts[i]));
            else
            {
                if (item.Account != accounts[i]) { item.Account = accounts[i]; item.Session?.Update(accounts[i]); }
                int at = Items.IndexOf(item);
                if (at != i && i < Items.Count) Items.Move(at, i);
            }
        }
        if (selected == null || !Items.Contains(selected))
        {
            selected = null;
            Selected = Items.FirstOrDefault();
            if (Items.Count == 0) { Raise(nameof(Selected)); Raise(nameof(Current)); Raise(nameof(HasSelection)); }
        }
        Raise(nameof(IsEmpty)); Raise(nameof(HasAccounts));
    }

    void Leave(BastionAssetsViewModel session)
    {
        session.Dispose();
        lock (leaving) leaving.Add(session.Closed);
    }

    /// <summary>Closing the program: every login is left, waiting at most <paramref name="limit"/>.</summary>
    public async Task LeaveAllAsync(TimeSpan limit)
    {
        foreach (var item in Items)
            if (item.Session is { } session) { Leave(session); item.Session = null; }
        // Should the window stay after all, the tab works on: the account shown gets a new, logged-out model.
        if (selected != null) selected.Session = Create(selected);
        Raise(nameof(Current));
        Task[] all;
        lock (leaving) all = leaving.ToArray();
        await Task.WhenAny(Task.WhenAll(all), Task.Delay(limit));
    }

    public void Dispose()
    {
        if (disposed) return;
        foreach (var item in Items) if (item.Session is { } session) Leave(session);
        disposed = true;
        store.Changed -= onChanged;
    }
}
