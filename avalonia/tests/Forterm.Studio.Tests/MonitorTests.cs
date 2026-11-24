using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Forterm.Core;
using Forterm.Studio.Library;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Xunit;

namespace Forterm.Studio.Tests;

public sealed class MonitorTests
{
    static string Sample(int n) =>
        "FTS1\nOS\nNAME=\"Kylin Linux Advanced Server\"\nVERSION=\"V10 (Lance)\"\nPRETTY_NAME=\"Kylin V10\"\n" +
        "TZ\nCST +0800\nUPTIME\n" + (100 + n * 3) + ".00 5.0\nLOAD\n0.50 0.40 0.30 2/300 999\n" +
        "CPU\ncpu " + (1000 + n * 30) + " 0 0 " + (9000 + n * 70) + " 0 0 0 0 0 0\ncpu0 " + (500 + n * 30) + " 0 0 " + (4500 + n * 0) + " 0 0 0 0\ncpu1 500 0 0 " + (4500 + n * 70) + " 0 0 0 0\n" +
        "MEM\nMemTotal: 1000000 kB\nMemFree: 200000 kB\nMemAvailable: 600000 kB\nBuffers: 100000 kB\nCached: 250000 kB\nSReclaimable: 50000 kB\n" +
        "DF\nFilesystem Type 1024-blocks Used Available Capacity Mounted on\n/dev/mapper/vg-root xfs 1000 250 750 25% /\ntmpfs tmpfs 100 1 99 1% /run\n/dev/sdb1 ext4 2000 100 1900 5% /data dir\n" +
        "DISKSTATS\n 253 0 dm-0 1 0 " + (100 + n * 20) + " 0 1 0 " + (200 + n * 40) + " 0 0 0 0\n 8 17 sdb1 1 0 10 0 1 0 10 0 0 0 0\n" +
        "DM\ndm-0 vg-root\n" +
        "NET\nInter-| x\n face | y\n lo: 5 0 0 0 0 0 0 0 5 0 0 0 0 0 0 0\n eth0: " + (1000 + n * 3000) + " 0 0 0 0 0 0 0 " + (500 + n * 1500) + " 0 0 0 0 0 0 0\n" +
        "PS\n  321 app      Sl   45.5 204800 Sat Nov 22 15:39:59 2026 /opt/app/bin/server  --port 8080   --name \"a b\"\n    1 root     Ss    0.0  1000 Mon Nov 10 09:21:44 2026 /sbin/init\n";

    [Fact]
    public void ParsesEverySectionAndComputesRatesFromTwoSamples()
    {
        var first = MonitorProbe.Parse(Sample(0));
        var second = MonitorProbe.Parse(Sample(1));
        Assert.Equal("Kylin Linux Advanced Server", first.Os!.Name);
        Assert.Equal("V10 (Lance)", first.Os.Version);
        Assert.Equal("CST GMT+0800", first.TimeZone);
        Assert.Equal(2, first.Mounts.Count); // tmpfs filtered
        Assert.Equal("/data dir", first.Mounts[1].Mount);
        var server = first.Processes![0];
        Assert.Equal(321, server.Pid);
        Assert.Equal("11-22 15:39:59", server.Started);
        Assert.Equal("/opt/app/bin/server  --port 8080   --name \"a b\"", server.Command);
        Assert.Equal(200L * 1024 * 1024, (long)server.RssBytes);

        var none = MonitorProbe.Compare(first, null);
        Assert.Null(none.CpuTotal);
        Assert.Null(none.ReceivePerSecond);
        Assert.All(none.Mounts, m => Assert.Null(m.ReadPerSecond));

        var r = MonitorProbe.Compare(second, first);
        Assert.Equal(30, r.CpuTotal!.Value, 3);             // 30 busy of 100 ticks
        Assert.Equal(100, r.Cores[0].Percent!.Value, 3);     // core 0 only busy
        Assert.Equal(0, r.Cores[1].Percent!.Value, 3);
        Assert.Equal(1000, r.ReceivePerSecond!.Value, 3);    // 3000 B over 3 s, lo excluded
        Assert.Equal(500, r.SendPerSecond!.Value, 3);
        var root = r.Mounts[0];
        Assert.Equal(20 * 512 / 3d, root.ReadPerSecond!.Value, 3);   // /dev/mapper/vg-root → dm-0
        Assert.Equal(40 * 512 / 3d, root.WritePerSecond!.Value, 3);
        Assert.Equal(25, root.UsedPercent, 3);
        var m = r.Memory!;
        Assert.Equal(1000000L * 1024, (long)m.Total);
        Assert.Equal(400000L * 1024, (long)m.Cache);       // buffers + cached + sreclaimable
        Assert.Equal(400000L * 1024, (long)m.Used);        // total - free - cache
    }

    [Fact]
    public void RebootOrNonLinuxNeverInventsNumbers()
    {
        var later = MonitorProbe.Parse(Sample(5));
        var rebooted = MonitorProbe.Parse(Sample(1));
        var r = MonitorProbe.Compare(rebooted, later);
        Assert.Null(r.CpuTotal);
        Assert.Null(r.ReceivePerSecond);
        Assert.Throws<IOException>(() => MonitorProbe.Parse("Darwin\n"));
        var noType = MonitorProbe.Parse("FTS1\nDF\nFilesystem 1024-blocks Used Available Capacity Mounted on\n/dev/sda1 100 10 90 10% /\n");
        Assert.Equal("", Assert.Single(noType.Mounts).Type);
    }

    static async Task Pump(Func<bool> until, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (!until() && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); await Task.Delay(40); }
        Dispatcher.UIThread.RunJobs();
    }

    static int MonitorExecs(LoopbackFixture f) => f.Stats().GetProperty("commands").EnumerateArray().Count(c => c.GetString()!.Contains("FTS1"));

    [AvaloniaFact]
    public async Task SavedConnectionMonitorsAutomaticallyLaunchWaitsForTheUser()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var p = h.Ssh(fixture.Port, "监控");
        var w = await h.Model.OpenProfileAsync(p);
        Assert.True(w.Monitor!.AutoRefresh);
        await Pump(() => w.Monitor.HasData && w.Monitor.CpuTrend.Any(v => v.Value != null), TimeSpan.FromSeconds(12));
        Assert.True(w.Monitor.HasData, w.Monitor.Message);
        Assert.Equal("Ubuntu", w.Monitor.OsName);
        Assert.Contains(w.Monitor.CpuTrend, v => v.Value != null);
        Assert.Null(w.Monitor.CpuTrend[0].Value); // the first reading has nothing to compare with
        Assert.Equal(2, w.Monitor.Cores.Count);
        Assert.Equal(2, w.Monitor.Mounts.Count);
        Assert.Equal("/usr/bin/java -jar app.jar --spring.profiles.active=prod", w.Monitor.TopProcesses[0].Row!.Command);
        Assert.NotEqual("—", w.Monitor.DownloadSpeed);
        // Hiding the column stops the loop.
        w.ShowMonitor = false;
        int before = MonitorExecs(fixture);
        await Pump(() => false, TimeSpan.FromSeconds(4));
        Assert.Equal(before, MonitorExecs(fixture));

        using var launchFixture = LoopbackFixture.Ssh();
        var launch = await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{launchFixture.Port}"]));
        Assert.False(launch!.Monitor!.AutoRefresh);
        await Pump(() => false, TimeSpan.FromSeconds(1));
        Assert.Equal(0, MonitorExecs(launchFixture));
        await launch.Monitor.RefreshAsync();
        Assert.True(launch.Monitor.HasData, launch.Monitor.Message);
        Assert.Equal(1, MonitorExecs(launchFixture));
    }

    [AvaloniaFact]
    public async Task ProcessManagerSignalsOnlyConfirmedValidPids()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var launch = await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}"]));
        var monitor = launch!.Monitor!;
        await monitor.RefreshAsync();
        monitor.ShowProcessesCommand!.Execute(null);
        var list = h.Interaction.Processes!;
        Assert.Equal(3, list.Rows.Count);
        list.Search = "java";
        var java = Assert.Single(list.Rows);
        Assert.Equal(4242, java.Pid);

        h.Interaction.ConfirmAnswer = false;
        await list.SignalAsync(java, force: false);
        Assert.False(fixture.Stats().TryGetProperty("kills", out _));

        h.Interaction.ConfirmAnswer = true;
        await list.SignalAsync(java, force: false);
        Assert.Contains("SIGTERM", list.Notice);
        list.Search = "bash";
        await list.SignalAsync(list.Rows.Single(), force: true);
        Assert.Contains("失败", list.Notice);
        Assert.Contains("Operation not permitted", list.Notice);
        var kills = fixture.Stats().GetProperty("kills").EnumerateArray().Select(k => k.GetString()).ToList();
        Assert.Equal(["kill -TERM 4242", "kill -KILL 4300"], kills);

        // A PID reused since the list was read (different start time) is never signalled.
        list.Search = "java";
        var stale = list.Rows.Single() with { Info = list.Rows.Single().Info with { StartRaw = "Sat Nov 22 15:41:00 2026" } };
        await list.SignalAsync(stale, force: true);
        Assert.Contains("复用", list.Notice);
        Assert.Equal(2, fixture.Stats().GetProperty("kills").GetArrayLength());
        Assert.Contains(fixture.Stats().GetProperty("commands").EnumerateArray(), c => c.GetString()!.StartsWith("LC_ALL=C; export LC_ALL; set -- $(ps -o lstart= -p 4242"));

        list.Search = "init";
        var init = Assert.Single(list.Rows);
        Assert.False(init.CanSignal);
        await list.SignalAsync(init, force: true);
        Assert.Contains("PID 1", list.Notice);
        Assert.Equal(2, fixture.Stats().GetProperty("kills").GetArrayLength());
        list.Dispose();
    }

    [AvaloniaFact]
    public async Task MonitorColumnRenders()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        var window = new MainWindow { Width = 1440, Height = 900, DataContext = h.Model };
        window.Show();
        try
        {
            var w = await h.Model.OpenProfileAsync(h.Ssh(fixture.Port, "监控截图"));
            await Pump(() => w.Monitor!.History.Count >= 3, TimeSpan.FromSeconds(12));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, "workspace-monitor.png"));
            var processes = new ProcessWindow { DataContext = new ProcessListViewModel(w.Monitor!, () => w.Session, h.Interaction) };
            processes.Show();
            processes.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            processes.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, "process-manager.png"));
            processes.Close();
        }
        finally { window.Close(); }
    }
}
