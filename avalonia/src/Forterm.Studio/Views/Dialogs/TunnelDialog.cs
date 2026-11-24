using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Forterm.Studio.Library;
using Forterm.Studio.ViewModels;

namespace Forterm.Studio.Views.Dialogs;

/// <summary>New / edit tunnel: name, kind, the saved SSH connection to go through, the local port
/// (always on 127.0.0.1) and, for a local forward, the remote target as seen from that connection.</summary>
public sealed class TunnelDialog : Window
{
    readonly TextBox name = new(), localPort = new() { Width = 100 }, remoteHost = new(), remotePort = new() { Width = 100 }, note = new();
    readonly ComboBox via = new() { HorizontalAlignment = HorizontalAlignment.Stretch }, kind = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    static readonly Choice[] Kinds = [new(TunnelDefinition.Local, "本地转发（到固定目标）"), new(TunnelDefinition.Dynamic, "动态转发（SOCKS 代理）")];
    readonly TextBlock error = new() { FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    readonly IReadOnlyList<StudioProfile> profiles;
    readonly TunnelDefinition original;
    TunnelDefinition? result;

    TunnelDialog(TunnelDefinition? existing, IReadOnlyList<StudioProfile> profiles)
    {
        this.profiles = profiles;
        original = existing ?? new TunnelDefinition();
        Title = existing == null ? "新建隧道" : "编辑隧道";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Bind(BackgroundProperty, this.GetResourceObservable("St.Dialog"));
        error.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("St.Danger"));
        var choices = profiles.Select(p => new Choice(p.Id, (p.Name.Length > 0 ? p.Name + "  " : "") + $"{p.User}@{p.Host}:{p.Port}")).ToArray();
        via.ItemsSource = choices;
        via.SelectedItem = choices.FirstOrDefault(c => c.Value == original.ProfileId) ?? choices.FirstOrDefault();
        name.Text = original.Name;
        localPort.Text = original.LocalPort > 0 ? original.LocalPort.ToString(CultureInfo.InvariantCulture) : "";
        remoteHost.Text = original.RemoteHost;
        remotePort.Text = original.RemotePort > 0 ? original.RemotePort.ToString(CultureInfo.InvariantCulture) : "";
        note.Text = original.Note;
        kind.ItemsSource = Kinds;
        kind.SelectedItem = Kinds.FirstOrDefault(c => c.Value == original.Kind) ?? Kinds[0];
        void Sync() { bool fixedTarget = (kind.SelectedItem as Choice)?.Value != TunnelDefinition.Dynamic; remoteHost.IsEnabled = fixedTarget; remotePort.IsEnabled = fixedTarget; }
        kind.SelectionChanged += (_, _) => Sync();
        Sync();
        name.PlaceholderText = "例如 数据库";
        localPort.PlaceholderText = "13306";
        remotePort.PlaceholderText = "3306";

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto"), Margin = new Thickness(0, 4) };
        void Row(int row, string label, Control control)
        {
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, Margin = new Thickness(0, 6) };
            Grid.SetRow(text, row);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            control.Margin = new Thickness(0, 4);
            grid.Children.Add(text);
            grid.Children.Add(control);
        }
        Row(0, "名称", name);
        Row(1, "类型", kind);
        Row(2, "经由连接", via);
        Row(3, "本地端口", localPort);
        Row(4, "远程地址", remoteHost);
        Row(5, "远程端口", remotePort);
        Row(6, "备注", note);
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(18) };
        body.Children.Add(new TextBlock { Text = "本地只监听 127.0.0.1；远程地址按“经由连接”所在网络解析（例如 127.0.0.1 表示那台服务器本机）。本机任何程序，以及多人同时登录的 Windows（如远程桌面服务器）上的其他用户，都能连接这个端口；动态转发是不需认证的 SOCKS4/5 代理，可经它访问服务器所在网络的任意地址，这类机器上请勿使用。", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 11.5, Opacity = 0.7 });
        body.Children.Add(grid);
        body.Children.Add(error);
        var cancel = new Button { Content = "取消" };
        cancel.Classes.Add("secondary");
        cancel.Click += (_, _) => Close();
        var ok = new Button { Content = "保存", IsDefault = true };
        ok.Classes.Add("primary");
        ok.Click += (_, _) => Accept();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        footer.Children.Add(cancel);
        footer.Children.Add(ok);
        body.Children.Add(footer);
        Content = body;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    void Accept()
    {
        static int Port(string? text) => int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0;
        bool dynamic = (kind.SelectedItem as Choice)?.Value == TunnelDefinition.Dynamic;
        var candidate = original with
        {
            Name = name.Text?.Trim() ?? "",
            Kind = dynamic ? TunnelDefinition.Dynamic : TunnelDefinition.Local,
            ProfileId = (via.SelectedItem as Choice)?.Value ?? "",
            LocalPort = Port(localPort.Text),
            RemoteHost = dynamic ? "" : remoteHost.Text?.Trim() ?? "",
            RemotePort = dynamic ? 0 : Port(remotePort.Text),
            Note = note.Text?.Trim() ?? "",
        };
        try { LibraryStore.ValidateTunnel(candidate, profiles); }
        catch (InvalidDataException e) { error.Text = e.Message; return; }
        result = candidate;
        Close();
    }

    public static async Task<TunnelDefinition?> AskAsync(Window? owner, TunnelDefinition? existing, IReadOnlyList<StudioProfile> profiles)
    {
        var dialog = new TunnelDialog(existing, profiles);
        await DialogHost.ShowAsync(dialog, owner, CancellationToken.None);
        return dialog.result;
    }
}
