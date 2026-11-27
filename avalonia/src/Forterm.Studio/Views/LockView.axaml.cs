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
    readonly Border waiting;
    readonly TextBlock waitingText;
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
        waiting = this.FindControl<Border>("WaitingNote")!;
        waitingText = this.FindControl<TextBlock>("WaitingText")!;
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
        AppLock.QuestionsChanged += ShowWaiting;
        ShowWaiting();
        window = TopLevel.GetTopLevel(this) as Window;
        if (window != null) window.Activated += OnActivated;
        Dispatcher.UIThread.Post(() => { if (AppLock.IsLocked && TakesKeys()) password.Focus(); }, DispatcherPriority.Input);
    }

    /// <summary>Unlocked: hidden, and the keyboard goes back where it was, in the window that is in front. Also when its
    /// window closes (<paramref name="closing"/>: the keyboard stays where it is).</summary>
    public void End(bool closing = false)
    {
        if (window != null) window.Activated -= OnActivated;
        AppLock.QuestionsChanged -= ShowWaiting;
        bool keys = !closing && TakesKeys();
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

    /// <summary>Connections that wait for an answer (they were opened while locked): said, so the user unlocks.</summary>
    void ShowWaiting()
    {
        int n = AppLock.Questions;
        waiting.IsVisible = n > 0;
        waitingText.Text = n > 0 ? $"有 {n} 个问题在等你回答（主机指纹、登录或 sudo 密码），解锁后回答。连接登录时，等你回答的时间不算连接超时；服务器自己的登录时限，以及启动隧道（2 分钟）和 sudo 的时限仍然有效。" : "";
    }

    /// <summary>Subscribed to the questions now (for tests: a closed window must have let go).</summary>
    internal static int Listening => AppLock.QuestionsListeners;

    internal string WaitingSaid => waiting.IsVisible ? waitingText.Text ?? "" : "";

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
