using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;

namespace Forterm.Studio.ViewModels;

/// <summary>How a run of transfers and file operations ended.</summary>
public sealed record TransferBatch(int Uploads, int Downloads, int Operations, IReadOnlyList<string> Failures);

public sealed class TransferItemViewModel : ObservableObject
{
    TransferSnapshot snapshot;

    public TransferItemViewModel(TransferSnapshot snapshot, TransferKind kind, Action<Guid> cancel)
    {
        this.snapshot = snapshot;
        Kind = kind;
        CancelCommand = new RelayCommand(() => cancel(Id));
    }

    public Guid Id => snapshot.Id;
    public TransferKind Kind { get; }
    public string Name => snapshot.Name;
    public string Source => snapshot.Source;
    public bool IsUpload => Kind == TransferKind.Upload;
    public bool IsDownload => Kind == TransferKind.Download;
    public bool IsOperation => Kind == TransferKind.Operation;
    public bool IsActive => snapshot.State is TransferState.Queued or TransferState.Running or TransferState.Verifying or TransferState.Committing;
    public bool CanCancel => snapshot.State is TransferState.Queued or TransferState.Running or TransferState.Verifying;
    public bool IsFailed => snapshot.State is TransferState.Failed or TransferState.Uncertain;
    public bool IsDone => snapshot.State == TransferState.Completed;
    public double Percent => snapshot.Total is > 0 and var total ? Math.Clamp(snapshot.Bytes * 100.0 / total, 0, 100) : IsDone ? 100 : 0;
    public bool HasProgress => snapshot.Total is > 0 || IsDone;
    public string StateText => snapshot.State switch
    {
        TransferState.Queued => "排队中",
        TransferState.Running => snapshot.Message.Length > 0 ? snapshot.Message : "进行中",
        TransferState.Verifying => "校验中",
        TransferState.Committing => "提交中",
        TransferState.Completed => "已完成",
        TransferState.Cancelled => "已取消",
        TransferState.Uncertain => "需核对：" + snapshot.Message,
        _ => "失败：" + snapshot.Message,
    };
    public string AmountText => snapshot.Total is { } total
        ? snapshot.Unit == "B" ? $"{Bytes(snapshot.Bytes)} / {Bytes(total)}" : $"{snapshot.Bytes}/{total} {snapshot.Unit}"
        : snapshot.Bytes > 0 && snapshot.Unit == "B" ? Bytes(snapshot.Bytes) : "";
    public ICommand CancelCommand { get; }

    public void Update(TransferSnapshot next)
    {
        if (next == snapshot) return;
        snapshot = next;
        Raise(nameof(Name)); Raise(nameof(IsActive)); Raise(nameof(CanCancel)); Raise(nameof(IsFailed)); Raise(nameof(IsDone));
        Raise(nameof(Percent)); Raise(nameof(HasProgress)); Raise(nameof(StateText)); Raise(nameof(AmountText));
    }

    static string Bytes(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{b / 1024.0:0.0} KB",
        _ => b + " B",
    };
}

/// <summary>"传输管理": the workspace's serial queue as a filterable list.</summary>
public sealed class TransferListViewModel : ObservableObject, IDisposable
{
    readonly TransferQueue queue;
    readonly Dictionary<Guid, TransferKind> kinds = new();
    readonly Dictionary<Guid, TransferItemViewModel> items = new();
    string filter = "all";
    bool refreshQueued;

    public TransferListViewModel(TransferQueue queue)
    {
        this.queue = queue;
        queue.Changed += OnChanged;
        ShowAllCommand = new RelayCommand(() => Filter = "all");
        ShowUploadsCommand = new RelayCommand(() => Filter = "up");
        ShowDownloadsCommand = new RelayCommand(() => Filter = "down");
        ClearFinishedCommand = new RelayCommand(() => queue.ClearFinished());
    }

    public ObservableCollection<TransferItemViewModel> Visible { get; } = new();
    public int AllCount => items.Count;
    public int UploadCount => items.Values.Count(i => i.IsUpload);
    public int DownloadCount => items.Values.Count(i => i.IsDownload);
    public int ActiveCount => items.Values.Count(i => i.IsActive);
    public bool HasActive => ActiveCount > 0;
    public bool IsEmpty => Visible.Count == 0;
    public string Filter { get => filter; set { if (Set(ref filter, value)) { Raise(nameof(FilterAll)); Raise(nameof(FilterUp)); Raise(nameof(FilterDown)); Rebuild(); } } }
    public bool FilterAll => filter == "all";
    public bool FilterUp => filter == "up";
    public bool FilterDown => filter == "down";
    public ICommand ShowAllCommand { get; }
    public ICommand ShowUploadsCommand { get; }
    public ICommand ShowDownloadsCommand { get; }
    public ICommand ClearFinishedCommand { get; }

    public void Register(Guid id, TransferKind kind) { lock (kinds) kinds[id] = kind; OnChanged(); }

    /// <summary>Queue changes arrive on the worker thread; coalesce onto the UI thread.</summary>
    void OnChanged()
    {
        lock (kinds) { if (refreshQueued) return; refreshQueued = true; }
        Dispatcher.UIThread.Post(() => { lock (kinds) refreshQueued = false; Refresh(); });
    }

    readonly HashSet<Guid> batch = new();
    /// <summary>Everything that was queued has ended (UI thread): how many of each, and the first failure.</summary>
    public event Action<TransferBatch>? BatchEnded;

    void FollowBatch()
    {
        foreach (var i in items.Values.Where(i => i.IsActive)) batch.Add(i.Id);
        if (batch.Count == 0 || HasActive) return;
        var ended = items.Values.Where(i => batch.Contains(i.Id)).ToList();
        batch.Clear();
        if (ended.Count > 0) BatchEnded?.Invoke(new TransferBatch(ended.Count(i => i.IsUpload && i.IsDone), ended.Count(i => i.IsDownload && i.IsDone), ended.Count(i => i.IsOperation && i.IsDone), ended.Where(i => i.IsFailed).Select(i => $"{i.Name}：{i.StateText}").ToArray()));
    }

    public void Refresh()
    {
        var snapshot = queue.Snapshot();
        var present = snapshot.Select(s => s.Id).ToHashSet();
        foreach (var gone in items.Keys.Where(id => !present.Contains(id)).ToList()) items.Remove(gone);
        foreach (var s in snapshot)
        {
            if (items.TryGetValue(s.Id, out var item)) item.Update(s);
            else
            {
                TransferKind kind;
                lock (kinds) kind = kinds.GetValueOrDefault(s.Id, TransferKind.Operation);
                items[s.Id] = new TransferItemViewModel(s, kind, queue.Cancel);
            }
        }
        Rebuild();
        FollowBatch();
    }

    void Rebuild()
    {
        var wanted = items.Values.Where(i => filter switch { "up" => i.IsUpload, "down" => i.IsDownload, _ => true })
            .OrderByDescending(i => i.IsActive).ToList();
        Visible.Clear();
        foreach (var item in wanted) Visible.Add(item);
        Raise(nameof(AllCount)); Raise(nameof(UploadCount)); Raise(nameof(DownloadCount)); Raise(nameof(ActiveCount)); Raise(nameof(HasActive)); Raise(nameof(IsEmpty));
    }

    public void Dispose() => queue.Changed -= OnChanged;
}
