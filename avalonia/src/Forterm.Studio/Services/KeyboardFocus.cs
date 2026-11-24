using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Forterm.Studio.Services;

/// <summary>Moving the keyboard by code. Avalonia has one keyboard focus for the whole process: whatever is given the
/// focus gets the keys, also those being typed into a dialog or another window. So the keyboard is only taken from
/// nowhere, or from the parts of the window the caller names — never from where somebody may be typing.</summary>
public static class KeyboardFocus
{
    /// <summary>What has the keyboard and can be seen; in any window.</summary>
    static Visual? Now(TopLevel top) => top.FocusManager?.GetFocusedElement() is Visual { IsEffectivelyVisible: true } now && TopLevel.GetTopLevel(now) != null ? now : null;

    /// <summary>The user is in another window (a dialog, the settings), or not in the program at all.</summary>
    public static bool IsElsewhere(Visual from) =>
        TopLevel.GetTopLevel(from) is not Window { IsActive: true } window || Now(window) is { } now && TopLevel.GetTopLevel(now) is Window other && !ReferenceEquals(other, window);

    /// <summary>Nothing has the keyboard, or it is in one of these parts.</summary>
    public static bool IsFree(Visual from, params Visual[] parts) =>
        !IsElsewhere(from) && (Now(TopLevel.GetTopLevel(from)!) is not { } now || parts.Any(p => ReferenceEquals(p, now) || p.IsVisualAncestorOf(now)));

    /// <summary>The user is typing into a field of the window (find, a filter of the page, an editor). A field in a
    /// list that has been opened does not count: it goes away with the choice.</summary>
    public static bool IsInField(Visual from) => TopLevel.GetTopLevel(from) is { } top && Now(top) is { } now
        && (now is TextBox || now.FindAncestorOfType<AvaloniaEdit.Editing.TextArea>(includeSelf: true) != null) && now.FindAncestorOfType<FlyoutPresenter>() == null;

    /// <summary>An input method is composing in this box: the keys are its own.</summary>
    public static bool FindDescendantPreedit(this TextBox box) => box.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().FirstOrDefault()?.PreeditText is { Length: > 0 };
}
