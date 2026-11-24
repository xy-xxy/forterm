using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Forterm.Studio.Views;

public sealed partial class MonitorView : UserControl
{
    /// <summary>Wide enough for two columns of cards: charts get taller.</summary>
    const double WideFrom = 690;

    public MonitorView()
    {
        AvaloniaXamlLoader.Load(this);
        SizeChanged += (_, e) => Classes.Set("wide", e.NewSize.Width >= WideFrom);
        if (this.FindControl<Button>("MenuButton")!.Flyout is Flyout { Content: Control content } flyout)
            content.AddHandler(Button.ClickEvent, (_, _) => Dispatcher.UIThread.Post(flyout.Hide)); // after the button has run its command
    }

    /// <summary>A meter of the overview leads to its details.</summary>
    void OnVitalClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string section }) this.FindControl<Border>("Card_" + section)?.BringIntoView();
    }
}
