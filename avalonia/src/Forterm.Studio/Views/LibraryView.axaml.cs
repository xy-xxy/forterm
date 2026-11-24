using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Forterm.Studio.ViewModels;

namespace Forterm.Studio.Views;

public sealed partial class LibraryView : UserControl
{
    public LibraryView()
    {
        AvaloniaXamlLoader.Load(this);
        // Double-click a host row (not its buttons) or a host in the tree to connect.
        this.FindControl<ListBox>("Hosts")!.DoubleTapped += (_, e) =>
        {
            if (IsOnButton(e.Source)) return;
            if (e.Source is Control { DataContext: HostRowViewModel row } && row.ConnectCommand?.CanExecute(null) == true) row.ConnectCommand.Execute(null);
        };
        this.FindControl<TreeView>("Tree")!.DoubleTapped += (_, e) =>
        {
            if (e.Source is Control { DataContext: LibraryNode { IsHost: true } } && DataContext is LibraryViewModel model && model.OpenNodeCommand.CanExecute(null))
                model.OpenNodeCommand.Execute(null);
        };
    }

    static bool IsOnButton(object? source)
    {
        for (var visual = source as Avalonia.Visual; visual != null; visual = visual.GetVisualParent())
        {
            if (visual is Button or CheckBox) return true;
            if (visual is ListBoxItem) return false;
        }
        return false;
    }
}
