using Forterm.Studio.Launch;
using Forterm.Studio.Services;

namespace Forterm.Studio.ViewModels;

/// <summary>"关联文件会话": the file column of a terminal session shows the files of an open file session (xftp, SFTP, FTP).
/// The user chooses which; nothing is matched by host, address or name, and nothing logs in again. The file session keeps
/// its connection, queue and files; its tab is put away while it is shown elsewhere and comes back with the last link.
/// Drafts and transfers stay with the session they came from.</summary>
public sealed partial class WorkspaceViewModel
{
    WorkspaceViewModel? linked;
    readonly List<WorkspaceViewModel> viewers = new();

    /// <summary>Every open session; set by the window.</summary>
    internal Func<IEnumerable<WorkspaceViewModel>>? Sessions { get; set; }
    /// <summary>A link was made or ended: which tabs there are has changed. Set by the window.</summary>
    internal Action? LinksChanged { get; set; }

    bool IsFileSession => Kind is WorkspaceKind.Sftp or WorkspaceKind.Ftp;
    /// <summary>A terminal session may show the files of a file session.</summary>
    public bool CanLinkFiles => Kind == WorkspaceKind.Terminal;
    /// <summary>The session whose files the file column shows: this one, or the one it is linked to.</summary>
    public WorkspaceViewModel FileView => linked ?? this;
    /// <summary>The file column shows another session's files.</summary>
    public bool ShowsLinkedFiles => linked != null;
    /// <summary>This session's files are shown in another session.</summary>
    public bool IsLinked => viewers.Count > 0;
    /// <summary>A file session that is shown elsewhere has no tab, unless it has more than its files.</summary>
    public bool IsTabShown => !IsLinked || HasShell || Editors.Any();
    /// <summary>The session that shows the files of this one, the one in front first.</summary>
    internal WorkspaceViewModel? Viewer => viewers.FirstOrDefault(v => v.IsActive) ?? viewers.FirstOrDefault();
    /// <summary>Where things opened from the files of this session go: the session on screen. That is this one when it is
    /// in front (a file session with a terminal or editors of its own keeps its tab), otherwise the one showing its files.</summary>
    WorkspaceViewModel Desk => IsActive ? this : Viewer ?? this;
    public string FilesTitle => linked?.Title ?? "文件工作区";
    public string LinkTip => linked == null ? "关联已打开的文件会话（xftp / SFTP / FTP）" : $"正在显示“{linked.Title}”的文件";
    /// <summary>No files of its own, and none from elsewhere: the column offers to link.</summary>
    public bool OffersLink => CanLinkFiles && linked == null && !HasFiles && !IsBusy && connectStarted != 0;
    /// <summary>The files on show are of a connection that has ended.</summary>
    public bool IsLinkedSourceLost => IsLinked && !IsConnected && !IsBusy;

    /// <summary>The file sessions that can be shown here now.</summary>
    public IReadOnlyList<WorkspaceViewModel> LinkChoices =>
        CanLinkFiles && Sessions != null ? Sessions().Where(w => w.IsFileSession && w.HasFiles && (w.IsConnected || ReferenceEquals(w, linked))).ToArray() : [];

    /// <summary>What the menu says about a file session.</summary>
    public string LinkLabel(WorkspaceViewModel source) => $"{source.Title}　{source.ProtocolLabel} {source.Host}:{source.Port}"
        + (source.viewers.FirstOrDefault(v => !ReferenceEquals(v, this)) is { } other ? $"（已关联到“{other.Title}”）" : "");

    public void Link(WorkspaceViewModel source)
    {
        if (disposed != 0 || source.disposed != 0 || ReferenceEquals(source, linked)) return;
        if (!CanLinkFiles || !source.IsFileSession || !source.HasFiles) { FileNotice = "只能关联已经打开的文件会话"; return; }
        EndLink();
        linked = source;
        source.viewers.Add(this);
        ShowFiles = true;
        RaiseLink();
        source.RaiseLink();
        LinksChanged?.Invoke();
        interaction.Toast(new ToastMessage(ToastKind.Success, source.IsTabShown ? $"已关联“{source.Title}”的文件" : $"已关联“{source.Title}”的文件，它的页签已收起；解除关联后恢复"));
    }

    /// <summary>The user ends the link: the file session has its tab again.</summary>
    public void Unlink()
    {
        if (linked is not { } source) return;
        EndLink();
        interaction.Toast(new ToastMessage(ToastKind.Info, source.IsLinked ? $"已解除关联“{source.Title}”" : $"已解除关联，“{source.Title}”恢复为单独的页签"));
    }

    void EndLink()
    {
        if (linked is not { } source) return;
        linked = null;
        source.viewers.Remove(this);
        RaiseLink();
        source.RaiseLink();
        LinksChanged?.Invoke();
    }

    /// <summary>This session is being closed: what it showed has its tab again, and those that showed it have their own files.</summary>
    internal void EndLinks()
    {
        EndLink();
        foreach (var viewer in viewers.ToArray()) viewer.EndLink();
    }

    /// <summary>A file session that is shown elsewhere got something that needs its tab, or no longer has it.</summary>
    void RaiseTab()
    {
        if (!IsLinked) return;
        Raise(nameof(IsTabShown));
        LinksChanged?.Invoke();
    }

    /// <summary>Which session is in front, or whether the one showing these files can have a terminal, has changed.</summary>
    void RaiseDesk()
    {
        if (IsLinked) Raise(nameof(CanUseTerminalFromFiles));
        linked?.Raise(nameof(CanUseTerminalFromFiles));
    }

    /// <summary>An editor of this file that is open anywhere in the window, where it is shown first.</summary>
    (WorkspaceViewModel Owner, EditorTabViewModel Editor)? OpenedAnywhere(WorkspaceViewModel desk, Guid sourceId, string path)
    {
        foreach (var w in new[] { desk }.Concat(Sessions?.Invoke() ?? []))
            if (w.disposed == 0 && w.Editors.FirstOrDefault(e => e.Source.SourceId == sourceId && e.Path == path) is { } editor) return (w, editor);
        return null;
    }

    void RaiseLink()
    {
        foreach (string name in new[] { nameof(FileView), nameof(ShowsLinkedFiles), nameof(IsLinked), nameof(IsTabShown), nameof(FilesTitle), nameof(LinkTip), nameof(OffersLink), nameof(IsLinkedSourceLost), nameof(CanUseTerminalFromFiles) }) Raise(name);
    }

    /// <summary>Unsaved drafts of this session's files that are open in other sessions.</summary>
    IEnumerable<EditorTabViewModel> DraftsElsewhere => actions is { } own && Sessions != null
        ? Sessions().Where(w => !ReferenceEquals(w, this)).SelectMany(w => w.Editors).Where(e => e.Source.SourceId == own.Source.SourceId && (e.IsDirty || e.IsSaving)) : [];
}
