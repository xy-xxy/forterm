using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Forterm.Studio.Views;

/// <summary>"堡垒机资产": see <see cref="ViewModels.BastionAssetsViewModel"/>.</summary>
public sealed partial class BastionAssetsWindow : Window
{
    public BastionAssetsWindow() => AvaloniaXamlLoader.Load(this);
}
