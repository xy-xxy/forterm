using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Forterm.Studio.Services;

namespace Forterm.Studio.Views.Dialogs;

/// <summary>"粘贴确认": shows exactly what would be pasted into the terminal (read-only).</summary>
public sealed class PasteDialog : Window
{
    const int PreviewLimit = 20_000;
    bool accepted;

    public PasteDialog(PasteReview review)
    {
        Title = "粘贴到终端";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Bind(BackgroundProperty, this.GetResourceObservable("St.Dialog"));
        var body = new StackPanel { Spacing = 10, Margin = new Thickness(18) };
        body.Children.Add(new TextBlock { Text = review.Summary, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
        bool cut = review.Preview.Length > PreviewLimit;
        var preview = new TextBox
        {
            Text = cut ? review.Preview[..PreviewLimit] : review.Preview,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily(Appearance.MonospaceStack),
            FontSize = 12.5,
            MinHeight = 90,
            MaxHeight = 320,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(preview, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(preview, ScrollBarVisibility.Auto);
        Avalonia.Automation.AutomationProperties.SetName(preview, "将要粘贴的内容");
        body.Children.Add(preview);
        if (cut) body.Children.Add(new TextBlock { Text = $"预览只显示前 {PreviewLimit} 个字符，其余内容也会粘贴。", FontSize = 11.5, Opacity = 0.7 });
        body.Children.Add(new TextBlock { Text = "可在“系统配置 → 通用终端 → 粘贴确认”中关闭此提示；控制字符始终会去掉。", FontSize = 11.5, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        var cancel = new Button { Content = "取消" };
        cancel.Classes.Add("secondary");
        cancel.Click += (_, _) => Close();
        var ok = new Button { Content = "粘贴" };
        ok.Classes.Add("primary");
        ok.Click += (_, _) => { accepted = true; Close(); };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        footer.Children.Add(cancel);
        footer.Children.Add(ok);
        body.Children.Add(footer);
        Content = body;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Opened += (_, _) => Dispatcher.UIThread.Post(() => ok.Focus());
    }

    public static async Task<bool> AskAsync(Window? owner, PasteReview review)
    {
        var dialog = new PasteDialog(review);
        await DialogHost.ShowAsync(dialog, owner, CancellationToken.None);
        return dialog.accepted;
    }
}
