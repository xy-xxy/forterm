using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Forterm.Studio.Views;
using Xunit;

namespace Forterm.Studio.Tests;

/// <summary>0.4.0-alpha.9: the controller page, the entries, the icon.</summary>
public sealed class Alpha9Tests
{
    static async Task Pump(Func<bool> until, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end && !until()) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }
        Dispatcher.UIThread.RunJobs();
    }
    static void Settle(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    static void Shot(Window window, string name) { Settle(window); window.CaptureRenderedFrame()!.Save(Path.Combine(Paths.Evidence, name + ".png")); }

    [Fact]
    public void TheEntriesAreThoseNextToTheProgram()
    {
        string folder = Paths.TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(folder, "xshell.exe"), "");
            var windows = ControllerAccess.Entries(folder, windows: true);
            Assert.Equal([Path.Combine(folder, "xshell.exe"), Path.Combine(folder, "xftp.exe")], windows.Select(e => e.Path));
            Assert.Equal([true, false], windows.Select(e => e.IsThere));
            Assert.Equal("已就绪", windows[0].State);
            Assert.Contains("找不到", windows[1].State);
            Assert.Equal([Path.Combine(folder, "xshell"), Path.Combine(folder, "xftp")], ControllerAccess.Entries(folder, windows: false).Select(e => e.Path));
            Assert.Equal(AppContext.BaseDirectory, new ControllerAccess().Folder);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void OnlyTheLatestWakeUpsAreKept()
    {
        var access = new ControllerAccess();
        for (int i = 0; i < 12; i++) access.Began("xshell", new DateTimeOffset(2026, 11, 24, 8, 0, i, TimeSpan.Zero));
        Assert.Equal(ControllerAccess.MostShown, access.WakeUps.Count);
        Assert.Equal(("08:00:11", "正在打开…"), (access.WakeUps[0].Time, access.WakeUps[0].Result));
        var seen = new List<string?>();
        access.WakeUps[0].PropertyChanged += (_, e) => seen.Add(e.PropertyName);
        access.WakeUps[0].End("已连接", bad: false);
        Assert.Contains(nameof(WakeUp.Result), seen);
    }

    [AvaloniaFact]
    public async Task TheSettingsSayWhereTheEntriesAreAndWhatCameThroughThem()
    {
        Appearance.Apply(new StudioPreferences());
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        string folder = Paths.TempDirectory();
        foreach (var e in ControllerAccess.Entries(folder, OperatingSystem.IsWindows())) File.WriteAllText(e.Path, "");
        var access = new ControllerAccess { Folder = folder };
        var vm = new SettingsViewModel(null, interaction: h.Interaction, access: access) { Section = "access" };
        var window = new SettingsWindow { DataContext = vm, Width = 900, Height = 620 };
        window.Show();
        try
        {
            Settle(window);
            Assert.True(vm.IsAccess);
            Assert.False(vm.EntriesMissing || vm.HasWakeUps || vm.HasEntryStatus);
            var boxes = window.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsEffectivelyVisible && t.IsReadOnly).Select(t => t.Text).ToList();
            Assert.Equal(vm.EntryPrograms.Select(e => e.Path), boxes);
            vm.CopyEntryCommand.Execute(vm.EntryPrograms[1]);
            await Pump(() => vm.HasEntryStatus, TimeSpan.FromSeconds(2));
            Assert.Equal(vm.EntryPrograms[1].Path, h.Interaction.Clipboard);
            Assert.Contains("已复制", vm.EntryStatus);
            vm.OpenEntryFolderCommand.Execute(null);
            await Pump(() => h.Interaction.OpenedFolders.Count > 0, TimeSpan.FromSeconds(2));
            Assert.Equal([folder], h.Interaction.OpenedFolders);
            var wake = access.Began("xshell", DateTimeOffset.Now);
            Assert.True(vm.HasWakeUps);
            wake.End("已连接", bad: false);
            access.Began("xftp", DateTimeOffset.Now).End("未建立连接：包含不支持的启动参数", bad: true);
            Shot(window, "settings-access");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "已连接");
            // Found by what people call it.
            foreach (string word in new[] { "xshell", "堡垒机", "没反应", "控制器 路径" })
            {
                vm.SearchText = word;
                Assert.True(vm.Shown["entries"] || vm.Shown["wakes"], word);
                Assert.True(vm.IsAccess || vm.ShowAccessSection, word);
            }
            vm.SearchText = "";
        }
        finally { window.Close(); vm.Dispose(); Directory.Delete(folder, true); }
        // A closed window listens no more.
        int raised = 0;
        vm.PropertyChanged += (_, _) => raised++;
        access.Began("xshell", DateTimeOffset.Now);
        Assert.Equal(0, raised);
        // Missing entries are said.
        using var bare = new SettingsViewModel(null, access: new ControllerAccess { Folder = folder });
        Assert.True(bare.EntriesMissing);
        bare.CopyEntryCommand.Execute(bare.EntryPrograms[0]); // without a way to copy: nothing, and no error
    }

    [AvaloniaFact]
    public async Task LaunchesAreRememberedWithoutWhatTheyCarry()
    {
        using var fixture = LoopbackFixture.Ssh();
        using var h = new StudioHarness();
        async Task<WakeUp> Launch(params string[] args)
        {
            await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(args));
            return h.Model.Access.WakeUps[0];
        }
        var good = await Launch("--entry=xshell", "-newtab", "唤醒", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}");
        Assert.Equal(("xshell", "已连接", false), (good.Entry, good.Result, good.IsFailed));
        var files = await Launch("--entry=xftp", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}");
        Assert.Equal(("xftp", "已连接", false), (files.Entry, files.Result, files.IsFailed));
        var wrong = await Launch("--entry=xshell", "-bogus", "-url", $"ssh://synthetic:synthetic-only@127.0.0.1:{fixture.Port}");
        Assert.True(wrong.IsFailed);
        Assert.Contains("未建立连接", wrong.Result);
        var refused = await Launch("--entry=xshell", "-url", $"ssh://synthetic:not-the-password@127.0.0.1:{fixture.Port}");
        Assert.True(refused.IsFailed);
        Assert.Contains("没有连上", refused.Result);
        Assert.Equal(4, h.Model.Access.WakeUps.Count);
        Assert.All(h.Model.Access.WakeUps, w => Assert.DoesNotContain("synthetic", w.Entry + w.Result + w.Time));
        // Opening the program itself, or asking for a report, is no wake-up.
        await h.Model.AcceptLaunchAsync(LaunchPlan.Parse([]));
        Assert.Equal(4, h.Model.Access.WakeUps.Count);
        await h.Model.AcceptLaunchAsync(LaunchPlan.Parse(["--entry=xshell", "--diagnose", "-url", "ssh://a:b@127.0.0.1:22"]));
        Assert.Equal(4, h.Model.Access.WakeUps.Count);
        // An entry without a connection is one, and says so.
        var nowhere = await Launch("--entry=xshell");
        Assert.True(nowhere.IsFailed);
        Assert.Contains("没有带连接地址", nowhere.Result);
        // The windows of the program share one list.
        using (var other = new StudioHarness())
        {
            var shown = MainWindow.Create(other.KnownHosts, other.Store, other.Credentials);
            Assert.Same(ControllerAccess.Shared, ((MainWindowViewModel)shown.DataContext!).Access);
            ((MainWindowViewModel)shown.DataContext!).Dispose();
        }
        // The settings window of the program shows these.
        h.Model.OpenSettings("access");
        Assert.Same(h.Model.Access.WakeUps, h.Interaction.SettingsShown!.WakeUps);
        h.Interaction.SettingsShown.Dispose();
    }

    [AvaloniaFact]
    public void TheWindowHasTheIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Forterm/Assets/forterm.ico"));
        var head = new byte[6];
        stream.ReadExactly(head);
        Assert.Equal([0, 0, 1, 0], head[..4]);
        Assert.True(head[4] >= 6); // sizes from 16 to 256
        using var h = new StudioHarness();
        var window = new MainWindow { DataContext = h.Model };
        Assert.NotNull(window.Icon);
        window.Close();
    }

    static string PipeName(string name) => "ft-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name)))[..16];

    /// <summary>A controller that opens several sessions starts as many entries at the same moment. Each is taken, and
    /// each is told so; before, one of two was turned away on macOS.</summary>
    [Fact]
    public async Task EntriesThatStartAtTheSameMomentAreAllTaken()
    {
        string name = "forterm-test-" + Guid.NewGuid().ToString("N");
        var got = new List<string>();
        int inside = 0, most = 0, slow = 2;
        // The one that owns the mutex has to be the thread that started it, and release it.
        using var ready = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        var owner = new Thread(() =>
        {
            var (primary, _) = InstanceBroker.StartAsync(name, [], a =>
            {
                most = Math.Max(most, Interlocked.Increment(ref inside));
                Thread.Sleep(slow);
                lock (got) got.Add(a[0]);
                Interlocked.Decrement(ref inside);
                return true;
            }, CancellationToken.None, queue: true).GetAwaiter().GetResult();
            ready.Set();
            done.Wait();
            primary!.Dispose();
        });
        owner.Start();
        ready.Wait();
        try
        {
            for (int round = 0; round < 3; round++)
            {
                lock (got) got.Clear();
                var all = Enumerable.Range(0, 30).Select(i => Task.Run(() => InstanceBroker.StartAsync(name, ["launch " + i], _ => true, CancellationToken.None))).ToArray();
                var results = await Task.WhenAll(all).WaitAsync(TimeSpan.FromSeconds(20));
                Assert.All(results, r => Assert.True(r.Forwarded && r.Primary == null));
                lock (got) Assert.Equal(Enumerable.Range(0, 30).Select(i => "launch " + i).Order(), got.Order());
            }
            Assert.Equal(1, most); // one at a time for whoever takes them
            // Also when several are complete at the same moment.
            async Task<System.IO.Pipes.NamedPipeClientStream> Half(byte[] message)
            {
                var c = new System.IO.Pipes.NamedPipeClientStream(".", PipeName(name), System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
                await c.ConnectAsync(3000);
                await c.WriteAsync(BitConverter.GetBytes(message.Length));
                await c.WriteAsync(message.AsMemory(0, 4));
                return c;
            }
            byte[] text = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new[] { "half" });
            slow = 40;
            var halves = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Half(text)));
            await Task.Delay(150);
            await Task.WhenAll(halves.Select(c => c.WriteAsync(text.AsMemory(4)).AsTask()));
            var answers = await Task.WhenAll(halves.Select(async c => { var b = new byte[1]; await c.ReadExactlyAsync(b); return b[0]; })).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.All(answers, x => Assert.Equal(1, x));
            Assert.Equal(1, most);
            foreach (var c in halves) c.Dispose();
            slow = 2;
            // One that has given up (and told the controller so) is not opened after all.
            lock (got) got.Clear();
            slow = 400;
            var busy = Task.Run(() => InstanceBroker.StartAsync(name, ["busy"], _ => true, CancellationToken.None));
            await Task.Delay(120);
            byte[] late = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new[] { "given up" });
            using (var c = await Half(late)) await c.WriteAsync(late.AsMemory(4));
            Assert.True((await busy.WaitAsync(TimeSpan.FromSeconds(5))).Forwarded);
            slow = 2;
            Assert.True((await Task.Run(() => InstanceBroker.StartAsync(name, ["next"], _ => true, CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(5))).Forwarded);
            lock (got) Assert.Equal(["busy", "next"], got);
            // Some that connect and say nothing hold up none of the others.
            string pipe = PipeName(name);
            var silent = new List<System.IO.Pipes.NamedPipeClientStream>();
            for (int i = 0; i < 5; i++)
            {
                var c = new System.IO.Pipes.NamedPipeClientStream(".", pipe, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
                await c.ConnectAsync(3000);
                silent.Add(c);
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Assert.True((await Task.Run(() => InstanceBroker.StartAsync(name, ["after"], _ => true, CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(8))).Forwarded);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), clock.Elapsed.ToString());
            // Rubbish ends nothing.
            foreach (var (c, rubbish) in new (System.IO.Pipes.NamedPipeClientStream, byte[])[] { (silent[0], [255, 255, 255, 127, 1, 2, 3]), (silent[1], [3, 0, 0, 0, (byte)'{', (byte)'x', (byte)'}']) })
                try { await c.WriteAsync(rubbish).AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (IOException) { } // turned away while it was still being written (Windows)
            foreach (var c in silent) c.Dispose();
            Assert.True((await Task.Run(() => InstanceBroker.StartAsync(name, ["last"], _ => true, CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(8))).Forwarded);
            lock (got) Assert.Equal("last", got[^1]);
        }
        finally { done.Set(); owner.Join(TimeSpan.FromSeconds(10)); }
        // Closed: the name is free for the next.
        var again = new Thread(() => { var (primary, forwarded) = InstanceBroker.StartAsync(name, [], _ => true, CancellationToken.None, queue: true).GetAwaiter().GetResult(); Assert.False(forwarded); primary!.Dispose(); });
        again.Start();
        Assert.True(again.Join(TimeSpan.FromSeconds(10)));
    }

    /// <summary>Seen on Windows: a window larger than the screen, and its buttons over the settings.</summary>
    [AvaloniaFact]
    public void TheWindowFitsTheScreenAndLeavesRoomForItsButtons()
    {
        Appearance.Apply(new StudioPreferences());
        using var h = new StudioHarness();
        try
        {
            foreach (var (pixels, scaling, width, height) in new[] { (new PixelRect(0, 0, 1920, 1040), 1.0, 1440d, 860d), (new PixelRect(0, 0, 1920, 1032), 1.5, 1264d, 672d), (new PixelRect(0, 0, 1024, 720), 1.0, 1008d, 704d), (new PixelRect(0, 0, 1366, 720), 1.5, 910 - 15.333, 464d) })
            {
                var window = new MainWindow { DataContext = h.Model, Width = 1440, Height = 860 };
                window.MinWidth = 960;
                window.MinHeight = 600;
                window.FitTo(pixels, scaling);
                Assert.Equal(width, window.Width, 1.0);
                Assert.Equal(height, window.Height, 1.0);
                Assert.True(window.MinWidth <= window.Width && window.MinHeight <= window.Height);
                Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
                window.FitTo(new PixelRect(0, 0, 0, 0), 1); // no screen: as it is
                Assert.Equal(width, window.Width, 1.0);
                window.Close();
            }
            // The room for the system's buttons is in real size, whatever the scale of the rest.
            var shown = new MainWindow { DataContext = h.Model, Width = 1200, Height = 700 };
            shown.Show();
            foreach (double scale in new[] { 1.0, 1.5, 0.5 })
            {
                Appearance.Apply(new StudioPreferences { UiScale = scale });
                Settle(shown);
                string inset = OperatingSystem.IsMacOS() ? "LeftInset" : "RightInset";
                double real = OperatingSystem.IsMacOS() ? 76 : OperatingSystem.IsWindows() ? MainWindow.CaptionButtons : 8;
                Assert.Equal(real, shown.FindControl<Border>(inset)!.Width * scale, 0.5);
            }
            shown.Close();
            // Neither the title nor "full screen" are drawn over the window's own title bar.
            var theme = Assert.IsType<Avalonia.Styling.ControlTheme>(shown.WindowDecorationsTheme);
            Assert.NotNull(theme.BasedOn);
            var selectors = theme.Children.OfType<Avalonia.Styling.Style>().Select(x => x.Selector!.ToString()).ToList();
            Assert.Contains(selectors, x => x.Contains("PART_TitleTextPanel", StringComparison.Ordinal));
            Assert.Contains(selectors, x => x.Contains("PART_FullScreenButton", StringComparison.Ordinal));
            Assert.True(MainWindow.CaptionButtons >= 3 * 45);
        }
        finally { Appearance.Apply(new StudioPreferences()); }
    }

    [Fact]
    public void OnWindowsChineseComesFromAFontForSimplifiedChinese()
    {
        Assert.Null(Program.Fonts(windows: false).FontFallbacks);
        var fallbacks = Program.Fonts(windows: true).FontFallbacks!;
        Assert.StartsWith("Microsoft YaHei", fallbacks[0].FontFamily.Name);
        foreach (int c in new[] { '终', '、', '。', '，', '（', '；' }) Assert.True(fallbacks[0].UnicodeRange.IsInRange(c), ((char)c).ToString());
        Assert.False(fallbacks[0].UnicodeRange.IsInRange('a'));
    }

    /// <summary>An installed entry without its program says so by its exit code; the message box is for Windows.</summary>
    [Fact]
    public void AnEntryNeverPutsItsArgumentsIntoAMessage()
    {
        string source = File.ReadAllText(Path.Combine(Paths.Root, "src", "Forterm.Launcher", "Program.cs"));
        foreach (string line in source.Split('\n').Where(l => l.Contains("return Fail(", StringComparison.Ordinal)))
            Assert.DoesNotContain("args", line, StringComparison.Ordinal);
    }
}
