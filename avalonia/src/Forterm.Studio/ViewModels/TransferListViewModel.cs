using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;

namespace Forterm.Studio.ViewModels;

/// <summary>How a run of transfers and file operations ended.</summary>
public sealed record TransferBatch(int Uploads, int Downloads, int Operations, IReadOnlyList<string> Failures);

/// <summary>How fast one transfer is. Only the time in which bytes move counts: checking and committing stand still.</summary>
public sealed class TransferMeter
{
    /// <summary>The speed shown is that of the last seconds.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(3);
    readonly List<(TimeSpan At, long Bytes)> samples = new();
    TimeSpan moving;
    long moved;

    /// <summary>Bytes per second lately; null until there is something to tell.</summary>
    public double? PerSecond { get; private set; }
    /// <summary>Bytes per second over all of the time in which bytes moved.</summary>
    public double? Average => moving >= TimeSpan.FromMilliseconds(50) && moved > 0 ? moved / moving.TotalSeconds : null;
    public TimeSpan Moving => moving;

    public void Add(TimeSpan now, long bytes)
    {
        if (samples.Count > 0)
        {
            var last = samples[^1];
            // Another file of a folder, or the same one from its start: counted from here.
            if (bytes < last.Bytes) { samples.Clear(); samples.Add((now, bytes)); PerSecond = null; return; }
            if (now < last.At) return;
            moving += now - last.At;
            moved += bytes - last.Bytes;
            // Many within a moment are one.
            if (samples.Count > 1 && now - samples[^2].At < TimeSpan.FromMilliseconds(150)) samples[^1] = (now, bytes);
            else samples.Add((now, bytes));
        }
        else samples.Add((now, bytes));
        while (samples.Count > 2 && now - samples[1].At >= Window) samples.RemoveAt(0);
        var span = now - samples[0].At;
        PerSecond = span >= TimeSpan.FromMilliseconds(300) ? (bytes - samples[0].Bytes) / span.TotalSeconds : PerSecond;
    }

    /// <summary>Bytes do not move now; what comes next does not count the pause.</summary>
    public void Rest() { samples.Clear(); PerSecond = null; }
}

public sealed class TransferItemViewModel : ObservableObject
{
    readonly TransferMeter meter = new();
    TransferSnapshot snapshot;

    public TransferItemViewModel(TransferSnapshot snapshot, TransferKind kind, Action<Guid> cancel)
    {
        this.snapshot = snapshot;
        Kind = kind;
        CancelCommand = new RelayCommand(() => cancel(Id));
    }

    /// <summary>Bytes are on their way (not queued, not being checked or committed).</summary>
    public bool IsMoving => snapshot is { State: TransferState.Running, Unit: "B" } && Kind != TransferKind.Operation;
    public double? Speed => IsMoving ? meter.PerSecond : null;
    /// <summary>"3.2 MB/s" while it runs, the average when it has ended.</summary>
    public string SpeedText => IsMoving ? meter.PerSecond is { } now ? Bytes((long)now) + "/s" : ""
        : !IsActive && Kind != TransferKind.Operation && meter.Average is { } average ? "平均 " + Bytes((long)average) + "/s" : "";
    /// <summary>"剩余 12 秒"; nothing while it stands still.</summary>
    public string RemainingText => IsMoving && snapshot.Total is { } total && meter.PerSecond is >= 1 and var speed && total > snapshot.Bytes ? "剩余 " + Duration(TimeSpan.FromSeconds(Math.Min((total - snapshot.Bytes) / speed, 359_999))) : "";
    public bool HasSpeed => SpeedText.Length > 0;

    /// <summary>The amount as it is now; also when nothing has changed, since that is a speed of zero.</summary>
    public void Measure(TimeSpan now)
    {
        if (IsMoving) meter.Add(now, snapshot.Bytes);
        else meter.Rest();
        Raise(nameof(Speed)); Raise(nameof(SpeedText)); Raise(nameof(RemainingText)); Raise(nameof(HasSpeed)); Raise(nameof(IsMoving));
    }

    static string Duration(TimeSpan t) => t.TotalSeconds < 60 ? Math.Max(1, (int)Math.Ceiling(t.TotalSeconds)) + " 秒"
        : t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes} 分 {t.Seconds:00} 秒" : $"{(int)t.TotalHours} 小时 {t.Minutes:00} 分";

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

    public void Update(TransferSnapshot next, TimeSpan now)
    {
        bool moved = IsMoving;
        if (next == snapshot) return;
        // The last bytes count, although it is being checked by the time they are seen.
        if (moved && next.Unit == "B" && next.State != TransferState.Running && next.Bytes >= snapshot.Bytes) meter.Add(now, next.Bytes);
        snapshot = next;
        Measure(now);
        Raise(nameof(Name)); Raise(nameof(IsActive)); Raise(nameof(CanCancel)); Raise(nameof(IsFailed)); Raise(nameof(IsDone));
        Raise(nameof(Percent)); Raise(nameof(HasProgress)); Raise(nameof(StateText)); Raise(nameof(AmountText));
    }

    internal static string Bytes(long b) => b switch
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
    readonly TimeProvider time;
    readonly long began;
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(500) };
    string filter = "all";
    bool refreshQueued;

    public TransferListViewModel(TransferQueue queue, TimeProvider? time = null)
    {
        this.queue = queue;
        this.time = time ?? TimeProvider.System;
        began = this.time.GetTimestamp();
        // A transfer that stands still reports nothing; its speed falls all the same.
        clock.Tick += (_, _) => Tick();
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
    TimeSpan Now => time.GetElapsedTime(began);
    string Speeds(TransferKind kind) => items.Values.Where(i => i.Kind == kind && i.Speed != null).ToList() is { Count: > 0 } moving ? TransferItemViewModel.Bytes((long)moving.Sum(i => i.Speed!.Value)) + "/s" : "";
    /// <summary>All uploads together, and all downloads: "3.2 MB/s"; "" when none is moving.</summary>
    public string UploadSpeed => Speeds(TransferKind.Upload);
    public string DownloadSpeed => Speeds(TransferKind.Download);
    public bool IsUploading => UploadSpeed.Length > 0;
    public bool IsDownloading => DownloadSpeed.Length > 0;
    void RaiseSpeeds() { Raise(nameof(UploadSpeed)); Raise(nameof(DownloadSpeed)); Raise(nameof(IsUploading)); Raise(nameof(IsDownloading)); }

    internal void Tick()
    {
        var now = Now;
        foreach (var item in items.Values) if (item.IsMoving) item.Measure(now);
        RaiseSpeeds();
        Run();
    }
    void Run()
    {
        bool moving = items.Values.Any(i => i.IsMoving);
        if (moving == clock.IsEnabled) return;
        if (moving) clock.Start(); else clock.Stop();
    }
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
            if (items.TryGetValue(s.Id, out var item)) item.Update(s, Now);
            else
            {
                TransferKind kind;
                lock (kinds) kind = kinds.GetValueOrDefault(s.Id, TransferKind.Operation);
                items[s.Id] = new TransferItemViewModel(s, kind, queue.Cancel);
                items[s.Id].Measure(Now);
            }
        }
        Rebuild();
        FollowBatch();
    }

    void Rebuild()
    {
        var wanted = items.Values.Where(i => filter switch { "up" => i.IsUpload, "down" => i.IsDownload, _ => true })
            .OrderByDescending(i => i.IsActive).ToList();
        // Rows that stay are left alone: several times a second is too often to build them again.
        for (int i = Visible.Count - 1; i >= 0; i--) if (!wanted.Contains(Visible[i])) Visible.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            int at = Visible.IndexOf(wanted[i]);
            if (at < 0) Visible.Insert(i, wanted[i]);
            else if (at != i) Visible.Move(at, i);
        }
        Raise(nameof(AllCount)); Raise(nameof(UploadCount)); Raise(nameof(DownloadCount)); Raise(nameof(ActiveCount)); Raise(nameof(HasActive)); Raise(nameof(IsEmpty));
        RaiseSpeeds();
        Run();
    }

    public void Dispose() { queue.Changed -= OnChanged; clock.Stop(); }
}
