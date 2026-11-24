using System.Globalization;

namespace Forterm.Studio.Services;

public sealed record OsInfo(string Name, string Version, string Pretty);
public sealed record MountInfo(string Source, string Type, ulong SizeBytes, ulong UsedBytes, ulong AvailableBytes, string Mount);
public sealed record DiskCounters(ulong SectorsRead, ulong SectorsWritten);
/// <summary><see cref="StartRaw"/> is ps's <c>lstart</c> (C locale, single-spaced); signals re-check it so
/// a PID reused since the list was read is never signalled.</summary>
public sealed record ProcessInfo(int Pid, string User, string State, double Cpu, ulong RssBytes, string Started, string Command, string StartRaw = "");

public sealed record MonitorSample(
    OsInfo? Os, string TimeZone, double? Uptime, double[]? Load,
    IReadOnlyDictionary<string, ulong[]> Cpu,
    IReadOnlyDictionary<string, ulong> MemoryKb,
    IReadOnlyList<MountInfo> Mounts,
    IReadOnlyDictionary<string, DiskCounters> Disks,
    IReadOnlyDictionary<string, string> DeviceMapper,
    IReadOnlyDictionary<string, (ulong Rx, ulong Tx)> Network,
    IReadOnlyList<ProcessInfo>? Processes);

public sealed record MemoryReading(ulong Total, ulong Used, ulong Cache, ulong Free, ulong Available, ulong SwapTotal, ulong SwapUsed);
public sealed record MountReading(MountInfo Info, double? ReadPerSecond, double? WritePerSecond)
{
    /// <summary>As df counts it: blocks reserved for root are neither used nor available, so a file system
    /// that is full for everyone else says 100%.</summary>
    public double UsedPercent => Info.UsedBytes + Info.AvailableBytes == 0 ? 0 : 100d * Info.UsedBytes / (Info.UsedBytes + Info.AvailableBytes);
    /// <summary>Images that are full by nature say nothing about the host.</summary>
    public bool IsImage => Info.Type is "iso9660" or "udf" or "cramfs" or "erofs" or "squashfs";
}

/// <summary>Where the CPU time went between two samples, in percent. User includes nice; system includes
/// interrupts; wait is time spent idle with disk requests outstanding; steal is time the hypervisor gave away.</summary>
public sealed record CpuBreakdown(double User, double System, double Wait, double Steal);
public sealed record InterfaceReading(string Name, double? ReceivePerSecond, double? SendPerSecond);

public sealed record MonitorReading(
    MonitorSample Sample, double? CpuTotal, IReadOnlyList<(string Core, double? Percent)> Cores, MemoryReading? Memory,
    double? ReceivePerSecond, double? SendPerSecond, IReadOnlyList<MountReading> Mounts)
{
    public CpuBreakdown? Breakdown { get; init; }
    /// <summary>All mounted devices together, each counted once.</summary>
    public double? ReadPerSecond { get; init; }
    public double? WritePerSecond { get; init; }
    public IReadOnlyList<InterfaceReading> Interfaces { get; init; } = Array.Empty<InterfaceReading>();
}

/// <summary>Studio's monitor probe: one fixed, read-only shell script on its own exec channel
/// (never the interactive terminal). Rates come from two real samples; the first sample and any
/// counter reset show "unknown" instead of a guessed value.</summary>
public static class MonitorProbe
{
    public const int MaxOutputBytes = 2 * 1024 * 1024;

    public const string Command = "LC_ALL=C; export LC_ALL; " +
        "printf 'FTS1\\nOS\\n'; head -n 40 /etc/os-release 2>/dev/null; " +
        "printf 'TZ\\n'; date '+%Z %z' 2>/dev/null; " +
        "printf 'UPTIME\\n'; cat /proc/uptime 2>/dev/null; " +
        "printf 'LOAD\\n'; cat /proc/loadavg 2>/dev/null; " +
        "printf 'CPU\\n'; grep '^cpu' /proc/stat 2>/dev/null | head -n 1025; " +
        "printf 'MEM\\n'; cat /proc/meminfo 2>/dev/null; " +
        // df exits with 1 when a single file system cannot be read, after it has printed the others.
        "printf 'DF\\n'; o=$(df -kPT 2>/dev/null); [ -n \"$o\" ] || o=$(df -kP 2>/dev/null); printf '%s\\n' \"$o\" | head -n 64; " +
        "printf 'DISKSTATS\\n'; head -n 512 /proc/diskstats 2>/dev/null; " +
        "printf 'DM\\n'; for d in /sys/block/dm-*; do [ -r \"$d/dm/name\" ] && printf '%s %s\\n' \"${d##*/}\" \"$(cat \"$d/dm/name\")\"; done 2>/dev/null; " +
        "printf 'NET\\n'; cat /proc/net/dev 2>/dev/null; " +
        "printf 'PS\\n'; ps -eo pid=,user=,stat=,pcpu=,rss=,lstart=,args= --sort=-pcpu 2>/dev/null | head -n 500";

    static readonly HashSet<string> PseudoFilesystems = new(StringComparer.Ordinal)
    { "tmpfs", "devtmpfs", "squashfs", "overlay", "proc", "sysfs", "devpts", "cgroup", "cgroup2", "efivarfs", "autofs", "mqueue", "debugfs", "tracefs", "securityfs", "pstore", "bpf", "fusectl", "configfs", "hugetlbfs", "ramfs", "nsfs", "binfmt_misc" };

    static string[] Words(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    static bool U(string s, out ulong n) => ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n);
    static double? D(string s, double most = 1e9) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= 0 && v <= most ? v : null;
    /// <summary>An exbibyte. More is not a size but somebody's idea of one; it would overflow in sums.</summary>
    const ulong MostKilobytes = 1UL << 50;
    static string Clean(string s, int max) { s = new string(s.Where(c => !char.IsControl(c)).ToArray()).Trim(); return s.Length > max ? s[..max] : s; }

    public static MonitorSample Parse(string output)
    {
        if (output.Length > MaxOutputBytes) throw new IOException("监控输出超出边界");
        var lines = output.Replace("\r", "").Split('\n');
        if (lines.Length == 0 || lines[0] != "FTS1") throw new IOException("监控响应格式不受支持（需要 Linux /proc）");
        string section = "";
        var os = new Dictionary<string, string>(StringComparer.Ordinal);
        string tz = "";
        double? uptime = null; double[]? load = null;
        var cpu = new Dictionary<string, ulong[]>(StringComparer.Ordinal);
        var mem = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var mounts = new List<MountInfo>();
        var disks = new Dictionary<string, DiskCounters>(StringComparer.Ordinal);
        var dm = new Dictionary<string, string>(StringComparer.Ordinal);
        var net = new Dictionary<string, (ulong, ulong)>(StringComparer.Ordinal);
        List<ProcessInfo>? processes = null;
        bool dfHasType = false, dfSeen = false, dfAgain = false;
        foreach (var raw in lines.Skip(1))
        {
            if (raw is "OS" or "TZ" or "UPTIME" or "LOAD" or "CPU" or "MEM" or "DF" or "DISKSTATS" or "DM" or "NET" or "PS")
            {
                section = raw;
                if (raw == "PS") processes = new();
                continue;
            }
            var w = Words(raw);
            if (w.Length == 0) continue;
            switch (section)
            {
                case "OS":
                    int eq = raw.IndexOf('=');
                    if (eq > 0 && os.Count < 64) os[raw[..eq].Trim()] = Clean(raw[(eq + 1)..].Trim().Trim('"', '\''), 80);
                    break;
                case "TZ":
                    tz = w.Length >= 2 ? $"{Clean(w[0], 10)} GMT{Clean(w[1], 6)}" : Clean(w[0], 16);
                    break;
                case "UPTIME": uptime = D(w[0], 1e10); break;
                case "LOAD": if (w.Length >= 3 && D(w[0], 1e6) is { } a && D(w[1], 1e6) is { } b && D(w[2], 1e6) is { } c) load = [a, b, c]; break;
                case "CPU":
                    if (cpu.Count >= 1025 || w.Length < 5 || !(w[0] == "cpu" || w[0].StartsWith("cpu", StringComparison.Ordinal) && int.TryParse(w[0].AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out int core) && core is >= 0 and < 1024)) break;
                    var ticks = new ulong[8]; bool ok = true;
                    for (int i = 0; i < Math.Min(8, w.Length - 1); i++) if (!U(w[i + 1], out ticks[i])) { ok = false; break; }
                    if (ok) cpu[w[0]] = ticks;
                    break;
                case "MEM":
                    if (w.Length >= 2 && w[0].EndsWith(':') && U(w[1], out ulong kb) && kb <= MostKilobytes && mem.Count < 128) mem[w[0][..^1]] = kb;
                    break;
                case "DF":
                    if (w[0] == "Filesystem") { dfAgain = dfSeen; dfSeen = true; if (!dfAgain) dfHasType = w.Length > 1 && w[1] == "Type"; break; }
                    if (dfAgain || mounts.Count >= 32) break; // a second listing says the same with fewer columns
                    int offset = dfHasType ? 1 : 0;
                    if (w.Length < 6 + offset) break;
                    string type = dfHasType ? w[1] : "";
                    if (PseudoFilesystems.Contains(type)) break;
                    if (!U(w[1 + offset], out ulong size) || !U(w[2 + offset], out ulong used) || !U(w[3 + offset], out ulong avail) || size == 0 || size > MostKilobytes || used > MostKilobytes || avail > MostKilobytes) break;
                    string mountPoint = string.Join(' ', w.Skip(5 + offset));
                    if (!mountPoint.StartsWith('/') || mountPoint.StartsWith("/snap/", StringComparison.Ordinal) || mountPoint.StartsWith("/boot/efi", StringComparison.Ordinal) && size < 2UL * 1024 * 1024) break;
                    mounts.Add(new MountInfo(Clean(w[0], 128), Clean(type, 24), size * 1024, used * 1024, avail * 1024, Clean(mountPoint, 256)));
                    break;
                case "DISKSTATS":
                    if (w.Length >= 10 && disks.Count < 512 && U(w[5], out ulong sr) && U(w[9], out ulong sw)) disks[w[2]] = new DiskCounters(sr, sw);
                    break;
                case "DM":
                    if (w.Length >= 2 && dm.Count < 256) dm[w[1]] = w[0];
                    break;
                case "NET":
                    int colon = raw.IndexOf(':');
                    if (colon < 1 || net.Count >= 4096) break;
                    string name = Clean(raw[..colon], 128);
                    var f = Words(raw[(colon + 1)..]);
                    if (name.Length is > 0 and <= 128 && f.Length >= 16 && U(f[0], out ulong rx) && U(f[8], out ulong tx)) net[name] = (rx, tx);
                    break;
                case "PS":
                    if (processes!.Count >= 500 || w.Length < 11) break;
                    if (!int.TryParse(w[0], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || D(w[3], 1e6) is not { } pcpu || !U(w[4], out ulong rss) || rss > MostKilobytes) break;
                    string started = FormatStart(w[5..10]);
                    // Keep the command line's own spacing: find where the 11th field starts.
                    int index = 0;
                    for (int field = 0; field < 10; field++)
                    {
                        while (index < raw.Length && char.IsWhiteSpace(raw[index])) index++;
                        while (index < raw.Length && !char.IsWhiteSpace(raw[index])) index++;
                    }
                    processes.Add(new ProcessInfo(pid, Clean(w[1], 32), Clean(w[2], 8), pcpu, rss * 1024, started, Clean(raw[index..], 2048), string.Join(' ', w[5..10])));
                    break;
            }
        }
        OsInfo? info = os.Count == 0 ? null : new OsInfo(os.GetValueOrDefault("NAME", os.GetValueOrDefault("ID", "Linux")), os.GetValueOrDefault("VERSION", os.GetValueOrDefault("VERSION_ID", "")), os.GetValueOrDefault("PRETTY_NAME", ""));
        return new MonitorSample(info, tz, uptime, load, cpu, mem, mounts, disks, dm, net, processes);
    }

    static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>"Sat Nov 22 15:39:59 2026" → "11-22 15:39:59".</summary>
    static string FormatStart(string[] w)
    {
        int month = Array.IndexOf(Months, w[1]) + 1;
        return month > 0 && int.TryParse(w[2], out int day) ? $"{month:00}-{day:00} {w[3]}" : string.Join(' ', w);
    }

    public static MonitorReading Compare(MonitorSample now, MonitorSample? previous)
    {
        bool restarted = now.Uptime is { } u && previous?.Uptime is { } p && u <= p;
        double? Percent(string name)
        {
            if (restarted || previous == null || !now.Cpu.TryGetValue(name, out var t) || !previous.Cpu.TryGetValue(name, out var o)) return null;
            double sum = 0, idle = 0;
            for (int j = 0; j < 8; j++) { if (t[j] < o[j]) return null; double d = t[j] - o[j]; sum += d; if (j is 3 or 4) idle += d; }
            return sum > 0 ? Math.Clamp(100d * (sum - idle) / sum, 0, 100) : null;
        }
        CpuBreakdown? Breakdown()
        {
            if (Percent("cpu") == null) return null;
            ulong[] t = now.Cpu["cpu"], o = previous!.Cpu["cpu"];
            double D(int i) => t[i] - o[i];
            double sum = Enumerable.Range(0, 8).Sum(D);
            return new CpuBreakdown(100 * (D(0) + D(1)) / sum, 100 * (D(2) + D(5) + D(6)) / sum, 100 * D(4) / sum, 100 * D(7) / sum);
        }
        var cores = now.Cpu.Keys.Where(k => k != "cpu")
            .OrderBy(k => int.Parse(k.AsSpan(3), CultureInfo.InvariantCulture))
            .Select(k => (k[3..], Percent(k))).ToList();

        MemoryReading? memory = null;
        if (now.MemoryKb.TryGetValue("MemTotal", out ulong total) && total > 0)
        {
            ulong Get(string key) => now.MemoryKb.GetValueOrDefault(key);
            ulong free = Get("MemFree"), cache = Get("Buffers") + Get("Cached") + Get("SReclaimable");
            ulong used = total > free + cache ? total - free - cache : 0;
            ulong available = now.MemoryKb.TryGetValue("MemAvailable", out ulong a) ? a : free + cache;
            ulong swapTotal = Get("SwapTotal"), swapUsed = swapTotal > Get("SwapFree") ? swapTotal - Get("SwapFree") : 0;
            memory = new MemoryReading(total * 1024, used * 1024, cache * 1024, free * 1024, available * 1024, swapTotal * 1024, swapUsed * 1024);
        }

        double? seconds = !restarted && now.Uptime is { } t1 && previous?.Uptime is { } t0 && t1 - t0 >= 0.01 ? t1 - t0 : null;
        double? rx = null, tx = null;
        var interfaces = new List<InterfaceReading>();
        foreach (var (name, n) in now.Network.Where(n => n.Key != "lo").OrderByDescending(n => (decimal)n.Value.Rx + n.Value.Tx).Take(32))
        {
            bool known = seconds != null && previous!.Network.TryGetValue(name, out var o) && n.Rx >= o.Rx && n.Tx >= o.Tx;
            var old = known ? previous!.Network[name] : default;
            interfaces.Add(new InterfaceReading(name, known ? (n.Rx - old.Rx) / seconds : null, known ? (n.Tx - old.Tx) / seconds : null));
        }
        if (seconds is { } dt && previous != null)
        {
            ulong sr = 0, st = 0; bool valid = true;
            foreach (var (name, n) in now.Network)
            {
                if (name == "lo") continue;
                if (!previous.Network.TryGetValue(name, out var o) || n.Rx < o.Rx || n.Tx < o.Tx) { valid = false; break; }
                sr += n.Rx - o.Rx; st += n.Tx - o.Tx;
            }
            if (valid) { rx = sr / dt; tx = st / dt; }
        }

        var mounts = now.Mounts.Select(m =>
        {
            double? read = null, write = null;
            if (seconds is { } dt && previous != null && Device(m.Source, now.DeviceMapper) is { } dev
                && now.Disks.TryGetValue(dev, out var c) && previous.Disks.TryGetValue(dev, out var o) && c.SectorsRead >= o.SectorsRead && c.SectorsWritten >= o.SectorsWritten)
            {
                read = (c.SectorsRead - o.SectorsRead) * 512d / dt;
                write = (c.SectorsWritten - o.SectorsWritten) * 512d / dt;
            }
            return new MountReading(m, read, write);
        }).ToList();
        // A device mounted in several places is one device.
        var devices = mounts.Where(m => m.ReadPerSecond != null).DistinctBy(m => m.Info.Source).ToList();
        return new MonitorReading(now, Percent("cpu"), cores, memory, rx, tx, mounts)
        {
            Breakdown = Breakdown(),
            ReadPerSecond = devices.Count > 0 ? devices.Sum(m => m.ReadPerSecond) : null,
            WritePerSecond = devices.Count > 0 ? devices.Sum(m => m.WritePerSecond) : null,
            Interfaces = interfaces,
        };
    }

    /// <summary>/dev/sda1 → sda1; /dev/mapper/vg-root → dm-N (via /sys/block/dm-*/dm/name).</summary>
    static string? Device(string source, IReadOnlyDictionary<string, string> mapper)
    {
        if (!source.StartsWith("/dev/", StringComparison.Ordinal)) return null;
        if (source.StartsWith("/dev/mapper/", StringComparison.Ordinal)) return mapper.GetValueOrDefault(source["/dev/mapper/".Length..]);
        return source[5..];
    }

    public static string Bytes(double? value, string suffix = "")
    {
        if (value is not { } v) return "—";
        string text = v switch
        {
            >= 1L << 40 => $"{v / (1L << 40):0.0} T",
            >= 1L << 30 => $"{v / (1L << 30):0.0} G",
            >= 1L << 20 => $"{v / (1L << 20):0.0} M",
            >= 1L << 10 => $"{v / 1024:0.0} K",
            _ => $"{v:0} B",
        };
        return text + suffix;
    }
}
