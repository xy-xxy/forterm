using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Forterm.Studio.Views;

public sealed partial class ProcessWindow : Window
{
    public ProcessWindow() => AvaloniaXamlLoader.Load(this);
}
