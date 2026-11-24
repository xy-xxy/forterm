using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Forterm.Desktop;

/// <summary>Applies the same compact, softly rising popup motion to every
/// application ComboBox. The popup remains a normal Avalonia popup, so keyboard
/// navigation, light dismiss and automation keep their stock behavior.</summary>
public static class DropDownMotion
{
    static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(150);

    public static void Attach(ComboBox combo)
    {
        if (combo.Classes.Contains("forterm-dropdown")) return;
        combo.Classes.Add("forterm-dropdown");
        combo.DropDownOpened += (_, _) => AnimatePopup(combo);
    }

    static void AnimatePopup(ComboBox combo)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var popup = combo.GetVisualDescendants().OfType<Popup>().FirstOrDefault();
            if (popup == null) return;

            popup.Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = Duration, Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
                new DoubleTransition { Property = Popup.VerticalOffsetProperty, Duration = Duration, Easing = new Avalonia.Animation.Easings.CubicEaseOut() }
            };
            popup.Opacity = 0;
            popup.VerticalOffset = -6;
            Dispatcher.UIThread.Post(() =>
            {
                if (!popup.IsOpen) return;
                popup.Opacity = 1;
                popup.VerticalOffset = 0;
            }, DispatcherPriority.Render);
        }, DispatcherPriority.Loaded);
    }
}
