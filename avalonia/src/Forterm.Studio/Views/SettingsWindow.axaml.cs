using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Forterm.Studio.ViewModels;

namespace Forterm.Studio.Views;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
        // While a new shortcut is being chosen, every key is meant for that.
        AddHandler(KeyDownEvent, (_, e) => { if (DataContext is SettingsViewModel vm && vm.Press(e.Key, e.KeyModifiers)) e.Handled = true; }, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => (DataContext as SettingsViewModel)?.EndCapture();
    }
}
