using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Forterm.Studio.Controls;

namespace Forterm.Studio.Views.Dialogs;

/// <summary>Small dialogs built in code (confirm, single-line prompt) with Studio's dialog chrome.</summary>
public sealed class SimpleDialog : Window
{
    bool accepted;
    readonly TextBox? input;

    SimpleDialog(string title, Control body, string confirmText, bool danger, TextBox? input)
    {
        this.input = input;
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.BorderOnly;
        this.Bind(BackgroundProperty, this.GetResourceObservable("St.Dialog"));

        var header = new Grid { Height = 40, Background = Brushes.Transparent, ColumnDefinitions = new ColumnDefinitions("40,*,40") };
        var titleText = new TextBlock { Text = title, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        titleText.Classes.Add("title");
        Grid.SetColumn(titleText, 1);
        var close = new Button { Content = new StrokeIcon { Data = IconGeometry("Icon.Close"), Width = 13, Height = 13 } };
        close.Classes.Add("flat");
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2);
        header.Children.Add(titleText);
        header.Children.Add(close);
        header.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };

        var cancel = new Button { Content = "取消" };
        cancel.Classes.Add("secondary");
        cancel.Click += (_, _) => Close();
        // Enter never confirms something that cannot be undone: the focus starts on "取消".
        var ok = new Button { Content = confirmText, IsDefault = !danger };
        ok.Classes.Add("primary");
        if (danger) ok.Bind(BackgroundProperty, this.GetResourceObservable("St.Danger"));
        ok.Click += (_, _) => { accepted = true; Close(); };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(16, 4, 16, 16) };
        footer.Children.Add(cancel);
        footer.Children.Add(ok);

        var section = new Border { Margin = new Thickness(16, 0, 16, 12), Padding = new Thickness(14), CornerRadius = new CornerRadius(6), Child = body };
        section.Bind(Border.BackgroundProperty, this.GetResourceObservable("St.DialogSection"));
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(section);
        Content = root;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Opened += (_, _) => Dispatcher.UIThread.Post(() => { if (input != null) { input.Focus(); input.SelectAll(); } else (danger ? cancel : ok).Focus(); });
    }

    static Avalonia.Media.Geometry? IconGeometry(string key) =>
        Application.Current!.TryGetResource(key, null, out var value) ? value as Avalonia.Media.Geometry : null;

    public static async Task<bool> ConfirmAsync(Window? owner, string title, string message, string confirmText, bool danger)
    {
        var dialog = new SimpleDialog(title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, confirmText, danger, null);
        await DialogHost.ShowAsync(dialog, owner, CancellationToken.None);
        return dialog.accepted;
    }

    public static async Task<string?> PromptAsync(Window? owner, string title, string label, string initial)
    {
        var box = new TextBox { Text = initial, MaxLength = 255 };
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock { Text = label, FontSize = 12.5 });
        body.Children.Add(box);
        var dialog = new SimpleDialog(title, body, "确定", false, box);
        await DialogHost.ShowAsync(dialog, owner, CancellationToken.None);
        return dialog.accepted ? box.Text?.Trim() : null;
    }
}
