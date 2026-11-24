using Avalonia.Media;
using XTerm.Options;

namespace Forterm.Studio.Services;

/// <summary>A terminal colour scheme ("终端配色"). <see cref="Dark"/> only describes the scheme itself;
/// a fixed scheme keeps its colours whatever the application theme is.</summary>
public sealed record TerminalScheme(string Name, bool Dark, Func<ThemeOptions> Palette)
{
    public string Background => Palette().Background ?? "#000000";
}

/// <summary>"跟随主题" switches between the Studio dark and light palettes with the application theme;
/// the others are well-known open colour schemes with their published values.</summary>
public static class TerminalSchemes
{
    public const string Auto = "跟随主题";

    public static ThemeOptions StudioDark() => new()
    {
        Background = "#00161C", Foreground = "#D7DEE0", Cursor = "#3FB176", CursorAccent = "#00161C", Selection = "#2E5A4A",
        Black = "#1B2B30", Red = "#E5534B", Green = "#4EC27F", Yellow = "#E2B93D", Blue = "#4D9DE0", Magenta = "#C678DD", Cyan = "#3FB6C6", White = "#D7DEE0",
        BrightBlack = "#5C6E73", BrightRed = "#FF6B63", BrightGreen = "#6FDC98", BrightYellow = "#F5D06A", BrightBlue = "#6FB7F2", BrightMagenta = "#D99BEA", BrightCyan = "#6ED3DE", BrightWhite = "#FFFFFF",
    };

    public static ThemeOptions StudioLight() => new()
    {
        Background = "#FBFBFB", Foreground = "#24292F", Cursor = "#2FA765", CursorAccent = "#FBFBFB", Selection = "#BFE3CF",
        Black = "#24292F", Red = "#CF222E", Green = "#1A7F37", Yellow = "#9A6700", Blue = "#0969DA", Magenta = "#8250DF", Cyan = "#1B7C83", White = "#6E7781",
        BrightBlack = "#57606A", BrightRed = "#A40E26", BrightGreen = "#2DA44E", BrightYellow = "#BF8700", BrightBlue = "#218BFF", BrightMagenta = "#A475F9", BrightCyan = "#3192AA", BrightWhite = "#8C959F",
    };

    static ThemeOptions Solarized(bool dark) => new()
    {
        Background = dark ? "#002B36" : "#FDF6E3", Foreground = dark ? "#839496" : "#657B83",
        Cursor = dark ? "#93A1A1" : "#586E75", CursorAccent = dark ? "#002B36" : "#FDF6E3", Selection = dark ? "#073642" : "#EEE8D5",
        Black = "#073642", Red = "#DC322F", Green = "#859900", Yellow = "#B58900", Blue = "#268BD2", Magenta = "#D33682", Cyan = "#2AA198", White = "#EEE8D5",
        BrightBlack = "#002B36", BrightRed = "#CB4B16", BrightGreen = "#586E75", BrightYellow = "#657B83", BrightBlue = "#839496", BrightMagenta = "#6C71C4", BrightCyan = "#93A1A1", BrightWhite = "#FDF6E3",
    };

    static ThemeOptions Dracula() => new()
    {
        Background = "#282A36", Foreground = "#F8F8F2", Cursor = "#F8F8F2", CursorAccent = "#282A36", Selection = "#44475A",
        Black = "#21222C", Red = "#FF5555", Green = "#50FA7B", Yellow = "#F1FA8C", Blue = "#BD93F9", Magenta = "#FF79C6", Cyan = "#8BE9FD", White = "#F8F8F2",
        BrightBlack = "#6272A4", BrightRed = "#FF6E6E", BrightGreen = "#69FF94", BrightYellow = "#FFFFA5", BrightBlue = "#D6ACFF", BrightMagenta = "#FF92DF", BrightCyan = "#A4FFFF", BrightWhite = "#FFFFFF",
    };

    static ThemeOptions Nord() => new()
    {
        Background = "#2E3440", Foreground = "#D8DEE9", Cursor = "#D8DEE9", CursorAccent = "#2E3440", Selection = "#434C5E",
        Black = "#3B4252", Red = "#BF616A", Green = "#A3BE8C", Yellow = "#EBCB8B", Blue = "#81A1C1", Magenta = "#B48EAD", Cyan = "#88C0D0", White = "#E5E9F0",
        BrightBlack = "#4C566A", BrightRed = "#BF616A", BrightGreen = "#A3BE8C", BrightYellow = "#EBCB8B", BrightBlue = "#81A1C1", BrightMagenta = "#B48EAD", BrightCyan = "#8FBCBB", BrightWhite = "#ECEFF4",
    };

    static ThemeOptions Gruvbox() => new()
    {
        Background = "#282828", Foreground = "#EBDBB2", Cursor = "#EBDBB2", CursorAccent = "#282828", Selection = "#504945",
        Black = "#282828", Red = "#CC241D", Green = "#98971A", Yellow = "#D79921", Blue = "#458588", Magenta = "#B16286", Cyan = "#689D6A", White = "#A89984",
        BrightBlack = "#928374", BrightRed = "#FB4934", BrightGreen = "#B8BB26", BrightYellow = "#FABD2F", BrightBlue = "#83A598", BrightMagenta = "#D3869B", BrightCyan = "#8EC07C", BrightWhite = "#EBDBB2",
    };

    public static readonly TerminalScheme[] All =
    [
        new(Auto, true, StudioDark),
        new("Studio 暗色", true, StudioDark),
        new("Studio 亮色", false, StudioLight),
        new("Solarized 暗色", true, () => Solarized(true)),
        new("Solarized 亮色", false, () => Solarized(false)),
        new("Dracula", true, Dracula),
        new("Nord", true, Nord),
        new("Gruvbox 暗色", true, Gruvbox),
    ];

    /// <summary>The view paints the selection over the glyphs, so a scheme's published selection colour
    /// (meant to go behind the text) would hide them. Its blue at a low alpha keeps selected text
    /// readable and the highlight visible; schemes with little contrast of their own get less.</summary>
    public static Color SelectionOverlay(ThemeOptions palette)
    {
        var blue = Color.Parse(palette.Blue ?? "#4D9DE0");
        double contrast = Contrast(Color.Parse(palette.Foreground ?? "#FFFFFF"), Color.Parse(palette.Background ?? "#000000"));
        return Color.FromArgb(contrast < 6 ? (byte)0x40 : (byte)0x58, blue.R, blue.G, blue.B);
    }

    /// <summary>Find matches are painted over the glyphs too: the scheme's yellow, and its magenta for the
    /// current one, thin enough to read the text through.</summary>
    public static (Color Match, Color Current) FindOverlays(ThemeOptions palette)
    {
        Color yellow = Color.Parse(palette.Yellow ?? "#E2B93D"), magenta = Color.Parse(palette.Magenta ?? "#C678DD");
        double contrast = Contrast(Color.Parse(palette.Foreground ?? "#FFFFFF"), Color.Parse(palette.Background ?? "#000000"));
        bool thin = contrast < 6;
        return (Color.FromArgb(thin ? (byte)0x38 : (byte)0x48, yellow.R, yellow.G, yellow.B), Color.FromArgb(thin ? (byte)0x50 : (byte)0x60, magenta.R, magenta.G, magenta.B));
    }

    /// <summary>WCAG contrast ratio.</summary>
    public static double Contrast(Color a, Color b)
    {
        static double Channel(byte v) { double x = v / 255d; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    /// <summary>The palette to use for a scheme name and the current application variant.</summary>
    public static ThemeOptions PaletteFor(string scheme, bool appDark)
    {
        if (scheme == Auto || All.FirstOrDefault(s => s.Name == scheme) is not { } fixedScheme) return appDark ? StudioDark() : StudioLight();
        return fixedScheme.Palette();
    }
}
