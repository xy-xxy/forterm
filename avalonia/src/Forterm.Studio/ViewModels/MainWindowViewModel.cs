using System.Collections.ObjectModel;
using System.Windows.Input;
using Forterm.Core;
using Forterm.Studio.Launch;
using Forterm.Studio.Library;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;

namespace Forterm.Studio.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    readonly IStudioInteraction interaction;
    readonly KnownHosts knownHosts;
    readonly SessionConnectOptions? options;
    readonly LibraryStore library;
    readonly ProfileConnector connector;
    readonly string? settingsDirectory;
    object selected;
    string notice = "";

    public MainWindowViewModel(IStudioInteraction interaction, KnownHosts knownHosts, LibraryStore library, CredentialStore credentials, SessionConnectOptions? options = null, AccountStore? accounts = null,
        QuickCommandStore? quickCommands = null, string? settingsDirectory = null)
    {
        Accounts = accounts ?? new AccountStore(null);
        QuickCommands = quickCommands ?? new QuickCommandStore(null);
        this.settingsDirectory = settingsDirectory;
        this.interaction = interaction;
        this.knownHosts = knownHosts;
        this.options = options;
        this.library = library;
        connector = new ProfileConnector(library, credentials, knownHosts, interaction);
        Home = new LibraryViewModel(library, credentials, connector, interaction, OpenProfileAsync);
        Home.Tunnels = new TunnelsViewModel(library, connector, interaction);
        Home.AccountsCommand = new RelayCommand(() => interaction.ShowAccounts(new AccountsViewModel(Accounts, credentials, interaction, open: OpenBastionTicketAsync)));
        Home.UseLiveInfo(id => Workspaces.Where(w => w.ProfileId == id && w.IsConnected).Select(w => w.Monitor?.Summary).FirstOrDefault(s => s != null));
        selected = Home;
        ShowHomeCommand = new RelayCommand(() => Selected = Home);
        OpenLocalTerminalCommand = new AsyncCommand(OpenLocalTerminalAsync, e => Notice = "本地终端启动失败：" + WorkspaceViewModel.Short(e.Message));
        DismissNoticeCommand = new RelayCommand(() => Notice = "");
        OpenSettingsCommand = new RelayCommand(p => OpenSettings(p as string ?? "theme"));
        Palette = new PaletteViewModel(PaletteEntries);
        OpenPaletteCommand = new RelayCommand(Palette.Open);
        // "锁屏": the password is checked against what is kept with the other secrets.
        AppLock.Use(credentials);
        LockCommand = new AsyncCommand(async () => { if (await AppLock.LockAsync() is { } why) Notice = why; }, e => Notice = "没有锁定：" + WorkspaceViewModel.Short(e.Message));
        // "空闲保活": minutes matter, so a look every few seconds is enough.
        idle = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromSeconds(5), Avalonia.Threading.DispatcherPriority.Background, (_, _) => IdleTick());
        idle.Start();
        // What the palette offers is about the sessions as they were when it was opened.
        Workspaces.CollectionChanged += (_, _) => Palette.Dismiss();
    }

    readonly Avalonia.Threading.DispatcherTimer idle;
    internal void IdleTick() { foreach (var w in Workspaces.ToArray()) w.IdleTick(); }

    public ToastCenter Toasts { get; } = new();
    /// <summary>The entries for the controller, and what came through them.</summary>
    public ControllerAccess Access { get; init; } = new();
    public PaletteViewModel Palette { get; }
    public ICommand OpenPaletteCommand { get; }
    /// <summary>"锁屏" now.</summary>
    public ICommand LockCommand { get; }

    /// <summary>What the command palette offers right now. An entry only acts on the session that is shown.</summary>
    IEnumerable<PaletteEntry> PaletteEntries()
    {
        object shown = selected;
        Action OnlyHere(Action run) => () => { if (ReferenceEquals(selected, shown)) run(); };
        PaletteEntry Do(string group, string title, ICommand command, string keys = "", string detail = "") => new(group, title, detail, keys, OnlyHere(() => { if (command.CanExecute(null)) command.Execute(null); }));
        for (int i = 0; i < Workspaces.Count; i++)
        {
            var w = Workspaces[i];
            if (!ReferenceEquals(w, selected) && w.IsTabShown) yield return new PaletteEntry("会话", w.Title, w.Endpoint, "", () => Show(w));
        }
        if (selected is WorkspaceViewModel current)
        {
            if (current.CanAddTerminal) yield return Do("终端", "新建终端页签", current.AddTerminalCommand, Shortcuts.NewTerminal.Keys);
            if (current.IsTerminalShown)
            {
                yield return Do("终端", "在终端中查找", current.FindInTerminalCommand, Shortcuts.Find.Keys);
                yield return Do("终端", current.RecordHeader, current.RecordTerminalCommand);
                if (current.CanIdle) yield return Do("终端", current.IdleHeader, current.ToggleIdleCommand);
                yield return Do("终端", "滚动到底部", current.ScrollToBottomCommand);
                yield return Do("终端", "清空显示（含回滚）", current.ClearTerminalCommand);
                foreach (var command in QuickCommands.Commands)
                    yield return new PaletteEntry("快速命令", command.Name, command.Preview, "", OnlyHere(() => current.RunQuickCommand.Execute(command)));
            }
            if (current.CanShowFiles)
            {
                yield return Do("界面", current.ShowFiles ? "收起文件栏" : "显示文件栏", current.ToggleFilesCommand);
                if (current.CanLinkFiles)
                {
                    foreach (var source in current.LinkChoices)
                        if (!ReferenceEquals(source, current.FileView)) yield return new PaletteEntry("文件", "关联文件会话：" + source.Title, source.Endpoint, "", OnlyHere(() => current.Link(source)));
                    if (current.ShowsLinkedFiles) yield return new PaletteEntry("文件", "解除关联文件会话", current.FilesTitle, "", OnlyHere(current.Unlink));
                }
                var files = current.FileView;
                if (files.HasFiles)
                {
                    yield return Do("文件", "上传文件到当前目录", files.UploadFilesCommand, detail: files.CurrentDirectory);
                    yield return Do("文件", "新建文件夹", files.NewFolderCommand, detail: files.CurrentDirectory);
                }
            }
            yield return Do("界面", current.ShowTerminal ? "收起终端栏" : "显示终端栏", current.ToggleTerminalCommand);
            if (current.CanReport) yield return Do("会话", "复制连接诊断", current.CopyReportCommand, detail: "协议消息的名称与时间，没有地址、账号和内容");
            if (current.CanShowMonitor) yield return Do("界面", current.ShowMonitor ? "收起监控栏" : "显示监控栏", current.ToggleMonitorCommand);
        }
        yield return Do("界面", "连接库", ShowHomeCommand, Shortcuts.Home.Keys);
        yield return Do("界面", "锁定 Forterm", LockCommand, Shortcuts.Lock.Keys, detail: "会话保持；输入锁屏密码解锁");
        yield return Do("终端", "新建本地终端", OpenLocalTerminalCommand);
        yield return new PaletteEntry("设置", "系统配置", "主题界面", Shortcuts.Settings.Keys, () => OpenSettings("theme"));
        foreach (var (key, title) in new[] { ("terminal", "通用终端"), ("highlight", "自定义高亮"), ("quick", "快速命令"), ("keys", "快捷键"), ("local", "本地终端"), ("lock", "锁屏") })
            yield return new PaletteEntry("设置", "系统配置：" + title, "", "", () => OpenSettings(key));
        foreach (var profile in library.Profiles)
            yield return new PaletteEntry("连接", profile.Name, $"{profile.User}@{profile.Host}:{profile.Port}", "", () => _ = OpenFromPaletteAsync(profile));
    }

    async Task OpenFromPaletteAsync(StudioProfile profile)
    {
        try { await OpenProfileAsync(profile); }
        catch (Exception e) { Notice = "打开连接失败：" + WorkspaceViewModel.Short(e.Message); }
    }

    /// <summary>The user went to other content and wants to go on there with the keyboard. Not raised for what
    /// comes by itself (a launch by the controller): keys meant for one session must not reach another.</summary>
    public event Action<WorkspaceViewModel>? FocusRequested;

    void Show(WorkspaceViewModel workspace)
    {
        Selected = workspace;
        // Itself, or the session that shows its files.
        if (selected is WorkspaceViewModel shown && (ReferenceEquals(shown, workspace) || ReferenceEquals(shown, workspace.Viewer))) FocusRequested?.Invoke(shown);
    }

    bool closing;

    /// <summary>Cmd+W / Ctrl+Shift+W: the tab in the middle, or the session when it is the last one. A session
    /// the controller opened cannot be opened again from here, so a key alone does not end it.</summary>
    public async Task CloseCurrentTabAsync()
    {
        if (closing || selected is not WorkspaceViewModel w) return;
        if (w.CenterTabs.Count > 1)
        {
            if (!w.ShowTerminal || w.IsCentreCovered) { w.Status = "终端栏没有显示，未关闭任何页签"; return; }
            if (w.ActiveTab?.CloseCommand is { } tab && tab.CanExecute(null)) tab.Execute(null);
            return;
        }
        closing = true;
        try
        {
            if (w.IsLaunchedSession && w.IsConnected && !await interaction.ConfirmAsync($"关闭“{w.Title}”？", "这个会话由控制器打开，临时凭据不能再次使用。\n关闭后要回到控制器重新发起。", "关闭会话", danger: true)) return;
            if (Workspaces.Contains(w) && w.CloseCommand?.CanExecute(null) == true) w.CloseCommand.Execute(null);
        }
        finally { closing = false; }
    }

    /// <summary>Cmd+T / Ctrl+Shift+T: another terminal of the session shown; a local one only where no session is.</summary>
    public void NewTerminal()
    {
        if (selected is not WorkspaceViewModel w) OpenLocalTerminalCommand.Execute(null);
        else if (w.CanAddTerminal) w.AddTerminalCommand.Execute(null);
        else w.Status = w.IsConnected ? "这个会话不能再开终端" : "连接不可用，不能新建终端";
    }

    /// <summary>Steps through the sessions, around the end.</summary>
    public void StepSession(int by)
    {
        var tabs = Tabs;
        if (tabs.Count == 0) return;
        int at = selected is WorkspaceViewModel w ? tabs.IndexOf(w) : by > 0 ? -1 : 0;
        Show(tabs[(at + by + tabs.Count) % tabs.Count]);
    }

    public void ShowSession(int number) { if (Tabs is var tabs && number >= 1 && number <= tabs.Count) Show(tabs[number - 1]); }

    /// <summary>The sessions that have a tab.</summary>
    List<WorkspaceViewModel> Tabs => Workspaces.Where(w => w.IsTabShown).ToList();

    /// <summary>0 = back to the default size.</summary>
    public void ZoomTerminal(int by) => SavePreferences(p => p with { TerminalFontSize = by == 0 ? new StudioPreferences().TerminalFontSize : p.TerminalFontSize + by });

    /// <summary>Changes the preferences, at once and on disk.</summary>
    public void SavePreferences(Func<StudioPreferences, StudioPreferences> change)
    {
        Appearance.Apply(change(Appearance.Current));
        try { Appearance.Save(settingsDirectory, Appearance.Current); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            Notice = "设置已生效，但没有保存到磁盘：" + WorkspaceViewModel.Short(e.Message);
        }
    }

    /// <summary>Opens the settings window at a category.</summary>
    public void OpenSettings(string section) => interaction.ShowSettings(new SettingsViewModel(settingsDirectory, QuickCommands, library, interaction, Access) { Section = section });

    public LibraryViewModel Home { get; }
    public AccountStore Accounts { get; }
    public QuickCommandStore QuickCommands { get; }
    public ObservableCollection<WorkspaceViewModel> Workspaces { get; } = new();

    public object Selected
    {
        get => selected;
        set
        {
            if (value is WorkspaceViewModel shown && !Workspaces.Contains(shown)) return; // closed meanwhile
            // Its files are shown in another session, and nothing else of it has a place.
            if (value is WorkspaceViewModel { IsTabShown: false, Viewer: { } viewer }) value = viewer;
            // Its terminal may be being laid out unseen ("标签渲染"): it is shown here now.
            if (value is WorkspaceViewModel next) TerminalWarmUp.Release(next.ActiveContent as Avalonia.Controls.Control);
            if (!Set(ref selected, value)) return;
            Palette.Dismiss();
            foreach (var workspace in Workspaces) workspace.IsActive = ReferenceEquals(workspace, value);
            Raise(nameof(IsHomeSelected));
        }
    }

    public bool IsHomeSelected => ReferenceEquals(selected, Home);
    public string Notice { get => notice; set { if (Set(ref notice, value)) Raise(nameof(HasNotice)); } }
    public bool HasNotice => Notice.Length > 0;

    public ICommand ShowHomeCommand { get; }
    public ICommand OpenLocalTerminalCommand { get; }
    public ICommand DismissNoticeCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    /// <summary>Handles one controller launch. Rejected arguments are reported, never
    /// ignored-and-continued; the password is cleared once the workspace has used it.</summary>
    /// <summary>A session the fort portal handed out ("堡垒机资产"): opened exactly as the vendor's client starts Xshell or
    /// Xftp for it, through the controller's own path — the sid as the user, two apostrophes as the password.</summary>
    internal async Task OpenBastionTicketAsync(Services.Bastion.FortTicket ticket, bool files, string title)
    {
        string host = ticket.Host.Contains(':') ? "[" + ticket.Host + "]" : ticket.Host;
        string url = (files ? "sftp" : "ssh") + "://" + ticket.Sid + ":''@" + host + ":" + ticket.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var plan = LaunchPlan.Parse(files ? ["--entry=xftp", "-newtab", title, "-url", url] : ["--entry=xshell", "-newtab", title, "-url", url]);
        if (plan.Error.Length > 0 || plan.Connection == null) throw new Services.Bastion.FortPortalException("堡垒机给出的会话无法打开：" + plan.Error);
        if (await AcceptLaunchAsync(plan) == null) throw new Services.Bastion.FortPortalException("会话没有打开");
    }

    public async Task<WorkspaceViewModel?> AcceptLaunchAsync(LaunchPlan plan)
    {
        var wake = plan.Entry is "xshell" or "xftp" && !plan.Diagnose ? Access.Began(plan.Entry, DateTimeOffset.Now) : null;
        if (plan.Error.Length > 0)
        {
            wake?.End("未建立连接：" + plan.Error, bad: true);
            Notice = "未建立连接：" + plan.Error + "。可使用 forterm-diagnose 查看脱敏参数报告。";
            if (plan.Connection != null) plan.Connection.Password = "";
            return null;
        }
        if (plan.Diagnose || plan.Connection is not { } connection)
        {
            wake?.End("没有带连接地址，只打开了主界面", bad: true);
            return null;
        }
        var workspace = WorkspaceViewModel.ForLaunch(LaunchRouter.Route(connection), connection, interaction, knownHosts, options);
        Add(workspace);
        try { await workspace.ConnectAsync(connection); }
        finally
        {
            connection.Password = "";
            wake?.End(workspace.IsConnected ? "已连接" : "已打开会话，但没有连上（原因见会话底部）", bad: !workspace.IsConnected);
        }
        return workspace;
    }

    /// <summary>Opens a saved connection in a new workspace tab. Use is recorded only after it connected.</summary>
    public async Task<WorkspaceViewModel> OpenProfileAsync(StudioProfile profile)
    {
        var workspace = WorkspaceViewModel.ForProfile(profile, connector, interaction, knownHosts);
        Add(workspace);
        await workspace.ConnectProfileAsync();
        if (ReferenceEquals(Selected, workspace)) FocusRequested?.Invoke(workspace);
        if (workspace.IsConnected && library.Find(profile.Id) != null)
        {
            try { library.RecordUse(profile.Id); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Notice = "最近使用记录未能保存：" + WorkspaceViewModel.Short(e.Message); }
        }
        return workspace;
    }

    async Task OpenLocalTerminalAsync()
    {
        var workspace = WorkspaceViewModel.ForLocal(interaction, knownHosts);
        Add(workspace);
        await workspace.StartLocalAsync();
        if (ReferenceEquals(Selected, workspace)) FocusRequested?.Invoke(workspace);
    }

    void Add(WorkspaceViewModel workspace)
    {
        workspace.QuickCommands = QuickCommands;
        workspace.ShowSettings = OpenSettings;
        workspace.SavePreferences = SavePreferences;
        workspace.Sessions = () => Workspaces;
        workspace.LinksChanged = () => { Palette.Dismiss(); Selected = Selected; };
        workspace.ActivateCommand = new RelayCommand(() => Show(workspace));
        workspace.CloseCommand = new AsyncCommand(async () =>
        {
            if (!await workspace.ConfirmCloseAsync()) return;
            await workspace.DrainTransfersAsync(TimeSpan.FromSeconds(5));
            bool shown = ReferenceEquals(Selected, workspace);
            Close(workspace);
            if (shown && Selected is WorkspaceViewModel next) FocusRequested?.Invoke(next);
        });
        Workspaces.Add(workspace);
        if (workspace.Monitor is { } monitor) monitor.Updated += () => Avalonia.Threading.Dispatcher.UIThread.Post(Home.RefreshInfo);
        Selected = workspace;
    }

    /// <summary>Closing the window: anything that would be lost (drafts, transfers, running tunnels)
    /// is confirmed first; transfers then get a bounded chance to clean up.</summary>
    public bool NeedsCloseConfirmation => Workspaces.Any(w => w.NeedsCloseConfirmation) || Home.Tunnels?.RunningCount > 0;

    public async Task<bool> ConfirmCloseAllAsync()
    {
        foreach (var workspace in Workspaces.ToList())
        {
            if (!workspace.NeedsCloseConfirmation) continue;
            Selected = workspace;
            if (!await workspace.ConfirmCloseAsync(all: true)) return false;
        }
        if (Home.Tunnels is { RunningCount: > 0 } tunnels &&
            !await interaction.ConfirmAsync("关闭窗口？", $"{tunnels.RunningCount} 个隧道正在运行，关闭窗口会停止它们。", "关闭", danger: false)) return false;
        var deadline = TimeSpan.FromSeconds(5);
        await Task.WhenAll(Workspaces.ToList().Select(w => w.DrainTransfersAsync(deadline)));
        return true;
    }

    public void Close(WorkspaceViewModel workspace)
    {
        int index = Workspaces.IndexOf(workspace);
        if (index < 0) return;
        workspace.EndLinks();
        Workspaces.RemoveAt(index);
        interaction.Moved();
        Avalonia.Threading.Dispatcher.UIThread.Post(Home.RefreshInfo);
        if (ReferenceEquals(Selected, workspace))
            Selected = Workspaces.Skip(index).Concat(Workspaces.Take(index).Reverse()).FirstOrDefault(w => w.IsTabShown) ?? (object)Home;
        workspace.Dispose();
    }

    public void Dispose()
    {
        idle.Stop();
        Home.Tunnels?.Dispose();
        Home.Dispose();
        foreach (var workspace in Workspaces) workspace.EndRecordings();
        foreach (var workspace in Workspaces.ToArray()) workspace.Dispose();
        Workspaces.Clear();
    }
}
