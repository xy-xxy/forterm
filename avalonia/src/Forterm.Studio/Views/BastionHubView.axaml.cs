using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Forterm.Studio.ViewModels;

namespace Forterm.Studio.Views;

/// <summary>The library's "堡垒机" tab: see <see cref="BastionHubViewModel"/>.</summary>
public sealed partial class BastionHubView : UserControl
{
    public BastionHubView()
    {
        AvaloniaXamlLoader.Load(this);
        // Double-click a machine (not its buttons or its account choice) to open its terminal, as in the library.
        this.FindControl<ListBox>("Assets")!.DoubleTapped += (_, e) =>
        {
            if (IsOnControl(e.Source)) return;
            if (e.Source is Control { DataContext: BastionAssetRow row } && row.SshCommand.CanExecute(null)) row.SshCommand.Execute(null);
        };
        // Double-click an account that is not logged in to log in.
        this.FindControl<ListBox>("Accounts")!.DoubleTapped += (_, e) =>
        {
            if (e.Source is Control { DataContext: BastionAccountItem { CanLogIn: true, Session: { IsLoggedOut: true } session } } && session.LoginCommand.CanExecute(null))
                session.LoginCommand.Execute(null);
        };
    }

    static bool IsOnControl(object? source)
    {
        for (var visual = source as Avalonia.Visual; visual != null; visual = visual.GetVisualParent())
        {
            if (visual is Button or ComboBox) return true;
            if (visual is ListBoxItem) return false;
        }
        return false;
    }
}
