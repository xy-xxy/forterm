using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Forterm.Core;
namespace Forterm.Desktop;

public sealed class PermissionWindow : Window
{
    readonly IRemoteFileSource source; readonly string path, title; readonly TransferQueue queue; readonly PermissionService service = new(); readonly SudoPermissionService privileged = new();
    readonly TextBox mode = new() { MaxLength = 3, Width = 100 }, fileMode = new() { MaxLength = 3, Width = 100, PlaceholderText = "保留" };
    readonly CheckBox recursive = new() { Content = "包含子目录和文件（先预览）" }, sudo = new() { Content = "使用同一SSH的安全sudo" }, identity = new() { Content = "我确认此SSH真实目标与文件路径空间一致" };
    readonly CheckBox[] bits = new CheckBox[9]; bool syncingBits;
    readonly TextBlock status = UI.Label("尚未生成计划；预览不会修改远端。特殊位按各项原值保留。", true);
    readonly ListBox rows = new() { Background = Brushes.Transparent };
    readonly Button preview, apply; readonly CancellationTokenSource lifetime = new();
    TransferTicket? activeTicket;
    PermissionPlan? plan; SudoPermissionPlan? sudoPlan; bool busy; long revision; string observed = "";
    public PermissionWindow(IRemoteFileSource source, string title, string path, TransferQueue queue)
    {
        Avalonia.Automation.AutomationProperties.SetName(mode, "普通权限"); Avalonia.Automation.AutomationProperties.SetName(fileMode, "递归文件权限"); this.source = source; this.title = title; this.path = path; this.queue = queue;
        Title = "远程权限计划"; Width = 900; Height = 650; MinWidth = 520; MinHeight = 360; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var controls = new WrapPanel(); controls.Children.Add(UI.Label("普通权限（000–777）")); controls.Children.Add(mode); controls.Children.Add(UI.Label("递归文件权限（留空保留）")); controls.Children.Add(fileMode);
        var bitGrid = new Avalonia.Controls.Primitives.UniformGrid { Columns = 3 };
        string[] labels = ["所有者读", "所有者写", "所有者执行", "组读", "组写", "组执行", "其他读", "其他写", "其他执行"];
        for (int i = 0; i < 9; i++)
        {
            bits[i] = new CheckBox { Content = labels[i] }; bitGrid.Children.Add(bits[i]);
            bits[i].IsCheckedChanged += (_, _) => { if (syncingBits) return; uint value = 0; for (int j = 0; j < 9; j++) if (bits[j].IsChecked == true) value |= 1u << (8 - j); mode.Text = Convert.ToString(value, 8).PadLeft(3, '0'); };
        }
        mode.TextChanged += (_, _) => { try { uint value = Mode(mode.Text); syncingBits = true; for (int i = 0; i < 9; i++) bits[i].IsChecked = (value & (1u << (8 - i))) != 0; } catch (IOException) { } finally { syncingBits = false; } };
        sudo.IsEnabled = source is SftpLease; identity.IsVisible = false;
        var header = new StackPanel { Margin = new Thickness(18), Spacing = 10, Children = { UI.Label(title + " · " + path), controls, bitGrid, recursive, sudo, identity, status } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18), Spacing = 12 };
        preview = UI.Button("预览计划", () => _ = Preview()); apply = UI.Button("应用已确认计划", () => _ = Apply()); apply.IsEnabled = false;
        buttons.Children.Add(UI.Button("取消剩余", () => { if (activeTicket != null) queue.Cancel(activeTicket.Id); else lifetime.Cancel(); })); buttons.Children.Add(UI.Button("关闭", () => Close())); buttons.Children.Add(preview); buttons.Children.Add(apply);
        rows.ItemTemplate = new FuncDataTemplate<PermissionItem>((item, _) => item == null ? new Border() : new TextBlock { Text = $"{item.Before.Path}   {Convert.ToString(item.Before.Mode, 8)} → {(item.Skip ? "保留" : Convert.ToString(item.Mode, 8))}   {(item.Before.Directory ? "目录" : "文件")}", TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(18, 6), Opacity = item.Skip ? .5 : 1 });
        var headerScroll = new ScrollViewer { Content = header, MaxHeight = 300 }; SizeChanged += (_, _) => headerScroll.MaxHeight = Math.Max(140, Bounds.Height * .52); var root = new DockPanel(); DockPanel.SetDock(headerScroll, Dock.Top); root.Children.Add(headerScroll); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons); root.Children.Add(rows); Content = root;
        void Invalidate() { string current = Signature(); if (current == observed) return; observed = current; revision++; plan = null; sudoPlan = null; apply.IsEnabled = false; if (!busy) status.Text = "参数已变化，请重新预览"; }
        mode.TextChanged += (_, _) => Invalidate(); fileMode.TextChanged += (_, _) => Invalidate(); recursive.IsCheckedChanged += (_, _) => Invalidate(); sudo.IsCheckedChanged += (_, _) => { identity.IsVisible = sudo.IsChecked == true; identity.IsChecked = false; Invalidate(); }; identity.IsCheckedChanged += (_, _) => apply.IsEnabled = !busy && plan != null && (sudo.IsChecked != true || identity.IsChecked == true && sudoPlan != null);
        Opened += async (_, _) => { try { var current = await service.InspectAsync(source, path, lifetime.Token); mode.Text = Convert.ToString(current.Mode & 511, 8).PadLeft(3, '0'); status.Text = "当前：" + Convert.ToString(current.Mode, 8) + "；预览只读取，应用前逐项复核"; } catch (Exception e) { status.Text = e.Message; preview.IsEnabled = false; } };
        Closing += (_, e) => { if (activeTicket != null) { e.Cancel = true; status.Text = "权限队列仍在运行，请先取消剩余并等待当前项确认"; } }; Closed += (_, _) => lifetime.Cancel();
    }
    string Signature() => (mode.Text ?? "") + "|" + (fileMode.Text ?? "") + "|" + recursive.IsChecked + "|" + sudo.IsChecked;
    static uint Mode(string? text) { if (text == null || text.Length is < 1 or > 3 || text.Any(c => c is < '0' or > '7')) throw new IOException("普通权限只能填写000–777"); return Convert.ToUInt32(text, 8); }
    void Busy(bool value) { busy = value; preview.IsEnabled = !value; apply.IsEnabled = !value && plan != null && (sudo.IsChecked != true || identity.IsChecked == true && sudoPlan != null); foreach (var c in new Control[] { mode, fileMode, recursive, sudo, identity }.Concat(bits)) c.IsEnabled = !value; if (!value && source is not SftpLease) sudo.IsEnabled = false; }
    async Task Preview()
    {
        if (busy) return; observed = Signature(); long epoch = revision;
        try
        {
            uint directory = Mode(mode.Text); uint? files = string.IsNullOrWhiteSpace(fileMode.Text) ? null : Mode(fileMode.Text); bool elevated = sudo.IsChecked == true; Busy(true); status.Text = "正在读取有界权限计划…";
            var next = await service.PreviewAsync(source, path, recursive.IsChecked == true, directory, files, lifetime.Token);
            SudoPermissionPlan? inspect = elevated ? await privileged.InspectAsync(next, lifetime.Token) : null;
            if (epoch != revision || !IsVisible) return;
            plan = next; sudoPlan = inspect; rows.ItemsSource = next.Items;
            status.Text = $"将修改{next.Items.Count(i => !i.Skip)}项，保留{next.Items.Count(i => i.Skip)}项文件权限，跳过{next.Skipped}项链接/特殊节点；取消不会回滚已完成项。" + (inspect == null ? "" : "\n" + string.Join('\n', inspect.Notes.Take(4)));
        }
        catch (Exception e) { plan = null; sudoPlan = null; status.Text = e.Message; }
        finally { Busy(false); }
    }
    async Task Apply()
    {
        if (busy || plan == null) return; var captured = plan; var elevated = sudoPlan; bool useSudo = sudo.IsChecked == true;
        try
        {
            Busy(true);
            if (useSudo && (identity.IsChecked != true || elevated == null)) throw new IOException("请完成sudo身份预览并明确确认真实目标");
            if (!await UI.Confirm(this, "应用远程权限计划？", title + " · " + path + $"\n{captured.Items.Count(i => !i.Skip)}项 · {(useSudo ? "sudo root" : "当前文件通道权限")}\n逐项复核后应用，遇变化/取消停止，已完成不回滚。", lifetime.Token)) return;
            async Task<byte[]?> Password(CancellationToken ct)
            {
                var answers = await Dispatcher.UIThread.InvokeAsync(() => UI.AskCredentials(this, new CredentialChallenge(1, false, true, "独立sudo命令出现本次随机提示；仅将密码写入该exec的stdin，不保存、不使用登录密码。", new[] { new CredentialQuestion("本次sudo密码", false) }), ct));
                if (answers == null) return null; try { return Encoding.UTF8.GetBytes(answers[0]); } finally { Array.Clear(answers); }
            }
            var ticket = queue.Add(System.IO.Path.GetFileName(path) + " · 权限", title + " · " + path, async (progress, token) =>
            {
                var counted = new CountProgress(progress);
                if (useSudo) await privileged.ApplyAsync(elevated!, true, Password, counted, token); else await service.ApplyAsync(captured, counted, token);
            }, source.Closed);
            activeTicket = ticket;
            status.Text = "已加入后台队列；此窗口保持到结果确认，本次密码不会保存。";
            var result = await ticket.Completion;
            status.Text = result.State == TransferState.Completed ? "权限计划已全部确认完成" : result.Message;
            plan = null; sudoPlan = null;
        }
        catch (Exception e) { status.Text = e.Message; }
        finally { activeTicket = null; Busy(false); }
    }
    sealed class CountProgress(IProgress<TransferProgress> progress) : IProgress<TransferProgress> { public void Report(TransferProgress p) => progress.Report(p with { Unit = "项" }); }
}
