using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Forterm.Core;
using Iciclecreek.Terminal;
using Porta.Pty;
namespace Forterm.Desktop;

public sealed class SessionPane : Grid, IDisposable
{
    readonly Window owner; readonly ConnectionProfile? profile; readonly bool filesOnly; readonly IFilePickers pickers;
    readonly ProfileRepository? repository; readonly KnownHosts knownHosts; readonly ConnectionCoordinator? coordinator; readonly Func<UserPreferences> preferences;
    readonly FileActionController fileActions;
    readonly TransferQueue transfers; readonly bool ownsQueue; readonly CancellationTokenSource lifetime = new();
    SharedSshSession? session; SftpLease? sftp; FtpFileSession? ftp; LocalFileSource? localFiles; SshPtyConnection? shell; IPtyConnection? telnet; IDisposable? transportLease; CancellationTokenRegistration sourceClosed; int disposed;
    readonly string? localRoot;
    readonly TextBlock state = UI.Label("文件通道未连接；可关联已打开的文件来源", true), terminalState = UI.Label("", true);
    readonly MonitorPanel monitor; readonly RemoteEditorPanel editor;
    readonly ContentControl fileHost = new(); readonly ComboBox sourcePicker = new() { HorizontalAlignment = HorizontalAlignment.Stretch }; readonly Border sourceBar = new(); readonly Border fileEmptyState = new();
    readonly Dictionary<Guid, FileBrowserPanel> fileViews = new(); readonly Dictionary<Guid, string> sourceNames = new();
    readonly CheckBox showTerminal, showMonitor; readonly CapabilityLayout capabilityLayout;
    public WorkspaceLayout CurrentLayout => capabilityLayout.Value;
    public event Action<WorkspaceLayout>? LayoutChanged;
    public void ToggleCapability(int index) { var control = new[] { showTerminal, showFiles, showEditor, showMonitor }[index]; if (control.IsEnabled) control.IsChecked = control.IsChecked != true; }
    readonly CheckBox showEditor = new() { Content = "在线编辑", IsChecked = false }, showFiles = new() { Content = "文件", IsChecked = true };
    readonly Button detach, associateEmpty; bool renderingSources; bool applyingLayout; WorkspaceLayout? expandedLayout; Action<bool>? applyLayout;
    public Guid WorkspaceId { get; } = Guid.NewGuid();
    public ConnectionProfile? Profile => profile; public bool FilesOnly => filesOnly; public string SessionTitle => profile?.Name.Length > 0 ? profile.Name : profile?.Host ?? "本地终端";
    public bool HasUsableConnection => ftp?.IsAvailable == true || telnet != null || session?.IsConnected == true && (shell != null || sftp?.IsAvailable == true);
    public bool HasFileCapability => disposed == 0 && (localFiles?.IsAvailable == true || sftp?.IsAvailable == true || ftp?.IsAvailable == true);
    public Guid ActiveFileSource { get; private set; }
    public IReadOnlyList<Guid> AssociatedSources => fileViews.Keys.ToArray();
    public event Action<SessionPane>? FileSourceReady, AssociationRequested, CloneRequested;
    public event Action<SessionPane, Guid>? DetachRequested, SourceSelected;
    public TerminalControl Terminal { get; } = CreateTerminal();
    public static TerminalControl CreateTerminal() => new WorkspaceTerminalControl() { AutoStartOnLoad = false, Process = "", FontSize = 14, AllowWindowOps = false, BufferSize = 3000, Options = new XTerm.Options.TerminalOptions { ReflowOnResize = false, ClipboardReadEnabled = false, ClipboardWriteEnabled = false, KittyNotificationsEnabled = false, SixelEnabled = false, KittyGraphicsEnabled = false } };
    public Task<bool> ConfirmCloseAsync() => editor.ConfirmCloseAsync();
    public SessionPane(Window owner, ConnectionProfile? profile, bool filesOnly = false, TransferQueue? transfers = null, IFilePickers? pickers = null, ProfileRepository? repository = null, KnownHosts? knownHosts = null, ConnectionCoordinator? coordinator = null, Func<UserPreferences>? preferences = null, FileActionController? fileActions = null, string? localRoot = null)
    {
        this.owner = owner; this.profile = profile; this.filesOnly = filesOnly; this.localRoot = localRoot; this.pickers = pickers ?? new NativeFilePickers(); this.repository = repository; this.knownHosts = knownHosts ?? new(null); this.coordinator = coordinator; this.preferences = preferences ?? (() => new UserPreferences()); this.transfers = transfers ?? new TransferQueue(); ownsQueue = transfers == null; this.fileActions = fileActions ?? new FileActionController(owner, this.transfers);
        DropDownMotion.Attach(sourcePicker);
        sourcePicker.Height = sourcePicker.MinHeight = 30; sourcePicker.FontSize = 12;
        monitor = new MonitorPanel(() => session) { IsVisible = false }; editor = new RemoteEditorPanel(owner, this.transfers) { IsVisible = false };
        RowDefinitions = new RowDefinitions("*");
        ColumnDefinitions = new ColumnDefinitions("40,*");
        var initial = this.preferences().Workspace;
        showTerminal = new CheckBox { Content = "终端", IsChecked = !filesOnly && initial.TerminalVisible, IsEnabled = profile?.Protocol != "ftp" };
        showMonitor = new CheckBox { Content = "资源监控", IsChecked = profile?.Protocol != "ftp" && initial.MonitorVisible, IsEnabled = profile?.Protocol != "ftp" };
        showFiles.IsChecked = true;
        showEditor.IsChecked = initial.EditorVisible;
        var rail = new StackPanel { Classes = { "workspace-icon-rail" } };
        foreach (var control in new[] { showFiles, showTerminal, showEditor, showMonitor })
        {
            control.Classes.Add("capability-toggle");
            ToolTip.SetTip(control, control.Content);
            Avalonia.Automation.AutomationProperties.SetName(control, control.Content?.ToString());
            rail.Children.Add(control);
        }
        var railSurface = new Border { Classes = { "activity-rail" }, Child = rail };
        Children.Add(railSurface);
        var strip = new Grid { ColumnDefinitions = new ColumnDefinitions("*,5,2*,5,*") };
        var workspaceViewport = new ScrollViewer { Content = strip, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Avalonia.Automation.AutomationProperties.SetName(workspaceViewport, "会话工作区视口");
        Grid.SetColumn(workspaceViewport, 1); Children.Add(workspaceViewport);
        var body = new Grid(); Grid.SetColumn(body, 2); strip.Children.Add(body);
        terminalState.FontSize = 10;
        var terminalPane = new DockPanel { Classes = { "workspace-pane" }, Margin = new Thickness(4) };
        var terminalHeader = WorkspaceChrome.Header("终端", () => showTerminal.IsChecked = false, () => Maximize(0), () => CloneRequested?.Invoke(this));
        DockPanel.SetDock(terminalHeader, Dock.Top); terminalPane.Children.Add(terminalHeader);
        DockPanel.SetDock(terminalState, Dock.Bottom); terminalPane.Children.Add(terminalState);
        terminalPane.Children.Add(Terminal); body.Children.Add(terminalPane);
        var firstSplit = new GridSplitter { ResizeDirection = GridResizeDirection.Columns }; Grid.SetColumn(firstSplit, 1); strip.Children.Add(firstSplit);
        var filePane = new DockPanel { Classes = { "workspace-pane" }, Margin = new Thickness(4) }; Grid.SetColumn(filePane, 0); strip.Children.Add(filePane);
        var filesHeader = WorkspaceChrome.Header("文件工作区", () => showFiles.IsChecked = false, () => Maximize(1), () => AssociationRequested?.Invoke(this));
        DockPanel.SetDock(filesHeader, Dock.Top); filePane.Children.Add(filesHeader);
        sourceBar.Classes.Add("file-source-toolbar"); sourceBar.Margin = new Thickness(8, 0, 8, 7); sourceBar.Padding = new Thickness(7, 4); sourceBar.IsVisible = false;
        var sourceGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,30,30") };
        sourceGrid.Children.Add(new TextBlock { Text = "文件来源", Classes = { "toolbar-caption" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 9, 0) });
        sourcePicker.Classes.Add("source-picker"); Grid.SetColumn(sourcePicker, 1); sourceGrid.Children.Add(sourcePicker);
        var associate = UI.Button("+", () => AssociationRequested?.Invoke(this)); associate.Classes.Add("icon"); Avalonia.Automation.AutomationProperties.SetName(associate, "关联文件来源"); ToolTip.SetTip(associate, "明确关联已打开的文件来源，不改变当前终端或重新认证"); Grid.SetColumn(associate, 2); sourceGrid.Children.Add(associate);
        detach = UI.Button("×", () => { if (ActiveFileSource != WorkspaceId) DetachRequested?.Invoke(this, ActiveFileSource); }); detach.Classes.Add("icon"); ToolTip.SetTip(detach, "解除此关联；原来源、已打开草稿和队列保留"); Grid.SetColumn(detach, 3); sourceGrid.Children.Add(detach);
        sourceBar.Child = sourceGrid;
        sourcePicker.SelectionChanged += (_, _) => { if (!renderingSources && sourcePicker.SelectedItem is SourceChoice item) { ShowFileSource(item.Id); SourceSelected?.Invoke(this, item.Id); } };
        DockPanel.SetDock(sourceBar, Dock.Top); filePane.Children.Add(sourceBar);
        var emptyBody = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        emptyBody.Children.Add(new TextBlock { Text = "▱", Classes = { "file-empty-icon" }, HorizontalAlignment = HorizontalAlignment.Center });
        emptyBody.Children.Add(new TextBlock { Text = "文件浏览", Classes = { "file-empty-title" }, HorizontalAlignment = HorizontalAlignment.Center });
        emptyBody.Children.Add(state);
        associateEmpty = UI.Button("关联已打开的文件会话", () => AssociationRequested?.Invoke(this)); associateEmpty.IsVisible = profile == null; associateEmpty.Classes.Add("empty-action"); emptyBody.Children.Add(associateEmpty);
        fileEmptyState.Classes.Add("file-empty-state"); fileEmptyState.Child = emptyBody; fileHost.Content = fileEmptyState;
        filePane.Children.Add(fileHost);
        var editorHeader = WorkspaceChrome.Header("在线编辑", () => showEditor.IsChecked = false, () => Maximize(2));
        DockPanel.SetDock(editorHeader, Dock.Top); editor.Children.Insert(0, editorHeader);
        var secondSplit = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, IsVisible = false }; Grid.SetColumn(secondSplit, 3); body.Children.Add(secondSplit); Grid.SetColumn(editor, 4); body.Children.Add(editor);
        var monitorSplit = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, IsVisible = false }; Grid.SetColumn(monitorSplit, 3); strip.Children.Add(monitorSplit);
        var monitorHeader = WorkspaceChrome.Header("监控", () => showMonitor.IsChecked = false, () => Maximize(3));
        DockPanel.SetDock(monitorHeader, Dock.Top); monitor.Children.Insert(0, monitorHeader);
        var monitorViewport = new Border { Classes = { "monitor-surface" }, Margin = new Thickness(4), Child = monitor }; Grid.SetColumn(monitorViewport, 4); strip.Children.Add(monitorViewport);
        capabilityLayout = new(body, strip, terminalPane, filePane, editor, firstSplit, secondSplit, monitorSplit, initial, value => LayoutChanged?.Invoke(value));
        workspaceViewport.SizeChanged += (_, _) => capabilityLayout.ResizeTo(workspaceViewport.Bounds.Width);
        var placement = new ComboBox { Classes = { "placement-picker" }, ItemsSource = new[] { "↔  左右并排", "◀  编辑器置左", "↕  编辑器置底" }, SelectedIndex = initial.Placement == "editorLeft" ? 1 : initial.Placement == "editorBottom" ? 2 : 0, MinWidth = 100, Height = 28, Margin = new Thickness(8, 0, 0, 0) };
        DropDownMotion.Attach(placement);
        Avalonia.Automation.AutomationProperties.SetName(placement, "工作区排列"); DockPanel.SetDock(placement, Dock.Top); editor.Children.Insert(1, placement);
        void LayoutCapabilities(bool persist = true)
        {
            if (applyingLayout) return;
            bool t = showTerminal.IsChecked == true, m = showMonitor.IsChecked == true;
            Terminal.IsVisible = t; monitor.IsVisible = monitorSplit.IsVisible = m;
            capabilityLayout.Set(capabilityLayout.Value with { Placement = placement.SelectedIndex == 1 ? "editorLeft" : placement.SelectedIndex == 2 ? "editorBottom" : "columns", TerminalVisible = t, FilesVisible = showFiles.IsChecked == true, EditorVisible = showEditor.IsChecked == true, MonitorVisible = m }, persist);
            if (t && shell == null && session?.IsConnected == true) _ = OpenTerminalOnDemand();
        }
        applyLayout = LayoutCapabilities;
        placement.SelectionChanged += (_, _) => LayoutCapabilities();
        showTerminal.IsCheckedChanged += (_, _) => LayoutCapabilities(); showFiles.IsCheckedChanged += (_, _) => LayoutCapabilities(); showEditor.IsCheckedChanged += (_, _) => LayoutCapabilities(); showMonitor.IsCheckedChanged += (_, _) => LayoutCapabilities(); LayoutCapabilities(false); RenderSources();
    }
    void Maximize(int index)
    {
        var restore = expandedLayout;
        expandedLayout = restore == null ? CurrentLayout : null;
        var layout = restore ?? CurrentLayout with
        {
            TerminalVisible = index == 0, FilesVisible = index == 1,
            EditorVisible = index == 2, MonitorVisible = index == 3
        };
        applyingLayout = true;
        showTerminal.IsChecked = layout.TerminalVisible;
        showFiles.IsChecked = layout.FilesVisible;
        showEditor.IsChecked = layout.EditorVisible;
        showMonitor.IsChecked = layout.MonitorVisible;
        applyingLayout = false;
        applyLayout?.Invoke(false);
    }
    RemoteFilesView CreateSftpView(SftpLease source, string title) => new(owner, title, source, transfers, pickers,
        async (directory, name, ct) => { showEditor.IsChecked = true; await editor.OpenAsync(directory, name, ct, title); },
        async (directory, ct) => { showEditor.IsChecked = true; await editor.NewAsync(directory, ct, title); }, fileActions);
    FileBrowserPanel CreateFilesView(SessionPane source)
    {
        string title = source.SessionTitle;
        if (source.profile == null) return new LocalFilesView(owner, title, source.localFiles ?? throw new IOException("本机文件来源未初始化"));
        if (source.ftp != null) return new FtpFilesView(owner, title, source.ftp, transfers, pickers,
            async (directory, name, ct) => { showEditor.IsChecked = true; await editor.OpenFtpAsync(directory, name, ct, title); },
            async (directory, ct) => { showEditor.IsChecked = true; await editor.NewFtpAsync(directory, ct, title); }, fileActions);
        return CreateSftpView(source.sftp ?? throw new IOException("文件通道未连接"), title);
    }
    void InstallOwnFiles()
    {
        fileViews.Add(WorkspaceId, CreateFilesView(this)); sourceNames[WorkspaceId] = SessionTitle; ShowFileSource(WorkspaceId);
    }
    void InstallLocalFiles()
    {
        if (localFiles != null) return;
        string root = localRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        localFiles = new LocalFileSource(root);
        fileViews.Add(WorkspaceId, CreateFilesView(this));
        sourceNames[WorkspaceId] = "本机";
        associateEmpty.IsVisible = false;
        ShowFileSource(WorkspaceId);
        ObserveFiles(localFiles);
    }
    void ObserveFiles(IRemoteFileSource source)
    {
        sourceClosed = source.Closed.Register(() => Dispatcher.UIThread.Post(() => { if (disposed == 0) { RefreshSourceLabels(); FileSourceReady?.Invoke(this); } })); FileSourceReady?.Invoke(this);
    }
    public void AttachFileSource(SessionPane source)
    {
        if (disposed != 0 || !source.HasFileCapability) throw new IOException("原文件来源不可用");
        if (!fileViews.ContainsKey(source.WorkspaceId)) { if (fileViews.Count >= 32) throw new IOException("最多32个文件来源"); fileViews.Add(source.WorkspaceId, CreateFilesView(source)); sourceNames[source.WorkspaceId] = source.SessionTitle; }
        showFiles.IsChecked = true; ShowFileSource(source.WorkspaceId);
        var view = fileViews[source.WorkspaceId]; _ = RefreshAttached(view);
    }
    async Task RefreshAttached(FileBrowserPanel view) { try { await view.RefreshAsync(lifetime.Token); } catch (Exception e) { if (disposed == 0) terminalState.Text = "文件来源刷新失败：" + e.Message; } }
    public void ShowFileSource(Guid source)
    {
        if (!fileViews.TryGetValue(source, out var view)) return; ActiveFileSource = source; fileHost.Content = view; RenderSources();
    }
    public void DetachFileSource(Guid source)
    {
        if (source == WorkspaceId || !fileViews.Remove(source, out var view)) return; view.Dispose(); sourceNames.Remove(source);
        if (ActiveFileSource == source) { if (fileViews.Count > 0) ShowFileSource(fileViews.Keys.Last()); else { ActiveFileSource = Guid.Empty; fileHost.Content = fileEmptyState; RenderSources(); } }
        else RenderSources();
    }
    public void RefreshSourceLabels() => RenderSources();
    void RenderSources()
    {
        renderingSources = true; try { var items = fileViews.Select(p => new SourceChoice(p.Key, sourceNames[p.Key] + (p.Value.SourceHandle.IsAvailable ? "" : " · 已离线"))).ToArray(); sourceBar.IsVisible = items.Length > 0; sourcePicker.ItemsSource = items; sourcePicker.SelectedItem = items.FirstOrDefault(p => p.Id == ActiveFileSource); sourcePicker.IsEnabled = items.Length > 0; detach.IsEnabled = ActiveFileSource != Guid.Empty && ActiveFileSource != WorkspaceId; } finally { renderingSources = false; }
    }
    sealed record SourceChoice(Guid Id, string Title) { public override string ToString() => Title; }
    bool openingTerminal;
    async Task OpenTerminalOnDemand()
    {
        if (openingTerminal || shell != null || session == null || disposed != 0) return; openingTerminal = true;
        try { shell = await session.OpenShellAsync(100, 30, lifetime.Token); shell.Faulted += TerminalFault; Terminal.AttachConnection(shell); terminalState.Text = "终端与其他能力复用同一认证"; }
        catch (Exception e) { terminalState.Text = "终端通道不可用：" + e.Message; }
        finally { openingTerminal = false; }
    }
    public async Task StartLocalAsync()
    {
        try
        {
            InstallLocalFiles();
            await fileViews[WorkspaceId].RefreshAsync(lifetime.Token);
            Terminal.Process = OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/zsh";
            await Terminal.LaunchProcess();
            showMonitor.IsChecked = showMonitor.IsEnabled;
            await monitor.RefreshAsync();
            state.Text = "本地终端 · 本机文件已载入";
        }
        catch (Exception e) { state.Text = e.Message; }
    }
    public async Task ConnectAsync(string password, bool temporary = false, string keyPath = "", string passphrase = "")
    {
        if (profile == null) return;
        if (session != null || ftp != null) { state.Text = "此会话已建立过连接；不会自动重新认证，请明确新建会话。"; return; }
        try
        {
            if (profile.Protocol is not ("ssh" or "sftp" or "ftp" or "telnet")) throw new IOException("不支持的连接协议");
            if (profile.Protocol == "telnet")
            {
                state.Text = "正在建立Telnet连接…"; telnet = await TelnetPtyConnection.ConnectAsync(profile.Host, profile.Port, lifetime.Token); Terminal.AttachConnection(telnet); state.Text = "Telnet已连接；文件与资源监控需要SSH会话"; return;
            }
            if (profile.Protocol == "ftp")
            {
                if (profile.AuthMode != "password" || profile.JumpId.Length > 0 || keyPath.Length > 0 || passphrase.Length > 0) throw new IOException("FTP仅支持明确账号密码；不能忽略SSH跳板设置直连");
                if (!temporary && password.Length == 0 && profile.HasPassword && repository != null) password = await repository.PasswordAsync(profile, lifetime.Token) ?? throw new IOException("系统密码不可用，请输入本次密码");
                state.Text = "正在建立FTP连接…"; ftp = await FtpFileSession.ConnectAsync(profile.Host, profile.Port, profile.User, password, lifetime.Token, preferences().SessionOptions());
                InstallOwnFiles(); ObserveFiles(ftp); await fileViews[WorkspaceId].RefreshAsync(lifetime.Token); state.Text = "FTP已连接；终端和监控需关联到已有SSH工作区"; return;
            }
            state.Text = "正在建立SSH连接…";
            if (coordinator != null) session = await coordinator.ConnectAsync(profile, password, passphrase, temporary, lifetime.Token, keyPath);
            else
            {
                string mode = keyPath.Length > 0 ? "key" : profile.AuthMode;
                if ((mode is "password" or "keyboard") && !temporary && password.Length == 0 && profile.HasPassword && repository != null) password = await repository.PasswordAsync(profile, lifetime.Token) ?? throw new IOException("系统密码不可用，请重新输入");
                if (mode == "key" && !temporary && passphrase.Length == 0 && profile.HasPassphrase && repository != null) passphrase = await repository.PassphraseAsync(profile, lifetime.Token) ?? throw new IOException("系统私钥口令不可用，请重新输入");
                async Task<SharedSshSession> Connect() => await SharedSshSession.ConnectAsync(profile.Host, profile.Port, profile.User, password, (fp, ct) => UI.VerifyFingerprint(owner, knownHosts, profile.Host, profile.Port, fp, true, ct), lifetime.Token, temporary, (q, ct) => Dispatcher.UIThread.InvokeAsync(() => UI.AskCredentials(owner, q, ct)), mode, keyPath, passphrase);
                try { session = await Connect(); }
                catch (Renci.SshNet.Common.SshPassPhraseNullOrEmptyException)
                {
                    var answer = await UI.AskCredentials(owner, new CredentialChallenge(1, false, temporary, "私钥已加密；请输入独立口令，不自动使用登录密码或临时票据。", new[] { new CredentialQuestion("私钥口令", false) }), lifetime.Token); if (answer == null) return; passphrase = answer[0]; Array.Clear(answer); session = await Connect();
                }
            }
            ObserveTransport(); await OpenCapabilities();
        }
        catch (Exception e) { state.Text = "连接失败：" + e.Message; associateEmpty.IsVisible = true; fileHost.Content = fileEmptyState; }
        finally { password = ""; passphrase = ""; }
    }
    public void ApplyPreferences(UserPreferences value) { Terminal.FontSize = value.TerminalFontSize; }
    public void FocusTerminal() { if (Terminal.IsVisible && Terminal.IsLive) Terminal.Focus(); }
    void ObserveTransport() { session!.Faulted += TransportFault; if (session.CloseReason != null) TransportFault(session.CloseReason); }
    void TransportFault(string message) => Dispatcher.UIThread.Post(() => { if (disposed == 0) { terminalState.Text = message; state.Text = message; } });
    public async Task CloneFromAsync(SessionPane source)
    {
        if (source.profile == null) { await StartLocalAsync(); return; }
        if (source.ftp != null) { ftp = source.ftp.CreateSibling(); InstallOwnFiles(); ObserveFiles(ftp); await fileViews[WorkspaceId].RefreshAsync(lifetime.Token); return; }
        var root = source.session ?? throw new IOException("原连接尚未认证"); transportLease = root.Retain(); session = root; ObserveTransport(); await OpenCapabilities();
    }
    async Task OpenCapabilities()
    {
        var live = session ?? throw new IOException("SSH transport is not available");
        // Shell and SFTP failures are displayed independently, never dispose owner.
        if (Terminal.IsVisible) { await OpenTerminalOnDemand(); if (!filesOnly && showMonitor.IsEnabled) { showMonitor.IsChecked = true; _ = monitor.RefreshAsync(); } }
        try { sftp = await live.OpenFilesAsync(lifetime.Token); var bound = sftp; InstallOwnFiles(); ObserveFiles(bound); await fileViews[WorkspaceId].RefreshAsync(lifetime.Token); state.Text = filesOnly ? "SFTP可用；按文件协议入口启动" : shell == null ? "SFTP可用；终端通道不可用" : "同一SSH连接：终端 + SFTP"; } catch (Exception e) { state.Text = "文件列表读取失败：" + e.Message; associateEmpty.IsVisible = true; fileHost.Content = fileEmptyState; }
    }
    void TerminalFault(string message) => Dispatcher.UIThread.Post(() => { if (disposed == 0) terminalState.Text = message; });
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return; lifetime.Cancel(); sourceClosed.Dispose();
        foreach (var view in fileViews.Values) view.Dispose(); fileViews.Clear(); monitor.Dispose(); editor.Dispose(); Terminal.EndReparent(); Terminal.Dispose();
        if (shell != null) shell.Faulted -= TerminalFault; shell?.Dispose(); telnet?.Dispose(); sftp?.Dispose(); ftp?.Dispose(); if (session != null) session.Faulted -= TransportFault;
        if (transportLease != null) transportLease.Dispose(); else session?.Dispose(); localFiles?.Dispose(); if (ownsQueue) transfers.Dispose(); lifetime.Dispose();
    }
}
