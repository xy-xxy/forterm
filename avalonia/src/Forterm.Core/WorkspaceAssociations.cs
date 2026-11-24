namespace Forterm.Core;

public enum WorkspaceProtocol { Local, Ssh, Sftp, Ftp }
public sealed record WorkspaceEntry(Guid Id, string Title, WorkspaceProtocol Protocol, bool Online);
public sealed record WorkspaceSources(IReadOnlyList<Guid> Sources, Guid Active);

/// <summary>Explicit UI associations only: no hostname matching, authentication or implicit connection.</summary>
public sealed class WorkspaceAssociations
{
    readonly Dictionary<Guid, WorkspaceEntry> entries = new();
    readonly List<Guid> order = new();
    readonly Dictionary<Guid, List<Guid>> groups = new();
    readonly Dictionary<Guid, Guid> selected = new();
    public IReadOnlyList<WorkspaceEntry> Entries => order.Select(id => entries[id]).ToArray();
    public void Add(WorkspaceEntry entry)
    {
        if (entries.ContainsKey(entry.Id)) throw new InvalidOperationException("会话标识重复");
        if (entries.Count >= 256) throw new IOException("最多同时打开256个会话");
        entries.Add(entry.Id, entry); order.Add(entry.Id); groups.Add(entry.Id, [entry.Id]); selected[entry.Id] = entry.Id;
    }
    WorkspaceEntry Require(Guid id) => entries.GetValueOrDefault(id) ?? throw new InvalidOperationException("会话已关闭");
    public void SetOnline(Guid id, bool online) { var entry = Require(id); entries[id] = entry with { Online = online }; }
    public WorkspaceSources Sources(Guid owner) { Require(owner); return new(groups[owner].ToArray(), selected[owner]); }
    public void Attach(Guid owner, Guid source)
    {
        Require(owner); var target = Require(source);
        if (target.Protocol == WorkspaceProtocol.Local || !target.Online) throw new IOException("请明确选择已有可用文件通道的远程会话");
        var list = groups[owner];
        if (!list.Contains(source)) { if (list.Count >= 32) throw new IOException("每个工作区最多关联32个来源"); list.Add(source); }
        selected[owner] = source;
    }
    public void Select(Guid owner, Guid source)
    { Require(owner); if (!groups[owner].Contains(source)) throw new InvalidOperationException("请先明确关联文件来源"); selected[owner] = source; }
    public void Detach(Guid owner, Guid source)
    {
        Require(owner); if (owner == source) return; groups[owner].Remove(source);
        if (selected[owner] == source) selected[owner] = groups[owner][^1];
    }
    public IReadOnlyList<Guid> Referrers(Guid source) => order.Where(id => id != source && groups[id].Contains(source)).ToArray();
    public void Close(Guid id)
    {
        entries.Remove(id); order.Remove(id); groups.Remove(id); selected.Remove(id);
        // Other groups may retain a closed source tab for explicit removal and draft provenance.
    }
    public IReadOnlyList<WorkspaceEntry> Visible()
    {
        var hidden = order.Select(id => entries[id]).Where(e => e.Protocol is WorkspaceProtocol.Sftp or WorkspaceProtocol.Ftp && Referrers(e.Id).Count > 0).Select(e => e.Id).ToHashSet();
        foreach (var entry in order.Select(id => entries[id]))
        {
            if (!hidden.Contains(entry.Id)) continue;
            var seen = new HashSet<Guid>(); var queue = new Queue<Guid>(Referrers(entry.Id)); bool reachable = false;
            while (queue.TryDequeue(out var parent))
            {
                if (!seen.Add(parent)) continue;
                if (!hidden.Contains(parent)) { reachable = true; break; }
                foreach (var next in Referrers(parent)) queue.Enqueue(next);
            }
            if (!reachable) hidden.Remove(entry.Id); // Keep one reachable root in a file-only cycle.
        }
        return order.Select(id => entries[id]).Where(e => !hidden.Contains(e.Id)).ToArray();
    }
    public Guid? VisibleOwner(Guid source)
    {
        var visible = Visible().Select(e => e.Id).ToHashSet(); var seen = new HashSet<Guid>(); var queue = new Queue<Guid>(); queue.Enqueue(source);
        while (queue.TryDequeue(out var current)) { if (!seen.Add(current)) continue; if (visible.Contains(current)) return current; foreach (var parent in Referrers(current)) queue.Enqueue(parent); }
        return null;
    }
}
