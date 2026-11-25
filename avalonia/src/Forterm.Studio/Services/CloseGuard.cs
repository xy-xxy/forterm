using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Forterm.Studio.Services;

/// <summary>A × goes away with what it closes, and the × of the next tab or column takes its place under the pointer. The
/// second click of a double click, or one that was meant for what has just gone, must not close that one as well: a
/// session the controller opened cannot be opened again.</summary>
public static class CloseGuard
{
    /// <summary>How long a × that has just moved does not react.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(500);
    /// <summary>A double click on a × is one click: for so long after one that acted, further clicks of it do not.</summary>
    public static readonly TimeSpan Double = TimeSpan.FromMilliseconds(1500);
    static long until, acted = long.MinValue / 2;
    static int registered;

    /// <summary>Something that had a × is gone: the others have moved.</summary>
    public static void Moved() => Moved(Environment.TickCount64);
    internal static void Moved(long now) => Volatile.Write(ref until, now + (long)Pause.TotalMilliseconds);
    internal static void EndPauseForTest() => Volatile.Write(ref until, 0);
    internal static void ForgetForTest() { Volatile.Write(ref until, 0); Volatile.Write(ref acted, long.MinValue / 2); }

    /// <summary>A press that would close something (a ×, or the middle button on a tab): whether it must be left alone. The
    /// second click of a double click is, when the first one acted; a click that was itself left alone does not make the
    /// next one a "second".</summary>
    public static bool Blocks(PointerPressedEventArgs e) => Blocks(e.ClickCount, Environment.TickCount64);
    internal static bool Blocks(int clicks, long now)
    {
        bool blocked = now < Volatile.Read(ref until) || clicks > 1 && now - Volatile.Read(ref acted) < (long)Double.TotalMilliseconds;
        if (!blocked) Volatile.Write(ref acted, now);
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
    }
}
