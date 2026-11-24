using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Controls.Templates;
using Forterm.Core;
using Iciclecreek.Terminal;
using Renci.SshNet.Sftp;

namespace Forterm.Desktop;

public sealed class MainWindow : Window
{
    readonly ContentControl host = new(); readonly TextBlock status = new() { Text = "就绪", Classes = { "note" } };
    readonly ListBox connections = new(); readonly ObservableCollection<ConnectionProfile> profiles = new(); readonly List<SessionPane> sessions = new(); readonly TabControl tabs = new(); readonly TransferQueue transferQueue = new();
    readonly IFilePickers? filePickers; readonly FileActionController fileActions; readonly AccountRepository accounts;
    readonly WorkspaceAssociations associations = new(); readonly Dictionary<Guid, TabItem> workspaceTabs = new();
    public IReadOnlyList<SessionPane> Workspaces => sessions.ToArray();
    public IReadOnlyList<Guid> VisibleWorkspaceIds => associations.Visible().Select(e => e.Id).ToArray();
    readonly ProfileStore store; readonly ProfileRepository repository; readonly KnownHosts knownHosts; readonly PreferenceStore preferenceStore; readonly ConnectionCoordinator coordinator;
    UserPreferences preferences = new(); readonly Grid chrome = new() { ColumnDefinitions = new ColumnDefinitions("280,5,*") }; readonly DockPanel navigation = new() { Margin = new Thickness(12) }; readonly GridSplitter divider = new() { ResizeDirection = GridResizeDirection.Columns }; readonly TextBox search = new() { PlaceholderText = "搜索连接 / 分组", Margin = new Thickness(0, 0, 0, 12) }; readonly TreeView groups = new();
    Control? libraryView; string? selectedGroup; bool navigationVisible = true; long navigationEpoch; string librarySort = "name"; bool librarySortDescending; string libraryProtocol = "";
    readonly List<Button> protocolModes = new();
    readonly List<Button> topNavigationButtons = new(); readonly Dictionary<string, Button> topNavigationByText = new(); Button? activeTopNavigation;
    public MainWindow() : this(ProfileStore.DefaultDirectory) { }
    public MainWindow(string? directory, ISecretVault? vault = null, IFilePickers? filePickers = null)
    {
        this.filePickers = filePickers; fileActions = new(this, transferQueue); tabs.Padding = new Thickness(0); tabs.Classes.Add("workspace-tabs"); AutomationProperties.SetName(tabs, "会话标签"); store = new(directory); var secrets = vault ?? (directory == null ? new UnavailableVault() : new OsCredentialVault(directory)); repository = new(store, secrets); accounts = new(directory, secrets); knownHosts = new(directory); preferenceStore = new(directory);
        try { preferences = preferenceStore.Load(); foreach (var p in store.Load()) profiles.Add(p); } catch (Exception e) { status.Text = "数据未加载：" + e.Message; }
        coordinator = new(this, store, repository, knownHosts, () => preferences); Title = "Forterm · 0.3.0-alpha.36"; Width = 1150; Height = 760; MinWidth = 700; MinHeight = 433; ApplyAppearance();
        var top = new DockPanel { Classes = { "top-toolbar" }, Margin = new Thickness(10, 4) }; var brand = new TextBlock { Text = "Forterm", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#49BA7A"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) }; DockPanel.SetDock(brand, Dock.Left); top.Children.Add(brand);
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; var theme = UI.Button("◐", ToggleTheme); theme.Classes.Add("icon"); AutomationProperties.SetName(theme, "切换主题"); ToolTip.SetTip(theme, "切换深浅主题"); options.Children.Add(theme); var settings = UI.Button("⚙", () => new PreferencesWindow(preferences, preferenceStore, ApplyPreferences).ShowDialog(this)); settings.Classes.Add("icon"); AutomationProperties.SetName(settings, "偏好设置"); ToolTip.SetTip(settings, "偏好、字号与协议心跳"); options.Children.Add(settings); var about = UI.Button("ⓘ", () => new AboutWindow().ShowDialog(this)); about.Classes.Add("icon"); AutomationProperties.SetName(about, "关于与验证说明"); ToolTip.SetTip(about, "关于 Forterm / 版本与验证说明"); options.Children.Add(about); DockPanel.SetDock(options, Dock.Right); top.Children.Add(options);
        var tools = new WrapPanel { Classes = { "top-navigation" } }; foreach (var item in new (string Text, Action Run)[] { ("连接库", ShowLibrary), ("新建", () => Edit()), ("本地终端", OpenLocal), ("验证器", () => new AccountsWindow(accounts, filePickers).ShowDialog(this)), ("导航", ManualNavigation), ("工作区", ShowWorkspace), ("快捷键", () => WorkspaceShortcuts.ShowHelp(this)) }) { var button = TopNavigationButton(item.Text, item.Run, item.Text == "连接库"); tools.Children.Add(button); }
        top.Children.Add(new Border { Classes = { "top-nav-island" }, Child = tools });
        DockPanel.SetDock(search, Dock.Top); navigation.Children.Add(search); navigation.Children.Add(groups); chrome.Children.Add(navigation); Grid.SetColumn(divider, 1); chrome.Children.Add(divider); Grid.SetColumn(host, 2); chrome.Children.Add(host);
        divider.DragStarted += (_, _) => Interlocked.Increment(ref navigationEpoch); divider.DragCompleted += (_, _) => { preferences = preferences with { NavigationWidth = Math.Clamp(chrome.ColumnDefinitions[0].ActualWidth, 180, 420) }; PersistPreferences(); };
        chrome.SizeChanged += (_, _) => SetNavigation(navigationVisible); search.TextChanged += (_, _) => RefreshRows(); groups.SelectionChanged += (_, _) => { if ((groups.SelectedItem as TreeViewItem)?.Tag is ConnectionProfile selected)
            {
                connections.SelectedItem = profiles.FirstOrDefault(item => item.Id == selected.Id);
            }
            else { selectedGroup = (groups.SelectedItem as TreeViewItem)?.Tag as string; RefreshRows(); } };
        var root = new DockPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top); var bottom = new Border { Padding = new Thickness(12, 6), BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Brush.Parse("#363639"), Child = status }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom); root.Children.Add(chrome); Content = root;
        connections.Background = Brushes.Transparent; AutomationProperties.SetName(connections, "连接列表"); connections.Classes.Add("connection-list"); connections.ItemTemplate = new FuncDataTemplate<ConnectionProfile>((p, _) =>
        {
            if (p == null) return new Border();
            var row = new Grid { ColumnDefinitions = LibraryColumns(), Margin = new Thickness(10, 0), Height = 44 };
            var favorite = UI.Button(p.Favorite ? "★" : "☆", () => ToggleFavorite(p));
            favorite.Classes.Add("icon"); favorite.Classes.Add("connection-favorite");
            AutomationProperties.SetName(favorite, p.Favorite ? "取消收藏 " + p.Name : "收藏 " + p.Name); row.Children.Add(favorite);
            var protocol = new Border { Classes = { "protocol-badge" }, VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = p.Protocol.ToUpperInvariant(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            Grid.SetColumn(protocol, 1); row.Children.Add(protocol);
            var label = new TextBlock { Text = p.Name.Length == 0 ? p.Host : p.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
            ToolTip.SetTip(label, p.Group.Length == 0 ? "默认分组" : p.Group); Grid.SetColumn(label, 2); row.Children.Add(label);
            var address = new TextBlock { Text = $"{p.User}@{p.Host}:{p.Port}", FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
            ToolTip.SetTip(address, address.Text); Grid.SetColumn(address, 3); row.Children.Add(address);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            var connect = UI.Button("连接", () => ConnectProfile(p)); connect.Classes.Add("connection-action"); connect.Classes.Add("connection-connect"); AutomationProperties.SetName(connect, "连接 " + p.Name); actions.Children.Add(connect);
            var edit = UI.Button("编辑", () => Edit(p)); edit.Classes.Add("connection-action"); AutomationProperties.SetName(edit, "编辑 " + p.Name); actions.Children.Add(edit);
            var more = UI.Button("⋯", () => { }); more.Classes.Add("icon"); ToolTip.SetTip(more, "更多操作");
            var menu = new ContextMenu { ItemsSource = new[]
            {
                new MenuItem { Header = "连接", Command = new ActionCommand(() => ConnectProfile(p)) },
                new MenuItem { Header = "编辑", Command = new ActionCommand(() => Edit(p)) },
                new MenuItem { Header = "删除", Command = new ActionCommand(() => DeleteProfile(p)) }
            } };
            more.Click += (_, _) => menu.Open(more); more.ContextMenu = menu; actions.Children.Add(more);
            Grid.SetColumn(actions, 4); row.Children.Add(actions);
            return row;
        }); connections.DoubleTapped += (_, _) => { if (connections.SelectedItem is ConnectionProfile p) Edit(p); };
        groups.Classes.Add("connection-groups"); AutomationProperties.SetName(groups, "连接分组"); RefreshNavigation(); host.Content = Library(); SetNavigation(preferences.NavigationVisible); tabs.SelectionChanged += (_, e) => { if (e.Source == tabs && tabs.SelectedItem is TabItem { Content: SessionPane p }) p.FocusTerminal(); };
        WorkspaceShortcuts.Install(this, key =>
        {
            var active = tabs.SelectedItem is TabItem { Content: SessionPane selected } ? selected : null;
            switch (key)
            {
                case Key.B: ManualNavigation(); break;
                case Key.L: ShowLibrary(); break;
                case Key.N: Edit(); break;
                case Key.T: if (active != null) Clone(active, active.SessionTitle); break;
                case Key.W: if (active != null && ReferenceEquals(host.Content, tabs)) _ = CloseWorkspaceAsync(active); break;
                case Key.PageUp: case Key.PageDown:
                    if (tabs.Items.Count > 0) { ShowWorkspace(); tabs.SelectedIndex = (tabs.SelectedIndex + (key == Key.PageDown ? 1 : tabs.Items.Count - 1)) % tabs.Items.Count; } break;
                case Key.D1: case Key.D2: case Key.D3: case Key.D4:
                    if (active != null && ReferenceEquals(host.Content, tabs)) active.ToggleCapability((int)key - (int)Key.D1); break;
                case Key.F1: WorkspaceShortcuts.ShowHelp(this); break;
            }
        });
        bool allowClose = false, checkingClose = false;
        Closing += async (_, args) => { if (allowClose) return; args.Cancel = true; if (checkingClose) return; checkingClose = true; try { foreach (var pane in sessions.ToArray()) if (!await pane.ConfirmCloseAsync()) return; allowClose = true; Close(); } finally { checkingClose = false; } };
        Closed += (_, _) => { foreach (var p in sessions) p.Dispose(); sessions.Clear(); tabs.Items.Clear(); host.Content = null; transferQueue.Dispose(); };
    }
    void ApplyAppearance() { Application.Current!.RequestedThemeVariant = preferences.Appearance == "dark" ? Avalonia.Styling.ThemeVariant.Dark : preferences.Appearance == "light" ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Default; FontSize = preferences.UiFontSize; }
    Button TopNavigationButton(string text, Action run, bool active = false)
    {
        Button? button = null;
        button = UI.Button(text, run);
        if (text == "工作区") button.IsEnabled = false;
        button.Classes.Add("top-nav");
        AutomationProperties.SetName(button, text);
        topNavigationButtons.Add(button);
        topNavigationByText[text] = button;
        if (active) SetTopNavigation(button);
        return button;
    }
    void SetTopNavigation(string text) { if (topNavigationByText.TryGetValue(text, out var button)) SetTopNavigation(button); }
    void SetTopNavigation(Button button)
    {
        foreach (var item in topNavigationButtons) item.Classes.Remove("active");
        button.Classes.Add("active");
        activeTopNavigation = button;
    }
    void ApplyPreferences(UserPreferences value) { preferences = value; ApplyAppearance(); foreach (var p in sessions) p.ApplyPreferences(value); SetNavigation(navigationVisible); }
    void PersistPreferences() { try { preferenceStore.Save(preferences); } catch (Exception e) { status.Text = "偏好保存失败：" + e.Message; } }
    void ToggleTheme() { preferences = preferences with { Appearance = Application.Current!.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? "light" : "dark" }; ApplyPreferences(preferences); PersistPreferences(); }
    void SetNavigation(bool visible) { navigationVisible = visible; navigation.IsVisible = divider.IsVisible = visible; double width = Math.Min(preferences.NavigationWidth, chrome.Bounds.Width < 1000 ? Math.Max(120, chrome.Bounds.Width * .18) : Math.Max(180, chrome.Bounds.Width * .35)); chrome.ColumnDefinitions[0].Width = new GridLength(visible ? width : 0); chrome.ColumnDefinitions[1].Width = new GridLength(visible ? 5 : 0); }
    void ManualNavigation() { Interlocked.Increment(ref navigationEpoch); SetNavigation(!navigationVisible); preferences = preferences with { NavigationVisible = navigationVisible }; PersistPreferences(); }
    void ShowLibrary() { SetTopNavigation("连接库"); host.Content = Library(); SetNavigation(true); }
    void ShowWorkspace() { if (tabs.Items.Count == 0) return; SetTopNavigation("工作区"); host.Content = tabs; SetNavigation(false); }
    void RefreshNavigation()
    {
        var root = new TreeViewItem { Header = NavigationHeader("全部连接", profiles.Count), Tag = "", IsExpanded = true };
        root.Classes.Add("all-connections");
        var children = new List<TreeViewItem>();
        if (profiles.Count > 0)
            children.Add(new TreeViewItem { Header = NavigationHeader("收藏", profiles.Count(p => p.Favorite)), Tag = "\0favorite" });
        children.AddRange(profiles.GroupBy(p => p.Group.Length == 0 ? "默认分组" : p.Group).OrderBy(g => g.Key).Select(group => new TreeViewItem
        {
            Header = NavigationHeader(group.Key, group.Count()), Tag = group.Key,
            ItemsSource = group.OrderBy(p => p.Name).Select(profile => new TreeViewItem { Header = profile.Name, Tag = profile }).ToArray()
        }));
        root.ItemsSource = children;
        groups.ItemsSource = new[] { root }; groups.SelectedItem = children.FirstOrDefault(item => Equals(item.Tag, selectedGroup)) ?? root; RefreshRows();
    }
    static Control NavigationHeader(string title, int count)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock { Text = title == "全部连接" ? "▦" : "▰", Classes = { "nav-icon" }, VerticalAlignment = VerticalAlignment.Center });
        var label = new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(label, 1); row.Children.Add(label);
        var badge = new Border { Classes = { "nav-count" }, Child = new TextBlock { Text = count.ToString(), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center } }; Grid.SetColumn(badge, 2); row.Children.Add(badge);
        return row;
    }
    void RefreshRows()
    {
        string query = search.Text ?? "";
        var rows = profiles.Where(p => (string.IsNullOrEmpty(selectedGroup) || selectedGroup == "\0favorite" && p.Favorite || (p.Group.Length == 0 ? "默认分组" : p.Group) == selectedGroup) && (libraryProtocol.Length == 0 || p.Protocol == libraryProtocol) && $"{p.Name} {p.Host} {p.User} {p.Group}".Contains(query, StringComparison.OrdinalIgnoreCase));
        rows = librarySort switch
        {
            "host" => rows.OrderBy(p => p.Host, StringComparer.OrdinalIgnoreCase),
            "protocol" => rows.OrderBy(p => p.Protocol, StringComparer.OrdinalIgnoreCase),
            "favorite" => rows.OrderByDescending(p => p.Favorite).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
        };
        connections.ItemsSource = (librarySortDescending ? rows.Reverse() : rows).ToArray();
    }
    Control Library()
    {
        if (libraryView != null) return libraryView;
        var right = new DockPanel { Margin = new Thickness(6, 0, 8, 8) };
        var heading = new StackPanel { Spacing = 8 };
        var modes = new WrapPanel { Classes = { "library-modes" } };
        foreach (var (text, protocol) in new[] { ("全部", ""), ("SSH", "ssh"), ("SFTP", "sftp"), ("FTP", "ftp"), ("Telnet", "telnet") })
        {
            Button? mode = null;
            mode = UI.Button(text, () =>
            {
                libraryProtocol = protocol;
                foreach (var item in protocolModes) item.Classes.Set("active", ReferenceEquals(item, mode));
                RefreshRows();
            });
            mode.Classes.Add("protocol-mode"); mode.Classes.Set("active", protocol.Length == 0);
            protocolModes.Add(mode); modes.Children.Add(mode);
        }
        heading.Children.Add(modes);
        var libraryTitle = new TextBlock { Text = "连接库", FontSize = 14, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 4, 0, 0) };
        AutomationProperties.SetName(libraryTitle, "连接库标题"); heading.Children.Add(libraryTitle);
        var tools = new WrapPanel(); AutomationProperties.SetName(tools, "连接库操作");
        var create = UI.Button("＋ 新建连接", () => Edit(), true);
        var exchange = UI.Button("导入/导出配置", () => new MetadataWindow(store, repository, accounts, () => { profiles.Clear(); foreach (var item in store.Load()) profiles.Add(item); RefreshNavigation(); }, filePickers).ShowDialog(this));
        var delete = UI.Button("删除连接", DeleteSelectedProfile);
        foreach (var button in new[] { create, exchange, delete }) { button.Margin = new Thickness(0, 0, 10, 6); tools.Children.Add(button); }
        var sort = new ComboBox { ItemsSource = new[] { "名称", "地址", "协议", "收藏" }, SelectedIndex = librarySort switch { "host" => 1, "protocol" => 2, "favorite" => 3, _ => 0 }, Width = 96, Height = 34, Margin = new Thickness(8, 0, 6, 6) }; DropDownMotion.Attach(sort); AutomationProperties.SetName(sort, "连接排序"); sort.SelectionChanged += (_, _) => { librarySort = sort.SelectedIndex switch { 1 => "host", 2 => "protocol", 3 => "favorite", _ => "name" }; RefreshRows(); }; tools.Children.Add(sort);
        Button? direction = null; direction = UI.Button("↓", () => { librarySortDescending = !librarySortDescending; direction!.Content = librarySortDescending ? "↑" : "↓"; RefreshRows(); }); direction.Classes.Add("icon"); ToolTip.SetTip(direction, "切换排序方向"); direction.Margin = new Thickness(0, 0, 10, 6); tools.Children.Add(direction);
        heading.Children.Add(tools);
        var headingSurface = new Border { Classes = { "library-heading" }, Child = heading };
        DockPanel.SetDock(headingSurface, Dock.Top); right.Children.Add(headingSurface);
        var header = new Grid { ColumnDefinitions = LibraryColumns() };
        foreach (var (text, column) in new[] { ("☆", 0), ("协议", 1), ("名称", 2), ("主机 / 地址", 3), ("操作", 4) })
        {
            var label = UI.Label(text, true); label.Margin = new Thickness(6, 0); Grid.SetColumn(label, column); header.Children.Add(label);
        }
        var columnSurface = new Border { Classes = { "library-columns" }, Child = header };
        DockPanel.SetDock(columnSurface, Dock.Top); right.Children.Add(columnSurface);
        right.Children.Add(connections); libraryView = right; return right;
    }
    static ColumnDefinitions LibraryColumns() => new("28,58,1.2*,1.6*,144");
    SessionPane Pane(ConnectionProfile? p, bool filesOnly = false) => new(this, p, filesOnly || p?.Protocol is "ftp" or "sftp", transferQueue, pickers: filePickers, repository: repository, knownHosts: knownHosts, coordinator: coordinator, preferences: () => preferences, fileActions: fileActions);
    async void DeleteSelectedProfile()
    {
        if (connections.SelectedItem is not ConnectionProfile profile) { status.Text = "请先选择连接"; return; }
        if (!await UI.Confirm(this, "删除保存连接？", profile.Name + " · " + profile.Host + "\n同时删除本应用保存的密码/私钥口令，已打开会话继续保留。被其他连接引用的跳板不会删除。")) return;
        try { await repository.DeleteAsync(profile, CancellationToken.None); profiles.Remove(profile); RefreshNavigation(); status.Text = "保存连接与应用凭据已删除"; }
        catch (Exception e) { status.Text = "删除未完成：" + e.Message; }
    }
    void ToggleFavorite(ConnectionProfile profile)
    {
        var updated = profile with { Favorite = !profile.Favorite };
        int index = profiles.IndexOf(profile); if (index < 0) return; profiles[index] = updated;
        try { store.Save(profiles); RefreshNavigation(); RefreshRows(); } catch (Exception e) { status.Text = "收藏保存失败：" + e.Message; }
    }
    async void ConnectProfile(ConnectionProfile profile)
    {
        long epoch = Volatile.Read(ref navigationEpoch); var pane = Pane(profile); AddPane(pane, profile.Name.Length == 0 ? profile.Host : profile.Name);
        await pane.ConnectAsync("", false, profile.KeyPath);
        if (pane.HasUsableConnection) CollapseAfterSuccess(pane, epoch); else { status.Text = "连接未建立，请查看工作区中的错误状态"; ShowWorkspace(); }
    }
    async void DeleteProfile(ConnectionProfile profile)
    {
        if (!profiles.Contains(profile)) return;
        if (!await UI.Confirm(this, "删除保存连接？", profile.Name + " · " + profile.Host + "\n同时删除本应用保存的密码/私钥口令，已打开会话继续保留。被其他连接引用的跳板不会删除。")) return;
        try { await repository.DeleteAsync(profile, CancellationToken.None); profiles.Remove(profile); RefreshNavigation(); status.Text = "保存连接与应用凭据已删除"; }
        catch (Exception e) { status.Text = "删除未完成：" + e.Message; }
    }
    async void Edit(ConnectionProfile? profile = null)
    {
        var form = new ConnectionWindow(profile, repository, knownHosts, () => store.Load(), coordinator); var answer = await form.ShowDialog<ConnectionRequest?>(this); if (answer == null) return;
        if (answer.Save) { if (profile != null) profiles.Remove(profile); profiles.Add(answer.Profile); RefreshNavigation(); }
        if (!answer.Connect) return;
        long epoch = Volatile.Read(ref navigationEpoch); var pane = Pane(answer.Profile); AddPane(pane, answer.Profile.Name.Length == 0 ? answer.Profile.Host : answer.Profile.Name); await pane.ConnectAsync(answer.Password, false, answer.Profile.KeyPath, answer.Passphrase); answer.Password = answer.Passphrase = ""; CollapseAfterSuccess(pane, epoch);
    }
    void CollapseAfterSuccess(SessionPane pane, long epoch) { if (ReferenceEquals(host.Content, tabs) && preferences.AutoCollapseNavigation && pane.HasUsableConnection && Volatile.Read(ref navigationEpoch) == epoch && tabs.SelectedItem is TabItem { Content: SessionPane active } && ReferenceEquals(pane, active)) ShowWorkspace(); }
    public async Task AcceptLaunch(LaunchPlan plan)
    {
        if (plan.Error.Length > 0) { status.Text = plan.Error; return; }
        if (plan.Diagnose) { status.Text = plan.ToString(); return; }
        if (plan.Connection is not { } c) return;
        try
        {
            if (!c.HasPassword && c.KeyPath.Length == 0) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)); var answer = await UI.AskCredentials(this, new(1, false, true, "入口未提供密码；内容仅用于本次连接。", new[] { new CredentialQuestion("登录密码", false) }), timeout.Token); if (answer == null) return; c.Password = answer[0]; Array.Clear(answer); }
            var p = new ConnectionProfile(c.Title, "临时会话", c.Host, c.Port, c.User) { Protocol = c.Protocol }; long epoch = Volatile.Read(ref navigationEpoch); var pane = Pane(p, c.Protocol == "sftp"); AddPane(pane, p.Name.Length == 0 ? p.Host : p.Name); await pane.ConnectAsync(c.Password, true, c.KeyPath); CollapseAfterSuccess(pane, epoch);
        }
        finally { c.Password = ""; }
    }
    void AddPane(SessionPane pane, string title)
    {
        associations.Add(new(pane.WorkspaceId, title, pane.Profile == null ? WorkspaceProtocol.Local : pane.Profile.Protocol == "ftp" ? WorkspaceProtocol.Ftp : pane.FilesOnly ? WorkspaceProtocol.Sftp : WorkspaceProtocol.Ssh, pane.HasFileCapability));
        topNavigationByText["工作区"].IsEnabled = true;
        sessions.Add(pane); pane.LayoutChanged += layout => { preferences = preferences with { Workspace = layout }; PersistPreferences(); }; pane.ApplyPreferences(preferences); var tab = new TabItem { Content = pane };
        pane.FileSourceReady += source => { if (sessions.Contains(source)) { associations.SetOnline(source.WorkspaceId, source.HasFileCapability); foreach (var view in sessions) view.RefreshSourceLabels(); } };
        pane.AssociationRequested += ChooseFileSource;
        pane.CloneRequested += original => Clone(original, original.SessionTitle);
        pane.DetachRequested += (owner, source) => DetachFiles(owner, source);
        pane.SourceSelected += (owner, source) => { if (sessions.Contains(owner)) associations.Select(owner.WorkspaceId, source); };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeight.Normal, MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var close = UI.Button("×", async () => await CloseWorkspaceAsync(pane)); close.Classes.Add("icon"); AutomationProperties.SetName(close, "关闭会话 " + title); ToolTip.SetTip(close, "关闭此来源会取消绑定任务；其他来源和草稿保留");
        header.Children.Add(close); var clone = new MenuItem { Header = "新建相同会话（共享认证）", Command = new ActionCommand(() => Clone(pane, title)) }; header.ContextMenu = new ContextMenu { ItemsSource = new[] { clone } };
        tab.Header = header; workspaceTabs.Add(pane.WorkspaceId, tab); RebuildWorkspaceTabs(pane.WorkspaceId); host.Content = tabs;
    }
    void RebuildWorkspaceTabs(Guid? requested = null)
    {
        Guid? current = requested ?? (tabs.SelectedItem is TabItem { Content: SessionPane selected } ? selected.WorkspaceId : null);
        var visible = associations.Visible(); tabs.Items.Clear(); foreach (var item in visible) tabs.Items.Add(workspaceTabs[item.Id]);
        if (current is { } id && !visible.Any(e => e.Id == id)) current = associations.VisibleOwner(id);
        tabs.SelectedItem = current is { } active && workspaceTabs.TryGetValue(active, out var tab) && tabs.Items.Contains(tab) ? tab : tabs.Items.Cast<TabItem>().FirstOrDefault();
    }
    public void AttachFiles(SessionPane owner, SessionPane source)
    {
        if (!sessions.Contains(owner) || !sessions.Contains(source) || !source.HasFileCapability) throw new IOException("请选择当前已打开且可用的文件来源");
        var before = associations.Sources(owner.WorkspaceId);
        associations.Attach(owner.WorkspaceId, source.WorkspaceId);
        try { owner.AttachFileSource(source); }
        catch { if (!before.Sources.Contains(source.WorkspaceId)) associations.Detach(owner.WorkspaceId, source.WorkspaceId); associations.Select(owner.WorkspaceId, before.Active); throw; }
        RebuildWorkspaceTabs(owner.WorkspaceId); ShowWorkspace();
    }
    public void DetachFiles(SessionPane owner, Guid source)
    {
        if (!sessions.Contains(owner)) return; owner.DetachFileSource(source); associations.Detach(owner.WorkspaceId, source);
        var selected = associations.Sources(owner.WorkspaceId).Active; owner.ShowFileSource(selected); RebuildWorkspaceTabs(owner.WorkspaceId);
    }
    public async Task<bool> CloseWorkspaceAsync(SessionPane pane)
    {
        if (!sessions.Contains(pane) || !await pane.ConfirmCloseAsync()) return false;
        associations.Close(pane.WorkspaceId); sessions.Remove(pane); workspaceTabs.Remove(pane.WorkspaceId); pane.Dispose(); foreach (var remaining in sessions) remaining.RefreshSourceLabels(); RebuildWorkspaceTabs();
        if (tabs.Items.Count == 0) { topNavigationByText["工作区"].IsEnabled = false; ShowLibrary(); } return true;
    }
    async void ChooseFileSource(SessionPane pane)
    {
            var candidates = sessions.Where(p => p.Profile != null && p.HasFileCapability).ToArray();
        if (candidates.Length == 0) { status.Text = "请先明确打开一个可用SSH/SFTP/FTP文件会话；不会根据主机名自动关联或重新登录。"; return; }
        var labels = candidates.Select((p, i) => $"{i + 1}. {(p.Profile?.Protocol == "ftp" ? "FTP" : p.FilesOnly ? "SFTP" : "SSH")} · {p.SessionTitle}").ToArray();
        var select = new ComboBox { ItemsSource = labels, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch }; DropDownMotion.Attach(select);
        var dialog = new Window { Title = "关联文件来源", Width = 540, Height = 260, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.Content = new StackPanel { Spacing = 16, Margin = new Thickness(20), Children = { UI.Label("保留当前终端，同时显示所选来源。只隐藏被关联文件协议会话的顶部标签；解除最后关联后恢复。", true), select, UI.Button("关联", () => dialog.Close(true)), UI.Button("取消", () => dialog.Close(false)) } };
        if (!await dialog.ShowDialog<bool>(this)) return;
        try { if (select.SelectedIndex >= 0) AttachFiles(pane, candidates[select.SelectedIndex]); } catch (Exception e) { status.Text = e.Message; }
    }
    async void Clone(SessionPane original, string title) { try { var pane = Pane(original.Profile, original.FilesOnly); AddPane(pane, title + " · 新页"); await pane.CloneFromAsync(original); } catch (Exception e) { status.Text = "新页未就绪，原会话保留：" + e.Message; } }
    async void OpenLocal() { var pane = Pane(null); AddPane(pane, "本地终端"); ShowWorkspace(); await pane.StartLocalAsync(); }
}
public sealed record ConnectionProfile(string Name, string Group, string Host, int Port, string User)
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N"); public string Revision { get; init; } = "0";
    public bool HasPassword { get; init; }
    public bool HasPassphrase { get; init; }
    public string Protocol { get; init; } = "ssh"; public bool Favorite { get; init; }
    public string AuthMode { get; init; } = "password"; public string KeyPath { get; init; } = ""; public string AgentFingerprint { get; init; } = ""; public string JumpId { get; init; } = "";
    public override string ToString() => $"{Name}     {Host}:{Port}     SSH";
}
public sealed class ConnectionRequest(ConnectionProfile profile, string password, bool save, bool connect, string passphrase = "") { public string Passphrase { get; set; } = passphrase; public ConnectionProfile Profile { get; } = profile; public string Password { get; set; } = password; public bool Save { get; } = save; public bool Connect { get; } = connect; }
public static partial class UI
{
    static readonly ButtonLabelTemplate ButtonLabels = new();
    public static Button Button(string text, Action action, bool primary = false) { var b = new Button { Content = text }; if (ButtonLabels.Match(text)) b.ContentTemplate = ButtonLabels; if (primary) b.Classes.Add("primary"); b.Click += (_, _) => action(); AutomationProperties.SetName(b, text); return b; }
    public static TextBlock Label(string text, bool note = false) { var t = new TextBlock { Text = text }; if (note) t.Classes.Add("note"); return t; }
    public static async Task<bool> Confirm(Window parent, string title, string message, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var w = new Window { Title = title, Width = 560, Height = 250, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 12 }; actions.Children.Add(Button("取消", () => w.Close(false))); actions.Children.Add(Button("确认", () => w.Close(true), true));
        var body = new DockPanel { Margin = new Thickness(20) }; DockPanel.SetDock(actions, Dock.Bottom); body.Children.Add(actions); body.Children.Add(new ScrollViewer { Margin = new Thickness(0, 0, 0, 16), Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } }); w.Content = body; using var cancel = ct.Register(() => Dispatcher.UIThread.Post(() => w.Close(false))); return await w.ShowDialog<bool>(parent);
    }
}
