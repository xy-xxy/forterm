using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Threading;
using Forterm.Studio.Library;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;

namespace Forterm.Studio.ViewModels;

public enum NodeKind { All, Favorites, Recent, Frequent, Group, Host }

/// <summary>Left sidebar tree: 全部 → 收藏 / 最近 / 常用 / groups (with hosts) / ungrouped hosts.</summary>
public sealed class LibraryNode : ObservableObject
{
    bool expanded, selected;
    public LibraryNode(NodeKind kind, string name, string id = "", int count = -1)
    {
        Kind = kind; Name = name; Id = id; Count = count;
    }
    public NodeKind Kind { get; }
    public string Name { get; }
    public string Id { get; }
    public int Count { get; }
    public string Label => Count >= 0 ? $"{Name}({Count})" : Name;
    public string CountLabel => Count >= 0 ? $"({Count})" : "";
    public bool IsFolder => Kind != NodeKind.Host;
    public bool IsHost => Kind == NodeKind.Host;
    public bool IsGroup => Kind == NodeKind.Group;
    public ObservableCollection<LibraryNode> Children { get; } = new();
    public bool IsExpanded { get => expanded; set => Set(ref expanded, value); }
    public bool IsSelected { get => selected; set => Set(ref selected, value); }
}

public sealed class HostRowViewModel : ObservableObject
{
    bool isChecked;
    string latency = "", info = "";
    LatencyState latencyState = LatencyState.Unknown;

    public HostRowViewModel(StudioProfile profile, string groupPath)
    {
        Profile = profile;
        GroupPath = groupPath;
    }

    public StudioProfile Profile { get; }
    public string Id => Profile.Id;
    public string Name => Profile.Name.Length > 0 ? Profile.Name : Profile.Host;
    public string Address => Profile.Endpoint;
    public string Note => Profile.Note.ReplaceLineEndings(" ");
    public string GroupPath { get; }
    public bool Favorite => Profile.Favorite;
    public string ProtocolLabel => Profile.ProtocolLabel;
    public string Badge => Profile.Protocol switch { "ftp" => "FTP", "telnet" => "TEL", "sftp" => "SFTP", _ => "SSH" };
    public bool ViaJumpOrProxy => Profile.JumpChain.Count > 0 || (!Profile.ProxyDisabled && Profile.Proxy.Length > 0);
    public bool IsChecked { get => isChecked; set => Set(ref isChecked, value); }
    /// <summary>Live CPU / memory / disk of an open session for this connection; empty otherwise.</summary>
    public string Info { get => info; set { if (Set(ref info, value)) Raise(nameof(HasInfo)); } }
    public bool HasInfo => Info.Length > 0;
    public string Latency { get => latency; set => Set(ref latency, value); }
    public LatencyState LatencyState
    {
        get => latencyState;
        set { if (Set(ref latencyState, value)) { Raise(nameof(LatencyGood)); Raise(nameof(LatencySlow)); Raise(nameof(LatencyFailed)); } }
    }
    public bool LatencyGood => latencyState == LatencyState.Good;
    public bool LatencySlow => latencyState == LatencyState.Slow;
    public bool LatencyFailed => latencyState == LatencyState.Failed;
    public int? LatencyMs { get; set; }

    public ICommand? ConnectCommand { get; set; }
    public ICommand? EditCommand { get; set; }
    public ICommand? ToggleFavoriteCommand { get; set; }
    public ICommand? DuplicateCommand { get; set; }
    public ICommand? DeleteCommand { get; set; }
    public ICommand? CopyAddressCommand { get; set; }
}

public enum LatencyState { Unknown, Good, Slow, Failed, NotMeasured }

public enum SortKey { None, Name, Address, Latency }

/// <summary>Connection library (连接库): protocol tabs, group tree, host table, batch actions.</summary>
public sealed class LibraryViewModel : ObservableObject, IDisposable
{
    static readonly (string Key, string Label, string[] Protocols)[] TabDefinitions =
    [
        ("ssh", "SSH", ["ssh", "sftp"]),
        ("ftp", "FTP", ["ftp"]),
        ("telnet", "Telnet", ["telnet"]),
        ("bastion", "堡垒机", []),
        ("tunnel", "隧道", []),
    ];

    readonly LibraryStore store;
    readonly CredentialStore credentials;
    readonly ProfileConnector connector;
    readonly IStudioInteraction interaction;
    readonly Func<StudioProfile, Task> open;
    Func<string, (double? Cpu, double? Memory, double? Disk)?> liveInfo = _ => null;
    readonly Dictionary<string, (int? Ms, DateTime At)> latencyCache = new();
    CancellationTokenSource? latencyRun;
    ProtocolTab selectedTab;
    LibraryNode? selectedNode;
    string search = "", notice = "";
    SortKey sortKey = SortKey.None;
    bool sortDescending, sidebarCollapsed, rebuildQueued, disposed;
    readonly Action onStoreChanged;

    public LibraryViewModel(LibraryStore store, CredentialStore credentials, ProfileConnector connector, IStudioInteraction interaction, Func<StudioProfile, Task> open)
    {
        this.store = store;
        this.credentials = credentials;
        this.connector = connector;
        this.interaction = interaction;
        this.open = open;
        Tabs = TabDefinitions.Select(t => new ProtocolTab(t.Key, t.Label, t.Protocols)).ToArray();
        foreach (var tab in Tabs) tab.SelectCommand = new RelayCommand(() => SelectedTab = tab);
        selectedTab = Tabs[0];
        selectedTab.IsActive = true;
        NewConnectionCommand = new AsyncCommand(NewConnectionAsync, Report);
        NewGroupCommand = new AsyncCommand(() => NewGroupAsync(""), Report);
        NewSubGroupCommand = new AsyncCommand(() => NewGroupAsync(SelectedNode?.IsGroup == true ? SelectedNode.Id : ""), Report);
        RenameGroupCommand = new AsyncCommand(RenameGroupAsync, Report);
        DeleteGroupCommand = new AsyncCommand(DeleteGroupAsync, Report);
        RefreshCommand = new RelayCommand(() => { latencyCache.Clear(); Rebuild(); });
        ToggleSidebarCommand = new RelayCommand(() => SidebarCollapsed = !SidebarCollapsed);
        SortCommand = new RelayCommand(p => SortBy(p is SortKey key ? key : Enum.Parse<SortKey>(p?.ToString() ?? "None")));
        ConnectCheckedCommand = new AsyncCommand(ConnectCheckedAsync, Report);
        DeleteCheckedCommand = new AsyncCommand(() => DeleteAsync(Checked().ToArray()), Report);
        DuplicateCheckedCommand = new RelayCommand(() => Duplicate(Checked().ToArray()));
        ExportCheckedCommand = new AsyncCommand(() => ExportAsync(Checked().Select(r => r.Id).ToArray()), Report);
        ExportAllCommand = new AsyncCommand(() => ExportAsync(store.Profiles.Select(p => p.Id).ToArray()), Report);
        ImportCommand = new AsyncCommand(ImportAsync, Report);
        ClearCheckedCommand = new RelayCommand(() => { foreach (var row in Rows) row.IsChecked = false; RaiseChecked(); });
        DismissNoticeCommand = new RelayCommand(() => Notice = "");
        ClearFilterCommand = new RelayCommand(() => { Search = ""; SelectedNode = null; });
        OpenNodeCommand = new AsyncCommand(async () => { if (SelectedNode is { IsHost: true } node && store.Find(node.Id) is { } p) await open(p); }, Report);
        // Coalesce: a batch (duplicate, import, delete) rebuilds the tree and table once.
        onStoreChanged = () =>
        {
            if (rebuildQueued || disposed) return;
            rebuildQueued = true;
            Dispatcher.UIThread.Post(() => { rebuildQueued = false; if (!disposed) Rebuild(); });
        };
        store.Changed += onStoreChanged;
        Rebuild();
    }

    public sealed class ProtocolTab(string key, string label, string[] protocols) : ObservableObject
    {
        bool active;
        public string Key { get; } = key;
        public string Label { get; } = label;
        public string[] Protocols { get; } = protocols;
        public bool IsActive { get => active; set => Set(ref active, value); }
        public ICommand? SelectCommand { get; set; }
    }

    public ProtocolTab[] Tabs { get; }
    public ProtocolTab SelectedTab
    {
        get => selectedTab;
        set
        {
            if (value == null || !Set(ref selectedTab, value)) return;
            foreach (var tab in Tabs) tab.IsActive = ReferenceEquals(tab, value);
            Raise(nameof(NewConnectionLabel));
            Raise(nameof(IsTunnelTab));
            Raise(nameof(IsBastionTab));
            Raise(nameof(IsHostTab));
            selectedNode = null;
            Rebuild();
        }
    }
    public string NewConnectionLabel => "+ " + selectedTab.Label;

    public ObservableCollection<LibraryNode> Tree { get; } = new();
    public ObservableCollection<HostRowViewModel> Rows { get; } = new();

    public LibraryNode? SelectedNode
    {
        get => selectedNode;
        set { if (Set(ref selectedNode, value)) { Raise(nameof(CurrentLabel)); Raise(nameof(CanEditGroup)); RebuildRows(); } }
    }
    public string CurrentLabel => selectedNode is null or { Kind: NodeKind.All } ? "全部" : selectedNode.IsHost ? "全部" : selectedNode.Kind == NodeKind.Group ? store.GroupPath(selectedNode.Id) : selectedNode.Name;
    public bool CanEditGroup => selectedNode?.IsGroup == true;
    public string Search { get => search; set { if (Set(ref search, value)) RebuildRows(); } }
    public bool SidebarCollapsed { get => sidebarCollapsed; set { if (Set(ref sidebarCollapsed, value)) Raise(nameof(SidebarExpanded)); } }
    public bool SidebarExpanded => !sidebarCollapsed;
    public string Notice { get => notice; set { if (Set(ref notice, value)) Raise(nameof(HasNotice)); } }
    public bool HasNotice => Notice.Length > 0;
    public int CheckedCount => Rows.Count(r => r.IsChecked);
    public bool HasChecked => CheckedCount > 0;
    public string CheckedLabel => $"已选 {CheckedCount} 项";
    public bool IsEmpty => Rows.Count == 0;
    /// <summary>No saved connection of this protocol at all: show the onboarding empty state.</summary>
    public bool ShowLibraryEmpty => IsEmpty && !InTab.Any();
    /// <summary>Connections exist but the group/search filter hides them all.</summary>
    public bool ShowFilteredEmpty => IsEmpty && InTab.Any();
    public bool HasSearch => search.Trim().Length > 0;
    public string EmptyTitle => $"还没有保存的 {selectedTab.Label} 连接";
    public string NewConnectionLongLabel => $"新建 {selectedTab.Label} 连接";
    public bool AllChecked
    {
        get => Rows.Count > 0 && Rows.All(r => r.IsChecked);
        set { foreach (var row in Rows) row.IsChecked = value; RaiseChecked(); }
    }
    public SortKey SortKey => sortKey;
    public bool SortDescending => sortDescending;

    public ICommand NewConnectionCommand { get; }
    public ICommand NewGroupCommand { get; }
    public ICommand NewSubGroupCommand { get; }
    public ICommand RenameGroupCommand { get; }
    public ICommand DeleteGroupCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand SortCommand { get; }
    public ICommand ConnectCheckedCommand { get; }
    public ICommand DeleteCheckedCommand { get; }
    public ICommand DuplicateCheckedCommand { get; }
    public ICommand ExportCheckedCommand { get; }
    public ICommand ExportAllCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ClearCheckedCommand { get; }
    public ICommand DismissNoticeCommand { get; }
    public ICommand ClearFilterCommand { get; }
    public ICommand OpenNodeCommand { get; }
    public ICommand? AccountsCommand { get; set; }
    /// <summary>The 隧道 tab's content (set by the main view model).</summary>
    public TunnelsViewModel? Tunnels { get; set; }
    public bool IsTunnelTab => selectedTab.Key == "tunnel";
    /// <summary>The "堡垒机" tab's content (set by the main view model): bastion accounts and the machines of one.</summary>
    public BastionHubViewModel? Bastions { get; set; }
    public bool IsBastionTab => selectedTab.Key == "bastion";
    public bool IsHostTab => !IsTunnelTab && !IsBastionTab;
    public void ShowBastions() => SelectedTab = Tabs.First(t => t.Key == "bastion");

    void Report(Exception e) => Notice = WorkspaceViewModel.Short(e.Message);

    IEnumerable<StudioProfile> InTab => store.Profiles.Where(p => selectedTab.Protocols.Contains(p.Protocol));

    void Rebuild()
    {
        string? keep = selectedNode is { } n ? n.Kind + ":" + n.Id : null;
        Tree.Clear();
        var profiles = InTab.ToList();
        var all = new LibraryNode(NodeKind.All, "全部") { IsExpanded = true };
        var favorites = profiles.Where(p => p.Favorite).OrderBy(p => Display(p), StringComparer.CurrentCulture).ToList();
        var recent = profiles.Where(p => p.LastUsed != null).OrderByDescending(p => p.LastUsed).Take(10).ToList();
        var frequent = profiles.Where(p => p.UseCount > 0).OrderByDescending(p => p.UseCount).ThenByDescending(p => p.LastUsed).Take(10).ToList();
        all.Children.Add(Folder(NodeKind.Favorites, "收藏", favorites));
        all.Children.Add(Folder(NodeKind.Recent, "最近", recent));
        all.Children.Add(Folder(NodeKind.Frequent, "常用", frequent));
        foreach (var group in store.Groups.Where(g => g.ParentId.Length == 0).OrderBy(g => g.Name, StringComparer.CurrentCulture))
            all.Children.Add(GroupNode(group, profiles));
        foreach (var p in profiles.Where(p => p.GroupId.Length == 0).OrderBy(Display, StringComparer.CurrentCulture))
            all.Children.Add(new LibraryNode(NodeKind.Host, Display(p), p.Id));
        Tree.Add(all);
        selectedNode = keep == null ? null : Flatten(Tree).FirstOrDefault(x => x.Kind + ":" + x.Id == keep && x.Kind != NodeKind.Host);
        if (selectedNode != null) selectedNode.IsSelected = true;
        Raise(nameof(SelectedNode));
        Raise(nameof(CurrentLabel));
        Raise(nameof(CanEditGroup));
        RebuildRows();
    }

    static LibraryNode Folder(NodeKind kind, string name, List<StudioProfile> profiles)
    {
        var node = new LibraryNode(kind, name, kind.ToString(), profiles.Count);
        foreach (var p in profiles) node.Children.Add(new LibraryNode(NodeKind.Host, Display(p), p.Id));
        return node;
    }

    LibraryNode GroupNode(ProfileGroup group, List<StudioProfile> profiles)
    {
        var members = MembersOf(group.Id, profiles).ToList();
        var node = new LibraryNode(NodeKind.Group, group.Name, group.Id, members.Count);
        foreach (var child in store.Groups.Where(g => g.ParentId == group.Id).OrderBy(g => g.Name, StringComparer.CurrentCulture))
            node.Children.Add(GroupNode(child, profiles));
        foreach (var p in profiles.Where(p => p.GroupId == group.Id).OrderBy(Display, StringComparer.CurrentCulture))
            node.Children.Add(new LibraryNode(NodeKind.Host, Display(p), p.Id));
        return node;
    }

    IEnumerable<StudioProfile> MembersOf(string groupId, IEnumerable<StudioProfile> profiles)
    {
        var ids = new HashSet<string> { groupId };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var g in store.Groups) if (ids.Contains(g.ParentId) && ids.Add(g.Id)) grew = true;
        }
        return profiles.Where(p => ids.Contains(p.GroupId));
    }

    static IEnumerable<LibraryNode> Flatten(IEnumerable<LibraryNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    static string Display(StudioProfile p) => p.Name.Length > 0 ? p.Name : p.Host;

    void RebuildRows()
    {
        var profiles = InTab;
        profiles = selectedNode switch
        {
            { Kind: NodeKind.Favorites } => profiles.Where(p => p.Favorite),
            { Kind: NodeKind.Recent } => profiles.Where(p => p.LastUsed != null).OrderByDescending(p => p.LastUsed).Take(10),
            { Kind: NodeKind.Frequent } => profiles.Where(p => p.UseCount > 0).OrderByDescending(p => p.UseCount).Take(10),
            { Kind: NodeKind.Group } node => MembersOf(node.Id, profiles),
            _ => profiles,
        };
        string q = search.Trim();
        if (q.Length > 0)
            profiles = profiles.Where(p => Matches(p, q));
        var checkedIds = Rows.Where(r => r.IsChecked).Select(r => r.Id).ToHashSet();
        var rows = profiles.Select(p => Row(p, checkedIds.Contains(p.Id))).ToList();
        rows = (sortKey, sortDescending) switch
        {
            (SortKey.Name, false) => rows.OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList(),
            (SortKey.Name, true) => rows.OrderByDescending(r => r.Name, StringComparer.CurrentCulture).ToList(),
            (SortKey.Address, false) => rows.OrderBy(r => r.Address, StringComparer.Ordinal).ToList(),
            (SortKey.Address, true) => rows.OrderByDescending(r => r.Address, StringComparer.Ordinal).ToList(),
            (SortKey.Latency, false) => rows.OrderBy(r => r.LatencyMs ?? int.MaxValue).ToList(),
            (SortKey.Latency, true) => rows.OrderByDescending(r => r.LatencyMs ?? -1).ToList(),
            _ => rows,
        };
        Rows.Clear();
        foreach (var row in rows) Rows.Add(row);
        RefreshInfo();
        Raise(nameof(IsEmpty));
        Raise(nameof(ShowLibraryEmpty));
        Raise(nameof(ShowFilteredEmpty));
        Raise(nameof(HasSearch));
        Raise(nameof(EmptyTitle));
        Raise(nameof(NewConnectionLongLabel));
        RaiseChecked();
        _ = MeasureLatencyAsync(rows);
    }

    /// <summary>Matches name, host, user, note, and "user@host" (e.g. "ssh root@10.0").</summary>
    internal static bool Matches(StudioProfile p, string query)
    {
        foreach (string term in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (term.Equals("ssh", StringComparison.OrdinalIgnoreCase)) continue;
            string t = term.Trim('*');
            bool hit = p.Name.Contains(t, StringComparison.CurrentCultureIgnoreCase) || p.Host.Contains(t, StringComparison.OrdinalIgnoreCase)
                || p.User.Contains(t, StringComparison.OrdinalIgnoreCase) || p.Note.Contains(t, StringComparison.CurrentCultureIgnoreCase)
                || $"{p.User}@{p.Host}:{p.Port}".Contains(t, StringComparison.OrdinalIgnoreCase);
            if (!hit) return false;
        }
        return true;
    }

    HostRowViewModel Row(StudioProfile p, bool isChecked)
    {
        var row = new HostRowViewModel(p, store.GroupPath(p.GroupId)) { IsChecked = isChecked };
        row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(HostRowViewModel.IsChecked)) RaiseChecked(); };
        row.ConnectCommand = new AsyncCommand(() => open(p), Report);
        row.EditCommand = new AsyncCommand(() => EditAsync(p), Report);
        row.ToggleFavoriteCommand = new RelayCommand(() => store.Upsert(p with { Favorite = !p.Favorite }));
        row.DuplicateCommand = new RelayCommand(() => Duplicate([row]));
        row.DeleteCommand = new AsyncCommand(() => DeleteAsync([row]), Report);
        row.CopyAddressCommand = new AsyncCommand(() => interaction.CopyTextAsync(p.Endpoint), Report);
        if (row.ViaJumpOrProxy) { row.Latency = p.JumpChain.Count > 0 ? "跳板" : "代理"; row.LatencyState = LatencyState.NotMeasured; }
        else if (latencyCache.TryGetValue(p.Endpoint, out var cached)) ApplyLatency(row, cached.Ms);
        return row;
    }

    static void ApplyLatency(HostRowViewModel row, int? ms)
    {
        row.LatencyMs = ms;
        row.Latency = ms is { } v ? v + " ms" : "超时";
        row.LatencyState = ms switch { null => LatencyState.Failed, > 300 => LatencyState.Slow, _ => LatencyState.Good };
    }

    /// <summary>TCP handshake timing only, at most 8 at a time and 200 per refresh, cached for a minute.</summary>
    async Task MeasureLatencyAsync(List<HostRowViewModel> rows)
    {
        latencyRun?.Cancel();
        if (disposed) return;
        var run = latencyRun = new CancellationTokenSource();
        var targets = rows.Where(r => !r.ViaJumpOrProxy && !(latencyCache.TryGetValue(r.Address, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(1)))
            .Take(200).ToList();
        using var gate = new SemaphoreSlim(8);
        await Task.WhenAll(targets.Select(async row =>
        {
            await gate.WaitAsync();
            try
            {
                if (run.IsCancellationRequested) return;
                int? ms = await LatencyProbe.MeasureAsync(row.Profile.Host, row.Profile.Port, TimeSpan.FromSeconds(3), run.Token);
                if (run.IsCancellationRequested) return;
                latencyCache[row.Address] = (ms, DateTime.UtcNow);
                Dispatcher.UIThread.Post(() => ApplyLatency(row, ms));
            }
            finally { gate.Release(); }
        }));
    }

    void SortBy(SortKey key)
    {
        if (sortKey == key) sortDescending = !sortDescending;
        else { sortKey = key; sortDescending = false; }
        Raise(nameof(SortKey));
        Raise(nameof(SortDescending));
        RebuildRows();
    }

    /// <summary>The library store is shared by every window: a closed window stops listening and
    /// probing.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        store.Changed -= onStoreChanged;
        latencyRun?.Cancel();
    }

    void RaiseChecked()
    {
        Raise(nameof(CheckedCount));
        Raise(nameof(HasChecked));
        Raise(nameof(CheckedLabel));
        Raise(nameof(AllChecked));
    }

    IEnumerable<HostRowViewModel> Checked() => Rows.Where(r => r.IsChecked);

    public ConnectionEditorViewModel CreateEditor(StudioProfile? existing) =>
        new(store, credentials, connector, interaction, existing, selectedTab.Protocols.FirstOrDefault() ?? "ssh", selectedNode?.IsGroup == true ? selectedNode.Id : "");

    async Task NewConnectionAsync()
    {
        var editor = CreateEditor(null);
        try { await interaction.EditConnectionAsync(editor); }
        finally { editor.ClearSecrets(); }
    }

    async Task EditAsync(StudioProfile p)
    {
        var editor = CreateEditor(store.Find(p.Id) ?? p);
        try { await interaction.EditConnectionAsync(editor); }
        finally { editor.ClearSecrets(); }
    }

    async Task NewGroupAsync(string parentId)
    {
        string title = parentId.Length > 0 ? "新建子分组（" + store.GroupPath(parentId) + "）" : "新建分组";
        if (await interaction.PromptTextAsync(title, "分组名称", "") is not { } name || name.Trim().Length == 0) return;
        store.AddGroup(name, parentId);
    }

    async Task RenameGroupAsync()
    {
        if (selectedNode is not { IsGroup: true } node || store.FindGroup(node.Id) is not { } group) return;
        if (await interaction.PromptTextAsync("重命名分组", "分组名称", group.Name) is not { } name || name.Trim().Length == 0) return;
        store.RenameGroup(group.Id, name);
    }

    async Task DeleteGroupAsync()
    {
        if (selectedNode is not { IsGroup: true } node || store.FindGroup(node.Id) is not { } group) return;
        string parent = group.ParentId.Length > 0 ? "“" + store.GroupPath(group.ParentId) + "”" : "顶层";
        if (!await interaction.ConfirmAsync("删除分组", $"删除分组“{store.GroupPath(group.Id)}”？其中的连接和子分组会移到{parent}，不会删除连接。", "删除分组", danger: true)) return;
        store.RemoveGroup(group.Id);
    }

    void Duplicate(IReadOnlyList<HostRowViewModel> rows)
    {
        foreach (var row in rows)
        {
            var copy = row.Profile with
            {
                Id = StudioProfile.NewId(),
                Name = (row.Profile.Name.Length > 0 ? row.Profile.Name : row.Profile.Host) + " - 副本",
                HasPassword = false,
                HasPassphrase = false,
                Favorite = false,
                LastUsed = null,
                UseCount = 0,
                Created = DateTimeOffset.UtcNow,
            };
            store.Upsert(copy);
        }
        if (rows.Count > 0) Notice = rows.Count == 1 ? "已复制连接（不含已保存的密码）" : $"已复制 {rows.Count} 个连接（不含已保存的密码）";
    }

    async Task DeleteAsync(IReadOnlyList<HostRowViewModel> rows)
    {
        if (rows.Count == 0) return;
        string what = rows.Count == 1 ? "“" + rows[0].Name + "”" : $"选中的 {rows.Count} 个连接";
        int dependents = store.Profiles.Count(p => !rows.Any(r => r.Id == p.Id) && p.JumpChain.Any(j => rows.Any(r => r.Id == j)));
        string extra = dependents > 0 ? $"\n有 {dependents} 个连接把它用作跳板，删除后会从它们的跳板链中移除。" : "";
        if (!await interaction.ConfirmAsync("删除连接", $"删除{what}？已保存的密码也会从系统凭据中移除。{extra}", "删除", danger: true)) return;
        var ids = rows.Select(r => r.Id).ToHashSet();
        var failed = new List<string>();
        foreach (var row in rows)
        {
            try { await credentials.ForgetAllAsync(row.Id, CancellationToken.None); }
            catch (Exception e) when (e is IOException or PlatformNotSupportedException) { failed.Add(row.Name); }
        }
        store.Remove(ids);
        Notice = failed.Count == 0 ? "" : "连接已删除，但以下连接的系统凭据未能确认移除：" + string.Join("、", failed);
    }

    async Task ConnectCheckedAsync()
    {
        var rows = Checked().ToList();
        if (rows.Count > 10 && !await interaction.ConfirmAsync("批量连接", $"将同时打开 {rows.Count} 个会话，确定继续？", "全部连接", danger: false)) return;
        foreach (var row in rows) row.IsChecked = false;
        await Task.WhenAll(rows.Select(r => open(r.Profile)));
    }

    async Task ExportAsync(IReadOnlyCollection<string> ids)
    {
        if (ids.Count == 0) { Notice = "没有可导出的连接"; return; }
        string? path = await interaction.PickSaveFileAsync("导出连接（不含密码）", "forterm-connections.json");
        if (path == null) return;
        await File.WriteAllBytesAsync(path, LibraryExchange.Export(store, ids));
        Notice = $"已导出 {ids.Count} 个连接到 {Path.GetFileName(path)}（不含密码和信任的主机指纹）";
    }

    async Task ImportAsync()
    {
        string? path = await interaction.PickOpenFileAsync("导入连接");
        if (path == null) return;
        int count = LibraryExchange.Import(store, path, out int cleared);
        Notice = $"已导入 {count} 个连接。导入的连接不含密码，首次连接时需要输入或重新保存。" +
                 (cleared > 0 ? $"其中 {cleared} 个连接的“初始执行/保活命令/会话环境变量/算法偏好”未导入（不沿用他人文件中的这些设置），如需要请在编辑中自行填写。" : "");
    }

    /// <summary>Where the "信息" column reads open sessions' monitor numbers.</summary>
    public void UseLiveInfo(Func<string, (double? Cpu, double? Memory, double? Disk)?> source) { liveInfo = source; RefreshInfo(); }

    /// <summary>Updates the "信息" column from open sessions only; never connects to collect numbers.</summary>
    public void RefreshInfo()
    {
        static string P(string label, double? v) => v is { } x ? $"{label} {x:0}%" : "";
        foreach (var row in Rows)
        {
            // "禁止列表监控开启": this host's numbers never appear in the list.
            var summary = row.Profile.DisableListMonitor ? null : liveInfo(row.Id);
            row.Info = summary is { } s ? string.Join("  ", new[] { P("CPU", s.Cpu), P("内存", s.Memory), P("磁盘", s.Disk) }.Where(t => t.Length > 0)) : "";
        }
    }

    /// <summary>For the view's tree context menu and double-click.</summary>
    public StudioProfile? FindProfile(string id) => store.Find(id);

    internal static string FormatCount(int n) => n.ToString(CultureInfo.InvariantCulture);
}
