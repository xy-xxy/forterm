using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Studio.Controls;

namespace Forterm.Studio.Services;

/// <summary>"标签渲染": a terminal begins to take in its shell's output when it is first laid out. One that has not been
/// shown yet (another tab or session in front) leaves the output waiting — at most about 2 MiB, after which the server
/// is held back. With "StartHiddenTerminals" such a terminal is laid out for a moment in a place nobody sees, at the size
/// terminals have here, and from then on takes in output like one that has been shown.</summary>
public static class TerminalWarmUp
{
    /// <summary>The hidden places of the main windows, newest last.</summary>
    static readonly List<WeakReference<Canvas>> hosts = new();
    public const string HostName = "WarmUpHost";
    /// <summary>The size the last terminal on screen had: a terminal laid out here gets the same.</summary>
    static Size size = new(960, 540);
    /// <summary>How long a terminal may still be shown in the usual way before it is laid out here.</summary>
    internal static TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(1);
    /// <summary>How long it stays in the hidden place: one layout is enough.</summary>
    internal static TimeSpan Hold { get; set; } = TimeSpan.FromMilliseconds(200);

    public static void Use(Canvas place) => hosts.Add(new(place));

    /// <summary>Its window closes: what is laid out there is let go, and the place is not used again.</summary>
    public static void Forget(Canvas place)
    {
        foreach (var terminal in place.Children.OfType<StudioTerminalControl>().ToArray()) Release(terminal);
        hosts.RemoveAll(h => !h.TryGetTarget(out var p) || ReferenceEquals(p, place));
    }

    /// <summary>The newest main window's place.</summary>
    static Canvas? Host()
    {
        hosts.RemoveAll(h => !h.TryGetTarget(out _));
        return hosts.Count > 0 && hosts[^1].TryGetTarget(out var place) ? place : null;
    }

    /// <summary>A terminal on screen has this size.</summary>
    public static void Seen(Size shown) { if (shown.Width >= 100 && shown.Height >= 60) size = shown; }

    /// <summary>A terminal has a shell and no layout yet: after a moment, unless it has been shown meanwhile, it is laid
    /// out here.</summary>
    public static void Schedule(StudioTerminalControl terminal)
    {
        if (!Appearance.Current.StartHiddenTerminals) return;
        DispatcherTimer.RunOnce(() => Start(terminal), Delay, DispatcherPriority.Background);
    }

    static void Start(StudioTerminalControl terminal)
    {
        if (!terminal.HasPendingConnection || terminal.IsDisposed || terminal.Parent != null || terminal.GetVisualParent() != null) return;
        // Locked, nothing is laid out (the windows' content is hidden): later.
        if (AppLock.IsLocked) { Schedule(terminal); return; }
        if (Host() is not { } place) return;
        terminal.Width = size.Width;
        terminal.Height = size.Height;
        place.Children.Add(terminal);
        // Laid out and attached: it goes on taking in output where it is put next, or nowhere.
        DispatcherTimer.RunOnce(() => Release(terminal), Hold, DispatcherPriority.Background);
    }

    /// <summary>Out of the hidden place, before it is shown anywhere else (a control has one parent).</summary>
    public static void Release(Control? control)
    {
        // Whichever window's place it is in.
        if (control is not StudioTerminalControl terminal || terminal.Parent is not Canvas { Name: HostName } place) return;
        place.Children.Remove(terminal);
        terminal.ClearValue(Avalonia.Layout.Layoutable.WidthProperty);
        terminal.ClearValue(Avalonia.Layout.Layoutable.HeightProperty);
    }
}
