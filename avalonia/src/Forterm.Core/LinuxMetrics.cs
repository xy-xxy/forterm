using System.Globalization;

namespace Forterm.Core;

public sealed record CpuTicks(ulong[] Values);
public sealed record NetworkTicks(ulong Received, ulong Sent);
public sealed record LinuxSample(double? Uptime, double[]? Load, IReadOnlyDictionary<string, CpuTicks> Cpu,
    ulong? MemoryTotal, ulong? MemoryAvailable, ulong? DiskTotal, ulong? DiskUsed,
    IReadOnlyDictionary<string, NetworkTicks> Network);
public sealed record LinuxReading(LinuxSample Sample, IReadOnlyDictionary<string, double?> CpuPercent,
    double? ReceiveBytesPerSecond, double? SendBytesPerSecond)
{
    public double? MemoryPercent => Sample.MemoryTotal is > 0 && Sample.MemoryAvailable is { } a ? 100d * (Sample.MemoryTotal.Value - a) / Sample.MemoryTotal.Value : null;
    public double? DiskPercent => Sample.DiskTotal is > 0 && Sample.DiskUsed is { } u ? 100d * u / Sample.DiskTotal.Value : null;
}

/// <summary>Linux procfs cumulative counters. No load-average-as-CPU or fabricated first-sample rates.</summary>
public static class LinuxMetrics
{
    public const string Command = "LC_ALL=C; export LC_ALL; " +
        "printf 'FT_MONITOR1\\nUPTIME\\n'; cat /proc/uptime; " +
        "printf 'LOAD\\n'; cat /proc/loadavg; " +
        "printf 'CPU\\n'; cat /proc/stat; " +
        "printf 'MEM\\n'; cat /proc/meminfo; " +
        "printf 'DISK\\n'; df -kP /; " +
        "printf 'NET\\n'; cat /proc/net/dev";
    static string[] Words(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    static bool Number(string s, out ulong n) => ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n);
    static double? Real(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) && n >= 0 ? n : null;
    public static LinuxSample Parse(string output)
    {
        if (output.Length > 1024 * 1024) throw new IOException("监控输出超出1MiB边界");
        var lines = output.Replace("\r", "").Split('\n');
        if (lines.Length == 0 || lines[0] != "FT_MONITOR1") throw new IOException("监控响应格式不受支持");
        string section = ""; double? uptime = null; double[]? load = null;
        var cpu = new Dictionary<string, CpuTicks>(StringComparer.Ordinal);
        var network = new Dictionary<string, NetworkTicks>(StringComparer.Ordinal);
        ulong? total = null, available = null, diskTotal = null, diskUsed = null;
        foreach (var line in lines.Skip(1))
        {
            if (line is "UPTIME" or "LOAD" or "CPU" or "MEM" or "DISK" or "NET") { section = line; continue; }
            var w = Words(line); if (w.Length == 0) continue;
            switch (section)
            {
                case "UPTIME": uptime = Real(w[0]); break;
                case "LOAD": if (w.Length >= 3 && Real(w[0]) is { } l1 && Real(w[1]) is { } l5 && Real(w[2]) is { } l15) load = [l1, l5, l15]; break;
                case "CPU":
                    if (cpu.Count >= 1025 || !(w[0] == "cpu" || w[0].StartsWith("cpu", StringComparison.Ordinal) && int.TryParse(w[0].AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out int core) && core is >= 0 and < 1024) || w.Length < 5) break;
                    // Fields beyond steal (guest and guest_nice) are already included in user/nice.
                    var ticks = new ulong[8]; bool valid = true;
                    for (int j = 0; j < Math.Min(8, w.Length - 1); j++) if (!Number(w[j + 1], out ticks[j])) { valid = false; break; }
                    if (valid) cpu[w[0]] = new(ticks);
                    break;
                case "MEM":
                    if (w.Length == 3 && w[2] == "kB" && Number(w[1], out ulong kb) && kb <= ulong.MaxValue / 1024)
                    { if (w[0] == "MemTotal:") total = kb * 1024; if (w[0] == "MemAvailable:") available = kb * 1024; }
                    break;
                case "DISK":
                    if (w.Length >= 6 && w[^1] == "/" && Number(w[1], out ulong dt) && Number(w[2], out ulong du) && du <= dt && dt <= ulong.MaxValue / 1024)
                    { diskTotal = dt * 1024; diskUsed = du * 1024; }
                    break;
                case "NET":
                    int colon = line.IndexOf(':'); if (colon < 1 || network.Count >= 4096) break;
                    string name = line[..colon].Trim(); var fields = Words(line[(colon + 1)..]);
                    if (name.Length is > 0 and <= 128 && fields.Length >= 16 && Number(fields[0], out ulong rx) && Number(fields[8], out ulong tx)) network[name] = new(rx, tx);
                    break;
            }
        }
        if (total is null || total == 0 || available > total) available = null;
        return new(uptime, load, cpu, total, available, diskTotal, diskUsed, network);
    }
    public static LinuxReading Compare(LinuxSample now, LinuxSample? previous)
    {
        var percentages = new Dictionary<string, double?>();
        bool restarted = now.Uptime is { } u && previous?.Uptime is { } p && u <= p;
        foreach (var (name, ticks) in now.Cpu)
        {
            double? percent = null;
            if (!restarted && previous?.Cpu.TryGetValue(name, out var old) == true)
            {
                double sum = 0, idle = 0; bool valid = ticks.Values.Length == 8 && old.Values.Length == 8;
                if (valid) for (int j = 0; j < 8; j++) { if (ticks.Values[j] < old.Values[j]) { valid = false; break; } double delta = ticks.Values[j] - old.Values[j]; sum += delta; if (j is 3 or 4) idle += delta; }
                if (valid && sum > 0) percent = Math.Clamp(100d * (sum - idle) / sum, 0, 100);
            }
            percentages[name] = percent;
        }
        double? receive = null, send = null;
        if (!restarted && now.Uptime is { } t && previous?.Uptime is { } before && t > before)
        {
            var names = now.Network.Keys.Where(n => n != "lo").Order().ToArray(); if (names.Length == 0) names = now.Network.Keys.Order().ToArray();
            var oldNames = previous.Network.Keys.Where(n => n != "lo").Order().ToArray(); if (oldNames.Length == 0) oldNames = previous.Network.Keys.Order().ToArray();
            if (names.Length > 0 && names.SequenceEqual(oldNames))
            {
                double rx = 0, tx = 0; bool valid = true;
                foreach (var name in names) { var n = now.Network[name]; var o = previous.Network[name]; if (n.Received < o.Received || n.Sent < o.Sent) { valid = false; break; } rx += n.Received - o.Received; tx += n.Sent - o.Sent; }
                if (valid) { receive = rx / (t - before); send = tx / (t - before); }
            }
        }
        return new(now, percentages, receive, send);
    }
}
