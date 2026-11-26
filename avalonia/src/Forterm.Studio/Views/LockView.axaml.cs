using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Forterm.Studio.Services;

namespace Forterm.Studio.Views;

/// <summary>The main window's lock screen (<see cref="AppLock"/>): over everything, with the password box. Whatever had the
/// keyboard before gets it back once unlocked.</summary>
public sealed partial class LockView : UserControl
{
    readonly TextBox password;
    readonly TextBlock problem;
    readonly Button quit;
    readonly StackPanel forgot;
    IInputElement? before;
    bool trying, quitAsked;
    long quitAskedAt;

    public LockView()
    {
        AvaloniaXamlLoader.Load(this);
        password = this.FindControl<TextBox>("PasswordBox")!;
        problem = this.FindControl<TextBlock>("ProblemText")!;
        quit = this.FindControl<Button>("QuitButton")!;
        forgot = this.FindControl<StackPanel>("ForgotPanel")!;
        this.FindControl<Button>("UnlockButton")!.Click += (_, _) => _ = UnlockAsync();
        this.FindControl<Button>("ForgotButton")!.Click += (_, _) => forgot.IsVisible = !forgot.IsVisible;
        quit.Click += (_, _) =>
        {
            // Asked again, and not by the second click of a double click.
            if (!quitAsked) { quitAsked = true; quitAskedAt = Environment.TickCount64; quit.Content = "再点一次：确认退出，所有会话都会断开"; return; }
            if (Environment.TickCount64 - quitAskedAt < 700) return;
            AppLock.Quit();
        };
        password.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = UnlockAsync(); } };
    }

    Window? window;

    /// <summary>Its window is locked: shown, and the keyboard comes here when this window is the one in front. What had
    /// the keyboard before is remembered.</summary>
    public void Begin()
    {
        if (IsVisible) return;
        before = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        Say("");
        password.Text = "";
        forgot.IsVisible = false;
        quitAsked = false;
        quit.Content = "退出 Forterm";
        IsVisible = true;
        window = TopLevel.GetTopLevel(this) as Window;
        if (window != null) window.Activated += OnActivated;
        Dispatcher.UIThread.Post(() => { if (AppLock.IsLocked && TakesKeys()) password.Focus(); }, DispatcherPriority.Input);
    }

    /// <summary>Unlocked: hidden, and the keyboard goes back where it was, in the window that is in front.</summary>
    public void End()
    {
        if (window != null) window.Activated -= OnActivated;
        bool keys = TakesKeys();
        window = null;
        IsVisible = false;
        password.Text = "";
        if (keys && before is Visual { IsEffectivelyVisible: true } visual && TopLevel.GetTopLevel(visual) != null && before is InputElement element) element.Focus();
        before = null;
    }

    /// <summary>This window is in front; or none of Studio's is, and this is the main one.</summary>
    bool TakesKeys() => TopLevel.GetTopLevel(this) is Window w && (w.IsActive || w is MainWindow && !AppLock.AnyActive());

    /// <summary>The user went to this window while locked: the keys are for its password box.</summary>
    void OnActivated(object? sender, EventArgs e) { if (AppLock.IsLocked && IsVisible) password.Focus(); }

    void Say(string text) { problem.Text = text; problem.IsVisible = text.Length > 0; }

    /// <summary>Also what the window says when it is to be closed while locked.</summary>
    public void Tell(string text) => Say(text);

    async Task UnlockAsync()
    {
        if (trying) return;
        trying = true;
        try
        {
            string typed = password.Text ?? "";
            password.Text = "";
            var (unlocked, say) = await AppLock.UnlockAsync(typed);
            if (!unlocked) { Say(say); password.Focus(); }
        }
        finally { trying = false; }
    }
}
