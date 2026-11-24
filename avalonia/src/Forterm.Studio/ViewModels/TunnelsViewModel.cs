using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.Library;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;

namespace Forterm.Studio.ViewModels;

public sealed class TunnelRowViewModel : ObservableObject
{
    string status = "未启动";
    bool running, busy;

    public TunnelRowViewModel(TunnelDefinition definition, string via)
    {
        Definition = definition;
        Via = via;
    }

    public TunnelDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Via { get; }
    public string Local => $"127.0.0.1:{Definition.LocalPort}";
    public string Remote => Definition.IsDynamic ? "SOCKS 动态转发" : $"{Definition.RemoteHost}:{Definition.RemotePort}";
    public string Status { get => status; set => Set(ref status, value); }
    public bool IsRunning { get => running; set { if (Set(ref running, value)) Raise(nameof(IsStopped)); } }
    public bool IsStopped => !running;
    public bool IsBusy { get => busy; set => Set(ref busy, value); }
    public ICommand? StartCommand { get; set; }
    public ICommand? StopCommand { get; set; }
    public ICommand? EditCommand { get; set; }
    public ICommand? DeleteCommand { get; set; }
}

/// <summary>隧道 tab: saved local and dynamic (SOCKS) port forwards. Each running tunnel owns its own authenticated SSH
/// transport (the saved connection with its jump chain and proxy). Started and stopped only by the
/// user; a dropped transport stops the tunnel and is never reconnected automatically.</summary>
public sealed class TunnelsViewModel : ObservableObject, IDisposable
{
    readonly LibraryStore store;
    readonly ProfileConnector connector;
    readonly IStudioInteraction ui;
    readonly Dictionary<string, (SharedSshSession Session, LocalForward Forward)> running = new();
    readonly Dictionary<string, string> statuses = new();
    readonly HashSet<string> starting = new();
    /// <summary>Background teardown of stopped forwards, by local port: a restart waits for it.</summary>
    readonly Dictionary<int, Task> teardowns = new();
    readonly Action onStoreChanged;
    readonly CancellationTokenSource lifetime = new();
    bool disposed;
    string notice = "";

    public TunnelsViewModel(LibraryStore store, ProfileConnector connector, IStudioInteraction ui)
    {
        this.store = store;
        this.connector = connector;
        this.ui = ui;
        NewCommand = new AsyncCommand(() => EditAsync(null), e => Notice = WorkspaceViewModel.Short(e.Message));
        onStoreChanged = () => Dispatcher.UIThread.Post(() => { if (!disposed) Rebuild(); });
        store.Changed += onStoreChanged;
        Rebuild();
    }

    public ObservableCollection<TunnelRowViewModel> Rows { get; } = new();
    public bool IsEmpty => Rows.Count == 0;
    public string Notice { get => notice; private set { if (Set(ref notice, value)) Raise(nameof(HasNotice)); } }
    public bool HasNotice => Notice.Length > 0;
    public ICommand NewCommand { get; }
    public int RunningCount => running.Count;

    void Rebuild()
    {
        Rows.Clear();
        foreach (var t in store.Tunnels.OrderBy(t => t.Name, StringComparer.CurrentCulture))
        {
            var via = store.Find(t.ProfileId);
            var row = new TunnelRowViewModel(t, via == null ? "（连接已删除）" : (via.Name.Length > 0 ? via.Name : via.Host) + (via.JumpChain.Count > 0 ? $"（经 {via.JumpChain.Count} 级跳板）" : ""));
            if (running.TryGetValue(t.Id, out var active)) { row.IsRunning = true; row.Status = Running(t, active.Forward); }
            else if (statuses.TryGetValue(t.Id, out var last)) row.Status = last;
            row.IsBusy = starting.Contains(t.Id);
            string id = t.Id;
            row.StartCommand = new AsyncCommand(() => StartAsync(row), e => SetStatus(id, "启动失败：" + WorkspaceViewModel.Short(e.Message)));
            row.StopCommand = new RelayCommand(() => Stop(row.Id, "已停止"));
            row.EditCommand = new AsyncCommand(() => EditAsync(row.Definition), e => Notice = WorkspaceViewModel.Short(e.Message));
            row.DeleteCommand = new AsyncCommand(() => DeleteAsync(row), e => Notice = WorkspaceViewModel.Short(e.Message));
            Rows.Add(row);
        }
        Raise(nameof(IsEmpty));
        Raise(nameof(RunningCount));
    }

    /// <summary>Rows are rebuilt whenever the library changes, so state is always applied to the
    /// row currently shown for the tunnel id, not to the row a command was started from.</summary>
    void SetStatus(string id, string text, bool? isRunning = null, bool? isBusy = null)
    {
        statuses[id] = text;
        var row = Rows.FirstOrDefault(r => r.Id == id);
        if (row == null) return;
        row.Status = text;
        if (isRunning is { } r) row.IsRunning = r;
        if (isBusy is { } b) row.IsBusy = b;
    }

    public async Task StartAsync(TunnelRowViewModel row)
    {
        string id = row.Id;
        var definition = row.Definition;
        if (running.ContainsKey(id) || !starting.Add(id)) return;
        SharedSshSession? session = null;
        try
        {
            var profile = store.Find(definition.ProfileId) ?? throw new IOException("隧道经由的连接已删除");
            SetStatus(id, "正在连接…", isBusy: true);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(120));
            session = await connector.ConnectSshAsync(profile, timeout.Token);
            if (disposed) return; // window closed while connecting: the finally releases the session
            // Edit and delete are refused while starting; this only guards a library replaced underneath.
            if (!store.Tunnels.Contains(definition)) throw new IOException("隧道配置已变化，请重新启动");
            // A forward stopped a moment ago may still hold the port.
            if (teardowns.Remove(definition.LocalPort, out var previous)) await previous;
            if (disposed) return;
            var forward = definition.IsDynamic
                ? session.ForwardDynamic("127.0.0.1", (uint)definition.LocalPort)
                : session.ForwardLocal("127.0.0.1", (uint)definition.LocalPort, definition.RemoteHost, (uint)definition.RemotePort);
            running[id] = (session, forward);
            var bound = session;
            session.Faulted += message => Dispatcher.UIThread.Post(() =>
            {
                if (running.TryGetValue(id, out var active) && ReferenceEquals(active.Session, bound)) Stop(id, "连接已断开：" + WorkspaceViewModel.Short(message) + "（不会自动重连）");
            });
            session = null;
            SetStatus(id, Running(definition, forward), isRunning: true);
            Raise(nameof(RunningCount));
            // A fault between connecting and subscribing raised no event: check once now.
            if (bound.CloseReason is { } reason) Stop(id, "连接已断开：" + WorkspaceViewModel.Short(reason) + "（不会自动重连）");
        }
        catch (ObjectDisposedException e)
        {
            throw new IOException("连接已断开，转发未建立（不会自动重连）", e);
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or Renci.SshNet.Common.SshException)
        {
            throw new IOException($"无法在 127.0.0.1:{definition.LocalPort} 监听或建立转发：{e.Message}", e);
        }
        finally
        {
            session?.Dispose();
            starting.Remove(id);
            var current = Rows.FirstOrDefault(r => r.Id == id);
            if (current != null) current.IsBusy = false;
        }
    }

    static string Running(TunnelDefinition t, LocalForward forward) => (t.IsDynamic ? "运行中 · SOCKS 127.0.0.1:" : "运行中 · 127.0.0.1:") + forward.Port;

    public void Stop(string id, string status)
    {
        if (running.Remove(id, out var active))
        {
            // Stopping a forward waits for its open channels to close (bounded by the connect
            // timeout); never on the UI thread. The session closes once the forward's lease is gone.
            int port = (int)active.Forward.Port;
            teardowns[port] = Task.Run(() =>
            {
                try { active.Forward.Dispose(); } catch (Exception) { }
                try { active.Session.Dispose(); } catch (Exception) { }
            });
        }
        statuses[id] = status;
        var row = Rows.FirstOrDefault(r => r.Id == id);
        if (row != null) { row.IsRunning = false; row.Status = status; }
        Raise(nameof(RunningCount));
    }

    async Task EditAsync(TunnelDefinition? existing)
    {
        if (existing != null && starting.Contains(existing.Id)) { Notice = "隧道正在启动，请稍后再编辑"; return; }
        var ssh = store.Profiles.Where(p => p.IsSsh).ToList();
        if (ssh.Count == 0) { Notice = "请先在 SSH 页签保存一个连接，隧道需要经由它转发。"; return; }
        var result = await ui.EditTunnelAsync(existing, ssh);
        if (result == null) return;
        if (starting.Contains(result.Id)) { Notice = "隧道正在启动，请稍后再编辑"; return; }
        bool wasRunning = running.ContainsKey(result.Id);
        store.UpsertTunnel(result); // validates first: a rejected edit leaves a running tunnel alone
        if (wasRunning) Stop(result.Id, "配置已修改，请重新启动");
        Notice = "";
    }

    async Task DeleteAsync(TunnelRowViewModel row)
    {
        if (starting.Contains(row.Id)) { Notice = "隧道正在启动，请稍后再删除"; return; }
        if (!await ui.ConfirmAsync("删除隧道", $"删除隧道“{row.Name}”？运行中的转发会先停止。", "删除", danger: true)) return;
        if (starting.Contains(row.Id)) { Notice = "隧道正在启动，请稍后再删除"; return; }
        Stop(row.Id, "已删除");
        statuses.Remove(row.Id);
        store.RemoveTunnel(row.Id);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel(); // a connect in flight stops instead of prompting for a closed window
        store.Changed -= onStoreChanged;
        foreach (var id in running.Keys.ToList()) Stop(id, "已停止");
    }
}
