using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
namespace Forterm.Desktop;

public sealed class MonitorPanel : DockPanel, IDisposable
{
    readonly Func<SharedSshSession?> root;
    readonly DispatcherTimer timer = new();
    readonly CheckBox automatic = new() { Content = "自动刷新", IsChecked = false };
    readonly NumericUpDown interval = new() { Minimum = 3, Maximum = 600, Value = 3, Width = 120 };
    readonly TextBlock status = UI.Label("手动刷新，或开启3秒自动刷新。指标来自 Linux /proc 与 df。", true);
    readonly ListBox cores = new() { Background = Brushes.Transparent, Height = 148, MinHeight = 0 };
    readonly CoreMetricRow cpu = new("总计");
    readonly MetricCard memory = new("内存"), disk = new("根分区");
    readonly TextBlock network = UI.Label("接收 —  /  发送 —"); readonly TextBlock load = UI.Label("负载 —", true);
    readonly NetworkTrend trend = new();
    readonly TextBlock uptime = UI.Label("运行时间 —", true);
    readonly TextBlock memoryBytes = UI.Label("已用 —\n可用 —\n总计 —", true);
    readonly TextBlock diskBytes = UI.Label("已用 —\n可用 —\n总计 —", true);
    readonly TextBlock networkTotals = UI.Label("接收累计 —\n发送累计 —", true);
    readonly StackPanel metricSections = new() { Spacing = 8, IsVisible = false };
    readonly List<Visual> visibilityOwners = new();
    KeyValuePair<string, double?>[] coreData = [];
    LinuxSample? previous; CancellationTokenSource? active; long generation; bool disposed;
    public MonitorPanel(Func<SharedSshSession?> root)
    {
        network.TextWrapping = TextWrapping.Wrap; this.root = root; Margin = new Thickness(0); MinWidth = 0; HorizontalAlignment = HorizontalAlignment.Stretch;
        automatic.FontSize = 12; automatic.MinHeight = automatic.Height = 26;
        interval.Width = 100; interval.Height = 26; interval.MinHeight = 0; interval.FontSize = 12;
        var refresh = UI.Button("刷新资源", () => _ = RefreshAsync()); refresh.MinWidth = 68; refresh.Height = 26; refresh.Padding = new Thickness(6, 0); refresh.FontSize = 12;
        var tools = new WrapPanel { Orientation = Orientation.Horizontal }; tools.Children.Add(refresh); tools.Children.Add(automatic); tools.Children.Add(interval); tools.Children.Add(UI.Label("秒", true));
        status.FontSize = 10; status.TextWrapping = TextWrapping.Wrap;
        load.TextWrapping = TextWrapping.Wrap; load.FontSize = 10; load.VerticalAlignment = VerticalAlignment.Center;
        cores.Classes.Add("core-grid"); AutomationProperties.SetName(cores, "逐核CPU仪表");
        cores.ItemTemplate = new FuncDataTemplate<KeyValuePair<string, double?>[]>((row, _) =>
        {
            var grid = new UniformGrid { Columns = 1 };
            foreach (var item in row ?? []) { var card = new CoreMetricRow(item.Key); card.Set(item.Value); grid.Children.Add(card); }
            return grid;
        });
        var source = UI.Label("Linux /proc · df · 同SSH exec", true); source.FontSize = 10; ToolTip.SetTip(source, "/proc/stat、meminfo、net/dev、uptime；df -kP /。固定只读独立exec，不向交互终端输入。");
        network.FontSize = 11;
        static Border Card(string title, string icon, params Control[] children)
        {
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            {
                WorkspaceChrome.Icon(icon),
                new TextBlock { Text = title, Classes = { "monitor-section-title" }, VerticalAlignment = VerticalAlignment.Center }
            } };
            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(heading);
            body.Children.Add(new Border { Classes = { "section-rule" } });
            foreach (var child in children) body.Children.Add(child);
            return new Border { Classes = { "monitor-card" }, Child = body };
        }
        static Grid Summary(Control gauge, Control details)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            row.Children.Add(gauge); Grid.SetColumn(details, 1); row.Children.Add(details); return row;
        }
        var controls = new StackPanel { Spacing = 6, Children = { tools, status } };
        metricSections.Children.Add(Card("系统", "终端", uptime, load, source));
        metricSections.Children.Add(Card("CPU", "资源监控", cpu, cores));
        metricSections.Children.Add(Card("内存", "文件", Summary(memory, memoryBytes)));
        metricSections.Children.Add(Card("网络", "资源监控", trend, network, networkTotals));
        metricSections.Children.Add(Card("磁盘 · /", "文件", Summary(disk, diskBytes)));
        var content = new StackPanel
        {
            Spacing = 8, Margin = new Thickness(6), Children =
            {
                new Border { Classes = { "monitor-controls" }, Child = controls },
                metricSections,
                UI.Label("绿：接收 · 蓝：发送 · 自动纵轴\n首样/重置显示 —，最多60个采样。", true)
            }
        };
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetName(scroll, "资源监控滚动区"); Children.Add(scroll);
        timer.Tick += (_, _) => _ = RefreshAsync();
        automatic.IsCheckedChanged += (_, _) => UpdateTimer(); interval.ValueChanged += (_, _) => UpdateTimer();
        AttachedToVisualTree += (_, _) => { foreach (var visual in this.GetVisualAncestors().Prepend(this)) { visual.PropertyChanged += VisibilityChanged; visibilityOwners.Add(visual); } UpdateTimer(); };
        DetachedFromVisualTree += (_, _) => { Pause(); timer.Stop(); UnsubscribeVisibility(); };
    }
    void VisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != IsVisibleProperty) return;
        if (e.NewValue is false) { Pause(); timer.Stop(); }
        else Dispatcher.UIThread.Post(UpdateTimer);
    }
    void UnsubscribeVisibility() { foreach (var visual in visibilityOwners) visual.PropertyChanged -= VisibilityChanged; visibilityOwners.Clear(); }
    static string Bytes(double? value) => value is { } bytes ? bytes >= 1073741824 ? $"{bytes / 1073741824:0.0} GiB" : $"{bytes / 1048576:0.0} MiB" : "—";
    static string Percent(double? n) => n is { } v ? $"{v:0.0}%" : "—";
    static string Rate(double? n) => n is { } v ? v >= 1048576 ? $"{v / 1048576:0.0} MiB/s" : $"{v / 1024:0.0} KiB/s" : "—";
    void UpdateTimer() { timer.Stop(); if (!disposed && IsEffectivelyVisible && automatic.IsChecked == true) { timer.Interval = TimeSpan.FromSeconds((double)(interval.Value ?? 3)); timer.Start(); } }
    void Pause() { generation++; active?.Cancel(); previous = null; trend.Gap(); }
    public async Task RefreshAsync()
    {
        if (disposed || !IsEffectivelyVisible || active != null) return;
        var session = root(); if (session == null || !session.IsConnected) { metricSections.IsVisible = false; status.Text = "当前会话没有可用的 SSH 连接"; automatic.IsChecked = false; return; }
        long epoch = generation; using var request = new CancellationTokenSource(); active = request;
        try
        {
            status.Text = "正在读取独立 exec 通道…";
            var sample = await session.ReadLinuxMetricsAsync(request.Token);
            if (disposed || epoch != generation || !IsEffectivelyVisible || session != root()) return;
            var reading = LinuxMetrics.Compare(sample, previous); previous = sample; metricSections.IsVisible = true;
            cpu.Set(reading.CpuPercent.GetValueOrDefault("cpu")); memory.Set(reading.MemoryPercent); disk.Set(reading.DiskPercent);
            coreData = reading.CpuPercent.Where(p => p.Key != "cpu").OrderBy(p => int.TryParse(p.Key.AsSpan(3), out int id) ? id : int.MaxValue).ToArray();
            cores.IsVisible = coreData.Length > 0; cores.Height = Math.Min(176, Math.Max(28, coreData.Length * 26)); cores.ItemsSource = coreData.Chunk(2).ToArray();
            network.Text = $"↓  {Rate(reading.ReceiveBytesPerSecond)}    ↑  {Rate(reading.SendBytesPerSecond)}";
            uptime.Text = sample.Uptime is { } seconds ? $"运行时间  {TimeSpan.FromSeconds(seconds).Days}天 {TimeSpan.FromSeconds(seconds).Hours}小时" : "运行时间 —";
            memoryBytes.Text = $"已用  {Bytes(sample.MemoryTotal is { } mt && sample.MemoryAvailable is { } ma ? mt - ma : null)}\n可用  {Bytes(sample.MemoryAvailable)}\n总计  {Bytes(sample.MemoryTotal)}";
            diskBytes.Text = $"已用  {Bytes(sample.DiskUsed)}\n可用  {Bytes(sample.DiskTotal is { } dt && sample.DiskUsed is { } du ? dt - du : null)}\n总计  {Bytes(sample.DiskTotal)}";
            networkTotals.Text = $"接收累计  {Bytes(sample.Network.Values.Sum(tick => (double)tick.Received))}\n发送累计  {Bytes(sample.Network.Values.Sum(tick => (double)tick.Sent))}";
            load.Text = sample.Load is { } values ? $"负载  {values[0]:0.00} / {values[1]:0.00} / {values[2]:0.00}" : "负载不可用";
            trend.Add(reading.ReceiveBytesPerSecond, reading.SendBytesPerSecond);
            status.Text = $"{DateTime.Now:HH:mm:ss} · 已更新";
        }
        catch (OperationCanceledException) { if (!disposed && epoch == generation) status.Text = "监控请求已取消"; }
        catch (InvalidOperationException e) { if (!disposed && epoch == generation) status.Text = e.Message; }
        catch (Exception e) { if (!disposed && epoch == generation) { previous = null; trend.Gap(); automatic.IsChecked = false; status.Text = "资源监控不可用：" + e.Message + "；终端和文件能力保留，可手动重试。"; } }
        finally { if (active == request) active = null; }
    }
    public void Dispose() { if (disposed) return; disposed = true; timer.Stop(); Pause(); UnsubscribeVisibility(); }
}

sealed class MetricCard : StackPanel
{
    readonly MetricRing ring; readonly TextBlock value = new() { Text = "—", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
    public MetricCard(string label, double size = 36)
    {
        Spacing = 2; Margin = new Thickness(2, 3); var title = UI.Label(label, true); title.FontSize = 11; title.HorizontalAlignment = HorizontalAlignment.Center; title.TextTrimming = TextTrimming.CharacterEllipsis;
        ring = new MetricRing(size); AutomationProperties.SetName(ring, label + " 仪表"); Children.Add(title); Children.Add(ring); Children.Add(value);
    }
    public void Set(double? percent) { ring.Percent = percent; value.Text = percent is { } p ? $"{p:0.0}%" : "—"; AutomationProperties.SetHelpText(ring, value.Text); ring.InvalidateVisual(); }
}
sealed class MetricRing : Control
{
    public double? Percent { get; set; }
    public MetricRing(double size = 36) { Height = Width = size; HorizontalAlignment = HorizontalAlignment.Center; }
    public override void Render(DrawingContext context)
    {
        base.Render(context); var center = new Point(Bounds.Width / 2, Bounds.Height / 2); double radius = Math.Max(2, Math.Min(Bounds.Width, Bounds.Height) / 2 - 3);
        for (int i = 0; i < 48; i++) { double a = i / 48d * Math.PI * 2 - Math.PI / 2, b = (i + .75) / 48d * Math.PI * 2 - Math.PI / 2; var brush = Percent is { } p && i < p / 100 * 48 ? Brushes.MediumSeaGreen : Brushes.DimGray; context.DrawLine(new Pen(brush, 3), center + new Vector(Math.Cos(a) * radius, Math.Sin(a) * radius), center + new Vector(Math.Cos(b) * radius, Math.Sin(b) * radius)); }
    }
}
sealed class CoreMetricRow : Grid
{
    readonly SegmentedUsage bar = new();
    readonly TextBlock value = new() { Text = "—", FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
    public CoreMetricRow(string label)
    {
        ColumnDefinitions = new ColumnDefinitions("30,*,34"); Height = 25; Margin = new Thickness(2, 1);
        var name = new TextBlock { Text = label, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        Children.Add(name); Grid.SetColumn(bar, 1); Children.Add(bar); Grid.SetColumn(value, 2); Children.Add(value);
        AutomationProperties.SetName(bar, label + " 使用率");
    }
    public void Set(double? percent)
    {
        bar.Percent = percent;
        value.Text = percent is { } number ? $"{number:0.0}%" : "—";
        bar.InvalidateVisual();
    }
}
sealed class SegmentedUsage : Control
{
    public double? Percent { get; set; }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        const int segments = 20;
        double width = Math.Max(1, (Bounds.Width - 3) / segments);
        for (int index = 0; index < segments; index++)
        {
            IBrush color = Percent is { } value && index < value / 100 * segments ? index > 16 ? Brushes.IndianRed : Brushes.MediumSeaGreen : new SolidColorBrush(Color.Parse("#343A3C"));
            context.FillRectangle(color, new Rect(index * width, Math.Max(0, Bounds.Height / 2 - 5), Math.Max(1, width - 2), 10));
        }
    }
}
sealed class NetworkTrend : Control
{
    readonly Queue<(double? Rx, double? Tx)> samples = new();
    public NetworkTrend() { Height = 52; }
    public void Add(double? rx, double? tx) { samples.Enqueue((rx, tx)); while (samples.Count > 60) samples.Dequeue(); InvalidateVisual(); }
    public void Gap() { if (samples.Count > 0) Add(null, null); }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        for (int line = 1; line <= 3; line++)
            context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#303638")), 1), new Point(0, line * Bounds.Height / 4), new Point(Bounds.Width, line * Bounds.Height / 4));
        var values = samples.ToArray(); if (values.Length < 2) return;
        double max = Math.Max(1, values.Max(p => Math.Max(p.Rx ?? 0, p.Tx ?? 0)));
        for (int i = 1; i < values.Length; i++)
        {
            Draw(values[i - 1].Rx, values[i].Rx, Brushes.MediumSeaGreen); Draw(values[i - 1].Tx, values[i].Tx, Brushes.CornflowerBlue);
            void Draw(double? before, double? now, IBrush brush) { if (before == null || now == null) return; context.DrawLine(new Pen(brush, 1.5), new Point((i - 1) * Bounds.Width / (values.Length - 1), Bounds.Height - before.Value / max * (Bounds.Height - 3)), new Point(i * Bounds.Width / (values.Length - 1), Bounds.Height - now.Value / max * (Bounds.Height - 3))); }
        }
    }
}
