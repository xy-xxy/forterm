using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Forterm.Studio.Library;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;

namespace Forterm.Studio.ViewModels;

public sealed record Choice(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One "会话环境变量" row in the form.</summary>
public sealed class EnvironmentRow : ObservableObject
{
    string name = "", value = "";
    public string Name { get => name; set => Set(ref name, value ?? ""); }
    public string Value { get => this.value; set => Set(ref this.value, value ?? ""); }
}

public sealed class JumpItem(StudioProfile profile) : ObservableObject
{
    public string Id { get; } = profile.Id;
    public string Display { get; } = (profile.Name.Length > 0 ? profile.Name + "  " : "") + $"{profile.User}@{profile.Host}:{profile.Port}";
    public int Order { get => order; set => Set(ref order, value); }
    int order;
}

/// <summary>"新建 / 编辑 连接" form. Typed secrets stay in this object only until the dialog
/// closes; saving puts them in the OS credential store, never in the library file.</summary>
public sealed class ConnectionEditorViewModel : ObservableObject
{
    public static readonly Choice[] ProtocolChoices = [new("ssh", "SSH"), new("sftp", "SFTP"), new("ftp", "FTP"), new("telnet", "Telnet")];
    static readonly Choice[] SshAuth = [new("password", "密码"), new("keyboard", "键盘交互（动态验证码）"), new("key", "私钥"), new("agent", "SSH Agent")];
    static readonly Choice[] PasswordOnly = [new("password", "密码")];
    static readonly Choice[] NoAuth = [new("password", "无（由服务器提示）")];

    readonly LibraryStore store;
    readonly CredentialStore credentials;
    readonly ProfileConnector connector;
    readonly IStudioInteraction interaction;
    readonly StudioProfile original;
    readonly bool isNew;
    string name, host, port, user, password = "", passphrase = "", keyPath, note, timeout, heartbeat, terminalType, proxy, defaultPath, initialScript, keepAliveCommand, keepAliveMs;
    Choice protocol, authMode, group, encoding;
    bool savePassword, savePassphrase, forgetPassword, forgetPassphrase, proxyDisabled, testing, disablePwdFollow, disableListMonitor;
    Choice cipher, keyExchange;
    string error = "", testResult = "";
    Choice? jumpCandidate;

    public ConnectionEditorViewModel(LibraryStore store, CredentialStore credentials, ProfileConnector connector, IStudioInteraction interaction,
        StudioProfile? existing, string protocolHint = "ssh", string groupHint = "")
    {
        this.store = store;
        this.credentials = credentials;
        this.connector = connector;
        this.interaction = interaction;
        isNew = existing == null;
        original = existing ?? new StudioProfile { Protocol = protocolHint, Port = StudioProfile.DefaultPort(protocolHint), GroupId = groupHint };
        name = original.Name; host = original.Host; port = original.Port.ToString(CultureInfo.InvariantCulture); user = original.User;
        keyPath = original.KeyPath; note = original.Note;
        timeout = original.TimeoutMs > 0 ? original.TimeoutMs.ToString(CultureInfo.InvariantCulture) : "";
        heartbeat = original.HeartbeatMs.ToString(CultureInfo.InvariantCulture);
        terminalType = original.TerminalType; proxy = original.Proxy; proxyDisabled = original.ProxyDisabled;
        defaultPath = original.DefaultPath; initialScript = original.InitialScript; keepAliveCommand = original.KeepAliveCommand;
        keepAliveMs = original.KeepAliveCommandMs > 0 ? original.KeepAliveCommandMs.ToString(CultureInfo.InvariantCulture) : "";
        savePassword = original.HasPassword; savePassphrase = original.HasPassphrase; disablePwdFollow = original.DisablePwdFollow;
        disableListMonitor = original.DisableListMonitor;
        CipherChoices = AlgorithmChoices(Forterm.Core.SshAlgorithms.Ciphers);
        KeyExchangeChoices = AlgorithmChoices(Forterm.Core.SshAlgorithms.KeyExchange);
        // The store clears names this build does not offer, so a saved value is always listed.
        cipher = CipherChoices.FirstOrDefault(c => c.Value == original.Cipher) ?? CipherChoices[0];
        keyExchange = KeyExchangeChoices.FirstOrDefault(c => c.Value == original.KeyExchange) ?? KeyExchangeChoices[0];
        foreach (var entry in original.SessionEnvironment) EnvironmentRows.Add(new EnvironmentRow { Name = entry.Name, Value = entry.Value });
        protocol = ProtocolChoices.First(c => c.Value == original.Protocol);
        Groups = new[] { new Choice("", "未分组") }.Concat(store.Groups.Select(g => new Choice(g.Id, store.GroupPath(g.Id))).OrderBy(c => c.Label, StringComparer.CurrentCulture)).ToArray();
        group = Groups.FirstOrDefault(c => c.Value == original.GroupId) ?? Groups[0];
        EncodingChoices = Encodings.Names.Select(n => new Choice(n, n)).ToArray();
        encoding = EncodingChoices.FirstOrDefault(c => c.Value == original.Encoding) ?? EncodingChoices[0];
        authMode = AuthChoices.FirstOrDefault(c => c.Value == original.AuthMode) ?? AuthChoices[0];
        foreach (string id in original.JumpChain) if (store.Find(id) is { } jump) Jumps.Add(new JumpItem(jump));
        Renumber();
        AddJumpCommand = new RelayCommand(AddJump);
        RemoveJumpCommand = new RelayCommand(p => { if (p is JumpItem item) { Jumps.Remove(item); Renumber(); } });
        MoveJumpUpCommand = new RelayCommand(p => Move(p as JumpItem, -1));
        MoveJumpDownCommand = new RelayCommand(p => Move(p as JumpItem, 1));
        TestCommand = new AsyncCommand(TestAsync);
        BrowseKeyCommand = new AsyncCommand(async () => { if (await interaction.PickOpenFileAsync("选择私钥文件") is { } file) KeyPath = file; });
        AddEnvironmentCommand = new RelayCommand(() =>
        {
            if (EnvironmentRows.Count >= Forterm.Core.ShellEnvironment.MaxEntries) { Error = $"环境变量最多 {Forterm.Core.ShellEnvironment.MaxEntries} 个"; return; }
            EnvironmentRows.Add(new EnvironmentRow());
        });
        RemoveEnvironmentCommand = new RelayCommand(p =>
        {
            if (p is not EnvironmentRow row) return;
            EnvironmentRows.Remove(row);
            if (Error.StartsWith("环境变量最多", StringComparison.Ordinal)) Error = "";
        });
    }

    /// <summary>Legacy algorithms kept by the SSH library for old servers; labelled in the lists.</summary>
    static readonly HashSet<string> Weak = new(StringComparer.Ordinal)
    { "3des-cbc", "aes128-cbc", "aes192-cbc", "aes256-cbc", "diffie-hellman-group1-sha1", "diffie-hellman-group14-sha1", "diffie-hellman-group-exchange-sha1" };

    static Choice[] AlgorithmChoices(IReadOnlyList<string> names) =>
        new[] { new Choice("", "默认（不指定）") }.Concat(names.Select(n => new Choice(n, Weak.Contains(n) ? n + "（较弱，仅老旧服务器）" : n))).ToArray();

    public string Title => (isNew ? "新建 " : "编辑 ") + protocol.Label + " 连接";
    public string SaveText => isNew ? "创建" : "保存";
    public string ProfileId => original.Id;

    public Choice[] Groups { get; }
    public Choice[] EncodingChoices { get; }
    public Choice[] Protocols => ProtocolChoices;
    public Choice[] AuthChoices => protocol.Value switch { "ftp" => PasswordOnly, "telnet" => NoAuth, _ => SshAuth };

    public string Name { get => name; set => Set(ref name, value); }
    public Choice Group { get => group; set => Set(ref group, value ?? Groups[0]); }
    public Choice Protocol
    {
        get => protocol;
        set
        {
            var old = protocol;
            if (value == null || !Set(ref protocol, value)) return;
            if (port == StudioProfile.DefaultPort(old.Value).ToString(CultureInfo.InvariantCulture)) Port = StudioProfile.DefaultPort(value.Value).ToString(CultureInfo.InvariantCulture);
            Raise(nameof(AuthChoices)); Raise(nameof(Title)); Raise(nameof(IsSsh)); Raise(nameof(IsTelnet)); Raise(nameof(HasTerminal)); Raise(nameof(HasFiles));
            AuthMode = AuthChoices.FirstOrDefault(c => c.Value == authMode.Value) ?? AuthChoices[0];
        }
    }
    public string Host { get => host; set => Set(ref host, value.Trim()); }
    public string Port { get => port; set => Set(ref port, value.Trim()); }
    public string User { get => user; set => Set(ref user, value); }
    public Choice AuthMode
    {
        get => authMode;
        set { if (value != null && Set(ref authMode, value)) { Raise(nameof(UsesPassword)); Raise(nameof(UsesKey)); } }
    }
    public bool UsesPassword => !IsTelnet && authMode.Value is "password" or "keyboard";
    public bool UsesKey => authMode.Value == "key" && IsSsh;
    public bool IsSsh => protocol.Value is "ssh" or "sftp";
    public bool IsTelnet => protocol.Value == "telnet";
    public bool HasTerminal => protocol.Value is "ssh" or "telnet" or "sftp";
    public bool HasFiles => protocol.Value is "ssh" or "sftp" or "ftp";

    /// <summary>Typed password. Empty keeps the saved one (if any).</summary>
    public string Password { get => password; set => Set(ref password, value); }
    public bool SavePassword { get => savePassword; set => Set(ref savePassword, value); }
    public bool HasSavedPassword => original.HasPassword && !forgetPassword;
    public bool ForgetPassword { get => forgetPassword; set { if (Set(ref forgetPassword, value)) { Raise(nameof(HasSavedPassword)); if (value) SavePassword = false; } } }
    public string PasswordHint => HasSavedPassword ? "已保存，留空保持不变" : "登录密码";
    public string KeyPath { get => keyPath; set => Set(ref keyPath, value); }
    public string Passphrase { get => passphrase; set => Set(ref passphrase, value); }
    public bool SavePassphrase { get => savePassphrase; set => Set(ref savePassphrase, value); }
    public bool HasSavedPassphrase => original.HasPassphrase && !forgetPassphrase;
    public bool ForgetPassphrase { get => forgetPassphrase; set { if (Set(ref forgetPassphrase, value)) { Raise(nameof(HasSavedPassphrase)); if (value) SavePassphrase = false; } } }
    public string Note { get => note; set => Set(ref note, value); }

    public string Timeout { get => timeout; set => Set(ref timeout, value.Trim()); }
    public string Heartbeat { get => heartbeat; set => Set(ref heartbeat, value.Trim()); }
    public Choice Encoding { get => encoding; set => Set(ref encoding, value ?? EncodingChoices[0]); }
    public string TerminalType { get => terminalType; set => Set(ref terminalType, value.Trim()); }
    public bool DisablePwdFollow { get => disablePwdFollow; set => Set(ref disablePwdFollow, value); }
    public bool DisableListMonitor { get => disableListMonitor; set => Set(ref disableListMonitor, value); }
    public Choice[] CipherChoices { get; }
    public Choice[] KeyExchangeChoices { get; }
    public Choice Cipher { get => cipher; set => Set(ref cipher, value ?? CipherChoices[0]); }
    public Choice KeyExchange { get => keyExchange; set => Set(ref keyExchange, value ?? KeyExchangeChoices[0]); }
    public ObservableCollection<EnvironmentRow> EnvironmentRows { get; } = new();

    public ObservableCollection<JumpItem> Jumps { get; } = new();
    public Choice[] JumpCandidates => store.Profiles
        .Where(p => p.IsSsh && p.Id != original.Id && Jumps.All(j => j.Id != p.Id))
        .Select(p => new Choice(p.Id, (p.Name.Length > 0 ? p.Name + "  " : "") + $"{p.User}@{p.Host}:{p.Port}"))
        .ToArray();
    public Choice? JumpCandidate { get => jumpCandidate; set => Set(ref jumpCandidate, value); }
    public bool HasJumpCandidates => JumpCandidates.Length > 0;

    public string Proxy { get => proxy; set => Set(ref proxy, value.Trim()); }
    public bool ProxyDisabled { get => proxyDisabled; set => Set(ref proxyDisabled, value); }

    public string DefaultPath { get => defaultPath; set => Set(ref defaultPath, value.Trim()); }
    public string InitialScript { get => initialScript; set => Set(ref initialScript, value); }
    public string KeepAliveCommand { get => keepAliveCommand; set => Set(ref keepAliveCommand, value); }
    public string KeepAliveMs { get => keepAliveMs; set => Set(ref keepAliveMs, value.Trim()); }

    public string Error { get => error; private set { if (Set(ref error, value)) Raise(nameof(HasError)); } }
    public bool HasError => Error.Length > 0;
    public string TestResult { get => testResult; private set => Set(ref testResult, value); }
    public bool IsTesting { get => testing; private set => Set(ref testing, value); }

    public ICommand AddJumpCommand { get; }
    public ICommand RemoveJumpCommand { get; }
    public ICommand MoveJumpUpCommand { get; }
    public ICommand MoveJumpDownCommand { get; }
    public ICommand TestCommand { get; }
    public ICommand BrowseKeyCommand { get; }
    public ICommand AddEnvironmentCommand { get; }
    public ICommand RemoveEnvironmentCommand { get; }

    void AddJump()
    {
        var pick = JumpCandidate ?? JumpCandidates.FirstOrDefault();
        if (pick == null || store.Find(pick.Value) is not { } jump) return;
        if (Jumps.Count >= LibraryStore.MaxJumps) { Error = $"跳板最多 {LibraryStore.MaxJumps} 级"; return; }
        Jumps.Add(new JumpItem(jump));
        JumpCandidate = null;
        Renumber();
    }

    void Move(JumpItem? item, int delta)
    {
        if (item == null) return;
        int from = Jumps.IndexOf(item), to = from + delta;
        if (from < 0 || to < 0 || to >= Jumps.Count) return;
        Jumps.Move(from, to);
        Renumber();
    }

    void Renumber()
    {
        for (int i = 0; i < Jumps.Count; i++) Jumps[i].Order = i + 1;
        Raise(nameof(JumpCandidates));
        Raise(nameof(HasJumpCandidates));
    }

    static int Number(string text, string field, int fallback)
    {
        if (text.Length == 0) return fallback;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)) throw new InvalidDataException(field + "必须是整数");
        return value;
    }

    /// <summary>Builds and validates the profile from the form (no secrets in it).</summary>
    public StudioProfile Build()
    {
        var p = original with
        {
            Name = Name.Trim(),
            Protocol = protocol.Value,
            Host = Host,
            Port = Number(Port, "端口", 0),
            User = User.Trim(),
            AuthMode = IsTelnet ? "password" : authMode.Value,
            KeyPath = UsesKey ? KeyPath.Trim() : "",
            GroupId = group.Value,
            Note = Note.Trim(),
            TimeoutMs = Number(Timeout, "超时时间", 0),
            HeartbeatMs = Number(Heartbeat, "心跳时间", 15000),
            Encoding = encoding.Value,
            TerminalType = TerminalType,
            DisablePwdFollow = DisablePwdFollow,
            DisableListMonitor = IsSsh && DisableListMonitor,
            Cipher = IsSsh ? cipher.Value : "",
            KeyExchange = IsSsh ? keyExchange.Value : "",
            // Fully empty rows are ignored; a value without a name fails validation.
            SessionEnvironment = IsSsh
                ? EnvironmentRows.Where(r => r.Name.Trim().Length > 0 || r.Value.Length > 0).Select(r => new EnvironmentEntry(r.Name.Trim(), r.Value)).ToArray()
                : Array.Empty<EnvironmentEntry>(),
            JumpChain = IsSsh ? Jumps.Select(j => j.Id).ToArray() : Array.Empty<string>(),
            Proxy = IsSsh ? Proxy : "",
            ProxyDisabled = ProxyDisabled,
            DefaultPath = DefaultPath,
            InitialScript = HasTerminal ? InitialScript : "",
            KeepAliveCommand = HasTerminal ? KeepAliveCommand.Trim() : "",
            KeepAliveCommandMs = HasTerminal && KeepAliveCommand.Trim().Length > 0 ? Number(KeepAliveMs, "保活周期", 60000) : 0,
            HasPassword = UsesPassword && (SavePassword && (Password.Length > 0 || HasSavedPassword)),
            HasPassphrase = UsesKey && (SavePassphrase && (Passphrase.Length > 0 || HasSavedPassphrase)),
        };
        LibraryStore.ValidateProfile(p);
        return p;
    }

    /// <summary>Saves secrets to the OS credential store first, then the metadata.
    /// Returns false (with <see cref="Error"/> set) when anything fails.</summary>
    public async Task<bool> SaveAsync()
    {
        Error = "";
        StudioProfile p;
        try { p = Build(); }
        catch (InvalidDataException e) { Error = e.Message; return false; }
        try
        {
            if (UsesPassword && SavePassword && Password.Length > 0) await credentials.SavePasswordAsync(p.Id, Password, CancellationToken.None);
            else if (original.HasPassword && !p.HasPassword) await credentials.ForgetPasswordAsync(p.Id, CancellationToken.None);
            if (UsesKey && SavePassphrase && Passphrase.Length > 0) await credentials.SavePassphraseAsync(p.Id, Passphrase, CancellationToken.None);
            else if (original.HasPassphrase && !p.HasPassphrase) await credentials.ForgetPassphraseAsync(p.Id, CancellationToken.None);
        }
        catch (Exception e) when (e is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            Error = "系统凭据存储失败，未保存连接：" + WorkspaceViewModel.Short(e.Message) + "。可以取消“保存密码”后再保存。";
            return false;
        }
        try { store.Upsert(p); }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Error = "连接库写入失败：" + WorkspaceViewModel.Short(e.Message);
            return false;
        }
        ClearSecrets();
        return true;
    }

    async Task TestAsync()
    {
        Error = "";
        TestResult = "";
        StudioProfile p;
        try { p = Build(); }
        catch (InvalidDataException e) { Error = e.Message; return; }
        var secrets = new OneTimeSecrets { Password = UsesPassword ? Password : "", Passphrase = UsesKey ? Passphrase : "" };
        // A form that is not saving its password must not silently use the saved one.
        if (UsesPassword && Password.Length == 0 && !HasSavedPassword) p = p with { HasPassword = false };
        IsTesting = true;
        TestResult = "正在测试…";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            TestResult = await connector.TestAsync(p, secrets, timeout.Token);
        }
        catch (OperationCanceledException) { TestResult = "测试已取消或超时"; }
        catch (Exception e) { TestResult = "测试失败：" + WorkspaceViewModel.Short(e.Message); }
        finally
        {
            secrets.Clear();
            IsTesting = false;
        }
    }

    public void ClearSecrets() { Password = ""; Passphrase = ""; }
}
