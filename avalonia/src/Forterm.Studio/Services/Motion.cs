using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;

namespace Forterm.Studio.Services;

/// <summary>"界面动画": Themes/Motion.axaml as a whole, on or off, and the few effects that are started by code.
/// Nothing here may change what an action does, when it happens or the size of a terminal.</summary>
public static class Motion
{
    static StyleInclude? styles;
    public static bool Enabled => styles != null;

    /// <summary>Follows the setting from now on.</summary>
    public static void Follow(Application app)
    {
        Apply(app, Appearance.Current.Motion);
        Appearance.Changed += p => Apply(app, p.Motion);
    }

    public static void Apply(Application app, bool on)
    {
        if (on == Enabled) return;
        if (on)
        {
            styles = new StyleInclude(new Uri("avares://Forterm/")) { Source = new Uri("avares://Forterm/Themes/Motion.axaml") };
            app.Styles.Add(styles);
        }
        else
        {
            app.Styles.Remove(styles!);
            styles = null;
        }
    }

    static Animation Build(TimeSpan duration, double opacity, double y) => new()
    {
        Duration = duration,
        Easing = new CubicEaseOut(),
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, opacity), new Setter(TranslateTransform.YProperty, y) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } },
        },
    };

    /// <summary>Content that has just been put in place of other content comes in.</summary>
    public static void Enter(Control control, double y = 6) { if (Enabled) _ = Build(TimeSpan.FromMilliseconds(180), 0, y).RunAsync(control); }

    /// <summary>A dialog's content, when it opens.</summary>
    public static void Open(Window dialog) { if (dialog.Content is Control content) Enter(content, 10); }

    /// <summary>Draws the eye to text that changed.</summary>
    public static void Flash(Control control) { if (Enabled) _ = Build(TimeSpan.FromMilliseconds(260), 0.35, 0).RunAsync(control); }
}
