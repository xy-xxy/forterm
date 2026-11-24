using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Forterm.Core;
namespace Forterm.Desktop;

public sealed class TransferPanel : DockPanel, IDisposable
{
    readonly CheckBox expand = new() { Content = "传输队列", IsChecked = true }; bool compact;
    readonly TransferQueue queue; readonly Window owner; readonly ListBox rows = new() { MaxHeight = 170, Background = Brushes.Transparent }; readonly TextBlock summary = new() { Classes = { "note" } }; int scheduled, disposed;
    public double ViewportHeight { get => rows.MaxHeight; set { rows.MaxHeight = value; InvalidateMeasure(); } }
    public TransferPanel(Window owner, TransferQueue queue)
    {
        this.owner = owner; this.queue = queue; rows.Classes.Add("transfer-rows"); expand.FontSize = 12; expand.Height = expand.MinHeight = 26;
        expand.IsCheckedChanged += (_, _) => rows.IsVisible = expand.IsChecked == true;
        var clear = UI.Button("⋯", () => queue.ClearFinished()); clear.Classes.Add("icon"); clear.Width = clear.Height = clear.MinWidth = clear.MinHeight = 26; clear.Padding = new Thickness(0); ToolTip.SetTip(clear, "清理已结束任务（保留需核对项）");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(0, 8, 0, 4) }; header.Children.Add(expand); Grid.SetRow(summary, 1); Grid.SetColumnSpan(summary, 2); header.Children.Add(summary); summary.FontSize = 10; summary.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(clear, 1); header.Children.Add(clear); SetDock(header, Dock.Top); Children.Add(header);
        rows.ItemTemplate = new FuncDataTemplate<TransferSnapshot>((item, _) => item == null ? new Border() : Row(item)); Children.Add(rows); queue.Changed += Changed; Render();
    }
    public void SetCompact(bool value) { if (value && !compact) expand.IsChecked = false; compact = value; }
    Control Row(TransferSnapshot item)
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("18,16,3"), ColumnDefinitions = new ColumnDefinitions("*,28,28"), Margin = new Thickness(0, 6) };
        root.Children.Add(new TextBlock { Text = item.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        var details = UI.Button("ⓘ", () => ShowDetails(item.Id)); details.Classes.Add("icon"); details.Width = details.Height = details.MinWidth = details.MinHeight = 26; details.Padding = new Thickness(0); details.FontSize = 14; details.VerticalAlignment = VerticalAlignment.Center; AutomationProperties.SetName(details, "任务详情 " + item.Name); ToolTip.SetTip(details, "查看来源与任务详情"); Grid.SetColumn(details, 1); Grid.SetRowSpan(details, 2); root.Children.Add(details);
        var cancel = UI.Button("×", () => queue.Cancel(item.Id)); cancel.Classes.Add("icon"); cancel.Width = cancel.Height = cancel.MinWidth = cancel.MinHeight = 26; cancel.Padding = new Thickness(0); cancel.FontSize = 14; cancel.VerticalAlignment = VerticalAlignment.Center; AutomationProperties.SetName(cancel, "取消任务 " + item.Name); cancel.IsEnabled = item.State is TransferState.Queued or TransferState.Running or TransferState.Verifying; ToolTip.SetTip(cancel, item.State == TransferState.Committing ? "正在提交，不可撤销" : "取消此任务"); Grid.SetColumn(cancel, 2); Grid.SetRowSpan(cancel, 2); root.Children.Add(cancel);
        string bytes = item.Unit == "项" ? item.Bytes + " / " + (item.Total?.ToString() ?? "—") + " 项" : item.Total is { } total ? $"{Size(item.Bytes)} / {Size(total)}" : Size(item.Bytes);
        var note = new TextBlock { Text = $"{item.Source} · {StateName(item.State)} · {bytes} · {item.Message}", Classes = { "note" }, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis }; ToolTip.SetTip(note, $"{item.Name}\n{item.Source}\n{StateName(item.State)} · {bytes}\n{item.Message}"); Grid.SetRow(note, 1); root.Children.Add(note);
        var progress = new ProgressBar { Minimum = 0, Maximum = 1, Value = item.State == TransferState.Completed ? 1 : item.Total is > 0 ? Math.Clamp((double)item.Bytes / item.Total.Value, 0, 1) : 0, IsIndeterminate = item.Total == null && item.State == TransferState.Running, Height = 3, Margin = new Thickness(0) }; Grid.SetRow(progress, 2); Grid.SetColumnSpan(progress, 3); root.Children.Add(progress); return root;
    }
    static string StateName(TransferState state) => state switch { TransferState.Queued => "等待", TransferState.Running => "传输", TransferState.Verifying => "校验", TransferState.Committing => "提交", TransferState.Completed => "完成", TransferState.Cancelled => "取消", TransferState.Failed => "失败", _ => "需核对" };
    static string Size(long n) => n >= 1024 * 1024 ? $"{n / (1024d * 1024):0.0} MiB" : n >= 1024 ? $"{n / 1024d:0.0} KiB" : $"{n} B";
    async void ShowDetails(Guid id)
    {
        var item = queue.Snapshot().FirstOrDefault(t => t.Id == id); if (item == null) return;
        var window = new Window { Title = "传输详情", Width = 560, Height = 300, MinWidth = 300, MinHeight = 200, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new DockPanel { Margin = new Thickness(16) }; var close = UI.Button("关闭", () => window.Close()); SetDock(close, Dock.Bottom); body.Children.Add(close);
        body.Children.Add(new ScrollViewer { Margin = new Thickness(0, 0, 0, 12), Content = new TextBlock { Text = $"{item.Name}\n来源：{item.Source}\n{item.State}\n{item.Message}", TextWrapping = TextWrapping.Wrap } }); window.Content = body; await window.ShowDialog(owner);
    }
    void Changed() { if (Volatile.Read(ref disposed) != 0 || Interlocked.Exchange(ref scheduled, 1) != 0) return; Dispatcher.UIThread.Post(() => { Interlocked.Exchange(ref scheduled, 0); if (disposed == 0) Render(); }); }
    void Render() { var items = queue.Snapshot(); rows.ItemsSource = items; summary.IsVisible = items.Count > 0; summary.Text = $"{items.Count(t => t.State is TransferState.Running or TransferState.Verifying or TransferState.Committing)} 运行 · {items.Count(t => t.State == TransferState.Queued)} 等待 · {items.Count(t => t.State == TransferState.Uncertain)} 需核对"; }
    public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) queue.Changed -= Changed; }
}
