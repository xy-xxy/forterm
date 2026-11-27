using System.Collections.ObjectModel;
using Forterm.Core;
using Forterm.Studio.Mvvm;

namespace Forterm.Studio.ViewModels;

/// <summary>Lists one directory. <c>refresh</c> bypasses any listing cache.</summary>
public delegate Task<IReadOnlyList<RemoteEntry>> DirectoryLister(string path, bool refresh, CancellationToken ct);

/// <summary>One row of the lazy file tree. A directory lists its children the first time it
/// is expanded; a failed or cancelled listing can be retried by expanding again.</summary>
public sealed class FileNodeViewModel : ObservableObject
{
    readonly DirectoryLister? list;
    readonly CancellationToken lifetime;
    Task? loading;
    bool expanded, selected;

    public FileNodeViewModel(RemoteEntry entry, DirectoryLister list, CancellationToken lifetime, FileNodeViewModel? parent = null)
    {
        Name = entry.Name;
        Path = entry.Path;
        IsDirectory = entry.Directory;
        IsLink = entry.Link;
        IsRegular = entry.Regular && !entry.Link;
        Size = entry.Size;
        Modified = entry.Modified;
        Parent = parent;
        this.list = list;
        this.lifetime = lifetime;
        if (IsDirectory) Children.Add(Message("加载中…", this));
    }

    FileNodeViewModel(string message, FileNodeViewModel? parent) { Name = message; Path = ""; IsPlaceholder = true; Parent = parent; }

    static FileNodeViewModel Message(string text, FileNodeViewModel? parent) => new(text, parent);

    public string Name { get; }
    public string Path { get; }
    public bool IsDirectory { get; }
    public bool IsLink { get; }
    /// <summary>A regular file (not a link, directory, FIFO, socket or device).</summary>
    public bool IsRegular { get; }
    public bool IsFile => !IsDirectory && !IsPlaceholder;
    public bool IsPlaceholder { get; }
    public long Size { get; }
    public DateTimeOffset? Modified { get; private set; }
    /// <summary>"1.2 M" for files; nothing for directories and links.</summary>
    public string SizeText => IsRegular ? Services.MonitorProbe.Bytes(Size) : "";
    /// <summary>In local time; the year only when it is another one.</summary>
    public string TimeText => Modified is { } m ? m.ToLocalTime() is var t && t.Year == DateTime.Now.Year ? t.ToString("MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) : t.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "";
    public string Tip => IsPlaceholder ? Name : Path + (IsRegular ? $"\n{Size:N0} 字节" : "") + (Modified is { } m ? $"\n修改于 {m.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)}" : "");
    public FileNodeViewModel? Parent { get; }
    /// <summary>Directory that contains this entry ("/" for top-level entries of a "/" root).</summary>
    public string ParentPath => Path.LastIndexOf('/') is var i and > 0 ? Path[..i] : "/";
    public ObservableCollection<FileNodeViewModel> Children { get; } = new();

    public bool IsExpanded
    {
        get => expanded;
        set { if (Set(ref expanded, value) && value) _ = EnsureLoadedAsync(); }
    }

    public bool IsSelected { get => selected; set => Set(ref selected, value); }

    public Task EnsureLoadedAsync() => IsDirectory && list != null ? loading ??= LoadAsync(false) : Task.CompletedTask;

    /// <summary>Lists again from the server (after an upload, rename, delete …).</summary>
    /// <param name="filling">Told when the listing is here and the rows are about to be exchanged.</param>
    public Task ReloadAsync(Action? filling = null)
    {
        if (!IsDirectory || list == null) return Task.CompletedTask;
        return loading = LoadAsync(true, filling);
    }

    IReadOnlyList<RemoteEntry>? listed;

    /// <summary>Hidden names that are shown nevertheless, because the user went there.</summary>
    readonly HashSet<string> revealed = new(StringComparer.Ordinal);

    /// <summary>Shows a hidden entry. True when it is shown now.</summary>
    public bool Reveal(string name)
    {
        if (Children.Any(c => c.Name == name)) return true;
        if (listed?.Any(e => e.Name == name) != true || !revealed.Add(name)) return false;
        Fill();
        return true;
    }

    /// <summary>The entry is to stay in sight; shown with the next <see cref="Rearrange"/>.</summary>
    public void Keep(string name) => revealed.Add(name);

    public void Conceal()
    {
        revealed.Clear();
        foreach (var child in Children) child.Conceal();
    }

    void Fill()
    {
        Merge(Children, listed!, e => new FileNodeViewModel(e, list!, lifetime, this), revealed);
        if (Children.Count == 0) Children.Add(Message(listed!.Count == 0 ? "空目录" : "只有隐藏文件", this));
    }

    /// <summary>Sorts and filters again what was listed, here and below; nothing is asked of the server.</summary>
    public void Rearrange()
    {
        if (listed != null && loading?.IsCompletedSuccessfully == true) Fill();
        foreach (var child in Children) child.Rearrange();
    }

    async Task LoadAsync(bool refresh, Action? filling = null)
    {
        try
        {
            listed = await list!(Path, refresh, lifetime);
            filling?.Invoke();
            Fill();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            loading = null;
        }
        catch (OperationCanceledException)
        {
            // Not the workspace closing: the listing timed out, or the file channel stopped. "加载中…" would stay for ever.
            filling?.Invoke();
            Children.Clear();
            Children.Add(Message("读取没有完成：服务器没有回应，或文件通道已停止；收起再展开可以重试", this));
            loading = null;
        }
        catch (Exception e)
        {
            filling?.Invoke();
            Children.Clear();
            Children.Add(Message("读取失败：" + WorkspaceViewModel.Short(e.Message), this));
            loading = null;
        }
    }

    /// <summary>Makes <paramref name="target"/> show a listing. What is still there keeps its row (and, for a
    /// directory, its expanded children), and rows are only moved or exchanged where they have to be, so that
    /// the selection stays.</summary>
    public static void Merge(ObservableCollection<FileNodeViewModel> target, IEnumerable<RemoteEntry> entries, Func<RemoteEntry, FileNodeViewModel> create, IReadOnlySet<string>? revealed = null)
    {
        var keep = target.Where(n => !n.IsPlaceholder).GroupBy(n => n.Name).ToDictionary(g => g.Key, g => g.First());
        var next = Arrange(entries, revealed).Select(e => keep.TryGetValue(e.Name, out var old) && old.IsSame(e) ? old.Follow(e) : create(e)).ToList();
        // First what is gone: otherwise every row behind it would be moved, and lose the keyboard.
        var stays = next.ToHashSet();
        for (int i = target.Count - 1; i >= 0; i--) if (!stays.Contains(target[i])) target.RemoveAt(i);
        for (int i = 0; i < next.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], next[i])) continue;
            int at = -1;
            for (int k = i + 1; k < target.Count && at < 0; k++) if (ReferenceEquals(target[k], next[i])) at = k;
            if (at >= 0) target.RemoveAt(at); // a move is the same for the tree
            target.Insert(i, next[i]);
        }
    }

    /// <summary>A file of another size is another row: what was confirmed for one size must not become true of the
    /// other.</summary>
    bool IsSame(RemoteEntry e) => IsDirectory == e.Directory && IsLink == e.Link && Path == e.Path && (IsDirectory || IsRegular == (e.Regular && !e.Link) && Size == e.Size);

    /// <summary>The same entry in a newer listing.</summary>
    FileNodeViewModel Follow(RemoteEntry entry)
    {
        if (Modified == entry.Modified) return this;
        Modified = entry.Modified;
        foreach (string name in new[] { nameof(Modified), nameof(TimeText), nameof(Tip) }) Raise(name);
        return this;
    }

    /// <summary>Everything in a directory, as plans (recursive download, delete, …) go through it.</summary>
    public static IEnumerable<RemoteEntry> Sort(IEnumerable<RemoteEntry> entries) =>
        entries.Where(e => e.Name is not ("." or ".."))
            .OrderByDescending(e => e.Directory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>What the tree shows: directories first, then in the chosen order (names decide between equals),
    /// hidden files as chosen. For showing only — plans use <see cref="Sort"/>, which leaves nothing out.</summary>
    public static IEnumerable<RemoteEntry> Arrange(IEnumerable<RemoteEntry> entries, IReadOnlySet<string>? revealed = null)
    {
        var p = Services.Appearance.Current;
        var ordered = entries.Where(e => e.Name is not ("." or "..") && (p.ShowHidden || !e.Name.StartsWith('.') || revealed?.Contains(e.Name) == true)).OrderByDescending(e => e.Directory);
        IOrderedEnumerable<RemoteEntry> By<T>(Func<RemoteEntry, T> key, IComparer<T>? order = null) => p.FileSortDescending ? ordered.ThenByDescending(key, order) : ordered.ThenBy(key, order);
        return (p.FileSort switch
        {
            "size" => By(e => e.Directory ? 0 : e.Size),
            "time" => By(e => e.Modified ?? DateTimeOffset.MinValue),
            "type" => By(e => e.Directory ? "" : System.IO.Path.GetExtension(e.Name), StringComparer.OrdinalIgnoreCase),
            _ => By(e => e.Name, StringComparer.OrdinalIgnoreCase),
        }).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
    }
}
