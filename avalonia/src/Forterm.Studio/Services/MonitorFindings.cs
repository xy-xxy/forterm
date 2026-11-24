using System.Globalization;

namespace Forterm.Studio.Services;

/// <summary>One moment of the monitor's history. Unknown values stay null; they are never drawn as zero.</summary>
public sealed record MonitorPoint(DateTime Time, double? Cpu, double? Memory, double? Receive, double? Send, double? Read, double? Write, double? Wait, double? Steal);

public enum MonitorLevel { Normal, Warning, Danger }

/// <summary>Something the user should know about the host, with the section that shows it.</summary>
public sealed record MonitorFinding(MonitorLevel Level, string Text, string Section)
{
    public bool IsDanger => Level == MonitorLevel.Danger;
    /// <summary>The level in words too, not only as an icon.</summary>
    public string Said => (IsDanger ? "严重：" : "注意：") + Text;
}

/// <summary>"监控阈值": from where on something is worth a notice, and from where on it is serious. Memory counts what
/// is left, the others what is used; load counts per core.</summary>
public sealed record MonitorThresholds
{
    public double DiskWarning { get; init; } = 85;
    public double DiskDanger { get; init; } = 95;
    public double CpuWarning { get; init; } = 85;
    public double CpuDanger { get; init; } = 95;
    public double MemoryWarning { get; init; } = 10;
    public double MemoryDanger { get; init; } = 5;
    public double LoadWarning { get; init; } = 2;
    public double LoadDanger { get; init; } = 4;

    /// <summary>In range, and the serious value beyond the other one.</summary>
    public MonitorThresholds Validated()
    {
        var d = new MonitorThresholds();
        static (double, double) Up(double warning, double danger, double least, double most, double w, double d)
        {
            if (!double.IsFinite(warning) || !double.IsFinite(danger)) return (w, d);
            warning = Math.Clamp(warning, least, most);
            return (warning, Math.Clamp(danger, warning, most));
        }
        var (disk, diskDanger) = Up(DiskWarning, DiskDanger, 1, 100, d.DiskWarning, d.DiskDanger);
        var (cpu, cpuDanger) = Up(CpuWarning, CpuDanger, 1, 100, d.CpuWarning, d.CpuDanger);
        var (load, loadDanger) = Up(LoadWarning, LoadDanger, 0.1, 100, d.LoadWarning, d.LoadDanger);
        bool memory = double.IsFinite(MemoryWarning) && double.IsFinite(MemoryDanger);
        double left = memory ? Math.Clamp(MemoryWarning, 0, 99) : d.MemoryWarning;
        return new()
        {
            DiskWarning = disk, DiskDanger = diskDanger, CpuWarning = cpu, CpuDanger = cpuDanger, LoadWarning = load, LoadDanger = loadDanger,
            MemoryWarning = left, MemoryDanger = memory ? Math.Clamp(MemoryDanger, 0, left) : d.MemoryDanger,
        };
    }
}

/// <summary>Turns readings into conclusions, worst first. Short-lived values (CPU, waiting, steal) count only
/// as an average over some time, so one busy moment is not an alarm.</summary>
public static class MonitorFindings
{
    public const double WaitWarning = 30, StealWarning = 10;
    static MonitorThresholds Limits => Appearance.Current.Thresholds;
    static readonly TimeSpan Span = TimeSpan.FromSeconds(75), LeastSpan = TimeSpan.FromSeconds(20);

    public static MonitorLevel Disk(double percent) => percent >= Limits.DiskDanger ? MonitorLevel.Danger : percent >= Limits.DiskWarning ? MonitorLevel.Warning : MonitorLevel.Normal;
    public static MonitorLevel Memory(MemoryReading m)
    {
        double available = m.Total == 0 ? 100 : 100d * m.Available / m.Total;
        return available < Limits.MemoryDanger ? MonitorLevel.Danger : available < Limits.MemoryWarning ? MonitorLevel.Warning : MonitorLevel.Normal;
    }
    public static MonitorLevel Cpu(double? average) => average >= Limits.CpuDanger ? MonitorLevel.Danger : average >= Limits.CpuWarning ? MonitorLevel.Warning : MonitorLevel.Normal;
    public static MonitorLevel Load(double load, int cores) => cores <= 0 ? MonitorLevel.Normal : load / cores >= Limits.LoadDanger ? MonitorLevel.Danger : load / cores >= Limits.LoadWarning ? MonitorLevel.Warning : MonitorLevel.Normal;

    /// <summary>The recent average and the time it is about; null while that is too short to say anything. Every
    /// value is itself the average since the reading before it, so two readings five minutes apart (refreshing
    /// by hand) say as much as many close ones. A long gap counts only while there is too little after it.</summary>
    public static (double Value, TimeSpan Over)? Average(IReadOnlyList<MonitorPoint> history, Func<MonitorPoint, double?> value)
    {
        if (history.Count == 0) return null;
        var last = history[^1].Time;
        (double Value, TimeSpan Over)? Of(TimeSpan longest)
        {
            double sum = 0, weight = 0;
            var from = last;
            for (int i = history.Count - 1; i > 0; i--)
            {
                var (begin, end) = (history[i - 1].Time, history[i].Time);
                if (end <= last - Span) break;
                // Not after the newest, and not backwards: the clock has been set.
                if (end > last || begin >= end || end - begin > longest || value(history[i]) is not { } v) continue;
                double seconds = (end - begin).TotalSeconds;
                sum += v * seconds;
                weight += seconds;
                from = begin;
            }
            return weight >= LeastSpan.TotalSeconds ? (sum / weight, last - from) : null;
        }
        return Of(Span) ?? Of(TimeSpan.MaxValue);
    }

    /// <summary>"近 40 秒", "近 1 分钟", "近 2 小时".</summary>
    public static string Over(TimeSpan time) => time.TotalSeconds < 55 ? $"近 {time.TotalSeconds:0} 秒" : time.TotalMinutes < 90 ? $"近 {Math.Max(1, Math.Round(time.TotalMinutes, MidpointRounding.AwayFromZero)):0} 分钟" : $"近 {time.TotalHours:0} 小时";

    public static IReadOnlyList<MonitorFinding> Evaluate(MonitorReading r, IReadOnlyList<MonitorPoint> history)
    {
        var found = new List<MonitorFinding>();
        void Add(MonitorLevel level, string text, string section) { if (level != MonitorLevel.Normal) found.Add(new MonitorFinding(level, text, section)); }
        string P(double v) => v.ToString("0", CultureInfo.InvariantCulture) + "%";

        foreach (var m in r.Mounts.Where(m => !m.IsImage).OrderByDescending(m => m.UsedPercent))
            Add(Disk(m.UsedPercent), $"磁盘 {m.Info.Mount} 已用 {P(Math.Floor(m.UsedPercent))}，余 {MonitorProbe.Bytes(m.Info.AvailableBytes)}", "disk");
        if (r.Memory is { Total: > 0 } memory)
        {
            double available = 100d * memory.Available / memory.Total;
            Add(Memory(memory), $"可用内存只剩 {P(available)}（{MonitorProbe.Bytes(memory.Available)}）", "memory");
            if (memory.SwapTotal > 0 && 100d * memory.SwapUsed / memory.SwapTotal >= 50 && available < 20)
                Add(MonitorLevel.Warning, $"交换区已用 {P(100d * memory.SwapUsed / memory.SwapTotal)}，内存紧张", "memory");
        }
        if (Average(history, p => p.Cpu) is { } cpu) Add(Cpu(cpu.Value), $"CPU {Over(cpu.Over)}平均 {P(cpu.Value)}", "cpu");
        if (Average(history, p => p.Wait) is { } wait && wait.Value >= WaitWarning) Add(MonitorLevel.Warning, $"CPU 等待磁盘 {P(wait.Value)}，磁盘可能跟不上", "disk");
        if (Average(history, p => p.Steal) is { } steal && steal.Value >= StealWarning) Add(MonitorLevel.Warning, $"CPU 被宿主机抢占 {P(steal.Value)}（steal）", "cpu");
        int cores = r.Cores.Count;
        if (r.Sample.Load is { Length: 3 } load)
            Add(Load(load[1], cores), $"5 分钟负载 {load[1].ToString("0.0", CultureInfo.InvariantCulture)}，是 {cores} 核的 {(load[1] / Math.Max(1, cores)).ToString("0.0", CultureInfo.InvariantCulture)} 倍", "cpu");
        return found.OrderByDescending(f => f.Level).ToArray();
    }
}
