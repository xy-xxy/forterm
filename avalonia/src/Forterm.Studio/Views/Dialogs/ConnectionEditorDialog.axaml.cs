using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Forterm.Studio.ViewModels;

namespace Forterm.Studio.Views.Dialogs;

/// <summary>Six-section connection form with anchor navigation and scroll spy.</summary>
public sealed partial class ConnectionEditorDialog : Window
{
    bool saved, saving;
    readonly ScrollViewer scroller;
    readonly Button[] nav;

    public ConnectionEditorDialog()
    {
        AvaloniaXamlLoader.Load(this);
        scroller = this.FindControl<ScrollViewer>("Scroller")!;
        nav = this.FindControl<StackPanel>("Nav")!.Children.OfType<Button>().ToArray();
        foreach (var button in nav)
            button.Click += (_, _) => { if (Section(button) is { } target) scroller.Offset = scroller.Offset.WithY(Math.Max(0, target.Bounds.Top)); };
        scroller.ScrollChanged += (_, _) => Spy();
        LayoutUpdated += (_, _) => Spy();
        this.FindControl<Button>("SaveButton")!.Click += async (_, _) => await SaveAsync();
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();
        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
        this.FindControl<Grid>("Header")!.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => (DataContext as ConnectionEditorViewModel)?.ClearSecrets();
    }

    Control? Section(Button button) => button.Tag is string name ? this.FindControl<Control>(name) : null;

    /// <summary>Highlights the last visible section whose top has scrolled past the viewport top.</summary>
    void Spy()
    {
        Button? active = null;
        double y = scroller.Offset.Y + 24;
        foreach (var button in nav)
        {
            if (!button.IsVisible || Section(button) is not { IsVisible: true } section) continue;
            if (active == null || section.Bounds.Top <= y) active = button;
        }
        if (scroller.Offset.Y + scroller.Viewport.Height >= scroller.Extent.Height - 2)
            active = nav.LastOrDefault(b => b.IsVisible) ?? active;
        foreach (var button in nav) button.Classes.Set("active", ReferenceEquals(button, active));
    }

    async Task SaveAsync()
    {
        if (saving || DataContext is not ConnectionEditorViewModel editor) return;
        saving = true;
        try
        {
            if (await editor.SaveAsync()) { saved = true; Close(); }
        }
        finally { saving = false; }
    }

    public static async Task<bool> ShowAsync(Window? owner, ConnectionEditorViewModel editor)
    {
        var dialog = new ConnectionEditorDialog { DataContext = editor };
        await DialogHost.ShowAsync(dialog, owner, CancellationToken.None);
        return dialog.saved;
    }
}
