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
    static long until;
    static int registered;

    /// <summary>Something that had a × is gone: the others have moved.</summary>
    public static void Moved() => Volatile.Write(ref until, Environment.TickCount64 + (long)Pause.TotalMilliseconds);
    internal static void ForgetForTest() => Volatile.Write(ref until, 0);

    public static void Register()
    {
        if (Interlocked.Exchange(ref registered, 1) != 0) return;
        // Before the button, and instead of it.
        InputElement.PointerPressedEvent.AddClassHandler<Button>((button, e) =>
        {
            if ((button.Classes.Contains("chip-close") || button.Classes.Contains("collapse")) && (e.ClickCount > 1 || Environment.TickCount64 < Volatile.Read(ref until))) e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }
}
