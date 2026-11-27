using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Forterm.Studio.Services;

/// <summary>A × goes away with what it closes, and the × of the next tab or column takes its place under the pointer. The
/// second click of a double click, or one that was meant for what has just gone, must not close that one as well: a
/// session the controller opened cannot be opened again. Each window keeps its own count: what moved in one window has
/// not moved in another.</summary>
public static class CloseGuard
{
    /// <summary>How long a × that has just moved does not react.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(500);
    /// <summary>A double click on a × is one click: for so long after one that acted, further clicks of it do not.</summary>
    public static readonly TimeSpan Double = TimeSpan.FromMilliseconds(1500);

    sealed class State { public long Until, Acted = long.MinValue / 2; }
    static readonly ConditionalWeakTable<TopLevel, State> windows = new();
    /// <summary>Where no window is known (nothing has been clicked or typed yet): every window.</summary>
    static readonly State anywhere = new();
    static readonly WeakReference<TopLevel?> lastInput = new(null);
    static int registered;

    static State For(TopLevel? window) => window == null ? anywhere : windows.GetValue(window, _ => new State());

    /// <summary>The window the user last clicked or typed in: what goes away now went away there.</summary>
    static TopLevel? LastWindow => lastInput.TryGetTarget(out var window) ? window : null;

    /// <summary>Something that had a × is gone: the others have moved, in the window the user acted in. Where the window
    /// is known, <see cref="MovedIn"/>.</summary>
    public static void Moved() => Moved(Environment.TickCount64);
    /// <summary>Something that had a × is gone from <paramref name="window"/>: its × have moved, whichever window the user
    /// is in. Unknown (null): the window the user acted in.</summary>
    public static void MovedIn(TopLevel? window) { if (window == null) Moved(); else Moved(window, Environment.TickCount64); }
    internal static void Moved(long now) => Moved(LastWindow, now);
    internal static void Moved(TopLevel? window, long now) => Volatile.Write(ref For(window).Until, now + (long)Pause.TotalMilliseconds);
    internal static void EndPauseForTest() { Volatile.Write(ref anywhere.Until, 0); foreach (var (_, state) in windows) Volatile.Write(ref state.Until, 0); }
    internal static void ForgetForTest()
    {
        windows.Clear();
        Volatile.Write(ref anywhere.Until, 0);
        Volatile.Write(ref anywhere.Acted, long.MinValue / 2);
        lastInput.SetTarget(null);
    }

    /// <summary>A press that would close something (a ×, or the middle button on a tab): whether it must be left alone. The
    /// second click of a double click is, when the first one acted; a click that was itself left alone does not make the
    /// next one a "second".</summary>
    public static bool Blocks(PointerPressedEventArgs e) => Blocks(TopLevel.GetTopLevel(e.Source as Visual), e.ClickCount, Environment.TickCount64);
    internal static bool Blocks(int clicks, long now) => Blocks(null, clicks, now);
    internal static bool Blocks(TopLevel? window, int clicks, long now)
    {
        var state = For(window);
        bool blocked = now < Volatile.Read(ref state.Until) || now < Volatile.Read(ref anywhere.Until)
            || clicks > 1 && now - Volatile.Read(ref state.Acted) < (long)Double.TotalMilliseconds;
        if (!blocked) Volatile.Write(ref state.Acted, now);
        return blocked;
    }

    public static void Register()
    {
        if (Interlocked.Exchange(ref registered, 1) != 0) return;
        // Before the button, and instead of it.
        InputElement.PointerPressedEvent.AddClassHandler<Button>((button, e) =>
        {
            if ((button.Classes.Contains("chip-close") || button.Classes.Contains("collapse")) && e.GetCurrentPoint(button).Properties.IsLeftButtonPressed && Blocks(e)) e.Handled = true;
        }, RoutingStrategies.Tunnel);
        // The window comes first on the way in: it is where the user is.
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((window, _) => lastInput.SetTarget(window), RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>((window, _) => lastInput.SetTarget(window), RoutingStrategies.Tunnel, handledEventsToo: true);
    }
}
