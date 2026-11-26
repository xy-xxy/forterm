using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.Controls;
using Forterm.Studio.Launch;
using Forterm.Studio.Library;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;
using Porta.Pty;
using Renci.SshNet.Common;

namespace Forterm.Studio.ViewModels;

public abstract class TabViewModel : ObservableObject
{
    string title = "";
    bool active;
    public string Title { get => title; protected set => Set(ref title, value); }
    public bool IsActive { get => active; set => Set(ref active, value); }
    public ICommand? ActivateCommand { get; set; }
    public ICommand? CloseCommand { get; set; }
}

/// <summary>Per-session terminal and file settings. Controller launches use the defaults;
/// saved connections take them from the profile.</summary>
public sealed record SessionSettings(string TerminalType = "", string Encoding = "UTF-8", string DefaultPath = "",
    string InitialScript = "", string KeepAliveCommand = "", int KeepAliveCommandMs = 0, bool PwdFollow = false,
    IReadOnlyList<KeyValuePair<string, string>>? Environment = null)
{
    /// <summary>Saved connections follow the shell's directory unless "禁用 pwd 自动跳转" is set.
    /// Controller launches (no saved settings) never write the hook and send no environment.</summary>
    public static SessionSettings From(StudioProfile p) =>
        new(p.TerminalType, p.Encoding, p.DefaultPath, p.InitialScript, p.KeepAliveCommand, p.KeepAliveCommandMs, p.IsSsh && !p.DisablePwdFollow,
            p.IsSsh ? LibraryStore.EnvironmentOf(p) : null);
}

public sealed record PathSegment(string Name, string FullPath);

/// <summary>One session workspace: files on the left, terminal tabs in the centre, monitor on the
/// right, all on one authenticated transport. A workspace connects at most once and never
/// re-authenticates or reconnects; after a disconnect the user starts a new connection.</summary>
public sealed partial class WorkspaceViewModel : TabViewModel, IDisposable
{
    readonly IStudioInteraction interaction;
    readonly KnownHosts knownHosts;
    readonly SessionConnectOptions options;
    readonly SessionSettings settings;
    /// <summary>What is said to the server while it connects and opens its channels (SSH only).</summary>
    readonly SshTrace? trace;
    readonly CancellationTokenSource lifetime = new();
    readonly CancellationTokenSource firstTerminalLifetime;
    readonly string keyPath;
    readonly StudioProfile? profile;
    readonly ProfileConnector? connector;
    readonly TransferQueue? queue;
    SharedSshSession? session;
    SftpLease? sftp;
    RemoteDirectoryCatalog? catalog;
    FtpFileSession? ftp;
    DirectoryLister? lister;
    FileActions? actions;
    TerminalTabViewModel? activeTerminal, firstTerminal;
    CenterTabViewModel? activeTab;
    FileNodeViewModel? selectedNode;
    string rootPath = "/";
    int connectStarted, disposed, terminalNumber;
    bool firstAttached, sshReady, transportEnded, offerReport;
    string environmentNotice = "", shellError = "";
    readonly Dictionary<TerminalTabViewModel, Task> openingShells = new();

    string status = "", fileMessage = "", fileNotice = "";
    bool connected, busy, showFiles, showTerminal, showMonitor, needsDirectory;

    WorkspaceViewModel(WorkspaceKind kind, string title, string protocol, string host, int port, string user, string keyPath,
        IStudioInteraction interaction, KnownHosts knownHosts, SessionConnectOptions? options, SessionSettings? settings,
        StudioProfile? profile = null, ProfileConnector? connector = null)
    {
        Kind = kind;
        Title = title;
        Protocol = protocol;
        Host = host;
        Port = port;
        User = user;
        this.keyPath = keyPath;
        this.interaction = interaction;
        this.knownHosts = knownHosts;
        if (kind is WorkspaceKind.Terminal or WorkspaceKind.Sftp) trace = new SshTrace();
        this.options = (options ?? new SessionConnectOptions { MaxChannels = ProfileConnector.MaxChannels }) with { CommonChannels = true, Trace = trace };
        this.settings = settings ?? new SessionSettings();
        this.profile = profile;
        this.connector = connector;
        firstTerminalLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        InitTerminalCommands();
        if (kind != WorkspaceKind.Ftp) firstTerminal = AddTab();
        if (CanShowFiles)
        {
            queue = new TransferQueue();
            Transfers = new TransferListViewModel(queue);
            Transfers.BatchEnded += OnTransfersEnded;
        }
        // "布局配置" says what a new SSH session shows; a file session is its files.
        showFiles = CanShowFiles && (kind != WorkspaceKind.Terminal || Appearance.Current.OpenFiles);
        showTerminal = kind is WorkspaceKind.Terminal or WorkspaceKind.Local or WorkspaceKind.Telnet;
        showMonitor = kind == WorkspaceKind.Terminal && Appearance.Current.OpenMonitor;
        if (CanShowFiles) Appearance.Changed += OnPreferences;
        CenterTabs.CollectionChanged += (_, _) => RaiseTab();
        ToggleFilesCommand = new RelayCommand(() => ShowFiles = !ShowFiles, () => CanShowFiles);
        ToggleTerminalCommand = new RelayCommand(() => ShowTerminal = !ShowTerminal, () => CanShowTerminal);
        ToggleMonitorCommand = new RelayCommand(() => ShowMonitor = !ShowMonitor, () => CanShowMonitor);
        HideFilesCommand = new RelayCommand(() => { ShowFiles = false; CloseGuard.Moved(); });
        HideTerminalCommand = new RelayCommand(() =>
        {
            ShowTerminal = false;
            CloseGuard.Moved();
            // The × looks like the end of the session, and is not.
            if (HasShell) interaction.Toast(new ToastMessage(ToastKind.Info, "终端栏已收起，会话没有断开", "恢复", () => { if (disposed == 0) ShowTerminal = true; }));
        });
        HideMonitorCommand = new RelayCommand(() => { ShowMonitor = false; CloseGuard.Moved(); });
        AddTerminalCommand = new AsyncCommand(async () => await AddTerminalAsync(), e => Status = "新建终端失败：" + Short(e.Message));
        DismissFileNoticeCommand = new RelayCommand(() => FileNotice = "");
        CopyReportCommand = new AsyncCommand(CopyReportAsync, e => Status = "复制失败：" + Short(e.Message));
        InitFileCommands();
        if (CanShowMonitor)
        {
            // Saved connections refresh automatically while the column is visible. Controller
            // launches (bastion sessions) start manual: every probe is an exec the bastion may audit.
            // "禁止列表监控开启" also keeps the column manual for a saved connection.
            Monitor = new MonitorViewModel(host, () => session, autoRefresh: profile is { DisableListMonitor: false });
            Monitor.ShowProcessesCommand = new RelayCommand(p => interaction.ShowProcesses(new ProcessListViewModel(Monitor, () => session, interaction) { Search = p as string ?? "" }));
            Monitor.CopyText = interaction.CopyTextAsync;
        }
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IsActive) or nameof(ShowMonitor) or nameof(IsConnected)) Monitor?.SetVisible(ShowMonitor && IsActive && IsConnected && session != null);
            if (e.PropertyName is nameof(IsActive) or nameof(IsConnected)) RaiseDesk();
        };
    }

    public MonitorViewModel? Monitor { get; }

    public static WorkspaceViewModel ForLaunch(LaunchRoute route, LaunchConnection connection, IStudioInteraction interaction, KnownHosts knownHosts, SessionConnectOptions? options = null) =>
        new(route.Kind, route.Title, connection.Protocol, connection.Host, connection.Port, connection.User, connection.KeyPath, interaction, knownHosts, options, null);

    public static WorkspaceViewModel ForProfile(StudioProfile p, ProfileConnector connector, IStudioInteraction interaction, KnownHosts knownHosts)
    {
        var kind = p.Protocol switch { "sftp" => WorkspaceKind.Sftp, "ftp" => WorkspaceKind.Ftp, "telnet" => WorkspaceKind.Telnet, _ => WorkspaceKind.Terminal };
        return new(kind, p.Name.Length > 0 ? p.Name : p.Host, p.Protocol, p.Host, p.Port, p.User, p.KeyPath, interaction, knownHosts,
            ProfileConnector.OptionsFor(p), SessionSettings.From(p), p, connector);
    }

    public static WorkspaceViewModel ForLocal(IStudioInteraction interaction, KnownHosts knownHosts) =>
        new(WorkspaceKind.Local, "本地终端", "local", "", 0, Environment.UserName, "", interaction, knownHosts, null, null);

    public WorkspaceKind Kind { get; }
    public string Protocol { get; }
    public string Host { get; }
    public int Port { get; }
    public string User { get; }
    public string? ProfileId => profile?.Id;
    public string Endpoint => Kind == WorkspaceKind.Local ? "本机" : User.Length > 0 ? $"{User}@{Host}:{Port}" : $"{Host}:{Port}";
    public string ProtocolLabel => Kind switch { WorkspaceKind.Ftp => "FTP", WorkspaceKind.Sftp => "SFTP", WorkspaceKind.Telnet => "Telnet", WorkspaceKind.Local => "本地", _ => "SSH" };
    /// <summary>The authenticated SSH transport (monitor, exec actions). Null for FTP/Telnet/local.</summary>
    public SharedSshSession? Session => session;

    // ---------- terminals ----------

    public ObservableCollection<TerminalTabViewModel> Terminals { get; } = new();
    /// <summary>Centre column tabs in order: terminals and editors.</summary>
    public ObservableCollection<CenterTabViewModel> CenterTabs { get; } = new();
    public CenterTabViewModel? ActiveTab
    {
        get => activeTab;
        set
        {
            // A terminal being laid out unseen ("标签渲染") is let go before it is shown here.
            if (value is TerminalTabViewModel shown) TerminalWarmUp.Release(shown.Control);
            if (!Set(ref activeTab, value)) return;
            foreach (var tab in CenterTabs) tab.IsActive = ReferenceEquals(tab, value);
            if (value is TerminalTabViewModel terminal) { activeTerminal = terminal; Raise(nameof(ActiveTerminal)); Raise(nameof(Terminal)); }
            Raise(nameof(ActiveContent));
            Raise(nameof(ShowTerminalOverlay)); Raise(nameof(ShowReportInFiles));
            OnActiveTabChanged();
        }
    }
    /// <summary>The terminal that "CD 到终端" writes to: the active tab if it is a terminal, else the last one used.</summary>
    public TerminalTabViewModel? ActiveTerminal
    {
        get => activeTerminal;
        set => ActiveTab = value;
    }
    /// <summary>The control of the current terminal tab.</summary>
    public StudioTerminalControl? Terminal => activeTerminal?.Control;
    public object? ActiveContent => activeTab?.Content;
    public bool HasShell => Terminals.Any(t => t.HasConnection);
    public bool ShowTerminalOverlay => Kind != WorkspaceKind.Local && (activeTab == null && Kind != WorkspaceKind.Ftp || activeTab is TerminalTabViewModel { HasConnection: false });
    public IEnumerable<EditorTabViewModel> Editors => CenterTabs.OfType<EditorTabViewModel>();
    public bool HasUnsavedEditors => Editors.Any(e => e.IsDirty || e.IsSaving);
    /// <summary>More shells on the same transport (SSH) or more local terminals; Telnet has one.</summary>
    public bool CanAddTerminal => Kind is WorkspaceKind.Terminal or WorkspaceKind.Sftp or WorkspaceKind.Local;

    TerminalTabViewModel AddTab()
    {
        terminalNumber++;
        var tab = new TerminalTabViewModel(terminalNumber == 1 ? "终端" : $"终端 {terminalNumber}", new StudioTerminalControl()) { Time = time };
        tab.ActivateCommand = new RelayCommand(() => ActiveTab = tab);
        tab.CloseCommand = new RelayCommand(() => CloseTerminal(tab));
        tab.Control.PasteFilter = text => ReviewPasteAsync(tab, text);
        tab.Control.PasteSender = (head, text, bracketed) => SendPasteAsync(tab, head, text, bracketed);
        tab.Control.FindRequested += () => { if (ReferenceEquals(activeTab, tab)) Find.Open(); };
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TerminalTabViewModel.IsRecording)) Raise(nameof(IsRecording));
            else if (e.PropertyName == nameof(TerminalTabViewModel.IsPasteSlow)) Raise(nameof(IsPasteSlow));
        };
        Terminals.Add(tab);
        CenterTabs.Add(tab);
        ActiveTab = tab;
        return tab;
    }

    void CloseTerminal(TerminalTabViewModel tab)
    {
        if (!Terminals.Remove(tab)) return;
        // Only the tab that actually received the saved connection's writes ends them; closing the
        // placeholder tab before connecting leaves them for the first terminal attached later.
        if (ReferenceEquals(tab, firstTerminal) && firstAttached) firstTerminalLifetime.Cancel();
        if (ReferenceEquals(activeTerminal, tab)) { activeTerminal = Terminals.LastOrDefault(); Raise(nameof(ActiveTerminal)); Raise(nameof(Terminal)); }
        RemoveCenterTab(tab);
        tab.Dispose();
        Raise(nameof(HasShell)); RaiseTab();
    }

    void RemoveCenterTab(CenterTabViewModel tab)
    {
        int index = CenterTabs.IndexOf(tab);
        if (index < 0) return;
        CenterTabs.RemoveAt(index);
        CloseGuard.Moved();
        if (ReferenceEquals(activeTab, tab)) ActiveTab = CenterTabs.Count == 0 ? null : CenterTabs[Math.Min(index, CenterTabs.Count - 1)];
        if (CenterTabs.Count == 0) { showTerminal = false; Raise(nameof(ShowTerminal)); }
    }

    public const int MaxEditors = 12;

    /// <summary>Opens the selected file in the built-in editor (or focuses its existing tab).</summary>
    public async Task<EditorTabViewModel?> OpenEditorAsync(FileNodeViewModel node)
    {
        if (actions == null || node.IsDirectory || node.IsPlaceholder) return null;
        // The editor opens where the files are shown; it stays with the source it came from.
        var desk = Desk;
        // One draft of a file: an editor already open for it, here or in another session of the window, is shown instead.
        EditorTabViewModel? ShowOpened()
        {
            if (OpenedAnywhere(desk, actions.Source.SourceId, node.Path) is not var (owner, existing)) return null;
            owner.ShowCenter(); owner.ActiveTab = existing;
            if (!ReferenceEquals(owner, desk)) owner.ActivateCommand?.Execute(null);
            return existing;
        }
        if (ShowOpened() is { } opened) return opened;
        if (desk.Editors.Count() >= MaxEditors) throw new IOException($"最多同时打开 {MaxEditors} 个文件，请先关闭已保存的文件");
        var document = await actions.OpenTextAsync(node, lifetime.Token);
        if (disposed != 0) return null;
        // Linked, or no longer, while it was read.
        if (!ReferenceEquals(desk, Desk) && desk.disposed != 0) desk = Desk;
        if (ShowOpened() is { } openedMeanwhile) return openedMeanwhile;
        var editor = new EditorTabViewModel(document, Title, queue!, interaction, (id, kind) => Transfers?.Register(id, kind), path => Dispatcher.UIThread.Post(() => _ = ReloadDirectoryAsync(path)));
        desk.Show(editor);
        return editor;
    }

    void Show(EditorTabViewModel editor)
    {
        editor.SavePreferences = change => SavePreferences?.Invoke(change);
        editor.ActivateCommand = new RelayCommand(() => ActiveTab = editor);
        editor.CloseCommand = new AsyncCommand(async () => { if (await editor.ConfirmCloseAsync()) { RemoveCenterTab(editor); editor.Dispose(); Raise(nameof(HasUnsavedEditors)); } });
        editor.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(EditorTabViewModel.IsDirty) or nameof(EditorTabViewModel.IsSaving)) Raise(nameof(HasUnsavedEditors)); };
        CenterTabs.Add(editor);
        ShowCenter();
        ActiveTab = editor;
    }

    void ShowCenter()
    {
        if (showTerminal) return;
        showTerminal = true;
        Raise(nameof(ShowTerminal));
    }

    int ActiveTransfers => queue?.Snapshot().Count(s => s.State is TransferState.Queued or TransferState.Running or TransferState.Verifying or TransferState.Committing) ?? 0;

    /// <summary>True when closing would discard drafts or cancel queued/running transfers.</summary>
    public bool NeedsCloseConfirmation => Editors.Any(e => e.IsDirty || e.IsSaving) || ActiveTransfers > 0 || DraftsElsewhere.Any();

    /// <summary>Before closing the workspace (tab or window): unsaved drafts and queued or running
    /// transfers need explicit confirmation; a save in progress must finish first. <paramref name="all"/>: everything is
    /// being closed, and each session speaks for the drafts it shows.</summary>
    public async Task<bool> ConfirmCloseAsync(bool all = false)
    {
        var unsaved = Editors.Where(e => e.IsDirty || e.IsSaving).ToList();
        var elsewhere = all ? [] : DraftsElsewhere.ToList();
        if (elsewhere.Any(e => e.IsSaving)) { Status = "有文件正在保存，请等待结果或在传输管理中取消。"; return false; }
        if (unsaved.Any(e => e.IsSaving)) { Status = "有文件正在保存，请等待结果或在传输管理中取消。"; return false; }
        int active = ActiveTransfers;
        if (unsaved.Count == 0 && active == 0 && elsewhere.Count == 0) return true;
        var parts = new List<string>();
        if (unsaved.Count > 0) parts.Add("以下内存草稿未保存，将无法恢复：\n" + string.Join("\n", unsaved.Select(e => e.Path)));
        if (elsewhere.Count > 0) parts.Add("以下草稿在关联的会话里打开，关闭后变为只读、不能再保存：\n" + string.Join("\n", elsewhere.Select(e => e.Path)));
        if (active > 0) parts.Add($"{active} 个传输或操作仍在排队或进行，关闭会取消它们（已完成的不回滚）。");
        return await interaction.ConfirmAsync($"关闭“{Title}”？", string.Join("\n\n", parts), "关闭", danger: true);
    }

    /// <summary>Cancels queued and running transfers and waits (bounded) for the running one to clean
    /// up its temporary items, so the file channel is not closed underneath a commit.</summary>
    public async Task DrainTransfersAsync(TimeSpan limit)
    {
        if (queue == null || disposed != 0) return;
        foreach (var item in queue.Snapshot().Where(s => s.State is TransferState.Queued or TransferState.Running or TransferState.Verifying)) queue.Cancel(item.Id);
        var end = DateTime.UtcNow + limit;
        while (ActiveTransfers > 0 && DateTime.UtcNow < end) await Task.Delay(100);
    }

    /// <summary>"+" in the terminal column and "新建终端到当前目录": another shell on the same transport.</summary>
    public async Task<TerminalTabViewModel?> AddTerminalAsync()
    {
        if (!CanAddTerminal || disposed != 0) return null;
        if (Kind == WorkspaceKind.Local)
        {
            var local = AddTab();
            showTerminal = true; Raise(nameof(ShowTerminal));
            string? note = ConfigureLocal(local.Control);
            await local.Control.LaunchProcess();
            if (note != null) Status = note;
            return local;
        }
        if (session?.IsConnected != true) { Status = "SSH 连接不可用，无法新建终端"; return null; }
        var tab = AddTab();
        showTerminal = true; Raise(nameof(ShowTerminal));
        await OpenShellAsync(tab);
        return tab.HasConnection ? tab : null;
    }

    // ---------- files ----------

    public ObservableCollection<FileNodeViewModel> FileRoots { get; } = new();
    public ObservableCollection<PathSegment> PathSegments { get; } = new();
    public TransferListViewModel? Transfers { get; }
    public FileNodeViewModel? SelectedNode
    {
        get => selectedNode;
        set
        {
            // A row that only says something ("空目录") stands for the directory it is in.
            if (value is { IsPlaceholder: true }) { Rest(value.Parent?.Path); return; }
            Choose(value, null);
        }
    }
    /// <summary>The directory the user is in although no row is selected: the selected one is gone, or only said
    /// something.</summary>
    string? resting;
    void Rest(string? directory) => Choose(null, directory == rootPath ? null : directory);
    void Choose(FileNodeViewModel? node, string? rest)
    {
        var old = selectedNode;
        if (ReferenceEquals(old, node) && rest == resting) return;
        (selectedNode, resting) = (node, rest);
        Raise(nameof(SelectedNode));
        // Rows that are not on screen keep their mark, and would select themselves again when they are.
        if (!ReferenceEquals(old, node) && old != null) old.IsSelected = false;
        if (node != null) node.IsSelected = true;
        OnSelection();
    }
    void OnSelection()
    {
        SetPath(CurrentDirectory);
        Raise(nameof(HasSelection)); Raise(nameof(SelectedIsDirectory)); Raise(nameof(SelectedIsFile)); Raise(nameof(CurrentDirectory));
    }
    /// <summary>Nothing is selected, and the user is at the top.</summary>
    public void SelectNothing() { navigation++; Choose(null, null); }
    /// <summary>Rows are about to be exchanged, and have been.</summary>
    public event Action? RowsChanging, RowsChanged;
    /// <summary>The row of the directory the user is in, when no row is selected; null at the top.</summary>
    public FileNodeViewModel? RestingNode => selectedNode == null && resting != null ? FindNode(resting) : null;
    /// <summary>The row is one of those that are listed now.</summary>
    public bool Owns(FileNodeViewModel node) => node.IsPlaceholder ? node.Parent is not { } parent || Owns(parent) : ReferenceEquals(FindNode(node.Path), node);
    public bool HasSelection => selectedNode != null;
    public bool SelectedIsDirectory => selectedNode is { IsDirectory: true, IsLink: false };
    public bool SelectedIsFile => selectedNode is { IsFile: true };
    /// <summary>The selected directory, or the directory holding the selected file.</summary>
    public string CurrentDirectory => selectedNode switch
    {
        null => resting ?? rootPath,
        { IsDirectory: true, IsLink: false } d => d.Path,
        var f => f.ParentPath,
    };
    public bool HasFiles => actions != null;
    internal FileActions? Actions => actions;
    public bool CanUseTerminalFromFiles => Desk is { session: not null, CanAddTerminal: true };
    public bool NeedsDirectory { get => needsDirectory; private set => Set(ref needsDirectory, value); }

    public string Status { get => status; internal set => Set(ref status, value); }
    public string FileMessage { get => fileMessage; private set { if (Set(ref fileMessage, value)) Raise(nameof(HasFileMessage)); } }
    public bool HasFileMessage => FileMessage.Length > 0;
    /// <summary>Result of the last file action (queued, refused, failed).</summary>
    public string FileNotice { get => fileNotice; private set { if (Set(ref fileNotice, value)) Raise(nameof(HasFileNotice)); } }
    public bool HasFileNotice => FileNotice.Length > 0;
    public bool IsConnected { get => connected; private set { if (!Set(ref connected, value)) return; Raise(nameof(ShowTerminalOverlay)); Raise(nameof(ShowReportInFiles)); Raise(nameof(IsLinkedSourceLost)); RaiseIdle(); } }
    public bool IsBusy { get => busy; private set { if (!Set(ref busy, value)) return; Raise(nameof(OffersLink)); Raise(nameof(IsLinkedSourceLost)); } }

    public bool CanShowFiles => Kind is WorkspaceKind.Terminal or WorkspaceKind.Sftp or WorkspaceKind.Ftp;
    /// <summary>The centre column holds terminals and editors; FTP workspaces use it for editors only.</summary>
    public bool CanShowTerminal => true;
    public bool CanShowMonitor => Kind is WorkspaceKind.Terminal or WorkspaceKind.Sftp;

    public bool ShowFiles { get => showFiles; set => Set(ref showFiles, value && CanShowFiles); }
    public bool ShowMonitor { get => showMonitor; set => Set(ref showMonitor, value && CanShowMonitor); }
    public bool ShowTerminal
    {
        get => showTerminal;
        set
        {
            if (!Set(ref showTerminal, value && CanShowTerminal)) return;
            Raise(nameof(ShowReportInFiles));
            if (!showTerminal) return;
            // SFTP workspaces open the terminal on demand on the same authenticated transport.
            if (session?.IsConnected == true && !HasShell && openingShells.Count == 0) _ = EnsureShellAsync();
        }
    }

    async Task<TerminalTabViewModel?> EnsureShellAsync()
    {
        if (activeTerminal is { HasConnection: true } ready) return ready;
        var tab = activeTerminal ?? AddTab();
        await OpenShellAsync(tab);
        return tab.HasConnection ? tab : null;
    }

    public ICommand ToggleFilesCommand { get; }
    public ICommand ToggleTerminalCommand { get; }
    public ICommand ToggleMonitorCommand { get; }
    public ICommand HideFilesCommand { get; }
    public ICommand HideTerminalCommand { get; }
    public ICommand HideMonitorCommand { get; }
    public ICommand AddTerminalCommand { get; }
    public ICommand DismissFileNoticeCommand { get; }
    public ICommand CopyReportCommand { get; }

    // ---------- connecting ----------

    bool BeginConnect()
    {
        if (Interlocked.Exchange(ref connectStarted, 1) == 0) return true;
        Status = "此工作区已发起过连接；不会自动重新认证，请重新发起连接。";
        return false;
    }

    /// <summary>Controller launch. The password is read here and never stored on the workspace.</summary>
    public async Task ConnectAsync(LaunchConnection connection)
    {
        if (!BeginConnect()) return;
        string password = connection.Password;
        var ct = lifetime.Token;
        IsBusy = true;
        try
        {
            if (!connection.HasPassword && keyPath.Length == 0)
            {
                Status = "等待输入本次登录密码…";
                var answer = await interaction.AskCredentialsAsync(
                    new CredentialChallenge(1, false, true, "入口未提供密码；内容仅用于本次连接，不保存。", new[] { new CredentialQuestion("登录密码", false) }), ct);
                if (answer == null) { Status = "已取消连接"; return; }
                password = answer[0];
                Array.Clear(answer);
            }
            if (Kind == WorkspaceKind.Ftp)
            {
                Status = $"正在连接 FTP {Host}:{Port} …";
                var connectedFtp = await FtpFileSession.ConnectAsync(Host, Port, User, password, ct, options);
                password = "";
                if (Abandoned(connectedFtp)) return;
                ftp = connectedFtp;
                await AfterFtpConnectedAsync(ct);
            }
            else
            {
                Status = $"正在连接 {Host}:{Port} …";
                Task<bool> Verify(string fingerprint, CancellationToken token) =>
                    knownHosts.VerifyAsync(Host, Port, fingerprint, true, interaction.ConfirmHostKeyAsync, token);
                trace?.ConcealSecret(password);
                var connected = await SharedSshSession.ConnectAsync(Host, Port, User, password, Verify, ct, temporary: true,
                    challenge: interaction.AskCredentialsAsync, authMode: keyPath.Length > 0 ? "key" : "password", keyPath: keyPath, options: options);
                password = "";
                if (Abandoned(connected)) return;
                session = connected;
                await AfterSshConnectedAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e) { Fail(e); }
        finally
        {
            password = "";
            connection.Password = "";
            IsBusy = false;
        }
    }

    /// <summary>Saved connection: secrets come from the OS credential store or a one-off prompt.</summary>
    public async Task ConnectProfileAsync()
    {
        if (profile == null || connector == null || !BeginConnect()) return;
        var ct = lifetime.Token;
        IsBusy = true;
        try
        {
            Status = profile.JumpChain.Count > 0 ? $"正在经 {profile.JumpChain.Count} 级跳板连接 {Host}:{Port} …" : $"正在连接 {Host}:{Port} …";
            switch (Kind)
            {
                case WorkspaceKind.Telnet:
                    var telnet = await ProfileConnector.ConnectTelnetAsync(profile, ct);
                    if (Abandoned(telnet)) return;
                    telnet.ProcessExited += (_, _) => OnTerminalFault(telnet.CloseReason ?? "Telnet 连接已关闭");
                    // The placeholder tab may have been closed while connecting: never attach to a disposed tab.
                    var telnetTab = firstTerminal is { } placeholder && Terminals.Contains(placeholder) ? placeholder : AddTab();
                    showTerminal = true; Raise(nameof(ShowTerminal));
                    AttachTerminal(telnetTab, telnet);
                    IsConnected = true;
                    Status = "已连接：Telnet 终端（明文协议）";
                    break;
                case WorkspaceKind.Ftp:
                    var connectedFtp = await connector.ConnectFtpAsync(profile, ct);
                    if (Abandoned(connectedFtp)) return;
                    ftp = connectedFtp;
                    await AfterFtpConnectedAsync(ct);
                    break;
                default:
                    var connected = await connector.ConnectSshAsync(profile, ct, trace: trace);
                    if (Abandoned(connected)) return;
                    session = connected;
                    await AfterSshConnectedAsync(ct);
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (OperationCanceledException) { Status = "已取消连接"; }
        catch (Exception e) { Fail(e); }
        finally { IsBusy = false; }
    }

    void Fail(Exception e)
    {
        // A server or network that hangs up says more in the messages before it than the exception does; what this program
        // gave up on itself (a host key, no common algorithm, not an SSH server) the exception says better.
        string why = trace is { EndedByPeer: true } && trace.Explanation is { Length: > 0 } explained ? explained : SshErrors.Explain(e);
        Status = "连接失败：" + Short(why, MostReason);
        if (FileRoots.Count == 0) FileMessage = Status;
        OfferReport();
    }

    const int MostReason = 400;

    /// <summary>The transport ended while its channels were being opened: that is the news then, not that each channel
    /// "was refused". The event that says so may still be on its way.</summary>
    bool IsLost()
    {
        if (session is { IsConnected: true } && trace is not { IsOver: true }) return false;
        EndTransport(trace?.Explanation is { Length: > 0 } explained ? explained : session?.CloseReason ?? "SSH 连接已中断");
        return true;
    }

    /// <summary>"复制连接诊断": which messages were exchanged, never what was in them.</summary>
    public bool CanReport => trace != null && connectStarted != 0;
    public bool ShowReport => CanReport && offerReport;
    /// <summary>Next to the files when the terminal does not show it.</summary>
    public bool ShowReportInFiles => ShowReport && !(ShowTerminal && ShowTerminalOverlay);
    /// <summary>What the server shows at login. It is only of interest when something went wrong.</summary>
    public string ServerNotice => trace?.Banner ?? "";
    public bool HasServerNotice => ShowReport && ServerNotice.Length > 0;
    void OfferReport()
    {
        offerReport = true;
        RaiseReport();
    }
    void RaiseReport()
    {
        Raise(nameof(CanReport));
        Raise(nameof(ShowReport));
        Raise(nameof(ShowReportInFiles));
        Raise(nameof(ServerNotice));
        Raise(nameof(HasServerNotice));
    }

    internal string Report()
    {
        string what = Kind == WorkspaceKind.Sftp ? "文件会话" : "终端会话";
        string[] head =
        [
            "Forterm Studio " + StudioPaths.Version + " 连接诊断",
            "系统：" + RuntimeInformation.OSDescription.Trim() + " " + RuntimeInformation.ProcessArchitecture,
            "打开方式：" + (profile == null ? "控制器唤醒" : profile.JumpChain.Count > 0 ? $"已保存连接，经 {profile.JumpChain.Count} 级跳板" : "已保存连接") + "，" + what,
        ];
        return trace!.Report(head, [Host, User, Title, keyPath]);
    }

    async Task CopyReportAsync()
    {
        if (!CanReport) return;
        await interaction.CopyTextAsync(Report());
        interaction.Toast(new ToastMessage(ToastKind.Info, "已复制连接诊断（没有地址、账号、票据和终端内容）"));
    }

    async Task AfterSshConnectedAsync(CancellationToken ct)
    {
        session!.Faulted += OnTransportFault;
        IsConnected = true;
        Monitor?.SetVisible(ShowMonitor && IsActive);
        Raise(nameof(CanUseTerminalFromFiles));
        Raise(nameof(Session));
        if (session.CloseReason is { } reason) OnTransportFault(reason);
        if (ShowTerminal && firstTerminal != null) await OpenShellAsync(firstTerminal);
        if (disposed != 0 || IsLost()) return;
        await OpenFilesAsync(ct);
        if (disposed != 0 || IsLost()) return;
        Status = (HasShell, sftp) switch
        {
            (true, not null) => "已连接：终端与文件复用同一 SSH 连接",
            (true, null) => "已连接：终端可用，文件通道不可用",
            (false, not null) when shellError.Length > 0 => "已连接：文件可用，终端没有打开：" + shellError,
            (false, not null) => Kind == WorkspaceKind.Sftp ? "已连接：SFTP 文件会话，可按需打开终端" : "已连接：文件可用，终端没有打开",
            _ when shellError.Length > 0 => "已登录，但终端和文件都没有打开：" + shellError,
            _ => "已登录，但文件没有打开",
        } + environmentNotice;
        if (!HasShell && sftp == null) OfferReport();
        sshReady = true;
    }

    async Task AfterFtpConnectedAsync(CancellationToken ct)
    {
        IsConnected = true;
        var files = ftp!;
        UseFileSource(files, async (path, refresh, token) => (await files.ListAsync(path, token)).Entries);
        await LoadTreeAsync(StartPath("/"), ct);
        Status = "已连接：FTP 文件会话（明文被动模式）";
    }

    /// <summary>Opens a shell for this tab, or joins the open already in progress for it. Tabs open
    /// independently, so "+" while another shell is opening still gets its own shell.</summary>
    Task OpenShellAsync(TerminalTabViewModel tab)
    {
        // A closed tab (for example the placeholder closed while connecting) never costs a channel.
        if (tab.HasConnection || session == null || disposed != 0 || !Terminals.Contains(tab)) return Task.CompletedTask;
        if (openingShells.TryGetValue(tab, out var pending)) return pending;
        var task = OpenShellCoreAsync(tab, session);
        if (!task.IsCompleted) openingShells[tab] = task;
        return task;
    }

    async Task OpenShellCoreAsync(TerminalTabViewModel tab, SharedSshSession ssh)
    {
        try
        {
            var shell = await ssh.OpenShellAsync(120, 32, lifetime.Token, settings.TerminalType, settings.Environment ?? Array.Empty<KeyValuePair<string, string>>(), flowControl: true);
            if (disposed != 0 || !Terminals.Contains(tab)) { shell.Dispose(); return; }
            if (ReferenceEquals(tab, firstTerminal)) shellError = "";
            shell.Faulted += OnTerminalFault;
            AttachTerminal(tab, shell);
            // Sent as protocol requests only; a refusal never falls back to typing "export".
            var names = shell.RejectedEnvironment;
            environmentNotice = names.Count == 0 ? "" : "；服务器未接受会话环境变量 " + string.Join("、", names.Take(5)) + (names.Count > 5 ? " 等" : "") + "（需要服务器 AcceptEnv 允许）";
            if (sshReady && names.Count > 0) Status = "已打开终端" + environmentNotice;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e)
        {
            string why = Short(SshErrors.Explain(e), MostReason);
            if (ReferenceEquals(tab, firstTerminal)) shellError = why;
            if (!transportEnded) Status = "终端通道不可用：" + why;
        }
        finally { openingShells.Remove(tab); }
    }

    /// <summary>A connection that completes after the workspace was closed is released at once
    /// (continuations run on the UI thread, as does <see cref="Dispose"/>).</summary>
    bool Abandoned(IDisposable resource)
    {
        if (Volatile.Read(ref disposed) == 0) return false;
        resource.Dispose();
        return true;
    }

    /// <summary>Attaches a terminal connection to a tab. The workspace's first attached terminal
    /// also gets the saved connection's user-configured writes ("初始执行", "保活命令").</summary>
    void AttachTerminal(TerminalTabViewModel tab, IPtyConnection connection)
    {
        IPtyConnection attached = connection;
        bool follow = settings.PwdFollow && connection is SshPtyConnection;
        if (follow)
        {
            var sniffer = new Osc7Sniffer(connection);
            sniffer.DirectoryChanged += path => Dispatcher.UIThread.Post(() => OnTerminalDirectory(tab, path));
            attached = sniffer;
        }
        if (settings.Encoding is not ("UTF-8" or "")) attached = new TranscodingPtyConnection(attached, Encodings.Get(settings.Encoding));
        tab.Owned = connection;
        tab.Attach(attached, connection as IImmediateInput);
        RaiseIdle();
        Raise(nameof(HasShell)); RaiseTab();
        Raise(nameof(ShowTerminalOverlay)); Raise(nameof(ShowReportInFiles));
        string writes = follow ? Osc7Sniffer.ShellHook : "";
        bool first = !firstAttached;
        if (first)
        {
            firstAttached = true;
            firstTerminal = tab;
            if (settings.InitialScript.Trim().Length > 0) writes += TerminalLines(settings.InitialScript);
            if (settings.KeepAliveCommand.Trim().Length > 0 && settings.KeepAliveCommandMs >= 5000) _ = KeepAliveCommandLoopAsync(tab);
        }
        // User-configured terminal writes for saved connections (pwd hook, "初始执行"), in one write.
        if (writes.Length > 0) _ = WriteTerminalAsync(tab, writes, first && settings.InitialScript.Trim().Length > 0 ? "初始执行" : "pwd 联动", byUser: false);
    }

    bool fileMenuOpen;
    string? deferredFollow;

    /// <summary>The file tree's context menu is open: its commands act on the selected row, so pwd
    /// follow waits until the menu has closed (commands capture the row before that).</summary>
    public void SetFileMenuOpen(bool open)
    {
        fileMenuOpen = open;
        if (open || deferredFollow is not { } path) return;
        deferredFollow = null;
        if (disposed == 0 && activeTerminal?.Directory == path && path != CurrentDirectory) _ = NavigateAsync(path, follow: true);
    }

    /// <summary>"pwd 自动跳转": the active terminal reported a new directory; select it in the tree.</summary>
    internal void OnTerminalDirectory(TerminalTabViewModel tab, string path)
    {
        bool again = tab.Directory == path;
        tab.Directory = path;
        if (disposed != 0 || lister == null || !ReferenceEquals(tab, activeTerminal) || path == CurrentDirectory) return;
        // The prompt that follows "空闲保活" or a keep-alive command: the user went nowhere, and may be looking elsewhere.
        if (again && tab.Idle?.IsUnasked == true) return;
        _ = NavigateAsync(path, follow: true);
    }

    /// <summary>Script lines are sent as typed lines: newlines become Enter (CR).</summary>
    internal static string TerminalLines(string script)
    {
        string text = script.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        return text.Replace("\n", "\r") + "\r";
    }

    [GeneratedRegex("^[A-Za-z0-9_./@%+=:,-]+$")]
    private static partial Regex PlainPath();

    /// <summary>"cd" as typed into the shell; single-quoted unless the path is plain. Null when the
    /// path holds any control character: quoting does not stop the line editor from acting on ^U,
    /// ^C, ESC and the like, so such a path is never typed into a terminal.</summary>
    internal static string? CdCommand(string path) =>
        path.Any(char.IsControl) ? null :
        PlainPath().IsMatch(path) ? $"cd {path}\r" : "cd -- '" + path.Replace("'", "'\\''") + "'\r";

    async Task WriteTerminalAsync(TerminalTabViewModel tab, string text, string purpose, bool byUser)
    {
        try { await tab.WriteAsync(text, byUser, ReferenceEquals(tab, firstTerminal) ? firstTerminalLifetime.Token : lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            // Fire-and-forget writer: report every failure instead of losing it as an unobserved task.
            Dispatcher.UIThread.Post(() => { if (disposed == 0) Status = purpose + "未能写入终端：" + e.GetType().Name + " " + Short(e.Message); });
        }
    }

    async Task KeepAliveCommandLoopAsync(TerminalTabViewModel tab)
    {
        string line = settings.KeepAliveCommand.Trim() + "\r";
        var token = firstTerminalLifetime.Token; // read once: the source is disposed with the workspace
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(settings.KeepAliveCommandMs));
        try { while (await timer.WaitForNextTickAsync(token)) await WriteTerminalAsync(tab, line, "保活命令", byUser: false); }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException) { }
    }

    async Task OpenFilesAsync(CancellationToken ct)
    {
        try
        {
            var files = await session!.OpenFilesAsync(ct);
            if (Abandoned(files)) return;
            sftp = files;
            catalog = new RemoteDirectoryCatalog(sftp);
            var bound = catalog;
            UseFileSource(sftp, async (path, refresh, token) => (await bound.ListAsync(path, refresh, token)).Entries);
            if (!sftp.HasWorkingDirectory && settings.DefaultPath.Length == 0)
            {
                // The server refused REALPATH: never guess a directory. The user names one explicitly.
                NeedsDirectory = true;
                FileMessage = "服务器没有提供当前目录（REALPATH 被拒绝）。请输入要打开的绝对目录。";
                return;
            }
            if (!sftp.HasWorkingDirectory) sftp.AuthorizeAbsoluteDirectory(settings.DefaultPath);
            await LoadTreeAsync(StartPath(sftp.HasWorkingDirectory ? sftp.Client.WorkingDirectory : "/"), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { if (!transportEnded) FileMessage = "文件通道不可用：" + Short(SshErrors.Explain(e), MostReason); }
    }

    void UseFileSource(IRemoteFileSource source, DirectoryLister list)
    {
        lister = list;
        actions = new FileActions(source, queue!, interaction, list, path => Dispatcher.UIThread.Post(() => _ = ReloadDirectoryAsync(path)), Title, session);
        actions.Queued += (id, kind) => Transfers?.Register(id, kind);
        Raise(nameof(HasFiles));
        Raise(nameof(OffersLink));
    }

    string StartPath(string fallback) => settings.DefaultPath.StartsWith('/') ? settings.DefaultPath.TrimEnd('/') is { Length: > 0 } p ? p : "/" : fallback;

    /// <summary>Shows the tree from "/" and expands it down to the start directory, as the
    /// reference does. If "/" cannot be listed (chroot, policy), the start directory is the root.</summary>
    async Task LoadTreeAsync(string home, CancellationToken ct)
    {
        var list = lister!;
        FileMessage = "";
        IReadOnlyList<RemoteEntry> top;
        rootPath = "/";
        try { top = await list("/", false, ct); }
        catch (Exception) when (home != "/" && !ct.IsCancellationRequested)
        {
            rootPath = home;
            top = await list(home, false, ct);
        }
        rootListing = top;
        rootRevealed.Clear();
        FileRoots.Clear();
        foreach (var entry in FileNodeViewModel.Arrange(top)) FileRoots.Add(new FileNodeViewModel(entry, list, lifetime.Token));
        SelectNothing();
        SetPath(home);
        if (home == rootPath) return;
        var current = await ExpandToAsync(home);
        if (current != null) SelectedNode = current;
        if (CurrentDirectory == home) return;
        SetPath(CurrentDirectory);
        FileNotice = $"没有找到目录 {home}，当前在 {CurrentDirectory}";
    }

    /// <summary>The root of the tree, or somewhere below it ("/home" is not below "/ho").</summary>
    bool IsInTree(string path) => path == rootPath || path.StartsWith(rootPath.TrimEnd('/') + "/", StringComparison.Ordinal);

    /// <summary>Expands the tree along <paramref name="path"/> and returns the deepest node reached.</summary>
    async Task<FileNodeViewModel?> ExpandToAsync(string path)
    {
        if (!IsInTree(path)) return null;
        FileNodeViewModel? current = null;
        IEnumerable<FileNodeViewModel> level = FileRoots;
        foreach (string part in path[rootPath.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current == null) RevealRoot(part); else current.Reveal(part);
            var next = level.FirstOrDefault(n => n.IsDirectory && n.Name == part);
            if (next == null) break;
            next.IsExpanded = true;
            await next.EnsureLoadedAsync();
            current = next;
            level = next.Children;
        }
        return current;
    }

    FileNodeViewModel? FindNode(string path)
    {
        if (!IsInTree(path)) return null;
        FileNodeViewModel? current = null;
        IEnumerable<FileNodeViewModel> level = FileRoots;
        foreach (string part in path[rootPath.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = level.FirstOrDefault(n => !n.IsPlaceholder && n.Name == part);
            if (current == null) return null;
            level = current.Children;
        }
        return current;
    }

    /// <summary>Lists one directory again after a change (keeps expanded folders).</summary>
    public async Task ReloadDirectoryAsync(string path)
    {
        if (lister == null || disposed != 0) return;
        try
        {
            if (path.TrimEnd('/') == rootPath.TrimEnd('/'))
            {
                var list = lister;
                rootListing = await list(rootPath, true, lifetime.Token);
                KeepSelection();
                FillRoots(list);
            }
            else if (FindNode(path) is { IsDirectory: true } node) await node.ReloadAsync(KeepSelection);
        }
        catch (Exception e) when (e is not OperationCanceledException) { FileNotice = "刷新失败：" + Short(e.Message); }
    }

    IReadOnlyList<RemoteEntry>? rootListing;
    (bool, string, bool) arranged = Arrangement(Appearance.Current);
    static (bool, string, bool) Arrangement(StudioPreferences p) => (p.ShowHidden, p.FileSort, p.FileSortDescending);

    /// <summary>Another order, or hidden files on or off: what is listed is arranged again. The selection stays when
    /// its row does.</summary>
    void OnPreferences(StudioPreferences p)
    {
        Raise(nameof(DownloadHereHeader));
        if (arranged == Arrangement(p)) return;
        arranged = Arrangement(p);
        if (rootListing == null || lister is not { } list || disposed != 0) return;
        string? path = selectedNode?.Path ?? resting;
        KeepSelection();
        // What is hidden from now on may be where the user is: that stays in sight, rows and all.
        rootRevealed.Clear();
        foreach (var node in FileRoots) node.Conceal();
        FileNodeViewModel? on = null;
        foreach (string part in path == null || !IsInTree(path) ? [] : path[rootPath.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (on == null) rootRevealed.Add(part); else on.Keep(part);
            on = (on == null ? FileRoots : on.Children).FirstOrDefault(n => n.Name == part);
            if (on == null) break;
        }
        FillRoots(list);
        foreach (var node in FileRoots) node.Rearrange();
    }

    bool settling;

    /// <summary>Rows are about to be exchanged: once they are, the selection is looked up again by its path (and the
    /// tree, which has lost it, told). When the row is gone, or another kind of thing has its name now, the user stays
    /// in its directory with nothing selected: pressing Delete once more must not mean something else.</summary>
    void KeepSelection()
    {
        if (settling) return;
        settling = true;
        RowsChanging?.Invoke();
        Dispatcher.UIThread.Post(() =>
        {
            settling = false;
            if (disposed != 0) return;
            Settle();
            RowsChanged?.Invoke();
        }, DispatcherPriority.Background);

        void Settle()
        {
            if (selectedNode is not { } was) { if (resting != null) Rest(Listed(resting)); return; }
            if (FindNode(was.Path) is not { } row || row.IsDirectory != was.IsDirectory || row.IsLink != was.IsLink) { Rest(Listed(was.ParentPath)); return; }
            if (!ReferenceEquals(row, was)) SelectedNode = row;
            else Raise(nameof(SelectedNode));
        }

        string? Listed(string directory)
        {
            while (directory.Length > rootPath.Length && FindNode(directory) is not { IsDirectory: true }) directory = directory.LastIndexOf('/') is > 0 and var i ? directory[..i] : "/";
            return directory.Length > rootPath.Length ? directory : null;
        }
    }

    /// <summary>Hidden names that are shown nevertheless, because the user went there.</summary>
    readonly HashSet<string> rootRevealed = new(StringComparer.Ordinal);

    void FillRoots(DirectoryLister list) => FileNodeViewModel.Merge(FileRoots, rootListing!, e => new FileNodeViewModel(e, list, lifetime.Token), rootRevealed);

    /// <summary>True when the name is shown now.</summary>
    bool RevealRoot(string name)
    {
        if (FileRoots.Any(n => n.Name == name)) return true;
        if (rootListing?.Any(e => e.Name == name) != true || lister is not { } list || !rootRevealed.Add(name)) return false;
        FillRoots(list);
        return true;
    }

    void SetPath(string path)
    {
        PathSegments.Clear();
        string full = "";
        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            full += "/" + part;
            PathSegments.Add(new PathSegment(part, full));
        }
    }

    public async Task StartLocalAsync()
    {
        if (Kind != WorkspaceKind.Local || firstTerminal == null || !BeginConnect()) return;
        try
        {
            string? note = ConfigureLocal(firstTerminal.Control);
            await firstTerminal.Control.LaunchProcess();
            IsConnected = true;
            Status = note ?? "本地终端";
        }
        catch (Exception e) { Status = "本地终端启动失败：" + Short(e.Message); }
    }

    /// <summary>What a local terminal will start: program, arguments (no shell in between) and a note when
    /// arguments are not used. macOS/Linux get an argument array. Windows has no such array: the text is
    /// appended to the command line exactly as typed, as a shortcut does, because re-quoting it changes
    /// its meaning for programs with their own rules (cmd.exe). Arguments that cannot be parsed, belong
    /// to a missing program, or would be run by cmd.exe as a batch file are left out with the reason.</summary>
    internal sealed record LocalLaunch(string Program, string[] Args, string? Note);

    internal static LocalLaunch PlanLocal(StudioPreferences prefs, bool windows)
    {
        string configured = prefs.LocalShell.Trim();
        string program = ResolveLocalShell(prefs.LocalShell);
        var (args, error) = CommandLineArgs.TrySplit(prefs.LocalShellArgs);
        if (configured.Length > 0 && program != configured)
            return new(program, [], prefs.LocalShellArgs.Trim().Length > 0 ? $"找不到默认程序，已改用 {program}，未使用默认程序参数" : null);
        if (error != null) return new(program, [], $"默认程序参数无法解析（{error}），已忽略");
        if (args.Length > 0 && windows && (program.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || program.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)))
            return new(program, [], "批处理程序（.bat/.cmd）的参数会由 cmd 解释，已忽略默认程序参数");
        return new(program, windows && args.Length > 0 ? [prefs.LocalShellArgs.Trim()] : args, null);
    }

    /// <summary>Applies <see cref="PlanLocal"/> and TERM to a local terminal control. Windows uses the
    /// verbatim mode: Porta.Pty's own quoting loses empty arguments and breaks trailing backslashes.
    /// Returns a note for the status line, or null.</summary>
    internal static string? ConfigureLocal(StudioTerminalControl control, bool? windows = null)
    {
        bool win = windows ?? OperatingSystem.IsWindows();
        var plan = PlanLocal(Appearance.Current, win);
        control.Process = plan.Program;
        control.VerbatimCommandLine = win && plan.Args.Length > 0;
        control.ProcessArgs = plan.Args;
        control.EnvironmentVariables = Appearance.Current.LocalTerminalType.Length > 0 ? new Dictionary<string, string> { ["TERM"] = Appearance.Current.LocalTerminalType } : null;
        return plan.Note;
    }

    /// <summary>The configured default program if it exists, otherwise the platform default.</summary>
    public static string ResolveLocalShell(string configured)
    {
        configured = configured.Trim();
        if (configured.Length > 0 && File.Exists(configured)) return configured;
        if (OperatingSystem.IsWindows()) return Path.Combine(Environment.SystemDirectory, "cmd.exe");
        string? shellPath = Environment.GetEnvironmentVariable("SHELL");
        return shellPath is { Length: > 0 } && File.Exists(shellPath) ? shellPath : "/bin/zsh";
    }

    void OnTransportFault(string message) => Dispatcher.UIThread.Post(() => EndTransport(message));

    void EndTransport(string message)
    {
        if (disposed != 0 || transportEnded) return;
        transportEnded = true;
        IsConnected = false;
        firstTerminalLifetime.Cancel();
        Status = "连接已断开：" + Short(message, MostReason).TrimEnd('。') + (profile == null ? "（票据不会重用，请从控制器重新打开）" : "（不会自动重连，请重新发起连接）");
        if (!HasFiles && CanShowFiles) FileMessage = Kind == WorkspaceKind.Sftp || !ShowTerminal ? Status : "连接已断开，文件没有打开。";
        OfferReport();
        // The status line of a session that is not shown is not seen.
        if (!IsActive) interaction.Toast(new ToastMessage(ToastKind.Error, $"“{Title}”的连接已断开", "查看", () => ActivateCommand?.Execute(null)));
    }

    void OnTerminalFault(string message) => Dispatcher.UIThread.Post(() =>
    {
        if (disposed != 0 || transportEnded) return;
        if (Kind == WorkspaceKind.Telnet) { IsConnected = false; firstTerminalLifetime.Cancel(); }
        Status = "终端通道已关闭：" + Short(message);
    });

    internal static string Short(string message, int most = 160)
    {
        message = message.ReplaceLineEndings(" ").Trim();
        return message.Length > most ? message[..most] + "…" : message;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        Appearance.Changed -= OnPreferences;
        Appearance.Changed -= OnIdlePreferences;
        QuickCommands = null;
        Find.Close();
        if (session != null) session.Faulted -= OnTransportFault;
        EndRecordings();
        foreach (var tab in CenterTabs) tab.Dispose();
        CenterTabs.Clear();
        Terminals.Clear();
        EndLinks();
        Monitor?.Dispose();
        Transfers?.Dispose();
        queue?.Dispose();
        catalog?.Dispose();
        sftp?.Dispose();
        ftp?.Dispose();
        session?.Dispose();
        firstTerminalLifetime.Dispose();
        lifetime.Dispose();
    }
}
