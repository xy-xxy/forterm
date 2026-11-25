using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>How fast transfers are.</summary>
public sealed class TransferSpeedTests
{
    sealed class Clock : TimeProvider
    {
        public TimeSpan Now { get; set; }
        public override long GetTimestamp() => Now.Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
    const long MB = 1 << 20;
    static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void TheSpeedIsThatOfTheLastSeconds()
    {
        var meter = new TransferMeter();
        meter.Add(S(0), 0);
        Assert.Null(meter.PerSecond);
        meter.Add(S(0.1), MB);
        Assert.Null(meter.PerSecond); // too little to tell
        for (int i = 1; i <= 10; i++) meter.Add(S(i), i * 2 * MB);
        Assert.Equal(2 * MB, meter.PerSecond!.Value, 1.0);
        // Four times as fast: soon shown, and not mixed with what was long ago.
        for (int i = 1; i <= 4; i++) meter.Add(S(10 + i), 20 * MB + i * 8 * MB);
        Assert.Equal(8 * MB, meter.PerSecond!.Value, 1.0);
        // It stands still.
        for (int i = 1; i <= 4; i++) meter.Add(S(14 + i), 52 * MB);
        Assert.Equal(0, meter.PerSecond!.Value);
        Assert.Equal(52d * MB / 18, meter.Average!.Value, 1.0);
    }

    [Fact]
    public void AMomentIsOneSampleAndPausesDoNotCount()
    {
        var meter = new TransferMeter();
        for (int i = 0; i <= 1000; i++) meter.Add(S(i * 0.01), i * 1000);
        Assert.Equal(100_000, meter.PerSecond!.Value, 2000.0);
        meter.Rest();
        Assert.Null(meter.PerSecond);
        // Checked for a minute, then the next file.
        meter.Add(S(70), 1_000_000);
        meter.Add(S(72), 1_200_000);
        Assert.Equal(100_000, meter.PerSecond!.Value, 1.0);
        Assert.Equal(S(12), meter.Moving);
        Assert.Equal(100_000, meter.Average!.Value, 1.0);
        // Back to the start (another file): no negative speed.
        meter.Add(S(73), 0);
        Assert.Null(meter.PerSecond);
        meter.Add(S(74), 50_000);
        Assert.Equal(50_000, meter.PerSecond!.Value, 1.0);
        meter.Add(S(73.5), 60_000); // a clock that goes back
        Assert.Equal(50_000, meter.PerSecond!.Value, 1.0);
    }

    [AvaloniaFact]
    public async Task TheListShowsSpeedWhatIsLeftAndTheAverage()
    {
        var clock = new Clock();
        using var queue = new TransferQueue();
        using var list = new TransferListViewModel(queue, clock);
        var step = new SemaphoreSlim(0);
        IProgress<TransferProgress>? report = null;
        var ready = new SemaphoreSlim(0);
        async Task Wait(SemaphoreSlim s) { while (!await s.WaitAsync(10)) Dispatcher.UIThread.RunJobs(); Dispatcher.UIThread.RunJobs(); }
        var ticket = queue.Add("大文件.bin", "合成 · /", async (progress, ct) => { report = progress; ready.Release(); await step.WaitAsync(ct); });
        list.Register(ticket.Id, TransferKind.Download);
        await Wait(ready);
        void At(double seconds, long bytes, string phase = "传输中")
        {
            clock.Now = S(seconds);
            report!.Report(new TransferProgress(bytes, 40 * MB, phase));
            list.Refresh();
        }
        At(0, 0);
        var item = Assert.Single(list.Visible);
        Assert.False(item.HasSpeed);
        Assert.Equal("", list.DownloadSpeed);
        At(1, 2 * MB);
        At(2, 4 * MB);
        Assert.Equal("2.0 MB/s", item.SpeedText);
        Assert.Equal("剩余 18 秒", item.RemainingText);
        Assert.Equal("2.0 MB/s", list.DownloadSpeed);
        Assert.True(list.IsDownloading);
        Assert.Equal("", list.UploadSpeed);
        Assert.False(list.IsUploading);
        // The server says nothing any more: the clock alone brings the speed down.
        for (int i = 3; i <= 7; i++) { clock.Now = S(i); list.Tick(); }
        Assert.Equal("0 B/s", item.SpeedText);
        Assert.Equal("", item.RemainingText);
        At(8, 6 * MB);
        for (int i = 9; i <= 12; i++) At(i, (i - 5) * 2 * MB);
        Assert.Equal("2.0 MB/s", item.SpeedText);
        // Being checked is not a speed.
        At(12.5, 40 * MB, "校验内容");
        Assert.Equal("", item.SpeedText);
        Assert.Equal("", list.DownloadSpeed);
        clock.Now = S(60);
        step.Release();
        await ticket.Completion;
        for (int i = 0; i < 50 && item.IsActive; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        list.Refresh();
        Assert.True(item.IsDone);
        // 40 MB in the 12.5 seconds in which bytes moved; the 47 seconds of checking are not among them.
        Assert.Equal("平均 3.2 MB/s", item.SpeedText);
        Assert.Equal("", item.RemainingText);
        Assert.Equal("", list.DownloadSpeed);
    }

    [AvaloniaFact]
    public async Task OperationsHaveNoSpeedAndRowsStayWhereTheyAre()
    {
        var clock = new Clock();
        using var queue = new TransferQueue();
        using var list = new TransferListViewModel(queue, clock);
        var hold = new SemaphoreSlim(0);
        var started = new SemaphoreSlim(0);
        IProgress<TransferProgress>? report = null;
        var first = queue.Add("删除", "合成", async (p, ct) => { report = p; started.Release(); await hold.WaitAsync(ct); });
        list.Register(first.Id, TransferKind.Operation);
        var second = queue.Add("上传.bin", "合成", async (p, ct) => { report = p; started.Release(); await hold.WaitAsync(ct); });
        list.Register(second.Id, TransferKind.Upload);
        while (!await started.WaitAsync(10)) Dispatcher.UIThread.RunJobs();
        list.Refresh();
        Assert.Equal(2, list.Visible.Count);
        clock.Now = S(1); report!.Report(new(1, 4, "已删除 1/4", "项")); list.Refresh();
        clock.Now = S(2); report!.Report(new(3, 4, "已删除 3/4", "项")); list.Refresh();
        Assert.All(list.Visible, i => Assert.False(i.HasSpeed));
        var rows = list.Visible.ToArray();
        int changes = 0;
        list.Visible.CollectionChanged += (_, _) => changes++;
        hold.Release();
        while (!await started.WaitAsync(10)) Dispatcher.UIThread.RunJobs();
        list.Refresh();
        // The one that has ended goes below the one that runs; nothing else is touched.
        Assert.Equal(1, changes);
        changes = 0;
        for (int i = 1; i <= 20; i++) { clock.Now = S(2 + i * 0.5); report!.Report(new(i * MB, 100 * MB, "传输中")); list.Refresh(); }
        Assert.Equal(0, changes);
        Assert.Equal([rows[1], rows[0]], list.Visible);
        Assert.Equal("2.0 MB/s", list.UploadSpeed);
        Assert.True(list.IsUploading);
        Assert.Equal("", list.DownloadSpeed);
        list.ShowDownloadsCommand.Execute(null);
        Assert.Empty(list.Visible);
        list.ShowUploadsCommand.Execute(null);
        Assert.Equal([rows[1]], list.Visible);
        // Cancelled: the average of what was sent.
        queue.Cancel(second.Id);
        for (int i = 0; i < 200 && rows[1].IsActive; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Assert.StartsWith("平均 ", rows[1].SpeedText);
        Assert.Equal("", list.UploadSpeed);
    }

    [AvaloniaFact]
    public async Task ARealTransferShowsItsSpeedInTheList()
    {
        using var server = LoopbackFixture.Ssh();
        server.Command("large");
        using var h = new StudioHarness();
        var window = new MainWindow { DataContext = h.Model, Width = 1200, Height = 720 };
        window.Show();
        string folder = Paths.TempDirectory();
        try
        {
            var w = (await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xftp", "-newtab", "合成", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{server.Port}"])))!;
            h.Interaction.SaveFile = Path.Combine(folder, "large.bin");
            var a = w.FileRoots.SelectMany(r => new[] { r }.Concat(r.Children)).First(n => n.Name == "a");
            a.IsExpanded = true;
            for (int i = 0; i < 200 && a.Children.All(c => c.Name != "large.bin"); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
            w.SelectedNode = a.Children.First(c => c.Name == "large.bin");
            for (int i = 0; i < 3; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "传输管理" && b.IsEffectivelyVisible);
            button.Flyout!.ShowAt(button);
            w.DownloadCommand.Execute(null);
            var seen = new HashSet<string>();
            string shown = "", state = "";
            for (int i = 0; i < 1500; i++)
            {
                Dispatcher.UIThread.RunJobs();
                if (w.Transfers!.DownloadSpeed is { Length: > 0 } speed) seen.Add(speed);
                if (w.Transfers.Visible.FirstOrDefault() is { IsActive: false } done) { shown = done.SpeedText; state = done.StateText + " " + done.AmountText; break; }
                await Task.Delay(10);
            }
            Assert.True(shown.StartsWith("平均 "), state + " | " + string.Join(",", seen));
            Assert.EndsWith("/s", shown);
            for (int i = 0; i < 3; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
            var flyout = (Flyout)button.Flyout;
            var texts = ((Control)flyout.Content!).GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains(shown, texts);
            window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, "transfer-speed.png"));
            button.Flyout.Hide();
        }
        finally { window.Close(); Directory.Delete(folder, true); }
    }
}
