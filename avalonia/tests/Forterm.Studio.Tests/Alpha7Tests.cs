using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Studio.Controls;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>0.4.0-alpha.7: the monitor, designed anew.</summary>
public sealed class Alpha7Tests
{
    const ulong G = 1UL << 30, M = 1UL << 20;

    static MonitorSample Host(int cores, double[] load, string os = "Ubuntu", string version = "24.04.5 LTS (Noble Numbat)") => new(
        new OsInfo(os, version, os + " " + version), "CST GMT+0800", 12 * 86400 + 3600, load,
        new Dictionary<string, ulong[]>(), new Dictionary<string, ulong>(), [], new Dictionary<string, DiskCounters>(), new Dictionary<string, string>(),
        new Dictionary<string, (ulong, ulong)> { ["eth0"] = (900 * M, 300 * M), ["docker0"] = (5 * M, 2 * M) },
        [
            new(4242, "app", "Sl", 182.5, 3 * G, "11-22 15:39:59", "/usr/lib/jvm/java-17/bin/java -Xmx4g -jar /opt/app/app.jar --spring.profiles.active=prod"),
            new(2001, "mysql", "Ssl", 41.0, 2 * G, "11-10 09:21:44", "/usr/sbin/mysqld"),
            new(3105, "www-data", "S", 12.3, 180 * M, "11-20 08:00:01", "nginx: worker process"),
            new(5120, "root", "R", 6.0, 12 * M, "11-24 10:01:00", "rsync -a /data/ /backup/"),
            new(77, "root", "I", 1.1, 0, "11-10 09:21:44", "[kworker/3:1-events]"),
            new(1, "root", "Ss", 0.0, 13 * M, "11-10 09:21:44", "/sbin/init"),
        ]);

    static MonitorReading Reading(int i, int cores, MountReading[] mounts, MemoryReading memory, double[] load, double cpu, double wait = 2)
    {
        double wave = Math.Sin(i / 6d);
        return new MonitorReading(Host(cores, load), i == 0 ? null : cpu,
            Enumerable.Range(0, cores).Select(c => (c.ToString(), i == 0 ? null : (double?)Math.Clamp(cpu + 35 * Math.Sin(c * 1.7 + i / 9d), 0, 100))).ToList(), memory,
            i == 0 ? null : 3.2 * M * (1.1 + wave), i == 0 ? null : 0.7 * M * (1.2 + Math.Cos(i / 4d)), mounts)
        {
            Breakdown = i == 0 ? null : new CpuBreakdown(cpu * 0.7, cpu * 0.3, wait, 0),
            ReadPerSecond = i == 0 ? null : 1.5 * M * (1 + Math.Sin(i / 3d)),
            WritePerSecond = i == 0 ? null : 9 * M * (1 + Math.Cos(i / 7d)),
            Interfaces = [new("eth0", 3.1 * M, 0.7 * M), new("docker0", 12_000, 9_000)],
        };
    }

    static MountReading Mount(string path, string type, double size, double percent) =>
        new(new MountInfo("/dev/" + path.Replace('/', 'x'), type, (ulong)(size * G), (ulong)(size * G * percent / 100), (ulong)(size * G * (100 - percent) / 100), path), 1.2 * M, 8.4 * M);

    /// <summary>A monitor with three minutes of readings, none of them from a server.</summary>
    static MonitorViewModel Filled(int cores, bool busy)
    {
        var vm = new MonitorViewModel("web-01", () => null, autoRefresh: true);
        var start = new DateTime(2026, 11, 24, 10, 38, 0);
        MountReading[] mounts = busy ? [Mount("/", "ext4", 95, 61), Mount("/data", "xfs", 500, 97.4), Mount("/backup", "xfs", 1800, 88), Mount("/mnt/cdrom", "iso9660", 4, 100)] : [Mount("/", "ext4", 95, 20), Mount("/data", "xfs", 500, 42)];
        var memory = busy ? new MemoryReading(16 * G, 14 * G, 1 * G, 1 * G, 1 * G, 4 * G, 3 * G) : new MemoryReading(16 * G, 5 * G, 6 * G, 5 * G, 10 * G, 4 * G, 0);
        for (int i = 0; i < 62; i++)
        {
            // Ten readings are missing: the column was hidden for half a minute.
            if (i is > 20 and < 31) continue;
            double cpu = busy ? 92 + 5 * Math.Sin(i / 5d) : 34 + 18 * Math.Sin(i / 8d) + 6 * Math.Cos(i / 2d);
            vm.Apply(Reading(i, cores, mounts, memory, busy ? [cores * 2.6, cores * 2.3, cores * 1.4] : [1.12, 0.94, 0.80], cpu, busy ? 36 : 2), start.AddSeconds(i * 3));
        }
        return vm;
    }

    static (Window Window, MonitorView View) Show(MonitorViewModel vm, double width, double height, ThemeVariant? theme = null)
    {
        var view = new MonitorView { DataContext = vm };
        var window = new Window { Width = width, Height = height, Content = view, RequestedThemeVariant = theme ?? ThemeVariant.Dark };
        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("St.Panel"));
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    static void Shot(Window window, string name)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, name + ".png"));
    }

    static void Tick(int frames = 1) { for (int i = 0; i < frames; i++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } }

    /// <summary>What Motion.axaml says really moves, ends where it would have been without, and switches off.</summary>
    [AvaloniaFact]
    public void AnimationsRunAndEndWhereTheyShould()
    {
        var app = Application.Current!;
        Motion.Apply(app, true);
        try
        {
            var tab = new Button { Content = "页签" };
            tab.Classes.Add("session-tab");
            var meter = new Meter { Value = 10, Width = 100 };
            var spinner = new BusyRing { IsActive = true };
            var panel = new StackPanel { Children = { tab, meter, spinner } };
            var window = new Window { Width = 300, Height = 200, Content = panel };
            window.Show();
            Tick(2);
            double early = tab.Opacity;
            Assert.True(early < 0.9, "the tab does not fade in: " + early);
            meter.Value = 90;
            Tick(2);
            Assert.InRange(meter.Value, 10.1, 89.9);
            double angle = spinner.Angle;
            Tick(6);
            Assert.NotEqual(angle, spinner.Angle);
            spinner.IsActive = false; // and it costs nothing when there is nothing to wait for
            Tick(2);
            angle = spinner.Angle;
            Tick(6);
            Assert.Equal(angle, spinner.Angle);
            for (int i = 0; i < 400 && (tab.Opacity < 1 || meter.Value < 90); i++) Tick();
            Assert.Equal(1, tab.Opacity);
            Assert.Equal(90, meter.Value);
            window.Close();

            Motion.Apply(app, false);
            var still = new Button { Content = "页签" };
            still.Classes.Add("session-tab");
            var second = new Window { Width = 300, Height = 200, Content = still };
            second.Show();
            Tick();
            Assert.Equal(1, still.Opacity);
            second.Close();
        }
        finally { Motion.Apply(app, false); }
    }

    [AvaloniaFact]
    public void NothingReachesPastItsCard()
    {
        Appearance.Apply(new StudioPreferences());
        foreach (double width in new[] { 200d, 280, 420, 760, 1200 })
        {
            using var vm = Filled(8, busy: true);
            var (window, view) = Show(vm, width, 900);
            foreach (var card in view.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("card")))
            {
                double right = card.TranslatePoint(new Point(card.Bounds.Width, 0), view)!.Value.X;
                Assert.True(right <= width - 7, $"{width}: {card.Name} ends at {right}");
                foreach (var text in card.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0))
                {
                    double end = text.TranslatePoint(new Point(text.Bounds.Width, 0), view)!.Value.X;
                    Assert.True(end <= right - 11, $"{width}: “{text.Text}” ends at {end}, its card at {right}; " + string.Join(" < ", text.GetSelfAndVisualAncestors().TakeWhile(v => v != view).Select(v => $"{v.GetType().Name}[{v.Bounds.X:0},{v.Bounds.Width:0}]")));
                }
            }
            window.Close();
        }
    }

    /// <summary>Numbers that grow while the column is shown get the room they need.</summary>
    [AvaloniaFact]
    public void NewNumbersAreNotCutOff()
    {
        Appearance.Apply(new StudioPreferences());
        var vm = new MonitorViewModel("web-01", () => null, autoRefresh: true);
        MountReading[] mounts = [Mount("/", "ext4", 95, 20)];
        var memory = new MemoryReading(16 * G, 5 * G, 6 * G, 5 * G, 10 * G, 4 * G, 0);
        var start = new DateTime(2026, 11, 24, 10, 38, 0);
        vm.Apply(Reading(0, 4, mounts, memory, [0.1, 0.1, 0.1], 5), start); // rates are unknown: "—"
        var (window, view) = Show(vm, 280, 1200);
        for (int i = 1; i < 4; i++)
        {
            vm.Apply(Reading(i, 4, [Mount("/", "ext4", 1800, 64.2)], memory, [123.45, 110.01, 98.76], 97), start.AddSeconds(3 * i));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
        int numbers = 0;
        foreach (var text in view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && (t.Classes.Contains("figure") || t.Classes.Contains("num") || t.Classes.Contains("strong")) && t.Text is { Length: > 0 } s && char.IsAsciiDigit(s[^1] == '%' || s.Contains("/s") ? s[0] : s[^1])))
        {
            var free = new TextBlock { Text = text.Text, FontSize = text.FontSize, FontWeight = text.FontWeight, FontFamily = text.FontFamily, FontFeatures = text.FontFeatures };
            free.Measure(Size.Infinity);
            Assert.True(text.Bounds.Width >= free.DesiredSize.Width - 1, $"“{text.Text}” needs {free.DesiredSize.Width} and got {text.Bounds.Width}");
            numbers++;
        }
        Assert.True(numbers > 15, numbers + " numbers");
        window.Close();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void MonitorRendersInTheSideColumnAndMaximised()
    {
        Appearance.Apply(new StudioPreferences());
        foreach (var (name, cores, busy, width, height, theme) in new[]
        {
            ("monitor-narrow", 8, false, 280d, 1180d, ThemeVariant.Dark),
            ("monitor-narrow-light", 8, false, 280d, 1180d, ThemeVariant.Light),
            ("monitor-narrow-busy", 8, true, 280d, 1330d, ThemeVariant.Dark),
            ("monitor-narrow-busy-light", 8, true, 280d, 1330d, ThemeVariant.Light),
            ("monitor-wide", 8, false, 1200d, 720d, ThemeVariant.Dark),
            ("monitor-wide-busy-96-cores", 96, true, 1200d, 820d, ThemeVariant.Dark),
            ("monitor-two-columns", 16, false, 760d, 900d, ThemeVariant.Light),
        })
        {
            using var vm = Filled(cores, busy);
            var (window, _) = Show(vm, width, height, theme);
            Shot(window, name);
            window.Close();
        }
    }

    static async Task Pump(Func<bool> until, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (!until() && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); await Task.Delay(30); }
        Dispatcher.UIThread.RunJobs();
    }

    static void Press(Window window, Shortcut s, PhysicalKey physical = PhysicalKey.None) 
    {
        window.KeyPress(s.Gesture.Key, (RawInputModifiers)(int)s.Gesture.KeyModifiers, physical, null);
        window.KeyRelease(s.Gesture.Key, (RawInputModifiers)(int)s.Gesture.KeyModifiers, physical, null);
    }

    // ---------- monitor: numbers and conclusions ----------

    [Fact]
    public void FindingsNameWhatIsWrongWorstFirst()
    {
        var calm = Filled(8, busy: false);
        Assert.Empty(calm.Findings);
        Assert.Equal("运行正常", calm.Headline);
        var vm = Filled(8, busy: true);
        Assert.Contains("/data 已用 97%", vm.Findings[0].Text);
        Assert.True(vm.Findings[0].IsDanger && vm.IsSerious && !vm.IsHealthy);
        Assert.Equal("1 项严重，6 项注意", vm.Headline);
        Assert.Equal(3, vm.ShownFindings.Count);
        Assert.Equal("另有 4 项…", vm.MoreFindingsText);
        vm.ToggleFindingsCommand.Execute(null);
        Assert.Equal(7, vm.ShownFindings.Count);
        string all = string.Join("|", vm.Findings.Select(f => f.Text));
        foreach (string part in new[] { "/backup 已用 88%", "可用内存只剩 6%", "交换区已用 75%", "CPU 近 1 分钟平均 9", "CPU 等待磁盘 36%", "5 分钟负载 18.4，是 8 核的 2.3 倍" }) Assert.Contains(part, all);
        Assert.DoesNotContain("cdrom", all); // a disc image is full by nature
        Assert.DoesNotContain(vm.Mounts, m => m.Mount.Contains("cdrom"));
        Assert.Equal("/data", vm.Mounts[0].Mount); // the fullest first
        calm.Dispose();
        vm.Dispose();
    }

    [Fact]
    public void OneBusyMomentIsNotAFinding()
    {
        var start = new DateTime(2026, 11, 24, 10, 0, 0);
        MonitorPoint P(int second, double? cpu) => new(start.AddSeconds(second), cpu, 10, 0, 0, 0, 0, 0, 0);
        double? Average(params MonitorPoint[] points) => MonitorFindings.Average(points, p => p.Cpu)?.Value;
        Assert.Null(Average());
        Assert.Null(Average(P(0, 100)));
        Assert.Null(Average(P(0, 100), P(3, 100), P(6, 100))); // too short
        Assert.Null(Average(P(0, null), P(30, null), P(45, 100))); // too short as well
        Assert.Equal(25, Average(P(0, 100), P(30, 0), P(60, 50))); // the first has no time before it
        Assert.Equal(45, Average(P(0, 100), P(100, 30), P(130, 40), P(160, 50))!.Value, 6); // not the long gap before
        Assert.Equal(50, Average(P(0, null), P(10, 80), P(40, 40))); // by the time each is about
        Assert.Null(Average(P(40, 100), P(0, 100), P(0, 100))); // the clock was set back
        Assert.Equal(MonitorLevel.Normal, MonitorFindings.Cpu(null));
        Assert.Equal(MonitorLevel.Normal, MonitorFindings.Disk(84.9));
        Assert.Equal(MonitorLevel.Warning, MonitorFindings.Disk(85));
        Assert.Equal(MonitorLevel.Danger, MonitorFindings.Disk(95));
        Assert.Equal(MonitorLevel.Normal, MonitorFindings.Load(15.9, 8));
        Assert.Equal(MonitorLevel.Warning, MonitorFindings.Load(16, 8));
        Assert.Equal(MonitorLevel.Normal, MonitorFindings.Load(100, 0)); // unknown core count
    }

    [Fact]
    public void ProbeReadsCpuBreakdownInterfacesAndDfPercent()
    {
        string Sample(int n) => $"FTS1\nUPTIME\n{100 + n * 10}.0 1\nCPU\ncpu {100 + n * 40} {n * 10} {100 + n * 20} {1000 + n * 10} {n * 10} {n * 2} {n * 3} {n * 5}\ncpu0 1 1 1 1 1 1 1 1\n" +
            "DF\nFilesystem Type 1024-blocks Used Available Capacity Mounted on\n/dev/sda1 ext4 1000 900 50 95% /\n/dev/sda1 ext4 1000 900 50 95% /again\n/dev/loop0 iso9660 10 10 0 100% /mnt\n" +
            $"DISKSTATS\n 8 1 sda1 1 0 {n * 20} 0 1 0 {n * 40} 0 0 0 0\nNET\n eth0: {n * 5000} 0 0 0 0 0 0 0 {n * 1000} 0 0 0 0 0 0 0\n eth1: 10 0 0 0 0 0 0 0 10 0 0 0 0 0 0 0\n lo: 1 0 0 0 0 0 0 0 1 0 0 0 0 0 0 0\n";
        var none = MonitorProbe.Compare(MonitorProbe.Parse(Sample(0)), null);
        Assert.Null(none.Breakdown);
        Assert.Null(none.ReadPerSecond);
        Assert.All(none.Interfaces, i => Assert.Null(i.ReceivePerSecond));
        var r = MonitorProbe.Compare(MonitorProbe.Parse(Sample(1)), MonitorProbe.Parse(Sample(0)));
        Assert.Equal(50, r.Breakdown!.User, 3);    // user + nice: 50 of 100 ticks
        Assert.Equal(25, r.Breakdown.System, 3);   // system + irq + softirq
        Assert.Equal(10, r.Breakdown.Wait, 3);
        Assert.Equal(5, r.Breakdown.Steal, 3);
        Assert.Equal(100d * 900 / 950, r.Mounts[0].UsedPercent, 3); // as df: reserved blocks are not available
        Assert.True(r.Mounts[2].IsImage);
        Assert.Equal(1024, r.ReadPerSecond!.Value, 3); // the device is mounted twice and counted once
        Assert.Equal(2048, r.WritePerSecond!.Value, 3);
        Assert.Equal(["eth0", "eth1"], r.Interfaces.Select(i => i.Name));
        Assert.Equal(500, r.Interfaces[0].ReceivePerSecond!.Value, 3);
    }

    [AvaloniaFact]
    public async Task AFailedReadingStopsAndSaysSoUntilTheUserRetries()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "监控"));
        await Pump(() => w.Monitor!.HasData, TimeSpan.FromSeconds(10));
        var dead = new MonitorViewModel("x", () => w.Session, autoRefresh: true);
        Assert.Equal("3 秒", dead.RefreshText);
        Assert.True(dead.AutoRefresh);
        dead.Interval = 30;
        Assert.Equal("30 秒", dead.RefreshText);
        Assert.Equal(TimeSpan.FromSeconds(90), dead.TrendGap);
        Assert.Equal([false, false, false, true], dead.Intervals.Select(i => i.IsSelected));
        dead.AutoRefresh = false;
        Assert.Equal("手动", dead.RefreshText);
        dead.AutoRefresh = true;
        Assert.Equal(3, dead.Interval);
        dead.Minutes = 10;
        Assert.Equal("最近 10 分钟", dead.RangeText);

        // The session is gone: the numbers stay, dimmed, and the loop does not go on.
        ((MainWindowViewModel)h.Model).Close(w);
        await dead.RefreshAsync();
        Assert.True(dead.IsStalled || dead.Message.Length > 0);
        Assert.Contains("监控", dead.Message);
        dead.Dispose();
    }

    [AvaloniaFact]
    public void RowsStayWhileNumbersChange()
    {
        var vm = Filled(4, busy: false);
        var vital = vm.Vitals[0];
        var mount = vm.Mounts[0];
        var process = vm.TopProcesses[0];
        string before = vital.Figure;
        vm.Apply(Reading(70, 4, [Mount("/data", "xfs", 500, 44), Mount("/", "ext4", 95, 20)], new MemoryReading(16 * G, 5 * G, 6 * G, 5 * G, 10 * G, 0, 0), [1, 1, 1], 77), new DateTime(2026, 11, 24, 10, 42, 0));
        Assert.Same(vital, vm.Vitals[0]);
        Assert.Same(mount, vm.Mounts[0]);
        Assert.Same(process, vm.TopProcesses[0]);
        Assert.NotEqual(before, vital.Figure);
        Assert.Equal("77%", vital.Figure);
        Assert.Equal("44%", mount.UsedText);
        Assert.Equal("未启用", vm.SwapText);
        Assert.Equal(("java", "-Xmx4g -jar /opt/app/app.jar --spring.profiles.active=prod"), (process.Name, process.Arguments));
        vm.SortByMemoryCommand.Execute(null);
        Assert.Equal("3.0 G", vm.TopProcesses[0].Value);
        Assert.Equal(("nginx: worker process", ""), MonitorViewModel.SplitCommand("nginx: worker process"));
        Assert.Equal(("[kworker/0:1]", ""), MonitorViewModel.SplitCommand("[kworker/0:1]"));
        Assert.Equal(("sh", "-c x"), MonitorViewModel.SplitCommand("  /bin/sh -c x "));
        // A click on a process opens the process manager at it.
        string? asked = null;
        vm.ShowProcessesCommand = new Forterm.Studio.Mvvm.RelayCommand(p => asked = p as string);
        vm.ShowProcessCommand.Execute(vm.TopProcesses[0]);
        Assert.Equal("4242", asked);
        string csv = vm.HistoryCsv();
        Assert.Equal(vm.History.Count + 1, csv.TrimEnd().Split('\n').Length);
        Assert.StartsWith("time,cpu_percent", csv);
        Assert.Contains("2026-11-24 10:42:00,77,", csv);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void ProcessListKeepsItsPlaceWhileItRefreshes()
    {
        var vm = Filled(4, busy: false);
        var ui = new ScriptedInteraction();
        var list = new ProcessListViewModel(vm, () => null, ui);
        var window = new ProcessWindow { DataContext = list };
        window.Show();
        Assert.Equal("CPU ↓", list.CpuHead);
        Assert.Equal("内存", list.MemoryHead);
        var mysql = list.Rows.Single(r => r.Pid == 2001);
        Assert.Equal(("mysqld", "睡眠", 100d * 2 / 16), (mysql.Name, mysql.StateText, mysql.MemoryShare));
        Assert.True(new ProcessRow(new ProcessInfo(9, "u", "D+", 0, 0, "", "x")).IsStuck);
        list.SelectedRow = mysql;
        var untouched = list.Rows[0];
        // mysqld falls behind nginx: its row moves, the selection goes with it, other rows are the same objects.
        var busy = Reading(80, 4, [Mount("/", "ext4", 95, 20)], new MemoryReading(16 * G, 5 * G, 6 * G, 5 * G, 10 * G, 0, 0), [1, 1, 1], 50);
        var changed = busy with { Sample = busy.Sample with { Processes = busy.Sample.Processes!.Select(p => p.Pid == 2001 ? p with { Cpu = 8 } : p).ToArray() } };
        vm.Apply(changed, new DateTime(2026, 11, 24, 10, 43, 0));
        Assert.Equal(2001, list.SelectedRow!.Pid);
        Assert.Equal(2, list.Rows.IndexOf(list.SelectedRow));
        Assert.Same(untouched, list.Rows[0]);
        list.SortCommand.Execute("mem");
        Assert.Equal("内存 ↓", list.MemoryHead);
        Assert.Equal(2001, list.SelectedRow!.Pid);
        list.CopyPidCommand.Execute(list.SelectedRow);
        Assert.Equal("2001", ui.Clipboard);
        // Gone: nothing else is selected in its place.
        vm.Apply(changed with { Sample = changed.Sample with { Processes = changed.Sample.Processes!.Where(p => p.Pid != 2001).ToArray() } }, new DateTime(2026, 11, 24, 10, 43, 3));
        Assert.Null(list.SelectedRow);
        list.SelectedRow = list.Rows[1];
        Shot(window, "process-manager-redesigned");
        window.Close();
        list.Dispose();
        vm.Dispose();
    }

    [AvaloniaFact]
    public void ChartsReadTheSameMomentByPointerAndKeyboard()
    {
        Assert.Equal(1024, TrendChart.RoundUp(0));
        Assert.Equal(2048, TrendChart.RoundUp(1500));
        Assert.Equal(5 * 1024 * 1024d, TrendChart.RoundUp(4.2 * M));
        Assert.Equal(200 * 1024 * 1024d, TrendChart.RoundUp(150.0 * M));
        using var vm = Filled(8, busy: false);
        var (window, view) = Show(vm, 280, 900);
        var charts = view.GetVisualDescendants().OfType<TrendChart>().ToArray();
        Assert.Equal(4, charts.Length);
        var at = charts[0].TranslatePoint(new Point(charts[0].Bounds.Width - 20, 30), window)!.Value;
        window.MouseMove(at);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(vm.HoverTime);
        Assert.All(charts, c => Assert.Equal(vm.HoverTime, c.HoverTime));
        Assert.Contains(vm.History, p => p.Time == vm.HoverTime); // a reading, not a point in between
        window.MouseMove(charts[0].TranslatePoint(new Point(charts[0].Bounds.Width / 2, 30), window)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(vm.History, p => p.Time == vm.HoverTime);
        Assert.InRange((vm.History[^1].Time - vm.HoverTime!.Value).TotalSeconds, 75, 105); // the middle of three minutes
        window.MouseMove(at);
        Dispatcher.UIThread.RunJobs();
        Shot(window, "monitor-hover");
        window.MouseMove(new Point(5, 5));
        Dispatcher.UIThread.RunJobs();
        Assert.Null(vm.HoverTime);
        charts[0].Focus();
        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        Assert.Equal(vm.History[^1].Time, vm.HoverTime);
        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
        Assert.Equal(vm.History[^2].Time, vm.HoverTime);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.Null(vm.HoverTime);
        window.Close();

        // Bars while there is room, squares on large machines, nothing outside.
        var (bars, isBars, height) = CoreBars.Arrange(16, 240);
        Assert.True(isBars && bars.Length == 16 && bars.All(b => b.Right <= 240));
        var (cells, isBars2, tall) = CoreBars.Arrange(128, 240);
        Assert.True(!isBars2 && cells.Length == 128 && cells.All(c => c.Right <= 240 && c.Bottom <= tall) && tall > height);
        Assert.Empty(CoreBars.Arrange(0, 240).Cells);
    }

    // ---------- palette, shortcuts, toasts ----------

    [Fact]
    public void PaletteScoresTitlesBeforeDetails()
    {
        PaletteEntry E(string title, string detail = "", string group = "组") => new(group, title, detail, "", () => { });
        Assert.True(PaletteViewModel.Score(E("web-01"), ["web"]) > PaletteViewModel.Score(E("my-web"), ["web"]));
        Assert.True(PaletteViewModel.Score(E("my-web"), ["web"]) > PaletteViewModel.Score(E("db", "root@web"), ["web"]));
        Assert.True(PaletteViewModel.Score(E("新建本地终端"), ["建终"]) > 0); // letters in order
        Assert.Equal(0, PaletteViewModel.Score(E("web-01"), ["web", "db"])); // every word
        Assert.Equal(1, PaletteViewModel.Score(E("x"), []));
    }

    [AvaloniaFact]
    public async Task PaletteAndShortcutsDriveTheWindow()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var store = new Forterm.Studio.Library.QuickCommandStore(h.Data);
        store.Upsert(new Forterm.Studio.Library.QuickCommand { Name = "磁盘", Text = "df -h", Confirm = true });
        var profile = h.Ssh(fixture.Port, "回环主机") with { DisablePwdFollow = true };
        h.Store.Upsert(profile);
        var vm = new MainWindowViewModel(h.Interaction, h.KnownHosts, h.Store, h.Credentials, quickCommands: store, settingsDirectory: h.Data);
        var window = new MainWindow { Width = 1200, Height = 720, DataContext = vm };
        window.Show();
        try
        {
            string Typed() => System.Text.Encoding.UTF8.GetString(fixture.Stats().GetProperty("input").EnumerateArray().SelectMany(x => Convert.FromHexString(x.GetString()!)).ToArray());
            // Open a connection from the palette.
            Press(window, Shortcuts.Palette);
            Assert.True(vm.Palette.IsOpen);
            await Pump(() => window.FindControl<TextBox>("PaletteText")!.IsFocused, TimeSpan.FromSeconds(2));
            vm.Palette.Text = "回环";
            Assert.Equal("回环主机", vm.Palette.Matches[0].Title);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.False(vm.Palette.IsOpen);
            await Pump(() => vm.Workspaces.Count == 1 && vm.Workspaces[0].HasShell, TimeSpan.FromSeconds(15));
            var first = vm.Workspaces[0];
            await Pump(() => first.Terminal!.IsLive, TimeSpan.FromSeconds(5));
            // A quick command from the palette asks like everywhere else.
            h.Interaction.ConfirmAnswer = false;
            Press(window, Shortcuts.Palette);
            vm.Palette.Text = "磁盘";
            Shot(window, "command-palette");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Pump(() => h.Interaction.Confirms.Count == 1, TimeSpan.FromSeconds(3));
            Assert.Contains("df -h", h.Interaction.Confirms[0]);
            Assert.Equal("", Typed());
            h.Interaction.ConfirmAnswer = true;
            // Escape and a second press of the shortcut close it; arrows move.
            Press(window, Shortcuts.Palette);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Equal(1, vm.Palette.Selected);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.Equal(vm.Palette.Matches.Count - 1, vm.Palette.Selected);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(vm.Palette.IsOpen);

            // Shortcuts act on the window and are not typed into the shell.
            first.Terminal!.FocusTerminal();
            Press(window, Shortcuts.NewTerminal);
            await Pump(() => first.Terminals.Count == 2 && first.Terminals[1].Control.IsLive, TimeSpan.FromSeconds(8));
            Assert.Equal(2, first.Terminals.Count);
            Press(window, Shortcuts.CloseTab);
            await Pump(() => first.Terminals.Count == 1, TimeSpan.FromSeconds(3));
            Assert.Single(first.Terminals);
            var second = await vm.OpenProfileAsync(profile);
            Assert.Same(second, vm.Selected);
            Press(window, Shortcuts.NextSession);
            Assert.Same(first, vm.Selected);
            Press(window, Shortcuts.PreviousSession);
            Assert.Same(second, vm.Selected);
            vm.ShowSession(1);
            Assert.Same(first, vm.Selected);
            vm.ShowSession(9); // there is none
            Assert.Same(first, vm.Selected);
            double size = Appearance.Current.TerminalFontSize;
            Press(window, Shortcuts.ZoomIn);
            Assert.Equal(size + 1, Appearance.Current.TerminalFontSize);
            Assert.Equal(size + 1, Appearance.Load(h.Data).TerminalFontSize);
            Press(window, Shortcuts.ZoomReset);
            Assert.Equal(14, Appearance.Current.TerminalFontSize);
            Press(window, Shortcuts.Home);
            Assert.True(vm.IsHomeSelected);
            Assert.Equal("", Typed());
            // The last tab: the session itself.
            vm.Selected = first;
            Press(window, Shortcuts.CloseTab);
            await Pump(() => vm.Workspaces.Count == 1, TimeSpan.FromSeconds(5));
            Assert.Same(second, Assert.Single(vm.Workspaces));
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    [Fact]
    public void ShortcutsDoNotCollideAndLeaveTheShellAlone()
    {
        Assert.Equal(Shortcuts.All.Length, Shortcuts.All.Select(s => s.Gesture.ToString()).Distinct().Count());
        foreach (var s in Shortcuts.All)
        {
            // Plain Ctrl+letter belongs to the program in the terminal.
            Assert.False(s.Gesture.KeyModifiers == KeyModifiers.Control && s.Gesture.Key is >= Key.A and <= Key.Z, s.Title);
            Assert.NotEqual("", s.Keys);
            Assert.DoesNotContain(Iciclecreek.Terminal.TerminalKeyGestures.Copy.Concat(Iciclecreek.Terminal.TerminalKeyGestures.Paste), g => g.ToString() == s.Gesture.ToString());
        }
    }

    [AvaloniaFact]
    public void ToastsStackMergeWaitAndLeave()
    {
        var center = new ToastCenter();
        center.Show(new ToastMessage(ToastKind.Success, "一"));
        center.Show(new ToastMessage(ToastKind.Success, "一"));
        Assert.Single(center.Items);
        bool acted = false;
        center.Show(new ToastMessage(ToastKind.Error, "二", "查看", () => acted = true));
        Assert.True(center.Items[1].HasAction && center.Items[1].IsError);
        center.Items[1].ActionCommand.Execute(null);
        Assert.True(acted);
        Assert.Single(center.Items); // without animations it goes at once
        for (int i = 0; i < 6; i++) center.Show(new ToastMessage(ToastKind.Info, "第 " + i));
        Assert.Equal(4, center.Items.Count); // the oldest make room
        Assert.Equal("第 5", center.Items[^1].Text);
        center.Items[0].DismissCommand.Execute(null);
        Assert.Equal(3, center.Items.Count);
    }

    [AvaloniaFact]
    public async Task EventsThatWouldBeMissedAreSaid()
    {
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "回环主机"));
        string a = Path.Combine(h.Data, "a.txt"), folder = Path.Combine(h.Data, "dropped");
        File.WriteAllText(a, "hello\n");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "b.txt"), "b\n");
        // Dropped files and folders take the same way as uploads from the menu.
        await w.DropAsync(w.DropDirectory(null), [a, folder]);
        await Pump(() => h.Interaction.Toasts.Any(t => t.Text.Contains("已完成")), TimeSpan.FromSeconds(20));
        Assert.Contains(h.Interaction.Toasts, t => t.Kind == ToastKind.Success && t.Text.Contains("回环主机") && t.Text.Contains("上传"));
        await Pump(() => w.FileRoots.Any(n => n.Name == "a.txt") && w.FileRoots.Any(n => n.Name == "dropped"), TimeSpan.FromSeconds(10));
        Assert.Contains(w.FileRoots, n => n.Name == "dropped");
        // A file there already is asked about, as from the menu.
        int asked = h.Interaction.Confirms.Count;
        h.Interaction.ConfirmAnswer = false;
        await w.DropAsync("/", [a]);
        Assert.Equal(asked + 1, h.Interaction.Confirms.Count);
        Assert.Equal("未上传任何文件", w.FileNotice);
        h.Interaction.ConfirmAnswer = true;
        var node = w.FileRoots.First(n => n.Name == "a.txt");
        Assert.Equal("/", w.DropDirectory(node));
        Assert.Equal(w.FileRoots.First(n => n.Name == "dropped").Path, w.DropDirectory(w.FileRoots.First(n => n.Name == "dropped")));
        // Copying says what was copied; a download offers its folder.
        w.SelectedNode = node;
        w.CopyPathCommand.Execute(null);
        await Pump(() => h.Interaction.Toasts.Any(t => t.Text.Contains("已复制路径")), TimeSpan.FromSeconds(2));
        Assert.Contains(h.Interaction.Toasts, t => t.Text == "已复制路径：/a.txt");
        h.Interaction.SaveFile = Path.Combine(h.Data, "down.txt");
        w.DownloadCommand.Execute(null);
        await Pump(() => h.Interaction.Toasts.Any(t => t.ActionTitle == "打开文件夹"), TimeSpan.FromSeconds(10));
        h.Interaction.Toasts.Single(t => t.ActionTitle == "打开文件夹").Action!();
        Assert.Equal([h.Data], h.Interaction.OpenedFolders);
        // A connection lost while another page is shown.
        h.Model.Selected = h.Model.Home;
        fixture.Dispose();
        await Pump(() => h.Interaction.Toasts.Any(t => t.Text.Contains("已断开")), TimeSpan.FromSeconds(20));
        var lost = h.Interaction.Toasts.Single(t => t.Text.Contains("已断开"));
        lost.Action!();
        Assert.Same(w, h.Model.Selected);
    }

    [AvaloniaFact]
    public void ColumnWidthsAreRememberedAndMotionIsASetting()
    {
        string data = Paths.TempDirectory();
        try
        {
            Appearance.Apply(new StudioPreferences { FilesWidth = 5, MonitorWidth = double.NaN });
            Assert.Equal((200, 280), (Appearance.Current.FilesWidth, Appearance.Current.MonitorWidth));
            var vm = new SettingsViewModel(data);
            vm.UseMotion = false;
            Assert.False(Appearance.Load(data).Motion);
            vm.SearchText = "动画";
            Assert.True(vm.ShowMotion && !vm.ShowPresets);
            vm.SearchText = "热键";
            Assert.True(vm.IsKeys && vm.ShowKeysSection);
            vm.SearchText = "";
            var window = new SettingsWindow { DataContext = vm, Width = 900, Height = 640 };
            vm.Section = "keys";
            window.Show();
            Shot(window, "settings-keys");
            window.Close();
            vm.Dispose();
        }
        finally { Appearance.Apply(new StudioPreferences()); Directory.Delete(data, true); }
    }

    // ---------- keyword colours ----------

    static Color? TerminalColour(Highlighter h, string line, string part)
    {
        int at = line.IndexOf(part, StringComparison.Ordinal);
        Assert.True(at >= 0, part);
        foreach (var span in h.Match(line)) if (at >= span.Start && at + part.Length <= span.Start + span.Length) return ((ISolidColorBrush)span.Brush).Color;
        return null;
    }

    [AvaloniaFact]
    public void TerminalKeywordsColourCommandLinesAndLogs()
    {
        var p = TerminalSchemes.StudioDark();
        Color C(string? c) => Color.Parse(c!);
        foreach (var rule in Highlighter.Defaults()) Assert.Null(Highlighter.Check(rule.Pattern));
        Assert.True(Highlighter.Defaults().Length <= Highlighter.MaxRules);
        var h = new Highlighter(Highlighter.Defaults(), p);
        foreach (var (line, part, colour) in new (string, string, string?)[]
        {
            ("[root@web-01 ~]# sudo systemctl status nginx --no-pager -l", "systemctl", p.Blue),
            ("[root@web-01 ~]# sudo systemctl status nginx --no-pager -l", "--no-pager", p.Yellow),
            ("[root@web-01 ~]# sudo systemctl status nginx --no-pager -l", "-l", p.Yellow),
            ("[root@web-01 ~]# sudo systemctl status nginx --no-pager -l", "root@web-01", p.Green),
            ("[root@web-01 ~]# sudo systemctl status nginx --no-pager -l", "status", null),
            ("ops@db:/var/log$ grep -i \"oom\" /var/log/messages", "grep", p.Blue),
            ("ops@db:/var/log$ grep -i \"oom\" /var/log/messages", "\"oom\"", p.Green),
            ("ops@db:/var/log$ grep -i \"oom\" /var/log/messages", "/var/log/messages", p.Cyan),
            ("(base) me@mac proj % ls ~/code", "ls", p.Blue),
            ("$ ./deploy.sh ../x/y", "./deploy.sh", p.Blue),
            ("$ ./deploy.sh ../x/y", "../x/y", p.Cyan),
            ("# Listen 80 in a configuration file", "Listen", null), // a comment is not a prompt
            ("2026-11-24 10:00:01.123 INFO  [main] started in 3 s", "2026-11-24 10:00:01.123", p.Magenta),
            ("2026-11-24 10:00:01.123 INFO  [main] started in 3 s", "INFO", p.Blue),
            ("2026-11-24 10:00:01.123 INFO  [main] started in 3 s", "started", p.Green),
            ("ERROR connection refused by 10.1.2.3:3306", "refused", p.Red),
            ("ERROR connection refused by 10.1.2.3:3306", "10.1.2.3:3306", p.Magenta),
            ("/dev/sda1  95G  20G  76G  21% /", "21%", p.Blue),
            ("the information is here", "information", null),
        })
            Assert.True(TerminalColour(h, line, part) == (colour == null ? null : C(colour)), $"{line} → {part}: {TerminalColour(h, line, part)}");
        // Only the part that is named is coloured.
        var part1 = new Highlighter([new HighlightRule { Pattern = @"id=(?<k>\d+)" }], p).Match("a id=42 b");
        Assert.Equal((5, 2), (Assert.Single(part1).Start, part1[0].Length));
        // Long and awkward rows stay quick.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (string row in new[] { new string('a', 500), new string('/', 500), string.Concat(Enumerable.Repeat("a@b.c ", 90)), new string('"', 500), string.Concat(Enumerable.Repeat("1.2.3.4.", 70)), string.Concat(Enumerable.Repeat("-a ", 160)) })
            for (int i = 0; i < 20; i++) h.Match(row);
        Assert.True(clock.ElapsedMilliseconds < 3000, clock.ElapsedMilliseconds + " ms");
        foreach (var rule in Highlighter.Defaults()) Assert.False(Highlighter.IsSlow(rule), rule.Name);
    }

    [AvaloniaFact]
    public void TerminalKeywordsAreDrawn()
    {
        Appearance.Apply(new StudioPreferences { CursorBlink = false });
        var terminal = new StudioTerminalControl();
        var window = new Window { Width = 900, Height = 300, Content = terminal };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        terminal.Terminal.Write("[root@web-01 ~]# systemctl status nginx --no-pager\r\n   Loaded: loaded (/usr/lib/systemd/system/nginx.service; enabled)\r\n   Active: active (running) since 2026-11-24 10:00:01\r\n" +
            "[root@web-01 ~]# df -h /data\r\n/dev/sdb1       500G  487G   13G  97% /data\r\n[root@web-01 ~]# curl -s https://example.com/health | grep \"status\"\r\n" +
            "2026-11-24 10:00:05 WARN  pool 90% used, retrying 10.0.0.21:3306\r\n2026-11-24 10:00:09 ERROR connection refused (nginx 1.24.0)\r\nops@db-02:/var/log$ tail -n 20 ./app.log\r\n");
        terminal.Refresh();
        Shot(window, "terminal-keywords");
        window.Close();
        terminal.Dispose();
    }

    [Theory]
    [InlineData("/root/deploy.sh", "", "Shell")]
    [InlineData("/home/a/.bashrc", "", "Shell")]
    [InlineData("/opt/run", "#!/usr/bin/env bash\n", "Shell")]
    [InlineData("/opt/tool", "#!/usr/bin/python3\n", "Python")]
    [InlineData("/srv/docker-compose.yml", "", "YAML")]
    [InlineData("/srv/Dockerfile", "", "Dockerfile")]
    [InlineData("/srv/Dockerfile.prod", "", "Dockerfile")]
    [InlineData("/srv/Makefile", "", "Makefile")]
    [InlineData("/etc/nginx/nginx.conf", "", "Nginx")]
    [InlineData("/etc/nginx/conf.d/site.conf", "", "Nginx")]
    [InlineData("/etc/nginx/sites-enabled/default", "", "Nginx")]
    [InlineData("/etc/systemd/system/app.service", "", "INI")]
    [InlineData("/etc/ssh/sshd_config", "", "INI")]
    [InlineData("/etc/my.cnf", "", "INI")]
    [InlineData("/etc/hosts", "", "Hosts")]
    [InlineData("/etc/crontab", "", "Crontab")]
    [InlineData("/etc/something", "x", "INI")]
    [InlineData("/app/application.properties", "", "INI")]
    [InlineData("/app/pom.xml", "", "XML")]
    [InlineData("/app/a.json", "", "Json")]
    [InlineData("/app/state", "{\n  \"a\": 1", "Json")]
    [InlineData("/app/app.log", "", "Log")]
    [InlineData("/app/app.log.1", "", "Log")]
    [InlineData("/app/app.log.2026-11-24", "", "Log")]
    [InlineData("/var/log/messages", "", "Log")]
    [InlineData("/var/log/nginx/access", "", "Log")]
    [InlineData("/etc/nginx/nginx.conf.bak", "", "Nginx")]
    [InlineData("/app/dump.sql", "", "SQL")]
    [InlineData("/app/main.go", "", "Go")]
    [InlineData("/app/Main.java", "", "Java")]
    [InlineData("/app/a.TS", "", "JavaScript")]
    [InlineData("/app/notes.txt", "", "Plain")]
    [InlineData("/app/README", "", "MarkDown")]
    [InlineData("/app/unknown", "hello", "Plain")]
    public void EditorRecognisesFiles(string path, string text, string id) => Assert.Equal(id, EditorSyntax.Detect(path, text).Id);

    static string RoleAt(AvaloniaEdit.Highlighting.IHighlightingDefinition definition, XTerm.Options.ThemeOptions palette, string text, string part)
    {
        var document = new AvaloniaEdit.Document.TextDocument(text);
        using var highlighter = new AvaloniaEdit.Highlighting.DocumentHighlighter(document, definition);
        int at = text.IndexOf(part, StringComparison.Ordinal);
        Assert.True(at >= 0, part);
        var line = highlighter.HighlightLine(document.GetLineByOffset(at).LineNumber);
        var section = line.Sections.LastOrDefault(s => s.Offset <= at && s.Offset + s.Length >= at + part.Length);
        var colour = section?.Color.Foreground?.GetColor(null);
        if (colour == null) return "";
        return string.Join('/', new[] { "Comment", "Keyword", "String", "Number", "Key", "Type", "Bad" }.Where(r => EditorSyntax.ColorOf(palette, r) == colour));
    }

    [AvaloniaFact] // the editor's document belongs to a thread
    public void EditorColoursTheLanguagesOfAServer()
    {
        var p = TerminalSchemes.StudioDark();
        foreach (var (id, text, parts) in new (string, string, (string Part, string Role)[])[]
        {
            ("Shell", "#!/bin/bash\n# note it's fine\nNAME=\"a $HOME b\"\nif [ -f /etc/x ]; then echo hi; fi\n", [("# note it's fine", "Comment"), ("#", "Comment"), ("\"a ", "String"), ("NAME", "Key"), ("$HOME", "Type"), ("if", "Keyword"), ("then", "Keyword"), ("-f", "Type")]),
            ("YAML", "# c\nserver:\n  port: 8080\n  name: \"x\" # tail\n  on: true\n", [("# c", "Comment"), ("server", "Key"), ("8080", "Number"), ("\"x\"", "String"), ("# tail", "Comment"), ("true", "Number")]),
            ("INI", "; c\n[mysqld]\nport = 3306\nPermitRootLogin no\npath=/var/lib/mysql\n", [("; c", "Comment"), ("[mysqld]", "Keyword"), ("port", "Key"), ("3306", "Number"), ("PermitRootLogin", "Key"), ("no", "Number"), ("/var/lib/mysql", "String")]),
            ("Nginx", "server {\n  listen 80; # c\n  root /var/www;\n  set $a \"b\";\n  location / { }\n}\n", [("server", "Keyword"), ("location", "Keyword"), ("listen", "Key"), ("80", "Number"), ("# c", "Comment"), ("/var/www", "String"), ("$a", "Type"), ("\"b\"", "String")]),
            ("Dockerfile", "FROM alpine:3 AS base\n# c\nRUN apk add --no-cache curl\nENV A=1\n", [("FROM", "Keyword"), ("AS", "Keyword"), ("# c", "Comment"), ("RUN", "Keyword"), ("--no-cache", "Type")]),
            ("Makefile", "CC = gcc\nall: main.o\n\t$(CC) -o x # c\n", [("CC", "Key"), ("all", "Key"), ("$(CC)", "Type"), ("# c", "Comment")]),
            ("Log", "2026-11-24 10:00:01 ERROR java.lang.NullPointerException at 10.0.0.1\n  at a.b.C(D.java:1)\n", [("2026-11-24 10:00:01", "Comment"), ("ERROR", "Bad"), ("java.lang.NullPointerException", "Bad"), ("10.0.0.1", "Number")]),
            ("SQL", "SELECT count(*) FROM `t` WHERE a = 'x' -- c\n", [("SELECT", "Keyword"), ("count", "Key"), ("`t`", "Key"), ("'x'", "String"), ("-- c", "Comment")]),
            ("Go", "func main() { x := \"a\" // c\n}\n", [("//", "Comment"), ("func", "Keyword"), ("main", "Key"), ("\"a\"", "String"), ("// c", "Comment")]),
            ("TOML", "# c\n[a.b]\nk = \"v\"\nn = 12\n", [("# c", "Comment"), ("[a.b]", "Keyword"), ("k", "Key"), ("\"v\"", "String"), ("12", "Number")]),
            ("Hosts", "10.0.0.1 db.internal # c\n", [("10.0.0.1", "Number"), ("db.internal", "Key"), ("# c", "Comment")]),
            ("Crontab", "# c\n*/5 * * * * root /usr/bin/x\n", [("# c", "Comment"), ("*/5 * * * *", "Number"), ("/usr/bin/x", "String")]),
            ("Json", "{\n  \"a\": 12,\n  \"b\": \"x\"\n}\n", [("\"a\"", "Key"), ("12", "Number"), ("\"x\"", "String")]),
            ("Python", "# c\ndef f():\n    return \"x\"\n", [("# c", "Comment"), ("\"x\"", "String")]),
            ("XML", "<!-- c -->\n<a b=\"c\"/>\n", [("<!-- c -->", "Comment"), ("\"c\"", "String")]),
            ("Java", "// c\nclass A { String s = \"x\"; }\n", [("// c", "Comment"), ("\"x\"", "String")]),
        })
        {
            var definition = EditorSyntax.Definition(EditorSyntax.Find(id), p, "test-dark");
            Assert.True(definition != null, id);
            foreach (var (part, role) in parts) Assert.True(RoleAt(definition!, p, text, part).Split('/').Contains(role), $"{id}: “{part}” is “{RoleAt(definition!, p, text, part)}”, not {role}");
        }
    }

    [Fact]
    public void EveryLanguageLoadsInEverySchemeAndCanBeRead()
    {
        foreach (bool dark in new[] { true, false })
            foreach (var scheme in TerminalSchemes.All)
            {
                var palette = TerminalSchemes.PaletteFor(scheme.Name, dark);
                var background = Color.Parse(palette.Background!);
                foreach (string role in new[] { "Comment", "Keyword", "String", "Number", "Key", "Type", "Variable", "Heading", "Good", "Warning", "Bad", "" })
                {
                    double contrast = TerminalSchemes.Contrast(EditorSyntax.ColorOf(palette, role), background);
                    Assert.True(contrast >= EditorSyntax.LeastContrast - 0.05, $"{scheme.Name} {role}: {contrast}");
                }
                foreach (var language in EditorSyntax.All)
                {
                    if (language.Id == "Plain") continue;
                    var definition = EditorSyntax.Definition(language, palette, "every-" + scheme.Name + dark);
                    Assert.True(definition != null, language.Id);
                    foreach (var colour in definition!.NamedHighlightingColors)
                    {
                        Assert.Null(colour.Background);
                        if (colour.Foreground?.GetColor(null) is { } c) Assert.True(TerminalSchemes.Contrast(c, background) >= EditorSyntax.LeastContrast - 0.05, $"{language.Id} {colour.Name} in {scheme.Name}");
                    }
                }
            }
        Assert.Equal(EditorSyntax.All.Length, EditorSyntax.All.Select(l => l.Id).Distinct().Count());
        Assert.Null(EditorSyntax.Definition(EditorSyntax.Plain, TerminalSchemes.StudioDark(), "x"));
        Assert.True(EditorSyntax.TooLong("a\n" + new string('x', EditorSyntax.LongestLine + 1)));
        Assert.False(EditorSyntax.TooLong(string.Concat(Enumerable.Repeat(new string('x', 100) + "\n", 1000))));
        Assert.Equal("Keyword", EditorSyntax.RoleOf("SomethingNew"));
    }

    [AvaloniaFact]
    public async Task TheEditorShowsColoursAndFollowsTheScheme()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh(new Dictionary<string, string> { ["FIXTURE_POSIX"] = "1" });
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1240, Height = 720, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "回环主机") with { DisablePwdFollow = true, DisableListMonitor = true });
            foreach (var (name, text) in new[]
            {
                ("nginx.conf", "# 站点\nserver {\n    listen 443 ssl http2;\n    server_name example.com;\n    root /var/www/html;\n\n    location ~ \\.php$ {\n        fastcgi_pass 127.0.0.1:9000;\n        fastcgi_param SCRIPT_FILENAME $document_root$fastcgi_script_name;\n        add_header X-Frame-Options \"DENY\" always;\n    }\n}\n"),
                ("deploy.sh", "#!/bin/bash\n# 发布脚本\nset -euo pipefail\nAPP=${1:-web}\nif [ ! -d \"/opt/$APP\" ]; then\n    echo \"missing $APP\" >&2\n    exit 1\nfi\nfor host in 10.0.0.11 10.0.0.12; do\n    rsync -az --delete /opt/$APP/ \"root@$host:/opt/$APP/\" && ssh \"$host\" systemctl restart \"$APP\"\ndone\n"),
                ("app.log", "2026-11-24 10:00:01.120 INFO  [main] com.demo.App - started in 3.2 s\n2026-11-24 10:00:05.871 WARN  [http-1] com.demo.Pool - pool 90% used\n2026-11-24 10:00:09.004 ERROR [http-2] com.demo.Api - GET /orders?id=42 failed\njava.net.ConnectException: Connection refused: 10.0.0.21:3306\n    at com.demo.Db.open(Db.java:88)\n"),
            })
            {
                string local = Path.Combine(h.Data, name);
                File.WriteAllText(local, text);
                h.Interaction.OpenFiles = [local];
                w.SelectedNode = null;
                w.UploadFilesCommand.Execute(null);
                await Pump(() => w.FileRoots.Any(n => n.Name == name), TimeSpan.FromSeconds(15));
                var tab = await w.OpenEditorAsync(w.FileRoots.First(n => n.Name == name));
                Assert.NotNull(tab);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Shot(window, "editor-" + name.Replace('.', '-'));
            }
            var view = window.GetVisualDescendants().OfType<EditorView>().Single();
            var editor = view.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
            var model = (EditorTabViewModel)view.DataContext!;
            Assert.Equal("Log", model.Language!.Id);
            Assert.NotNull(view.Definition);
            Assert.Equal(Color.Parse("#00161C"), ((ISolidColorBrush)editor.Background!).Color);
            // Another language, chosen by hand; a ComboBox that lost its list chooses nothing.
            var log = view.Definition;
            model.Language = EditorSyntax.Find("YAML");
            Assert.NotSame(log, view.Definition);
            model.Language = null;
            Assert.Equal("YAML", model.Language!.Id);
            Assert.False(model.IsDirty); // colours change nothing in the file
            // Another scheme: background and syntax together.
            var yaml = view.Definition;
            Appearance.Apply(Appearance.Current with { TerminalScheme = "Dracula" });
            Assert.Equal(Color.Parse("#282A36"), ((ISolidColorBrush)editor.Background!).Color);
            Assert.NotSame(yaml, view.Definition);
            Appearance.Apply(new StudioPreferences { Theme = "明亮" });
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            model.Language = EditorSyntax.Find("Log");
            Shot(window, "editor-light");
            Assert.Equal(Color.Parse("#FBFBFB"), ((ISolidColorBrush)editor.Background!).Color);
            // Lines that would stall the window are not coloured.
            string huge = Path.Combine(h.Data, "min.json");
            File.WriteAllText(huge, "{\"a\":\"" + new string('x', 30_000) + "\"}\n");
            h.Interaction.OpenFiles = [huge];
            w.SelectedNode = null;
            w.UploadFilesCommand.Execute(null);
            await Pump(() => w.FileRoots.Any(n => n.Name == "min.json"), TimeSpan.FromSeconds(15));
            var long_ = await w.OpenEditorAsync(w.FileRoots.First(n => n.Name == "min.json"));
            Assert.Equal("Json", long_!.Language!.Id);
            Assert.Equal("Plain", long_.ColouredLanguage.Id);
            Assert.Contains("没有着色", long_.LanguageTip);
            long_.Language = EditorSyntax.Find("YAML"); // chosen by hand: still too long
            Assert.Equal("Plain", long_.ColouredLanguage.Id);
            long_.Document.Text = "{\"a\": 1}\n";
            long_.Language = EditorSyntax.Find("Json");
            Assert.Equal("Json", long_.ColouredLanguage.Id);
        }
        finally { window.Close(); Appearance.Apply(new StudioPreferences()); }
    }

    /// <summary>Lines a server could make up to keep the rules busy.</summary>
    static IEnumerable<string> HostileLines(int length)
    {
        string Fill(string unit, string end = "x") => string.Concat(Enumerable.Repeat(unit, length / unit.Length + 1))[..(length - end.Length)] + end;
        foreach (string unit in new[] { " ", "a", "a.", "a.a ", "- ", "-", "${", "$(", "{{", "[", "[[", "#{", "\"", "'", "\"a", "'a", ":", "a:", "::", "=", "a=", "a ", "1", "1.", "1e", "0x", "/", "/a", "a/", "~", "\\", "\\\"", "{ ", "{,", "; ", "%(", "@", "a@", "<", "<a ", "<!--", "&", "&a", "*", "**", "_", "`", "``", "(", "((", "|", "|>", "!", "!!", "\t", "é", "2026-11-24 ", "10.0.0.", "http://", "a b", "export ", "at ", "a " })
            yield return Fill(unit);
        yield return string.Concat(Enumerable.Repeat("- ", length / 4)) + new string(' ', length / 2 - 1) + "x";
        yield return new string(' ', length / 2) + string.Concat(Enumerable.Repeat("a:", length / 4));
        yield return "key" + new string(' ', length - 4) + "x";
        yield return new string('a', length / 2) + new string(' ', length / 2 - 1) + "x";
        yield return "[" + new string('a', length - 1);
        yield return "{" + new string(' ', length - 1);
    }

    [AvaloniaFact]
    public void ColouringAFileCannotHoldUpTheWindow()
    {
        var palette = TerminalSchemes.StudioDark();
        var slowest = new List<(double Ms, string What)>();
        foreach (var language in EditorSyntax.All.Where(l => l.Id != "Plain"))
        {
            var definition = EditorSyntax.Definition(language, palette, "test-dark")!;
            foreach (string line in HostileLines(EditorSyntax.LongestLine))
            {
                var document = new AvaloniaEdit.Document.TextDocument(line);
                using var highlighter = new GuardedHighlighter(document, definition, _ => { });
                var clock = System.Diagnostics.Stopwatch.StartNew();
                highlighter.BeginHighlighting();
                highlighter.HighlightLine(1);
                highlighter.EndHighlighting();
                slowest.Add((clock.Elapsed.TotalMilliseconds, $"{language.Id} {line[..Math.Min(8, line.Length)]}"));
            }
        }
        // Rules of our own take milliseconds; the ones that come with the editor are cut off in time.
        Assert.All(slowest, x => Assert.True(x.Ms < (EditorSyntax.Find(x.What.Split(' ')[0]).Builtin ? 3000 : 600), $"{x.What}: {x.Ms:0} ms"));
    }

    [AvaloniaFact]
    public void ColouringStopsForOneEditorOnlyAndSaysSo()
    {
        var palette = TerminalSchemes.StudioDark();
        var document = new AvaloniaEdit.Document.TextDocument("a: 1\n" + new string('[', EditorSyntax.LongestLine) + "x\nb: 2\n");
        var reasons = new List<string>();
        using (var highlighter = new GuardedHighlighter(document, EditorSyntax.Definition(EditorSyntax.Find("YAML"), palette, "test-dark")!, reasons.Add))
        {
            highlighter.BeginHighlighting();
            Assert.NotEmpty(highlighter.HighlightLine(1).Sections);
            Assert.Empty(highlighter.HighlightLine(2).Sections); // too long
            Assert.Empty(highlighter.HighlightLine(3).Sections);
            highlighter.EndHighlighting();
            Assert.True(highlighter.IsOff);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal([EditorSyntax.LongLineText], reasons); // once
        }
        // A pattern that does not come to an end is cut off; the editor shows the text.
        document = new AvaloniaEdit.Document.TextDocument(new string('[', EditorSyntax.LongestLine - 1) + "x\nb\n");
        reasons.Clear();
        using (var highlighter = new GuardedHighlighter(document, EditorSyntax.Definition(EditorSyntax.Find("MarkDown"), palette, "test-dark")!, reasons.Add))
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 4; i++) { highlighter.BeginHighlighting(); highlighter.HighlightLine(1); highlighter.HighlightLine(2); highlighter.EndHighlighting(); }
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(8), clock.Elapsed.ToString());
        }
        // Lines before a change are not looked at again; those after it are.
        document = new AvaloniaEdit.Document.TextDocument("/* a\nb\nc */ int d;\n");
        using (var highlighter = new GuardedHighlighter(document, EditorSyntax.Definition(EditorSyntax.Find("Go"), palette, "test-dark")!, reasons.Add))
        {
            highlighter.BeginHighlighting();
            Assert.NotEmpty(highlighter.HighlightLine(2).Sections); // inside the comment
            highlighter.EndHighlighting();
            document.Remove(0, 2);
            highlighter.BeginHighlighting();
            Assert.Empty(highlighter.HighlightLine(2).Sections);
            highlighter.EndHighlighting();
            Assert.False(highlighter.IsOff);
        }
        // A line that became too long after it was looked at is noticed as well.
        document = new AvaloniaEdit.Document.TextDocument("a\nb\nc\n");
        using (var highlighter = new GuardedHighlighter(document, EditorSyntax.Definition(EditorSyntax.Find("Go"), palette, "test-dark")!, reasons.Add))
        {
            highlighter.BeginHighlighting();
            highlighter.HighlightLine(3);
            highlighter.EndHighlighting();
            document.Insert(0, new string('x', EditorSyntax.LongestLine + 1));
            highlighter.BeginHighlighting();
            highlighter.HighlightLine(3);
            highlighter.EndHighlighting();
            Assert.True(highlighter.IsOff);
        }
        // One round that takes too long, or several slow ones after each other.
        document = new AvaloniaEdit.Document.TextDocument(string.Concat(Enumerable.Repeat("func a() { x := \"a\" } // c\n", 400)));
        using (var highlighter = new GuardedHighlighter(document, EditorSyntax.Definition(EditorSyntax.Find("Go"), palette, "test-dark")!, reasons.Add) { LongestRound = TimeSpan.Zero })
        {
            highlighter.BeginHighlighting();
            highlighter.HighlightLine(300);
            Assert.True(highlighter.IsOff);
        }
        reasons.Clear();
        using (var highlighter = new GuardedHighlighter(document, EditorSyntax.Definition(EditorSyntax.Find("Go"), palette, "test-dark")!, reasons.Add) { SlowRound = TimeSpan.Zero })
        {
            void Round(int line) { highlighter.BeginHighlighting(); if (line > 0) highlighter.HighlightLine(line); highlighter.EndHighlighting(); }
            Round(10);
            Round(20);
            Round(0); // a quick one in between
            Round(30);
            Round(40);
            Assert.False(highlighter.IsOff);
            Round(50);
            Assert.True(highlighter.IsOff);
            for (int round = 0; round < 5; round++) Round(60);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["着色太慢"], reasons);
        }
        // Closed editors are not told any more.
        reasons.Clear();
        document = new AvaloniaEdit.Document.TextDocument(new string('x', EditorSyntax.LongestLine + 1));
        var closed = new GuardedHighlighter(document, EditorSyntax.Definition(EditorSyntax.Find("Go"), palette, "test-dark")!, reasons.Add);
        closed.BeginHighlighting();
        closed.HighlightLine(1);
        closed.Dispose();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(reasons);
    }

    [AvaloniaFact]
    public void AnOrdinaryLargeFileIsColouredToItsEnd()
    {
        var palette = TerminalSchemes.StudioDark();
        foreach (var (id, unit) in new[] { ("Shell", "if [ -f \"/opt/$APP\" ]; then echo \"missing $APP\" >&2; exit 1; fi # note\n"), ("YAML", "  - name: web-01 # c\n    port: 8080\n    tags: [a, b, \"c\"]\n"), ("Log", "2026-11-24 10:00:01.120 INFO  [main] com.demo.App - started in 3.2 s id=42 10.0.0.1:80 \"x\"\n"), ("Json", "  {\"name\": \"web\", \"n\": 12, \"b\": [true, null, 1.5]},\n"), ("XML", "<a b=\"c\"><!-- c --><d>text</d></a>\n"), ("Java", "public class A { String s = \"x\"; int f(int a) { return a + 1; } } // c\n"), ("Nginx", "    location ~ \\.php$ { fastcgi_pass 127.0.0.1:9000; } # c\n"), ("SQL", "INSERT INTO `t` (a, b) VALUES (1, 'x'); -- c\n"), ("INI", "port = 3306 ; c\n"), ("MarkDown", "# a **b** [c](d) `e`\n") })
        {
            var document = new AvaloniaEdit.Document.TextDocument(string.Concat(Enumerable.Repeat(unit, 1024 * 1024 / unit.Length)));
            using var highlighter = new GuardedHighlighter(document, EditorSyntax.Definition(EditorSyntax.Find(id), palette, "test-dark")!, _ => { });
            highlighter.BeginHighlighting();
            Assert.NotEmpty(highlighter.HighlightLine(document.LineCount - 1).Sections);
            highlighter.EndHighlighting();
            Assert.False(highlighter.IsOff, id);
        }
    }

    [Fact]
    public void FileNamesFromAServerAreReadInNoTime()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (string unit in new[] { ".20261124.111", ".111", "~", ".bak", "-20261124", "._-", "." })
        {
            string name = string.Concat(Enumerable.Repeat(unit, 4000));
            EditorSyntax.Detect("/x/" + name, "");
            EditorSyntax.Detect("/x/" + name + "x", "");
        }
        Assert.True(clock.ElapsedMilliseconds < 1000, clock.ElapsedMilliseconds + " ms");
        Assert.Equal("Nginx", EditorSyntax.Detect("/x/nginx.conf.bak.2026-11-24~", "").Id);
        Assert.Equal("Plain", EditorSyntax.Detect("/x/~", "").Id);
    }
}
