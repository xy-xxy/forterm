using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Studio.Controls;
using Forterm.Studio.Library;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;

namespace Forterm.Studio.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        WindowDecorationsTheme = (ControlTheme)Resources["StudioDecorations"]!;
        if (Screens.Primary is { } screen) FitTo(screen.WorkingArea, screen.Scaling);
        Wire();
        WireInteraction();
    }

    /// <summary>No larger than the screen (1440 x 860 is more than a small or much enlarged one has), and all of it on it.
    /// The system's own choice of a place puts a large window partly outside.</summary>
    internal void FitTo(PixelRect area, double scaling)
    {
        // What the system draws around a window that is not maximised.
        const double Frame = 16;
        double width = area.Width / scaling - Frame, height = area.Height / scaling - Frame;
        if (width < 200 || height < 200) return; // no real screen
        MinWidth = Math.Min(MinWidth, width);
        MinHeight = Math.Min(MinHeight, height);
        Width = Math.Min(Width, width);
        Height = Math.Min(Height, height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    public static MainWindow Create(KnownHosts knownHosts, LibraryStore library, CredentialStore credentials, AccountStore? accounts = null, QuickCommandStore? quickCommands = null)
    {
        var window = new MainWindow();
        window.DataContext = new MainWindowViewModel(new DialogInteraction(() => window), knownHosts, library, credentials, accounts: accounts,
            quickCommands: quickCommands, settingsDirectory: StudioPaths.DataDirectory) { Access = ControllerAccess.Shared };
        return window;
    }

    public MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    /// <summary>Set while the window confirms or performs its close; launches are routed elsewhere.</summary>
    public bool IsClosing { get; private set; }

    public Task<WorkspaceViewModel?> AcceptLaunchAsync(LaunchPlan plan) => ViewModel.AcceptLaunchAsync(plan);

    /// <summary>Minimise, maximise and close, 45 each, and a little distance.</summary>
    internal const double CaptionButtons = 3 * 45 + 5;

    void Wire()
    {
        // Leave room for the system window buttons drawn over the extended title bar (set below: not scaled with the rest).
        var left = this.FindControl<Border>("LeftInset")!;
        var right = this.FindControl<Border>("RightInset")!;

        var titleBar = this.FindControl<Border>("TitleBar")!;
        titleBar.PointerPressed += (_, e) =>
        {
            if (e.Source is Control source && IsInsideButton(source)) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else BeginMoveDrag(e);
        };
        this.FindControl<Button>("ThemeToggle")!.Click += (_, _) =>
        {
            // Quick toggle between the default dark and light looks (keeps the accent colour).
            var current = Appearance.Current;
            var next = current with { FollowSystem = false, Theme = Application.Current?.ActualThemeVariant == ThemeVariant.Light ? "暗夜" : "明亮" };
            Appearance.Apply(next);
            try { Appearance.Save(StudioPaths.DataDirectory, next); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
            {
                if (DataContext is MainWindowViewModel vm) vm.Notice = "外观已生效，但没有保存到磁盘：" + WorkspaceViewModel.Short(e.Message);
            }
        };
        this.FindControl<Button>("SettingsButton")!.Click += (_, _) => (DataContext as MainWindowViewModel)?.OpenSettings("theme");
        var scaler = this.FindControl<LayoutTransformControl>("Scaler")!;
        void Scale(StudioPreferences p)
        {
            scaler.LayoutTransform = Math.Abs(p.UiScale - 1) < 0.001 ? null : new Avalonia.Media.ScaleTransform(p.UiScale, p.UiScale);
            titleBar.Classes.Set("solid", p.TitleBarColor.Length > 0);
            // The system's buttons do not grow and shrink with the rest.
            left.Width = (OperatingSystem.IsMacOS() ? 76 : 8) / p.UiScale;
            right.Width = (OperatingSystem.IsWindows() ? CaptionButtons : 8) / p.UiScale;
        }
        Scale(Appearance.Current);
        Appearance.Changed += Scale;
        Closed += (_, _) => Appearance.Changed -= Scale;
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
        bool closeConfirmed = false;
        Closing += async (_, e) =>
        {
            if (closeConfirmed || DataContext is not MainWindowViewModel vm || !vm.NeedsCloseConfirmation) { IsClosing = true; return; }
            e.Cancel = true;
            if (IsClosing) return; // a confirmation (or the transfer drain) is already in progress
            IsClosing = true;      // new controller launches go to another window meanwhile
            try
            {
                if (!await vm.ConfirmCloseAllAsync()) return;
                closeConfirmed = true;
                Close();
            }
            catch (Exception ex) { vm.Notice = "关闭前检查失败：" + WorkspaceViewModel.Short(ex.Message); }
            finally { if (!closeConfirmed) IsClosing = false; }
        };
    }

    // ---------- sessions, shortcuts, palette ----------

    MainWindowViewModel? bound;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (bound != null) { bound.PropertyChanged -= OnModelChanged; bound.Palette.PropertyChanged -= OnPaletteChanged; bound.FocusRequested -= OnFocusRequested; }
        bound = DataContext as MainWindowViewModel;
        if (bound != null) { bound.PropertyChanged += OnModelChanged; bound.Palette.PropertyChanged += OnPaletteChanged; bound.FocusRequested += OnFocusRequested; }
    }

    void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.Selected)) return;
        Motion.Enter(this.FindControl<ContentControl>("Host")!);
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var tab in this.FindControl<ScrollViewer>("TabScroller")!.GetVisualDescendants().OfType<Button>())
                if (tab.Classes.Contains("session-tab") && ReferenceEquals(tab.DataContext, bound?.Selected)) tab.BringIntoView();
        }, DispatcherPriority.Background);
    }

    void OnFocusRequested(WorkspaceViewModel wanted) => Dispatcher.UIThread.Post(() => FocusShown(wanted), DispatcherPriority.Background);

    /// <summary>The keyboard follows to the content shown — if that is still what was asked for, and nobody is
    /// typing anywhere.</summary>
    void FocusShown(object wanted)
    {
        if (bound is not { Palette.IsOpen: false } vm || !ReferenceEquals(vm.Selected, wanted)) return;
        if (!KeyboardFocus.IsFree(this, this.FindControl<Border>("TitleBar")!)) return;
        if (this.FindControl<ContentControl>("Host")!.GetVisualDescendants().OfType<WorkspaceView>().FirstOrDefault() is { } view && ReferenceEquals(view.DataContext, wanted)) view.FocusContent();
    }

    /// <summary>Where the keyboard was when the palette opened, and over what.</summary>
    (IInputElement? Focus, object? Shown) before;

    void OnPaletteChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (bound is not { } vm) return;
        if (e.PropertyName == nameof(PaletteViewModel.IsOpen))
        {
            if (vm.Palette.IsOpen)
            {
                before = (FocusManager?.GetFocusedElement(), vm.Selected);
                Dispatcher.UIThread.Post(() => { if (vm.Palette.IsOpen && !KeyboardFocus.IsElsewhere(this)) this.FindControl<TextBox>("PaletteText")!.Focus(); });
            }
            else
            {
                var (back, shown) = before;
                before = default;
                if (vm.Palette.Dismissed) return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (vm.Palette.IsOpen || !KeyboardFocus.IsFree(this, this.FindControl<Panel>("PaletteLayer")!)) return;
                    if (back is Visual { IsEffectivelyVisible: true } visual && TopLevel.GetTopLevel(visual) == this) back.Focus();
                    else if (shown != null) FocusShown(shown);
                });
            }
        }
        else if (e.PropertyName == nameof(PaletteViewModel.Selected)) Dispatcher.UIThread.Post(() => { var list = this.FindControl<ListBox>("PaletteList")!; if (list.SelectedIndex >= 0) list.ScrollIntoView(list.SelectedIndex); });
    }

    void WireInteraction()
    {
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => { if (e.Key == held || e.Key is Key.LWin or Key.RWin or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift) held = Key.None; }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Deactivated += (_, _) => held = Key.None;
        // While the palette is open, nothing that is typed goes anywhere else.
        AddHandler(TextInputEvent, (_, e) => { if (bound?.Palette.IsOpen == true && !InPalette(e.Source)) e.Handled = true; }, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        void Tips(StudioPreferences _)
        {
            static string With(string title, Shortcut s) => s.IsOff ? title : $"{title}（{s.Keys}）";
            ToolTip.SetTip(this.FindControl<Button>("PaletteButton")!, With("命令面板", Shortcuts.Palette));
            ToolTip.SetTip(this.FindControl<Button>("SettingsButton")!, With("系统配置", Shortcuts.Settings));
        }
        Tips(Appearance.Current);
        Appearance.Changed += Tips;
        Closed += (_, _) => Appearance.Changed -= Tips;

        // The middle button closes a tab; the wheel moves along tabs that do not fit.
        var tabs = this.FindControl<ScrollViewer>("TabScroller")!;
        tabs.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(tabs).Properties.IsMiddleButtonPressed) return;
            if ((e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("session-tab"))?.DataContext is WorkspaceViewModel w && w.CloseCommand?.CanExecute(null) == true)
            {
                e.Handled = true;
                w.CloseCommand.Execute(null);
            }
        }, RoutingStrategies.Tunnel);
        tabs.PointerWheelChanged += (_, e) => { tabs.Offset = new Vector(Math.Max(0, tabs.Offset.X - (e.Delta.X + e.Delta.Y) * 40), 0); e.Handled = true; };

        var layer = this.FindControl<Panel>("PaletteLayer")!;
        layer.PointerPressed += (_, e) => { if (ReferenceEquals(e.Source, layer)) bound?.Palette.Close(); };
        this.FindControl<ListBox>("PaletteList")!.Tapped += (_, e) =>
        {
            if ((e.Source as Control)?.DataContext is PaletteEntry entry) bound?.Palette.RunCommand.Execute(entry);
        };
    }

    bool InPalette(object? source) => source is Visual v && this.FindControl<Panel>("PaletteLayer")!.IsVisualAncestorOf(v);

    Key held;
    long heldAt;

    /// <summary>One press of a key that opens or closes something is one action, however long it is held. Should a
    /// key-up get lost, a pause longer than any keyboard waits before it repeats counts as letting go.</summary>
    bool IsRepeat(KeyEventArgs e)
    {
        bool repeat = held == e.Key && Environment.TickCount64 - heldAt < 3000;
        held = e.Key;
        heldAt = Environment.TickCount64;
        return repeat;
    }

    void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (bound is not { } vm) return;
        if (vm.Palette.IsOpen)
        {
            var text = this.FindControl<TextBox>("PaletteText")!;
            // An input method that is composing has the keys first.
            bool composing = e.Key == Key.ImeProcessed || text.FindDescendantPreedit();
            if (composing && InPalette(e.Source)) return;
            if (e.Key == Key.Escape || Shortcuts.Matches(Shortcuts.Palette, e)) vm.Palette.Close();
            else if (e.Key == Key.Down || e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None) vm.Palette.Move(1);
            else if (e.Key == Key.Up || e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Shift) vm.Palette.Move(-1);
            else if (e.Key == Key.Enter) vm.Palette.RunCommand.Execute(null);
            else if (InPalette(e.Source)) return;
            else text.Focus(); // the keyboard had got away from the palette
            e.Handled = true;
            return;
        }
        bool done = true;
        int number = Shortcuts.SessionNumber(e);
        if (number > 0) vm.ShowSession(number);
        else if (Shortcuts.Matches(Shortcuts.Palette, e)) vm.Palette.Open();
        else if (Shortcuts.Matches(Shortcuts.NewTerminal, e)) { if (!IsRepeat(e)) vm.NewTerminal(); }
        else if (Shortcuts.Matches(Shortcuts.CloseTab, e)) { if (!IsRepeat(e)) _ = CloseTabAsync(vm); }
        else if (Shortcuts.Matches(Shortcuts.Home, e)) vm.ShowHomeCommand.Execute(null);
        else if (Shortcuts.Matches(Shortcuts.Settings, e)) vm.OpenSettings("theme");
        else if (Shortcuts.Matches(Shortcuts.NextSession, e)) vm.StepSession(1);
        else if (Shortcuts.Matches(Shortcuts.PreviousSession, e)) vm.StepSession(-1);
        else if (Shortcuts.Matches(Shortcuts.ZoomIn, e)) vm.ZoomTerminal(1);
        else if (Shortcuts.Matches(Shortcuts.ZoomOut, e)) vm.ZoomTerminal(-1);
        else if (Shortcuts.Matches(Shortcuts.ZoomReset, e)) vm.ZoomTerminal(0);
        else done = false;
        if (done) e.Handled = true;
    }

    static async Task CloseTabAsync(MainWindowViewModel vm)
    {
        try { await vm.CloseCurrentTabAsync(); }
        catch (Exception e) { vm.Notice = "关闭失败：" + WorkspaceViewModel.Short(e.Message); }
    }

    double turned;
    long turnedAt;

    /// <summary>Cmd / Ctrl + wheel over a terminal changes the size of its text, as in browsers. A touchpad turns
    /// the wheel by fractions: a whole turn is one step.</summary>
    void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (bound == null || e.Delta.Y == 0 || e.KeyModifiers != (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)) return;
        if ((e.Source as Visual)?.FindAncestorOfType<StudioTerminalControl>(includeSelf: true) == null) return;
        e.Handled = true;
        if (Environment.TickCount64 - turnedAt > 500 || Math.Sign(turned) != Math.Sign(e.Delta.Y)) turned = 0;
        turnedAt = Environment.TickCount64;
        turned += e.Delta.Y;
        if (Math.Abs(turned) < 1) return;
        bound.ZoomTerminal(Math.Sign(turned));
        turned = 0;
    }

    void OnToastEntered(object? sender, PointerEventArgs e) => ((sender as Control)?.DataContext as ToastItem)?.Hold(true);
    void OnToastExited(object? sender, PointerEventArgs e) => ((sender as Control)?.DataContext as ToastItem)?.Hold(false);

    static bool IsInsideButton(Control control)
    {
        for (Control? c = control; c != null; c = c.Parent as Control)
            if (c is Button) return true;
        return false;
    }
}
