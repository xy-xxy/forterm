using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using Forterm.Core;
using Forterm.Studio.Controls;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;

namespace Forterm.Studio.ViewModels;

public sealed record ProcessRow(ProcessInfo Info)
{
    public int Pid => Info.Pid;
    public string User => Info.User;
    public string State => Info.State;
    public string Cpu => Info.Cpu.ToString("0.0", CultureInfo.InvariantCulture) + "%";
    public string Memory => MonitorProbe.Bytes(Info.RssBytes);
    public string Started => Info.Started;
    public string Command => Info.Command;
    public bool CanSignal => Info.Pid > 1;
    /// <summary>Bars of the process manager: the share of one core, and of the host's memory.</summary>
    public double CpuShare => Math.Clamp(Info.Cpu, 0, 100);
    public double MemoryShare { get; init; }
    public string Name => MonitorViewModel.SplitCommand(Info.Command).Name;
    public string Arguments => MonitorViewModel.SplitCommand(Info.Command).Arguments;
    /// <summary>ps's state letter in words; the letters themselves are in the tip.</summary>
    public string StateText => Info.State.Length == 0 ? "" : Info.State[0] switch { 'R' => "运行", 'S' => "睡眠", 'D' => "等待磁盘", 'Z' => "僵尸", 'T' or 't' => "已停止", 'I' => "空闲", 'X' => "退出", _ => Info.State };
    public bool IsStuck => Info.State.Length > 0 && Info.State[0] is 'D' or 'Z';
}

/// <summary>A meter of the overview. Rows are updated in place, so hovering one survives a refresh.</summary>
public sealed class VitalItem(string section, string name) : ObservableObject
{
    double percent, secondary;
    string figure = "—", note = "";
    MonitorLevel level;
    public string Section { get; } = section;
    public string Name { get; private set; } = name;
    public double Percent { get => percent; set => Set(ref percent, value); }
    public double Secondary { get => secondary; set => Set(ref secondary, value); }
    public string Figure { get => figure; set => Set(ref figure, value); }
    public string Note { get => note; set => Set(ref note, value); }
    public MonitorLevel Level { get => level; set { if (Set(ref level, value)) { Raise(nameof(IsWarning)); Raise(nameof(IsDanger)); } } }
    public bool IsWarning => level == MonitorLevel.Warning;
    public bool IsDanger => level == MonitorLevel.Danger;
    public void Rename(string value) { if (Name != value) { Name = value; Raise(nameof(Name)); } }
}

/// <summary>One file system.</summary>
public sealed class MountItem : ObservableObject
{
    string mount = "", usedText = "", space = "", rates = "", detail = "";
    double percent;
    MonitorLevel level;
    public string Mount { get => mount; set => Set(ref mount, value); }
    public string UsedText { get => usedText; set => Set(ref usedText, value); }
    public double Percent { get => percent; set => Set(ref percent, value); }
    public MonitorLevel Level { get => level; set { if (Set(ref level, value)) { Raise(nameof(IsWarning)); Raise(nameof(IsDanger)); } } }
    public bool IsWarning => level == MonitorLevel.Warning;
    public bool IsDanger => level == MonitorLevel.Danger;
    public string Space { get => space; set => Set(ref space, value); }
    public string Rates { get => rates; set => Set(ref rates, value); }
    public string Detail { get => detail; set => Set(ref detail, value); }
}

/// <summary>One of the busiest processes, with its share of the busiest one as a bar.</summary>
public sealed class ProcessItem : ObservableObject
{
    ProcessRow? row;
    string name = "", arguments = "", value = "", tip = "";
    double share;
    public ProcessRow? Row { get => row; set => Set(ref row, value); }
    public string Name { get => name; set => Set(ref name, value); }
    public string Arguments { get => arguments; set => Set(ref arguments, value); }
    public string Value { get => value; set => Set(ref this.value, value); }
    public string Tip { get => tip; set => Set(ref tip, value); }
    public double Share { get => share; set => Set(ref share, value); }
}

public sealed class InterfaceItem : ObservableObject
{
    string name = "", down = "", up = "";
    public string Name { get => name; set => Set(ref name, value); }
    public string Down { get => down; set => Set(ref down, value); }
    public string Up { get => up; set => Set(ref up, value); }
}

/// <summary>A choice in the refresh / time-range menu.</summary>
public sealed class MonitorChoice(string title, int value) : ObservableObject
{
    bool selected;
    public string Title { get; } = title;
    public int Value { get; } = value;
    public bool IsSelected { get => selected; set => Set(ref selected, value); }
}

/// <summary>Monitor column: first what is wrong, then four meters, then the details. Reads the fixed probe on
/// the workspace's SSH transport (separate exec channel), one request at a time with an 8-second limit.
/// Auto refresh runs only while the column is visible and the workspace tab is active; hiding it cancels the
/// loop. A failed reading stops it, and the column says so.</summary>
public sealed class MonitorViewModel : ObservableObject, IDisposable
{
    /// <summary>Half an hour at the shortest interval.</summary>
    public const int HistoryLength = 600;
    readonly Func<SharedSshSession?> session;
    readonly List<MonitorPoint> history = new();
    MonitorSample? previous, first;
    CancellationTokenSource? loop;
    readonly CancellationTokenSource lifetime = new();
    int refreshing, disposed, interval, minutes = 3;
    bool visible, hasData, loading, stalled, byMemory;
    string message = "", systemLine = "", updated = "", cpuText = "—", user = "—", system = "—", wait = "—", steal = "—", load = "—", memText = "—", memUsed = "", memCache = "", memFree = "", memTotal = "",
        swap = "", upSpeed = "—", downSpeed = "—", upTotal = "—", downTotal = "—", readSpeed = "—", writeSpeed = "—", osName = "";
    IReadOnlyList<CoreRow> cores = Array.Empty<CoreRow>();
    CoreRow? hoveredCore;
    DateTime? hoverTime;
    IReadOnlyList<ProcessInfo> processes = Array.Empty<ProcessInfo>();

    /// <summary>Seconds between readings when refreshing by itself.</summary>
    public const int DefaultInterval = 3;

    public MonitorViewModel(string host, Func<SharedSshSession?> session, bool autoRefresh)
    {
        Host = host;
        this.session = session;
        interval = autoRefresh ? DefaultInterval : 0;
        RefreshCommand = new AsyncCommand(RefreshByUserAsync);
        ToggleAutoCommand = new RelayCommand(() => AutoRefresh = !AutoRefresh);
        IntervalCommand = new RelayCommand(p => { if (p is MonitorChoice c) Interval = c.Value; });
        RangeCommand = new RelayCommand(p => { if (p is MonitorChoice c) Minutes = c.Value; });
        ToggleFindingsCommand = new RelayCommand(() => { allFindings = !allFindings; RaiseFindings(); });
        SortByCpuCommand = new RelayCommand(() => SortByMemory = false);
        SortByMemoryCommand = new RelayCommand(() => SortByMemory = true);
        ShowProcessCommand = new RelayCommand(p => { if (p is ProcessItem { Row: { } row }) ShowProcessesCommand?.Execute(row.Pid.ToString(CultureInfo.InvariantCulture)); });
        CopyCommand = new AsyncCommand(async () => { if (CopyText != null) await CopyText(HistoryCsv()); });
        Vitals = [new VitalItem("cpu", "CPU"), new VitalItem("memory", "内存"), new VitalItem("disk", "磁盘"), new VitalItem("cpu", "负载")];
        MarkChoices();
    }

    public string Host { get; }

    // ---------- refreshing ----------

    public IReadOnlyList<MonitorChoice> Intervals { get; } = [new("手动", 0), new("每 3 秒", 3), new("每 10 秒", 10), new("每 30 秒", 30)];
    public IReadOnlyList<MonitorChoice> Ranges { get; } = [new("最近 3 分钟", 3), new("最近 10 分钟", 10), new("最近 30 分钟", 30)];
    void MarkChoices()
    {
        foreach (var c in Intervals) c.IsSelected = c.Value == interval;
        foreach (var c in Ranges) c.IsSelected = c.Value == minutes;
    }

    /// <summary>Seconds between readings; 0 = only when the user asks.</summary>
    public int Interval
    {
        get => interval;
        set
        {
            if (!Set(ref interval, Math.Max(0, value))) return;
            MarkChoices();
            Raise(nameof(AutoRefresh));
            Raise(nameof(RefreshText));
            Raise(nameof(TrendGap));
            IsStalled = false;
            Restart();
        }
    }
    public bool AutoRefresh { get => interval > 0; set => Interval = value ? interval > 0 ? interval : DefaultInterval : 0; }
    /// <summary>What the refresh button says: the interval, or why it stands still.</summary>
    public string RefreshText => stalled ? "已暂停" : interval == 0 ? "手动" : $"{interval} 秒";
    public int Minutes { get => minutes; set { if (Set(ref minutes, value)) { MarkChoices(); Raise(nameof(TrendWindow)); Raise(nameof(RangeText)); } } }
    public string RangeText => $"最近 {minutes} 分钟";
    public TimeSpan TrendWindow => TimeSpan.FromMinutes(minutes);
    /// <summary>Readings further apart than this were not taken one after the other.</summary>
    public TimeSpan TrendGap => TimeSpan.FromSeconds(Math.Max(12, interval * 3));

    public bool HasData { get => hasData; private set => Set(ref hasData, value); }
    public bool IsLoading { get => loading; private set => Set(ref loading, value); }
    /// <summary>Refreshing by itself ended with a failure: the numbers shown are old.</summary>
    public bool IsStalled { get => stalled; private set { if (Set(ref stalled, value)) { Raise(nameof(RefreshText)); Raise(nameof(ContentOpacity)); } } }
    public double ContentOpacity => stalled ? 0.5 : 1;
    public string Message { get => message; private set { if (Set(ref message, value)) Raise(nameof(HasMessage)); } }
    public bool HasMessage => Message.Length > 0;
    public string UpdatedText { get => updated; private set => Set(ref updated, value); }

    // ---------- overview ----------

    public string OsName { get => osName; private set => Set(ref osName, value); }
    /// <summary>"Ubuntu 24.04.5 LTS · 8 核 · 已运行 12 天 · CST GMT+0800".</summary>
    public string SystemLine { get => systemLine; private set => Set(ref systemLine, value); }
    /// <summary>Worst first; empty when nothing stands out.</summary>
    public IReadOnlyList<MonitorFinding> Findings { get; private set; } = Array.Empty<MonitorFinding>();
    /// <summary>Nothing was found although something was there to look at.</summary>
    public bool IsHealthy => Findings.Count == 0 && measured;
    bool measured, cpuJudged;
    public bool IsSerious => Findings.Any(f => f.IsDanger);
    public bool IsWarned => Findings.Count > 0 && !IsSerious;
    public string Headline
    {
        get
        {
            int serious = Findings.Count(f => f.IsDanger), warned = Findings.Count - serious;
            return serious > 0 ? $"{serious} 项严重" + (warned > 0 ? $"，{warned} 项注意" : "") : warned > 0 ? $"{warned} 项需要注意"
                : !measured ? "没有读到可用的数据" : cpuJudged ? "运行正常" : "暂无异常，CPU 观察中";
        }
    }
    const int FewFindings = 3;
    bool allFindings;
    /// <summary>The worst three, or all of them when the user asked.</summary>
    public IReadOnlyList<MonitorFinding> ShownFindings => allFindings ? Findings : Findings.Take(FewFindings).ToArray();
    public bool HasMoreFindings => Findings.Count > FewFindings;
    public string MoreFindingsText => allFindings ? "收起" : $"另有 {Findings.Count - FewFindings} 项…";
    public ICommand ToggleFindingsCommand { get; }
    void RaiseFindings() { foreach (string name in new[] { nameof(Findings), nameof(IsHealthy), nameof(IsSerious), nameof(IsWarned), nameof(Headline), nameof(ShownFindings), nameof(HasMoreFindings), nameof(MoreFindingsText) }) Raise(name); }
    /// <summary>The overview's meters, in fixed places: CPU, memory, the fullest disk, load.</summary>
    public VitalItem[] Vitals { get; }

    // ---------- CPU ----------

    public string CpuText { get => cpuText; private set => Set(ref cpuText, value); }
    public string UserText { get => user; private set => Set(ref user, value); }
    public string KernelText { get => system; private set => Set(ref system, value); }
    public string WaitText { get => wait; private set => Set(ref wait, value); }
    public string StealText { get => steal; private set => Set(ref steal, value); }
    public string LoadText { get => load; private set => Set(ref load, value); }
    public IReadOnlyList<CoreRow> Cores { get => cores; private set { if (Set(ref cores, value)) Raise(nameof(CoreCaption)); } }
    public CoreRow? HoveredCore { get => hoveredCore; set { if (Set(ref hoveredCore, value)) Raise(nameof(CoreCaption)); } }
    /// <summary>The core under the pointer, otherwise the busiest one.</summary>
    public string CoreCaption => hoveredCore is { } h ? $"核 {h.Index}：{cores.FirstOrDefault(c => c.Index == h.Index)?.Text ?? h.Text}"
        : cores.Where(c => c.Percent != null).MaxBy(c => c.Percent) is { } busiest ? $"{cores.Count} 核，最忙 核 {busiest.Index} {busiest.Text}" : cores.Count > 0 ? $"{cores.Count} 核" : "核数未知";

    // ---------- memory ----------

    /// <summary>All of the host's memory; 0 while unknown.</summary>
    public double MemoryBytes { get; private set; }
    public string MemoryText { get => memText; private set => Set(ref memText, value); }
    public string MemoryTotal { get => memTotal; private set => Set(ref memTotal, value); }
    public string MemoryUsed { get => memUsed; private set => Set(ref memUsed, value); }
    public string MemoryCache { get => memCache; private set => Set(ref memCache, value); }
    /// <summary>What programs can still get: free memory plus cache that can be given back.</summary>
    public string MemoryAvailable { get => memFree; private set => Set(ref memFree, value); }
    public string SwapText { get => swap; private set => Set(ref swap, value); }

    // ---------- network, disks, processes ----------

    public string UploadSpeed { get => upSpeed; private set => Set(ref upSpeed, value); }
    public string DownloadSpeed { get => downSpeed; private set => Set(ref downSpeed, value); }
    public string UploadTotal { get => upTotal; private set => Set(ref upTotal, value); }
    public string DownloadTotal { get => downTotal; private set => Set(ref downTotal, value); }
    public ObservableCollection<InterfaceItem> Interfaces { get; } = new();
    public bool HasInterfaces => Interfaces.Count > 1;
    public string ReadSpeed { get => readSpeed; private set => Set(ref readSpeed, value); }
    public string WriteSpeed { get => writeSpeed; private set => Set(ref writeSpeed, value); }
    public ObservableCollection<MountItem> Mounts { get; } = new();
    public ObservableCollection<ProcessItem> TopProcesses { get; } = new();
    public bool HasProcesses => TopProcesses.Count > 0;
    public bool SortByMemory { get => byMemory; set { if (Set(ref byMemory, value)) { Raise(nameof(SortByCpu)); ShowTopProcesses(); } } }
    public bool SortByCpu => !byMemory;
    public IReadOnlyList<ProcessInfo> Processes => processes;

    // ---------- trends ----------

    public DateTime? HoverTime { get => hoverTime; set => Set(ref hoverTime, value); }
    public IReadOnlyList<MonitorPoint> History => history;
    IReadOnlyList<TrendValue> Trend(Func<MonitorPoint, double?> value) => history.Select(p => new TrendValue(p.Time, value(p))).ToArray();
    public IReadOnlyList<TrendValue> CpuTrend { get; private set; } = Array.Empty<TrendValue>();
    public IReadOnlyList<TrendValue> MemoryTrend { get; private set; } = Array.Empty<TrendValue>();
    public IReadOnlyList<TrendValue> ReceiveTrend { get; private set; } = Array.Empty<TrendValue>();
    public IReadOnlyList<TrendValue> SendTrend { get; private set; } = Array.Empty<TrendValue>();
    public IReadOnlyList<TrendValue> ReadTrend { get; private set; } = Array.Empty<TrendValue>();
    public IReadOnlyList<TrendValue> WriteTrend { get; private set; } = Array.Empty<TrendValue>();

    /// <summary>The history as a table (the charts' data, for a spreadsheet).</summary>
    public string HistoryCsv()
    {
        var text = new StringBuilder("time,cpu_percent,memory_percent,receive_bytes_per_s,send_bytes_per_s,read_bytes_per_s,write_bytes_per_s,iowait_percent,steal_percent\n");
        string N(double? v) => v?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
        foreach (var p in history)
            text.Append(p.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(',').AppendJoin(',', new[] { p.Cpu, p.Memory, p.Receive, p.Send, p.Read, p.Write, p.Wait, p.Steal }.Select(N)).Append('\n');
        return text.ToString();
    }

    /// <summary>Latest numbers for the connection list's "信息" column (open sessions only).</summary>
    public (double? Cpu, double? Memory, double? Disk)? Summary { get; private set; }
    public ICommand RefreshCommand { get; }
    public ICommand ToggleAutoCommand { get; }
    public ICommand IntervalCommand { get; }
    public ICommand RangeCommand { get; }
    public ICommand SortByCpuCommand { get; }
    public ICommand SortByMemoryCommand { get; }
    public ICommand ShowProcessCommand { get; }
    public ICommand CopyCommand { get; }
    /// <summary>Opens the process manager; a string parameter is what to search for.</summary>
    public ICommand? ShowProcessesCommand { get; set; }
    public Func<string, Task>? CopyText { get; set; }
    public event Action? Updated;

    /// <summary>Visible = column shown, workspace tab active and connected.</summary>
    public void SetVisible(bool value)
    {
        if (visible == value) return;
        visible = value;
        Restart();
    }

    void Restart()
    {
        loop?.Cancel();
        loop = null;
        if (!visible || interval == 0 || stalled || disposed != 0) return;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        loop = cts;
        _ = LoopAsync(cts, TimeSpan.FromSeconds(interval));
    }

    /// <summary>Owns its token source: it is disposed when the loop ends, and forgotten first so
    /// <see cref="Restart"/> never cancels a disposed source (both run on the UI thread).</summary>
    async Task LoopAsync(CancellationTokenSource cts, TimeSpan every)
    {
        var ct = cts.Token;
        try
        {
            await RefreshAsync(byUser: false);
            using var timer = new PeriodicTimer(every);
            while (await timer.WaitForNextTickAsync(ct)) await RefreshAsync(byUser: false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(loop, cts)) loop = null;
            cts.Dispose();
        }
    }

    /// <summary>The refresh button: also takes up refreshing by itself again after a failure.</summary>
    public async Task RefreshByUserAsync()
    {
        bool resume = stalled;
        await RefreshAsync();
        if (resume && !stalled) Restart();
    }

    /// <summary>Asked before a command channel is opened on the connection (see <see cref="CommandChannels"/>).</summary>
    public CommandChannels? Commands { get; set; }

    /// <summary>One probe; overlapping requests are skipped, not queued. False when it failed. Not <paramref name="byUser"/>
    /// (refreshing by itself): nothing is asked, and where a question is due nothing is read.</summary>
    public async Task<bool> RefreshAsync(bool byUser = true)
    {
        if (session() is not { } ssh) { Message = "监控需要 SSH 会话"; return false; }
        if (Interlocked.Exchange(ref refreshing, 1) != 0) return true;
        IsLoading = true;
        try
        {
            if (Commands is { } commands && !await commands.AllowAsync("读取监控", "只读命令（读取 /proc、df、ps）", byUser))
            {
                Message = byUser ? "没有读取监控" : "这台服务器上次在执行命令后断开了连接，监控没有自动读取；点刷新按钮，确认后读取。";
                if (!byUser && interval > 0) { loop?.Cancel(); loop = null; IsStalled = true; }
                return false;
            }
            var result = await ssh.ExecAsync(MonitorProbe.Command, TimeSpan.FromSeconds(8), lifetime.Token, MonitorProbe.MaxOutputBytes);
            var sample = await Task.Run(() => MonitorProbe.Parse(result.Output));
            first ??= sample;
            Apply(MonitorProbe.Compare(sample, previous), DateTime.Now);
            previous = sample;
            Message = "";
            IsStalled = false;
            return true;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return false; }
        catch (Exception e)
        {
            Message = "监控读取失败：" + WorkspaceViewModel.Short(e.Message) + (interval > 0 ? "。已停止自动刷新，点刷新按钮重试。" : "");
            // Not again by itself: a bastion may audit, or be refusing, every request.
            if (interval > 0) { loop?.Cancel(); loop = null; IsStalled = true; }
            return false;
        }
        finally
        {
            IsLoading = false;
            Interlocked.Exchange(ref refreshing, 0);
        }
    }

    static string Percent(double? value) => value is { } v ? v.ToString("0", CultureInfo.InvariantCulture) + "%" : "—";

    /// <summary>Makes <paramref name="rows"/> show <paramref name="data"/> without replacing rows that stay.</summary>
    static void Fill<TRow, TData>(ObservableCollection<TRow> rows, IReadOnlyList<TData> data, Action<TRow, TData> show) where TRow : new()
    {
        while (rows.Count > data.Count) rows.RemoveAt(rows.Count - 1);
        while (rows.Count < data.Count) rows.Add(new TRow());
        for (int i = 0; i < data.Count; i++) show(rows[i], data[i]);
    }

    internal void Apply(MonitorReading r, DateTime now)
    {
        var s = r.Sample;
        double? memoryPercent = r.Memory is { Total: > 0 } mm ? 100d * mm.Used / mm.Total : null;
        history.Add(new MonitorPoint(now, r.CpuTotal, memoryPercent, r.ReceivePerSecond, r.SendPerSecond, r.ReadPerSecond, r.WritePerSecond, r.Breakdown?.Wait, r.Breakdown?.Steal));
        if (history.Count > HistoryLength) history.RemoveRange(0, history.Count - HistoryLength);
        UpdatedText = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " 更新";

        OsName = s.Os?.Name ?? "Linux";
        string version = s.Os?.Version.Length > 0 ? s.Os.Version : s.Os?.Pretty ?? "";
        SystemLine = string.Join(" · ", new[] { $"{OsName} {version}".Trim(), r.Cores.Count > 0 ? $"{r.Cores.Count} 核" : "", s.Uptime is { } up ? "已运行 " + FormatUptime(up) : "", s.TimeZone }.Where(x => x.Length > 0));

        CpuText = Percent(r.CpuTotal);
        UserText = Percent(r.Breakdown?.User);
        KernelText = Percent(r.Breakdown?.System);
        WaitText = Percent(r.Breakdown?.Wait);
        StealText = Percent(r.Breakdown?.Steal);
        LoadText = s.Load is { } l ? string.Join("  ", l.Select(v => v.ToString("0.00", CultureInfo.InvariantCulture))) : "—";
        Cores = r.Cores.Take(256).Select(c => new CoreRow(c.Core, c.Percent)).ToArray();

        if (r.Memory is { } m)
        {
            MemoryBytes = m.Total;
            MemoryText = Percent(memoryPercent);
            MemoryTotal = MonitorProbe.Bytes(m.Total);
            MemoryUsed = MonitorProbe.Bytes(m.Used);
            MemoryCache = MonitorProbe.Bytes(m.Cache);
            MemoryAvailable = MonitorProbe.Bytes(m.Available);
            SwapText = m.SwapTotal == 0 ? "未启用" : $"{MonitorProbe.Bytes(m.SwapUsed)} / {MonitorProbe.Bytes(m.SwapTotal)}";
        }

        DownloadSpeed = MonitorProbe.Bytes(r.ReceivePerSecond, "/s");
        UploadSpeed = MonitorProbe.Bytes(r.SendPerSecond, "/s");
        ReadSpeed = MonitorProbe.Bytes(r.ReadPerSecond, "/s");
        WriteSpeed = MonitorProbe.Bytes(r.WritePerSecond, "/s");
        ulong Sum(MonitorSample x, bool receive) => (ulong)Math.Min(ulong.MaxValue, x.Network.Where(n => n.Key != "lo").Sum(n => (decimal)(receive ? n.Value.Rx : n.Value.Tx)));
        ulong r0 = Sum(first ?? s, true), r1 = Sum(s, true), t0 = Sum(first ?? s, false), t1 = Sum(s, false);
        DownloadTotal = r1 >= r0 ? MonitorProbe.Bytes(r1 - r0) : "—";
        UploadTotal = t1 >= t0 ? MonitorProbe.Bytes(t1 - t0) : "—";
        Fill(Interfaces, r.Interfaces.Take(8).ToList(), (row, n) => { row.Name = n.Name; row.Down = MonitorProbe.Bytes(n.ReceivePerSecond, "/s"); row.Up = MonitorProbe.Bytes(n.SendPerSecond, "/s"); });
        Raise(nameof(HasInterfaces));

        // The fullest first: that is the one to look at. Images are full by nature and say nothing.
        var mounts = r.Mounts.Where(x => !x.IsImage).OrderByDescending(x => x.UsedPercent).ToList();
        Fill(Mounts, mounts, (row, x) =>
        {
            row.Mount = x.Info.Mount;
            row.Percent = x.UsedPercent;
            row.UsedText = Percent(Math.Floor(x.UsedPercent));
            row.Level = MonitorFindings.Disk(x.UsedPercent);
            row.Space = $"余 {MonitorProbe.Bytes(x.Info.AvailableBytes)} / {MonitorProbe.Bytes(x.Info.SizeBytes)}";
            row.Rates = x.ReadPerSecond == null && x.WritePerSecond == null ? "" : $"读 {MonitorProbe.Bytes(x.ReadPerSecond, "/s")}  写 {MonitorProbe.Bytes(x.WritePerSecond, "/s")}";
            row.Detail = $"{x.Info.Source}（{(x.Info.Type.Length > 0 ? x.Info.Type : "未知类型")}）\n已用 {MonitorProbe.Bytes(x.Info.UsedBytes)}，可用 {MonitorProbe.Bytes(x.Info.AvailableBytes)}，共 {MonitorProbe.Bytes(x.Info.SizeBytes)}";
        });

        processes = s.Processes ?? Array.Empty<ProcessInfo>();
        ShowTopProcesses();
        Raise(nameof(Processes));

        ShowVitals(r, memoryPercent, mounts.FirstOrDefault());
        var found = MonitorFindings.Evaluate(r, history);
        // The same findings stay the same rows: one being pointed at must not be replaced under the pointer.
        string headline = Headline;
        bool healthy = IsHealthy, same = found.SequenceEqual(Findings);
        measured = r.CpuTotal != null || r.Memory != null || r.Mounts.Count > 0 || s.Load != null;
        cpuJudged = MonitorFindings.Average(history, p => p.Cpu) != null;
        if (!same) Findings = found;
        if (!same || headline != Headline || healthy != IsHealthy) RaiseFindings();

        CpuTrend = Trend(p => p.Cpu);
        MemoryTrend = Trend(p => p.Memory);
        ReceiveTrend = Trend(p => p.Receive);
        SendTrend = Trend(p => p.Send);
        ReadTrend = Trend(p => p.Read);
        WriteTrend = Trend(p => p.Write);
        foreach (string name in new[] { nameof(CpuTrend), nameof(MemoryTrend), nameof(ReceiveTrend), nameof(SendTrend), nameof(ReadTrend), nameof(WriteTrend), nameof(History) }) Raise(name);

        Summary = (r.CpuTotal, memoryPercent, r.Mounts.FirstOrDefault(x => x.Info.Mount == "/")?.UsedPercent);
        HasData = true;
        Updated?.Invoke();
    }

    void ShowVitals(MonitorReading r, double? memoryPercent, MountReading? fullest)
    {
        var (cpu, memory, disk, loadItem) = (Vitals[0], Vitals[1], Vitals[2], Vitals[3]);
        cpu.Percent = r.CpuTotal ?? 0;
        cpu.Figure = Percent(r.CpuTotal);
        cpu.Level = MonitorFindings.Cpu(MonitorFindings.Average(history, p => p.Cpu)?.Value);
        if (r.Memory is { Total: > 0 } m)
        {
            memory.Percent = memoryPercent ?? 0;
            memory.Secondary = 100d * m.Cache / m.Total;
            memory.Figure = Percent(memoryPercent);
            memory.Level = MonitorFindings.Memory(m);
            memory.Note = $"已用 {MonitorProbe.Bytes(m.Used)}，缓存 {MonitorProbe.Bytes(m.Cache)}（浅色），可用 {MonitorProbe.Bytes(m.Available)}，共 {MonitorProbe.Bytes(m.Total)}";
        }
        if (fullest != null)
        {
            disk.Rename("磁盘 " + fullest.Info.Mount);
            disk.Percent = fullest.UsedPercent;
            disk.Figure = Percent(Math.Floor(fullest.UsedPercent));
            disk.Level = MonitorFindings.Disk(fullest.UsedPercent);
            disk.Note = $"用得最满的文件系统：{fullest.Info.Mount}，余 {MonitorProbe.Bytes(fullest.Info.AvailableBytes)}";
        }
        if (r.Sample.Load is { } l)
        {
            int n = Math.Max(1, r.Cores.Count);
            loadItem.Percent = 100 * l[0] / n;
            loadItem.Figure = l[0].ToString("0.00", CultureInfo.InvariantCulture);
            loadItem.Level = MonitorFindings.Load(l[1], r.Cores.Count);
            loadItem.Note = $"1 / 5 / 15 分钟负载：{LoadText}；满格相当于每个核都有一个任务（{n} 核）";
        }
        cpu.Note = $"用户 {UserText}，系统 {KernelText}，等待磁盘 {WaitText}，被宿主机抢占 {StealText}";
    }

    void ShowTopProcesses()
    {
        var top = (byMemory ? processes.Where(p => p.RssBytes > 0).OrderByDescending(p => p.RssBytes) : processes.Where(p => p.Cpu > 0).OrderByDescending(p => p.Cpu)).Take(6).ToList();
        double most = top.Count == 0 ? 1 : byMemory ? top[0].RssBytes : Math.Max(top[0].Cpu, 1);
        Fill(TopProcesses, top, (row, p) =>
        {
            var (name, arguments) = SplitCommand(p.Command);
            row.Row = new ProcessRow(p);
            row.Name = name;
            row.Arguments = arguments;
            row.Value = byMemory ? MonitorProbe.Bytes(p.RssBytes) : p.Cpu.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            row.Share = 100 * (byMemory ? p.RssBytes : p.Cpu) / most;
            row.Tip = $"PID {p.Pid} · {p.User} · CPU {p.Cpu.ToString("0.0", CultureInfo.InvariantCulture)}% · 内存 {MonitorProbe.Bytes(p.RssBytes)}\n{(p.Command.Length > 400 ? p.Command[..400] + "…" : p.Command)}\n点击在进程管理中查看";
        });
        Raise(nameof(HasProcesses));
    }

    /// <summary>"/usr/bin/java -jar app.jar" → "java", "-jar app.jar"; kernel threads keep their brackets.</summary>
    public static (string Name, string Arguments) SplitCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('[')) return (command, "");
        int space = command.IndexOf(' ');
        string program = space < 0 ? command : command[..space], rest = space < 0 ? "" : command[(space + 1)..].Trim();
        // "nginx: worker process" and the like name themselves.
        if (program.EndsWith(':')) return (command, "");
        string name = program[(program.LastIndexOf('/') + 1)..];
        return (name.Length > 0 ? name : program, rest);
    }

    static string FormatUptime(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalDays >= 1 ? $"{(int)t.TotalDays} 天" : t.TotalHours >= 1 ? $"{(int)t.TotalHours} 小时" : $"{Math.Max(1, (int)t.TotalMinutes)} 分钟";
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        loop?.Cancel();
        lifetime.Cancel();
        lifetime.Dispose();
    }
}

/// <summary>"查看全部" process manager: search, sort, and explicit TERM/KILL with confirmation.</summary>
public sealed partial class ProcessListViewModel : ObservableObject, IDisposable
{
    readonly MonitorViewModel monitor;
    readonly Func<SharedSshSession?> session;
    readonly IStudioInteraction ui;
    CancellationTokenSource? auto;
    string search = "", sort = "cpu", notice = "";
    bool descending = true, autoRefresh;

    public ProcessListViewModel(MonitorViewModel monitor, Func<SharedSshSession?> session, IStudioInteraction ui)
    {
        this.monitor = monitor;
        this.session = session;
        this.ui = ui;
        monitor.Updated += Rebuild;
        RefreshCommand = new AsyncCommand(monitor.RefreshByUserAsync, e => Notice = WorkspaceViewModel.Short(e.Message));
        SortCommand = new RelayCommand(p => { string key = p as string ?? "cpu"; if (sort == key) descending = !descending; else { sort = key; descending = key is "cpu" or "mem"; } Rebuild(); });
        TerminateCommand = new AsyncCommand<ProcessRow>(row => SignalAsync(row, force: false), e => Notice = WorkspaceViewModel.Short(e.Message));
        KillCommand = new AsyncCommand<ProcessRow>(row => SignalAsync(row, force: true), e => Notice = WorkspaceViewModel.Short(e.Message));
        CopyPidCommand = new AsyncCommand<ProcessRow>(row => row == null ? Task.CompletedTask : ui.CopyTextAsync(row.Pid.ToString(CultureInfo.InvariantCulture)));
        CopyCommandCommand = new AsyncCommand<ProcessRow>(row => row == null ? Task.CompletedTask : ui.CopyTextAsync(row.Command));
        Rebuild();
    }

    public string Title => "进程管理 · " + monitor.Host;
    public ObservableCollection<ProcessRow> Rows { get; } = new();
    ProcessRow? selectedRow;
    int? selectedPid;
    bool rebuilding;
    /// <summary>Follows its process through refreshes, whatever row it is on then.</summary>
    public ProcessRow? SelectedRow
    {
        get => selectedRow;
        set
        {
            if (rebuilding || !Set(ref selectedRow, value)) return;
            selectedPid = value?.Pid;
        }
    }
    string Head(string key, string title) => title + (sort == key ? descending ? " ↓" : " ↑" : "");
    public string PidHead => Head("pid", "PID");
    public string UserHead => Head("user", "用户");
    public string CpuHead => Head("cpu", "CPU");
    public string MemoryHead => Head("mem", "内存");
    public string StartHead => Head("start", "启动时间");
    public ICommand CopyPidCommand { get; }
    public ICommand CopyCommandCommand { get; }
    public string Search { get => search; set { if (Set(ref search, value)) Rebuild(); } }
    public string Notice { get => notice; private set { if (Set(ref notice, value)) Raise(nameof(HasNotice)); } }
    public bool HasNotice => Notice.Length > 0;
    public string CountText => $"{Rows.Count} / {monitor.Processes.Count} 个进程";
    public bool IsEmpty => Rows.Count == 0;
    public bool AutoRefresh
    {
        get => autoRefresh;
        set
        {
            if (!Set(ref autoRefresh, value)) return;
            auto?.Cancel();
            auto = null;
            if (value) { auto = new CancellationTokenSource(); _ = AutoAsync(auto.Token); }
        }
    }
    public ICommand RefreshCommand { get; }
    public ICommand SortCommand { get; }
    public ICommand TerminateCommand { get; }
    public ICommand KillCommand { get; }

    async Task AutoAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (await monitor.RefreshAsync(byUser: false)) continue;
                AutoRefresh = false; // not again by itself, as in the column
                Notice = monitor.Message;
                return;
            }
        }
        catch (OperationCanceledException) { }
    }

    void Rebuild()
    {
        IEnumerable<ProcessInfo> list = monitor.Processes;
        string q = search.Trim();
        if (q.Length > 0)
            list = list.Where(p => p.Pid.ToString(CultureInfo.InvariantCulture).Contains(q) || p.User.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Command.Contains(q, StringComparison.OrdinalIgnoreCase));
        list = (sort, descending) switch
        {
            ("pid", false) => list.OrderBy(p => p.Pid),
            ("pid", true) => list.OrderByDescending(p => p.Pid),
            ("mem", false) => list.OrderBy(p => p.RssBytes),
            ("mem", true) => list.OrderByDescending(p => p.RssBytes),
            ("user", false) => list.OrderBy(p => p.User, StringComparer.Ordinal),
            ("user", true) => list.OrderByDescending(p => p.User, StringComparer.Ordinal),
            ("start", false) => list.OrderBy(p => p.Started, StringComparer.Ordinal),
            ("start", true) => list.OrderByDescending(p => p.Started, StringComparer.Ordinal),
            (_, false) => list.OrderBy(p => p.Cpu),
            _ => list.OrderByDescending(p => p.Cpu),
        };
        double memory = monitor.MemoryBytes;
        var next = list.Select(p => new ProcessRow(p) { MemoryShare = memory > 0 ? Math.Clamp(100 * p.RssBytes / memory, 0, 100) : 0 }).ToList();
        // Only rows that changed are replaced, so the list keeps its place while it refreshes.
        rebuilding = true;
        try
        {
            while (Rows.Count > next.Count) Rows.RemoveAt(Rows.Count - 1);
            for (int i = 0; i < next.Count; i++)
                if (i >= Rows.Count) Rows.Add(next[i]);
                else if (Rows[i] != next[i]) Rows[i] = next[i];
        }
        finally { rebuilding = false; }
        selectedRow = selectedPid is { } pid ? Rows.FirstOrDefault(r => r.Pid == pid) : null;
        foreach (string name in new[] { nameof(CountText), nameof(IsEmpty), nameof(SelectedRow), nameof(PidHead), nameof(UserHead), nameof(CpuHead), nameof(MemoryHead), nameof(StartHead) }) Raise(name);
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Z][a-z]{2} [A-Z][a-z]{2} [0-9]{1,2} [0-9]{2}:[0-9]{2}:[0-9]{2} [0-9]{4}$")]
    private static partial System.Text.RegularExpressions.Regex LStart();

    /// <summary>Exit status of the guard when the PID now belongs to another process (or none).</summary>
    const int StaleProcess = 113;

    /// <summary>kill -TERM / kill -KILL on a separate exec channel, after explicit confirmation.
    /// Only a validated numeric PID greater than 1 ever reaches the command, and the same exec first
    /// checks the process start time still matches the list, so a reused PID is never signalled.</summary>
    public async Task SignalAsync(ProcessRow? row, bool force)
    {
        if (row == null || !row.CanSignal) { Notice = "不能结束 PID 1"; return; }
        if (session() is not { } ssh) { Notice = "SSH 会话不可用"; return; }
        string command = row.Command.Length > 120 ? row.Command[..120] + "…" : row.Command;
        string text = force
            ? $"强制结束进程 {row.Pid}（{row.User}）？\n{command}\n发送 SIGKILL，进程立即终止，未保存的数据可能丢失。"
            : $"结束进程 {row.Pid}（{row.User}）？\n{command}\n发送 SIGTERM，进程可以先自行清理再退出。";
        if (!await ui.ConfirmAsync(force ? "强制结束进程" : "结束进程", text, force ? "强制结束" : "结束", danger: true)) return;
        string pid = row.Pid.ToString(CultureInfo.InvariantCulture);
        if (!LStart().IsMatch(row.Info.StartRaw)) { Notice = "无法核对进程启动时间，未发送信号"; return; }
        if (monitor.Commands is { } commands && !await commands.AllowAsync("结束进程", "kill 命令")) { Notice = "没有结束进程"; return; }
        string guard = $"LC_ALL=C; export LC_ALL; set -- $(ps -o lstart= -p {pid} 2>/dev/null); [ \"$*\" = '{row.Info.StartRaw}' ] || exit {StaleProcess}; ";
        var result = await ssh.ExecAsync(guard + $"kill -{(force ? "KILL" : "TERM")} {pid}", TimeSpan.FromSeconds(8), CancellationToken.None, 64 * 1024);
        Notice = result.ExitStatus switch
        {
            0 => $"已向进程 {pid} 发送 {(force ? "SIGKILL" : "SIGTERM")}",
            StaleProcess => $"进程 {pid} 已退出，或 PID 已被其他进程复用；未发送信号",
            _ => $"结束进程 {pid} 失败：{WorkspaceViewModel.Short(result.Error.Trim())}",
        };
        await monitor.RefreshByUserAsync();
    }

    public void Dispose()
    {
        monitor.Updated -= Rebuild;
        auto?.Cancel();
    }
}
