using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Forterm.Core;
using Forterm.Studio.Services;

namespace Forterm.Studio.Views.Dialogs;

/// <summary>rwx grid + octal box for normal permission bits. Special bits are never edited here
/// (the service keeps them); directories can apply recursively with a separate file mode.</summary>
public sealed class PermissionDialog : Window
{
    readonly CheckBox[] bits = new CheckBox[9];
    readonly TextBox octal = new() { Width = 80 };
    readonly CheckBox recursive = new() { Content = "递归应用到所有子目录和文件（最多 2000 项）" };
    readonly TextBox fileOctal = new() { Width = 80, PlaceholderText = "留空不改文件" };
    readonly CheckBox sudo = new() { Content = "使用同一 SSH 连接的 sudo（root）修改" };
    readonly TextBlock error = new() { FontSize = 12 };
    PermissionRequest? result;
    bool syncing;

    PermissionDialog(PermissionSnapshot current, bool sudoAvailable)
    {
        Title = "修改权限";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Bind(BackgroundProperty, this.GetResourceObservable("St.Dialog"));
        error.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("St.Danger"));
        uint mode = current.Mode & 511u;
        var body = new StackPanel { Spacing = 10, Margin = new Thickness(18) };
        body.Children.Add(new TextBlock { Text = current.Path, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
        var special = current.Mode & 3584u;
        body.Children.Add(new TextBlock { Text = $"当前 {FileActions.Octal(current.Mode)}{(special != 0 ? $"（含特殊位 {Convert.ToString(special >> 9, 8)}，会保留）" : "")} · 不改所有者", FontSize = 11.5, Opacity = 0.7 });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("70,70,70,70"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), Margin = new Thickness(0, 4) };
        string[] who = ["所有者", "组", "其他"], what = ["读", "写", "执行"];
        for (int c = 0; c < 3; c++) Place(grid, new TextBlock { Text = what[c], FontSize = 12, Opacity = 0.7 }, 0, c + 1);
        for (int r = 0; r < 3; r++)
        {
            Place(grid, new TextBlock { Text = who[r], FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, r + 1, 0);
            for (int c = 0; c < 3; c++)
            {
                int index = r * 3 + c;
                var box = new CheckBox { IsChecked = (mode & (1u << (8 - index))) != 0 };
                box.IsCheckedChanged += (_, _) => FromBits();
                bits[index] = box;
                Place(grid, box, r + 1, c + 1);
            }
        }
        body.Children.Add(grid);
        var octalRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        octalRow.Children.Add(new TextBlock { Text = "数字形式", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        octalRow.Children.Add(octal);
        body.Children.Add(octalRow);
        octal.Text = FileActions.Octal(mode);
        octal.TextChanged += (_, _) => FromOctal();
        if (current.Directory)
        {
            body.Children.Add(recursive);
            var fileRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            fileRow.Children.Add(new TextBlock { Text = "其中文件使用", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            fileRow.Children.Add(fileOctal);
            fileRow.IsEnabled = false;
            recursive.IsCheckedChanged += (_, _) => fileRow.IsEnabled = recursive.IsChecked == true;
            body.Children.Add(fileRow);
        }
        if (sudoAvailable)
        {
            body.Children.Add(sudo);
            body.Children.Add(new TextBlock
            {
                Text = "在独立命令通道执行，不在终端输入。需要服务器有 /usr/bin/python3，且 sudo 策略允许；如需密码会在出现本次随机提示后单独询问，不保存。",
                TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Opacity = 0.7,
            });
        }
        body.Children.Add(error);
        var cancel = new Button { Content = "取消" };
        cancel.Classes.Add("secondary");
        cancel.Click += (_, _) => Close();
        var ok = new Button { Content = "下一步", IsDefault = true };
        ok.Classes.Add("primary");
        ok.Click += (_, _) => Accept(current.Directory);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        footer.Children.Add(cancel);
        footer.Children.Add(ok);
        body.Children.Add(footer);
        Content = body;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    uint Bits() => (uint)Enumerable.Range(0, 9).Sum(i => bits[i].IsChecked == true ? 1 << (8 - i) : 0);

    void FromBits()
    {
        if (syncing) return;
        syncing = true;
        octal.Text = FileActions.Octal(Bits());
        syncing = false;
    }

    void FromOctal()
    {
        if (syncing || !TryOctal(octal.Text, out uint value)) return;
        syncing = true;
        for (int i = 0; i < 9; i++) bits[i].IsChecked = (value & (1u << (8 - i))) != 0;
        syncing = false;
    }

    public static bool TryOctal(string? text, out uint value)
    {
        value = 0;
        text = text?.Trim() ?? "";
        return text.Length is >= 1 and <= 3 && text.All(c => c is >= '0' and <= '7') && uint.TryParse(text, out _) &&
               (value = Convert.ToUInt32(text, 8)) <= 511;
    }

    void Accept(bool directory)
    {
        if (!TryOctal(octal.Text, out uint mode)) { error.Text = "权限必须是 000–777 的八进制数"; return; }
        uint? fileMode = null;
        bool deep = directory && recursive.IsChecked == true;
        if (deep && !string.IsNullOrWhiteSpace(fileOctal.Text))
        {
            if (!TryOctal(fileOctal.Text, out uint f)) { error.Text = "文件权限必须是 000–777 的八进制数，或留空"; return; }
            fileMode = f;
        }
        result = new PermissionRequest(mode, deep, fileMode, sudo.IsChecked == true);
        Close();
    }

    public static async Task<PermissionRequest?> AskAsync(Window? owner, PermissionSnapshot current, bool sudoAvailable = false)
    {
        var dialog = new PermissionDialog(current, sudoAvailable);
        await DialogHost.ShowAsync(dialog, owner, CancellationToken.None);
        return dialog.result;
    }
}
