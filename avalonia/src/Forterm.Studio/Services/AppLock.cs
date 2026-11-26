using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Studio.Library;

namespace Forterm.Studio.Services;

/// <summary>The check for the lock password: PBKDF2-SHA256 with a random salt and many rounds, so that what is stored
/// does not easily give the password back.</summary>
public static class LockPassword
{
    public const int Rounds = 300_000;

    public static string Make(string password) => Make(password, RandomNumberGenerator.GetBytes(16), Rounds);

    static string Make(string password, byte[] salt, int rounds)
    {
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, rounds, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${rounds}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Whether a text is a check this program could have made.</summary>
    public static bool IsCheck(string? check) => Parts(check) != null;

    static (int Rounds, byte[] Salt, byte[] Hash)? Parts(string? check)
    {
        if (check is not { Length: <= 256 }) return null;
        string[] parts = check.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out int rounds) || rounds is < 100_000 or > 10_000_000) return null;
        try
        {
            byte[] salt = Convert.FromBase64String(parts[2]), hash = Convert.FromBase64String(parts[3]);
            return salt.Length >= 16 && hash.Length == 32 ? (rounds, salt, hash) : null;
        }
        catch (FormatException) { return null; }
    }

    public static bool Check(string password, string check)
    {
        if (Parts(check) is not { } p) return false;
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), p.Salt, p.Rounds, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(hash, p.Hash);
    }
}

/// <summary>"锁屏": covers every window of Studio until the lock password is given. Sessions go on as they are (protocol
/// heartbeats, "空闲保活", transfers); nothing of them can be seen, and no key or click reaches them. The password is
/// kept only as a check in the system's credential store; forgotten, the program can only be ended.</summary>
public static class AppLock
{
    public const int MinLength = 4;
    /// <summary>Wrong passwords in a row before each further try waits, longer every time.</summary>
    public const int FreeTries = 5;

    static CredentialStore? store;
    static string? check;
    static bool known, locked;
    static int failures, registered;
    static long resumeAt, lastInput;
    static DispatcherTimer? timer;

    /// <summary>The clock of waiting and of being away; replaced by tests.</summary>
    internal static TimeProvider Time { get; set; } = TimeProvider.System;
    static long Now => (long)Time.GetElapsedTime(0).TotalMilliseconds;

    public static bool IsLocked => locked;
    /// <summary>Counts every time it locks: what a picker of the system returns after a lock is not used.</summary>
    public static int Locks { get; private set; }
    /// <summary>Locked, or unlocked. On the UI thread.</summary>
    public static event Action? Changed;
    /// <summary>The user ends the program from the lock screen (the password is forgotten).</summary>
    public static event Action? QuitRequested;

    /// <summary>Where the check is kept. The latest store is the one (there is one per program).</summary>
    public static void Use(CredentialStore credentials)
    {
        if (ReferenceEquals(store, credentials)) return;
        store = credentials;
        known = false;
        check = null;
    }

    internal static void ForgetForTest()
    {
        store = null; check = null; known = locked = false; failures = 0; resumeAt = 0; lastInput = Now;
        Time = TimeProvider.System;
        lastInput = Now;
        Changed?.Invoke();
    }

    static async Task KnowAsync()
    {
        if (known) return;
        if (store == null) throw new IOException("没有系统凭据存储");
        string? read = await store.LockVerifierAsync(CancellationToken.None);
        check = LockPassword.IsCheck(read) ? read : null;
        known = true;
    }

    /// <summary>Whether a password is set; throws when the credential store cannot be read.</summary>
    public static async Task<bool> HasPasswordAsync()
    {
        await KnowAsync();
        return check != null;
    }

    /// <summary>Locks now. Null when it is locked; otherwise why not.</summary>
    public static async Task<string?> LockAsync()
    {
        if (locked) return null;
        try { await KnowAsync(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
        {
            return "没有锁定：读不到锁屏密码（" + e.Message + "）";
        }
        if (check == null) return "没有锁定：还没有设置锁屏密码（系统配置 → 锁屏）";
        if (locked) return null;
        locked = true;
        Locks++;
        Changed?.Invoke();
        return null;
    }

    /// <summary>"启动时锁定": locked at once, before anything is shown; unlocked again when no password turns out to be
    /// set.</summary>
    public static void LockAtStart()
    {
        if (locked) return;
        locked = true;
        Locks++;
        Changed?.Invoke();
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            bool none;
            try { none = !await HasPasswordAsync(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException) { none = false; }
            if (none && locked) { locked = false; Changed?.Invoke(); }
        });
    }

    /// <summary>Unlocks with the password. ("", true) when unlocked; otherwise what to say.</summary>
    public static async Task<(bool Unlocked, string Say)> UnlockAsync(string password)
    {
        if (!locked) return (true, "");
        long now = Now;
        if (now < resumeAt) return (false, $"请 {(resumeAt - now + 999) / 1000} 秒后再试");
        try { await KnowAsync(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
        {
            return (false, "读不到锁屏密码（" + e.Message + "），请稍后再试");
        }
        if (check is not { } against) { Release(); return (true, ""); }
        bool right = await Task.Run(() => LockPassword.Check(password, against));
        if (!locked) return (true, "");
        if (right)
        {
            failures = 0;
            resumeAt = 0;
            Release();
            return (true, "");
        }
        failures++;
        if (failures < FreeTries) return (false, "密码不对");
        long wait = Math.Min(300_000, 30_000L << Math.Min(failures - FreeTries, 4));
        resumeAt = Now + wait;
        return (false, $"密码不对，已连续 {failures} 次；请 {wait / 1000} 秒后再试");
    }

    static void Release()
    {
        locked = false;
        lastInput = Now;
        Changed?.Invoke();
    }

    /// <summary>Sets or changes the password. Null when done; otherwise why not.</summary>
    public static async Task<string?> SetPasswordAsync(string current, string next)
    {
        if (store == null) return "没有系统凭据存储";
        if (next.Length < MinLength) return $"密码至少 {MinLength} 个字符";
        if (next.Length > 256) return "密码太长";
        try
        {
            await KnowAsync();
            if (check is { } old && !await Task.Run(() => LockPassword.Check(current, old))) return "现在的密码不对";
            string made = await Task.Run(() => LockPassword.Make(next));
            await store.SaveLockVerifierAsync(made, CancellationToken.None);
            check = made;
            known = true;
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
        {
            return "没有保存：" + e.Message;
        }
    }

    /// <summary>Removes the password, which ends locking. Null when done; otherwise why not.</summary>
    public static async Task<string?> RemovePasswordAsync(string current)
    {
        if (store == null) return "没有系统凭据存储";
        try
        {
            await KnowAsync();
            if (check is not { } old) return null;
            if (!await Task.Run(() => LockPassword.Check(current, old))) return "现在的密码不对";
            await store.ForgetLockVerifierAsync(CancellationToken.None);
            check = null;
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
        {
            return "没有移除：" + e.Message;
        }
    }

    public static void Quit() => QuitRequested?.Invoke();

    // ---------- the windows ----------

    /// <summary>Inside the lock screen of the main window, where the password is typed.</summary>
    static bool IsInLock(object? source) => (source as Visual)?.FindAncestorOfType<Views.LockView>(includeSelf: true) != null;
    /// <summary>The window's own buttons that the system or Avalonia draws (minimise, maximise, close): they still work;
    /// closing the main window is refused while locked.</summary>
    static bool IsWindowButton(object? source) => (source as Visual)?.FindAncestorOfType<Avalonia.Controls.Chrome.WindowDrawnDecorations>(includeSelf: true) != null;

    /// <summary>Keys and clicks are the user's, and keep Studio from locking; while it is locked they reach nothing but the
    /// lock screen. Every window that is open or opens later is covered.</summary>
    public static void Register()
    {
        if (Interlocked.Exchange(ref registered, 1) != 0) return;
        lastInput = Now;
        void Present(TopLevel _, RoutedEventArgs e) { if (!locked) lastInput = Now; }
        void Stop(TopLevel _, RoutedEventArgs e) { if (locked && !IsInLock(e.Source)) e.Handled = true; }
        void StopPointer(TopLevel _, RoutedEventArgs e) { if (locked && !IsInLock(e.Source) && !IsWindowButton(e.Source)) e.Handled = true; }
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>(Present, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(Present, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>(Present, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>(Present, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>(Stop, RoutingStrategies.Tunnel);
        InputElement.KeyUpEvent.AddClassHandler<TopLevel>(Stop, RoutingStrategies.Tunnel);
        InputElement.TextInputEvent.AddClassHandler<TopLevel>(Stop, RoutingStrategies.Tunnel);
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(StopPointer, RoutingStrategies.Tunnel);
        InputElement.PointerReleasedEvent.AddClassHandler<TopLevel>(StopPointer, RoutingStrategies.Tunnel);
        InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>(Stop, RoutingStrategies.Tunnel);
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            lock (open) { open.RemoveAll(w => !w.TryGetTarget(out var gone)); open.Add(new(window)); }
            if (!locked) return;
            // A window that opens while locked (a question of a launch, say): covered at once where it can be, and as soon
            // as its layers are there.
            Cover(window);
            Dispatcher.UIThread.Post(() => { if (locked) Cover(window); }, DispatcherPriority.Loaded);
        });
        Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => { lock (open) open.RemoveAll(w => !w.TryGetTarget(out var x) || ReferenceEquals(x, window)); });
        Changed += () =>
        {
            foreach (var window in Windows())
            {
                if (!locked) { Uncover(window); continue; }
                Close(window);
                Cover(window);
            }
        };
        timer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => Tick());
        timer.Start();
    }

    /// <summary>Locks by itself after so long without the user.</summary>
    internal static void Tick()
    {
        int minutes = Appearance.Current.LockMinutes;
        if (locked || minutes <= 0 || Now - lastInput < minutes * 60_000L) return;
        lastInput = Now; // one try per period: no password, or one that cannot be read, is not asked for again at once
        _ = LockAsync();
    }

    /// <summary>The windows opened since this was registered.</summary>
    static readonly List<WeakReference<Window>> open = new();
    static IEnumerable<Window> Windows()
    {
        lock (open) return open.Select(w => w.TryGetTarget(out var window) ? window : null).OfType<Window>().ToArray();
    }

    /// <summary>A window of Studio is in front: its lock screen is the one that takes the keyboard.</summary>
    internal static bool AnyActive() => Windows().Any(w => w.IsActive);

    /// <summary>Menus, lists that are open, tool tips and flyouts close: they would stay drawn over the lock screen.</summary>
    static void Close(Window window)
    {
        foreach (var visual in window.GetVisualDescendants().ToArray())
        {
            if (visual is Popup { IsOpen: true } popup) popup.IsOpen = false;
            if (visual is ComboBox { IsDropDownOpen: true } list) list.IsDropDownOpen = false;
            if (visual is not Control control) continue;
            if (control.ContextMenu is { IsOpen: true } menu) menu.Close();
            if (control.ContextFlyout is { IsOpen: true } context) context.Hide();
            if (control is Button { Flyout: { IsOpen: true } flyout }) flyout.Hide();
            if (FlyoutBase.GetAttachedFlyout(control) is { IsOpen: true } attached) attached.Hide();
            if (ToolTip.GetIsOpen(control)) ToolTip.SetIsOpen(control, false);
        }
    }

    /// <summary>The main window has its lock screen in it; every other window (a dialog that was open, say) gets one in its
    /// top layer, because a dialog that waits for an answer keeps the main window from taking any keys. What is under a
    /// lock screen is hidden, not only covered: a screen reader must not read it, nor press its buttons.</summary>
    static void Cover(Window window)
    {
        if (window is Views.MainWindow || OverlayLayer.GetOverlayLayer(window) is not { } layer) return;
        if (layer.Children.OfType<LockCover>().Any()) return;
        var cover = new LockCover(window);
        layer.Children.Add(cover);
        cover.Begin();
    }

    static void Uncover(Window window)
    {
        if (OverlayLayer.GetOverlayLayer(window) is not { } layer) return;
        foreach (var cover in layer.Children.OfType<LockCover>().ToArray()) { cover.End(); layer.Children.Remove(cover); }
    }
}

/// <summary>The lock screen of a window other than the main one.</summary>
public sealed class LockCover : Border
{
    readonly Window window;
    readonly Views.LockView screen = new();
    bool hid;

    public LockCover(Window window)
    {
        this.window = window;
        Child = screen;
        DragDrop.SetAllowDrop(this, false);
        Fit();
        window.PropertyChanged += OnWindowChanged;
    }

    void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e) { if (e.Property == TopLevel.ClientSizeProperty) Fit(); }

    void Fit() { Width = window.ClientSize.Width; Height = window.ClientSize.Height; }

    internal void Begin()
    {
        screen.Begin();
        if (window.Content is Control { IsVisible: true } content) { content.SetCurrentValue(IsVisibleProperty, false); hid = true; }
    }

    internal void End()
    {
        window.PropertyChanged -= OnWindowChanged;
        if (hid && window.Content is Control content) content.SetCurrentValue(IsVisibleProperty, true);
        hid = false;
        screen.End();
    }
}
