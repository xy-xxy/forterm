using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Forterm.Studio.Services;

public sealed record StudioPreferences
{
    public string Theme { get; init; } = "暗夜";
    public bool FollowSystem { get; init; }
    public string Accent { get; init; } = "#3FB176";
    public double UiScale { get; init; } = 1.0;
    public double TerminalFontSize { get; init; } = 14;
    /// <summary>Empty = the platform default (cmd.exe on Windows, $SHELL or /bin/zsh elsewhere).</summary>
    public string LocalShell { get; init; } = "";
    /// <summary>"默认程序参数": split like a command line (double quotes group), never run through a shell.</summary>
    public string LocalShellArgs { get; init; } = "";
    /// <summary>TERM for local terminals; empty = xterm-256color.</summary>
    public string LocalTerminalType { get; init; } = "";
    /// <summary>"导航栏颜色": one of <see cref="Appearance.TitleBarColors"/>; empty = from the background preset.</summary>
    public string TitleBarColor { get; init; } = "";
    /// <summary>"系统字体" for everything except terminals; empty = the platform default.</summary>
    public string UiFont { get; init; } = "";
    /// <summary>Terminal font family; empty = the built-in monospace stack. Always followed by that stack.</summary>
    public string TerminalFont { get; init; } = "";
    /// <summary>"终端配色": a <see cref="TerminalSchemes"/> name.</summary>
    public string TerminalScheme { get; init; } = TerminalSchemes.Auto;
    /// <summary>"右键菜单": true shows the menu; false copies the selection, or pastes when there is none.</summary>
    public bool RightClickMenu { get; init; } = true;
    /// <summary>"粘贴确认": preview multi-line, long or cleaned-up text before it is pasted.</summary>
    public bool ConfirmPaste { get; init; } = true;
    /// <summary>"选中即复制".</summary>
    public bool CopyOnSelect { get; init; }
    /// <summary>One of <see cref="Appearance.CursorStyles"/>.</summary>
    public string CursorStyle { get; init; } = "bar";
    public bool CursorBlink { get; init; } = true;
    /// <summary>"回滚行数" of a newly opened terminal.</summary>
    public int Scrollback { get; init; } = 5000;
    /// <summary>"界面动画".</summary>
    public bool Motion { get; init; } = true;
    /// <summary>Widths of the workspace's side columns, as the user left them.</summary>
    public double FilesWidth { get; init; } = 320;
    public double MonitorWidth { get; init; } = 280;
    /// <summary>"空闲保活": off unless the user switches it on. It sends by itself, also into sessions the controller
    /// opened; see <see cref="Services.IdleKeeper"/>.</summary>
    public bool IdleKeepAlive { get; init; }
    public int IdleMinutes { get; init; } = 4;
    /// <summary>One of <see cref="Services.IdleKeeper.Inputs"/>.</summary>
    public string IdleInput { get; init; } = "space";
    public int IdleHours { get; init; } = 8;
    public bool IdleFullScreen { get; init; }

    // "编辑器"
    public double EditorFontSize { get; init; } = 13;
    /// <summary>Empty = the same family as the terminals.</summary>
    public string EditorFont { get; init; } = "";
    public bool EditorWrap { get; init; }
    public bool EditorWhitespace { get; init; }
    /// <summary>Columns a tab is wide, and what one Tab inserts when it inserts spaces.</summary>
    public int EditorTab { get; init; } = 4;
    public bool EditorSpaces { get; init; }
    public bool EditorLineNumbers { get; init; } = true;
    public bool EditorColours { get; init; } = true;
    /// <summary>Languages chosen by hand: ".conf" or a whole name, lower case → <see cref="EditorLanguage.Id"/>.</summary>
    public IReadOnlyDictionary<string, string>? EditorLanguages { get; init; }

    /// <summary>"快捷键" changed by the user: id → gesture; "" = off. null = all as they come.</summary>
    public IReadOnlyDictionary<string, string>? Shortcuts { get; init; }

    // "文件管理", "上传下载"
    public bool ShowHidden { get; init; } = true;
    /// <summary>One of <see cref="Appearance.FileSorts"/>.</summary>
    public string FileSort { get; init; } = "name";
    public bool FileSortDescending { get; init; }
    /// <summary>Size and time next to the names, when the column is wide enough.</summary>
    public bool FileDetails { get; init; } = true;
    /// <summary>Where "下载到…" goes; empty = the desktop.</summary>
    public string DownloadFolder { get; init; } = "";
    /// <summary>Say so in the corner of the window when transfers have ended.</summary>
    public bool NotifyTransfers { get; init; } = true;

    // "布局配置": the columns a new SSH session shows.
    public bool OpenFiles { get; init; } = true;
    public bool OpenMonitor { get; init; } = true;

    /// <summary>"监控阈值".</summary>
    public MonitorThresholds Thresholds { get; init; } = new();

    // "主题界面": a picture behind the window, and how much of the desktop shows through it.
    /// <summary>"自定义背景图片": a copy in the data directory (<see cref="Backdrop"/>); "" = none.</summary>
    public string BackgroundImage { get; init; } = "";
    /// <summary>How much of the picture shows through the columns, 0.1–0.9.</summary>
    public double BackgroundStrength { get; init; } = 0.35;
    /// <summary>"全局不透明度" of the main window, 0.3–1.</summary>
    public double WindowOpacity { get; init; } = 1;

    // "布局"
    /// <summary>"页签位置": the tabs of the terminal column below it instead of above.</summary>
    public bool TabsAtBottom { get; init; }
    /// <summary>"标签渲染": a terminal takes in its output from the start, also before it is first shown.</summary>
    public bool StartHiddenTerminals { get; init; }

    // "锁屏": the password itself is only in the system's credential store (<see cref="AppLock"/>).
    /// <summary>Minutes without input after which Studio locks by itself; 0 = never.</summary>
    public int LockMinutes { get; init; }
    /// <summary>Locked from the start, until the password is given.</summary>
    public bool LockAtStart { get; init; }

    /// <summary>"自定义高亮" on or off; null rules = the built-in set.</summary>
    public bool Highlight { get; init; } = true;
    public HighlightRule[]? HighlightRules { get; init; }

    public StudioPreferences Validated() => this with
    {
        Theme = Appearance.Presets.Any(p => p.Name == Theme) ? Theme : "暗夜",
        Accent = Appearance.Accents.Contains(Accent) ? Accent : "#3FB176",
        UiScale = Math.Round(Math.Clamp(UiScale, 0.5, 1.5), 2),
        TerminalFontSize = Math.Clamp(TerminalFontSize, 10, 24),
        // Not trimmed: these are bound to text boxes that commit on every keystroke, and trimming would
        // swallow a space typed between words. Users of the values trim or split them.
        LocalShell = Line(LocalShell, 1024, trim: false),
        LocalShellArgs = Line(LocalShellArgs, 1024, trim: false),
        LocalTerminalType = ValidTerm(LocalTerminalType),
        TitleBarColor = Appearance.TitleBarColors.Contains(TitleBarColor) ? TitleBarColor : "",
        UiFont = FontName(UiFont),
        TerminalFont = FontName(TerminalFont),
        TerminalScheme = TerminalSchemes.All.Any(s => s.Name == TerminalScheme) ? TerminalScheme : TerminalSchemes.Auto,
        CursorStyle = Appearance.CursorStyles.Any(c => c.Key == CursorStyle) ? CursorStyle : "bar",
        FilesWidth = double.IsFinite(FilesWidth) ? Math.Clamp(FilesWidth, 200, 1600) : 320,
        MonitorWidth = double.IsFinite(MonitorWidth) ? Math.Clamp(MonitorWidth, 200, 1600) : 280,
        Scrollback = Math.Clamp(Scrollback, 100, 100_000),
        HighlightRules = Highlighter.Validated(HighlightRules),
        IdleMinutes = Math.Clamp(IdleMinutes, 1, 60),
        IdleHours = Math.Clamp(IdleHours, 1, 24),
        IdleInput = IdleKeeper.Inputs.Any(i => i.Key == IdleInput) ? IdleInput : "space",
        EditorFontSize = double.IsFinite(EditorFontSize) ? Math.Clamp(Math.Round(EditorFontSize), 9, 28) : 13,
        EditorFont = FontName(EditorFont),
        EditorTab = EditorTab is >= 1 and <= 16 ? EditorTab : 4,
        EditorLanguages = Languages(EditorLanguages),
        Shortcuts = Services.Shortcuts.Validated(Shortcuts),
        FileSort = Appearance.FileSorts.Any(s => s.Key == FileSort) ? FileSort : "name",
        DownloadFolder = Line(DownloadFolder, 1024) is { Length: > 0 } folder && Path.IsPathFullyQualified(folder) ? folder : "",
        Thresholds = (Thresholds ?? new()).Validated(),
        BackgroundImage = Services.Backdrop.IsName(BackgroundImage) ? BackgroundImage : "",
        BackgroundStrength = double.IsFinite(BackgroundStrength) ? Math.Round(Math.Clamp(BackgroundStrength, 0.1, 0.9), 2) : 0.35,
        WindowOpacity = double.IsFinite(WindowOpacity) ? Math.Round(Math.Clamp(WindowOpacity, 0.3, 1), 2) : 1,
        LockMinutes = Math.Clamp(LockMinutes, 0, 240),
    };

    /// <summary>The newest ones of the languages chosen by hand.</summary>
    public const int MaxLanguages = 100;
    static IReadOnlyDictionary<string, string>? Languages(IReadOnlyDictionary<string, string>? chosen)
    {
        if (chosen == null) return null;
        var known = chosen.Where(c => c.Key is { Length: > 0 and <= 128 } && !c.Key.Any(char.IsControl) && EditorSyntax.All.Any(l => l.Id == c.Value)).TakeLast(MaxLanguages).ToDictionary(c => c.Key, c => c.Value);
        return known.Count == 0 ? null : known;
    }

    static string Line(string? value, int max, bool trim = true) => value is null || value.Length > max || value.Any(char.IsControl) ? "" : trim ? value.Trim() : value;

    /// <summary>One plain family name. Anything else (a comma list, "#", "avares:", "fonts:", paths)
    /// is refused: such a string can make font resolution fail while laying out every window.</summary>
    static string FontName(string? value) => Line(value, 128) is { Length: > 0 } name && name[0] != '.' && name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' or '(' or ')' or '&' or '+' or '\'') ? name : "";

    static string ValidTerm(string? value)
    {
        try { return string.IsNullOrWhiteSpace(value) ? "" : Forterm.Core.SharedSshSession.ValidTerminalType(value); }
        catch (ArgumentException) { return ""; }
    }
}

/// <summary>Background preset: the base light/dark variant plus window, panel and card colours.</summary>
public sealed record ThemePreset(string Name, bool Dark, string Window, string Panel, string Card, string TitleBar, string Hover, string Line)
{
    public IBrush Swatch => new SolidColorBrush(Color.Parse(Panel));
    public IBrush SwatchText => new SolidColorBrush(Dark ? Colors.White : Color.Parse("#222222"));
}

/// <summary>Applies Studio's appearance preferences at runtime: base variant, background preset,
/// accent colour, UI scale and terminal font size. Overrides go into the application's root
/// resource dictionary, which wins over the theme dictionaries.</summary>
public static class Appearance
{
    public static readonly ThemePreset[] Presets =
    [
        new("明亮", false, "#F3F4F5", "#FFFFFF", "#F6F7F8", "#E6E7E9", "#ECEDEF", "#E1E2E4"),
        new("暗夜", true, "#191919", "#191919", "#252525", "#2B2B2B", "#2E2E2E", "#2A2A2A"),
        new("朱红", false, "#FBEDEA", "#FFF7F5", "#FDEEEA", "#F6DED8", "#F8E4DF", "#EFD3CC"),
        new("翠绿", false, "#EAF5EC", "#F6FBF7", "#EAF5ED", "#DCEDE0", "#E1F0E4", "#CFE5D4"),
        new("银灰", false, "#EDEFF1", "#F8F9FA", "#EEF0F2", "#E0E3E6", "#E5E8EB", "#D6DADE"),
        new("天蓝", false, "#E8F2FC", "#F5FAFF", "#E9F2FC", "#D9E8F8", "#DFECFA", "#CCDFF3"),
        new("金黄", false, "#FCF6E3", "#FFFCF2", "#FBF4DF", "#F4EACB", "#F7EFD5", "#EADFBF"),
        new("橙黄", false, "#FDF1E4", "#FFF9F2", "#FCEFE0", "#F6E2CC", "#F8E8D6", "#EDD6BE"),
        new("艳红", false, "#FCEBEE", "#FFF6F7", "#FBE7EA", "#F4D6DB", "#F7DDE1", "#EBC8CE"),
        new("粉红", false, "#FCEEF4", "#FFF7FA", "#FBE9F1", "#F5DAE6", "#F8E1EB", "#ECCCDA"),
        new("雅紫", false, "#F3EEF8", "#FAF7FD", "#F1EBF8", "#E6DCF1", "#EBE3F4", "#DCCFEA"),
        new("湖青", false, "#E6F6F7", "#F4FBFC", "#E5F4F6", "#D4ECEF", "#DBF0F2", "#C5E2E6"),
        new("深蓝", true, "#0F1A2B", "#111D30", "#18263C", "#14213A", "#1C2B44", "#22314B"),
        new("褐棕", false, "#F1ECE8", "#FAF7F5", "#EFE9E4", "#E4DBD4", "#E9E1DB", "#D9CEC5"),
        new("墨黑", true, "#0D0D0D", "#101010", "#1A1A1A", "#1C1C1C", "#222222", "#232323"),
        new("碧绿", true, "#0E1F1C", "#10231F", "#173029", "#132A25", "#1B342E", "#203A33"),
        new("赤红", true, "#24110F", "#281311", "#341B18", "#2E1714", "#3A201C", "#3F2420"),
        new("靛蓝", true, "#14152B", "#171831", "#20223F", "#1B1D38", "#252847", "#2A2D4C"),
        new("茶褐", true, "#1E1813", "#221B15", "#2D241C", "#282018", "#332920", "#382D23"),
        new("雪白", false, "#FFFFFF", "#FFFFFF", "#F7F7F7", "#F2F2F2", "#F0F0F0", "#E6E6E6"),
    ];

    public static readonly string[] Accents = ["#3FB176", "#2DA9C9", "#3D7FE0", "#7B61D8", "#C7843A", "#E0A100", "#E8743B", "#E05A8B", "#E5534B", "#8C8C8C"];

    /// <summary>"导航栏颜色" choices (the reference offers the same kinds of solid colours).</summary>
    public static readonly string[] TitleBarColors = ["#2F9E63", "#2B2B2B", "#111111", "#6F7FC4", "#3D6FDB", "#A88B5E", "#1A8DB8", "#E0922A", "#D9609A", "#D9534F", "#B9A88F"];

    /// <summary>The terminal's own fallback stack, kept after a chosen terminal font.</summary>
    public const string MonospaceStack = "MesloLGS NF,JetBrainsMono Nerd Font,Hack Nerd Font,Cascadia Mono,Cascadia Code,Consolas,Menlo,DejaVu Sans Mono,Liberation Mono,Courier New," +
        "Segoe UI Emoji,Apple Color Emoji,Noto Color Emoji,monospace";

    /// <summary>Monospace families offered for terminals, when installed.</summary>
    public static readonly string[] MonospaceCandidates =
    [
        "Menlo", "Monaco", "SF Mono", "Courier New", "Consolas", "Cascadia Mono", "Cascadia Code", "Lucida Console", "JetBrains Mono", "JetBrainsMono Nerd Font",
        "MesloLGS NF", "Hack", "Fira Code", "Fira Mono", "Source Code Pro", "Ubuntu Mono", "DejaVu Sans Mono", "Liberation Mono", "Noto Sans Mono",
        "Sarasa Mono SC", "等距更纱黑体 SC", "PingFang SC", "Microsoft YaHei Mono",
    ];

    /// <summary>Installed font families, sorted; empty when the platform has not started.</summary>
    public static IReadOnlyList<string> InstalledFonts()
    {
        try
        {
            return FontManager.Current.SystemFonts.Select(f => f.Name).Where(n => n.Length > 0 && !n.StartsWith('.') && FontName(n) == n)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.CurrentCulture).ToArray();
        }
        catch (Exception) { return Array.Empty<string>(); }
    }

    static string FontName(string value) => new StudioPreferences { UiFont = value }.Validated().UiFont;

    public static FontFamily TerminalFontFamily(StudioPreferences preferences) =>
        new(preferences.TerminalFont.Length > 0 ? preferences.TerminalFont + "," + MonospaceStack : MonospaceStack);

    /// <summary>"光标样式" choices.</summary>
    public static readonly (string Key, string Title, XTerm.Common.CursorStyle Style)[] CursorStyles =
        [("bar", "竖线", XTerm.Common.CursorStyle.Bar), ("block", "方块", XTerm.Common.CursorStyle.Block), ("underline", "下划线", XTerm.Common.CursorStyle.Underline)];

    /// <summary>"排序方式" of the file list.</summary>
    public static readonly (string Key, string Title)[] FileSorts = [("name", "名称"), ("size", "大小"), ("time", "修改时间"), ("type", "类型")];

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    /// <summary>Highlight rules are the bulk: 30 rules of escaped text stay well below this.</summary>
    const int MaxFileSize = 512 * 1024;
    static readonly string[] OverrideKeys = ["St.Window", "St.Panel", "St.Card", "St.TitleBar", "St.Hover", "St.Line", "St.Dialog", "St.DialogSection", "St.Menu", "St.Field", "St.Pressed", "St.NavActive",
        "St.TitleBarText", "St.TitleBarHover", "St.TerminalBack", "St.Terminal", UiFontKey];
    /// <summary>The Fluent theme's window font resource; overriding it restyles every window.</summary>
    const string UiFontKey = "ContentControlThemeFontFamily";

    public static StudioPreferences Current { get; private set; } = new();
    public static event Action<StudioPreferences>? Changed;
    /// <summary>All preferences were written to this directory.</summary>
    public static event Action<string>? Saved;

    /// <summary>Files that could not be read: the next save keeps them under another name.</summary>
    static readonly HashSet<string> unreadable = new();

    public static StudioPreferences Load(string? directory) => Load(directory, out _);

    /// <summary>Anything unreadable means the defaults: it must not stop the application.
    /// <paramref name="problem"/> is why, or null.</summary>
    public static StudioPreferences Load(string? directory, out string? problem)
    {
        problem = null;
        if (directory == null) return new();
        string path = Path.Combine(directory, "preferences.json");
        try
        {
            if (!File.Exists(path)) return new();
            if (new FileInfo(path).Length > MaxFileSize) throw new IOException($"设置文件超过 {MaxFileSize / 1024} KiB");
            var loaded = (JsonSerializer.Deserialize<StudioPreferences>(File.ReadAllBytes(path)) ?? new()).Validated();
            unreadable.Remove(path);
            return loaded;
        }
        catch (Exception e)
        {
            unreadable.Add(path);
            problem = e.Message;
            return new();
        }
    }

    public static void Save(string? directory, StudioPreferences preferences)
    {
        if (directory == null) return;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "preferences.json"), temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(preferences.Validated(), Json));
            // What could not be read may be all of the user's settings: it is kept, not replaced.
            if (unreadable.Contains(path) && File.Exists(path))
                File.Move(path, Path.Combine(directory, $"preferences.unreadable-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json"), overwrite: false);
            unreadable.Remove(path);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        Saved?.Invoke(directory);
    }

    /// <summary>The part of the preferences <see cref="Apply"/> turns into resources.</summary>
    static (string, bool, string, string, string, string, double) Visual(StudioPreferences p) => (p.Theme, p.FollowSystem, p.Accent, p.TitleBarColor, p.UiFont, p.TerminalScheme, SeeThrough(p));
    static (Application App, (string, bool, string, string, string, string, double) Visual)? applied;

    /// <summary>How much of the background picture shows through the window's surfaces; 0 without one.</summary>
    public static double SeeThrough(StudioPreferences p) => p.BackgroundImage.Length > 0 ? p.BackgroundStrength : 0;
    /// <summary>The surfaces a background picture shows through. Dialogs, menus and fields stay solid.</summary>
    static readonly string[] Surfaces = ["St.Window", "St.Panel", "St.Card", "St.TitleBar", "St.TerminalBack", "St.Terminal"];
    static bool variantWatched;

    public static void Apply(StudioPreferences preferences)
    {
        preferences = preferences.Validated();
        Current = preferences;
        if (Application.Current is not { } app) { Changed?.Invoke(preferences); return; }
        // Typing in a text box commits on every keystroke: rebuild resources only when they change.
        if (applied is { } last && ReferenceEquals(last.App, app) && last.Visual == Visual(preferences)) { Changed?.Invoke(preferences); return; }
        applied = (app, Visual(preferences));
        var preset = Presets.First(p => p.Name == preferences.Theme);
        var resources = app.Resources;
        foreach (string key in OverrideKeys) resources.Remove(key);
        bool isDefault = preset.Name is "暗夜" or "明亮";
        if (preferences.FollowSystem) app.RequestedThemeVariant = ThemeVariant.Default;
        else app.RequestedThemeVariant = preset.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (!isDefault && !preferences.FollowSystem)
        {
            resources["St.Window"] = Brush(preset.Window);
            resources["St.Panel"] = Brush(preset.Panel);
            resources["St.Card"] = Brush(preset.Card);
            resources["St.TitleBar"] = Brush(preset.TitleBar);
            resources["St.Hover"] = Brush(preset.Hover);
            resources["St.Line"] = Brush(preset.Line);
            resources["St.Dialog"] = Brush(preset.Card);
            resources["St.DialogSection"] = Brush(preset.Panel);
            resources["St.Menu"] = Brush(preset.Card);
            resources["St.Field"] = Brush(preset.Dark ? Mix(preset.Card, "#FFFFFF", 0.06) : "#FFFFFF");
            resources["St.Pressed"] = Brush(Mix(preset.Hover, preset.Dark ? "#FFFFFF" : "#000000", 0.05));
            resources["St.NavActive"] = Brush(Mix(preset.Hover, preset.Dark ? "#FFFFFF" : "#000000", 0.08));
        }
        if (preferences.TitleBarColor.Length > 0)
        {
            // A solid navigation bar: its own text and hover colours, readable on that colour.
            var bar = Color.Parse(preferences.TitleBarColor);
            bool darkBar = Luminance(bar) < 0.55;
            resources["St.TitleBar"] = new SolidColorBrush(bar);
            resources["St.TitleBarText"] = Brush(darkBar ? "#F2F2F2" : "#1E1E1E");
            resources["St.TitleBarHover"] = Brush(Mix(preferences.TitleBarColor, darkBar ? "#FFFFFF" : "#000000", 0.14));
        }
        if (preferences.TerminalScheme != TerminalSchemes.Auto)
            resources["St.TerminalBack"] = Brush(TerminalSchemes.PaletteFor(preferences.TerminalScheme, true).Background ?? "#000000");
        if (preferences.UiFont.Length > 0) resources[UiFontKey] = new FontFamily(preferences.UiFont);
        var accent = Color.Parse(preferences.Accent);
        resources["St.Accent"] = new SolidColorBrush(accent);
        resources["St.Selected"] = new SolidColorBrush(accent);
        resources["St.AccentHover"] = Brush(Mix(preferences.Accent, "#FFFFFF", 0.12));
        resources["St.AccentSoft"] = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B));
        resources["SystemAccentColor"] = accent;
        if (SeeThrough(preferences) is > 0 and var through)
        {
            // The colours as they are now, with the picture showing through them.
            foreach (string key in Surfaces)
                if (app.TryGetResource(key, app.ActualThemeVariant, out object? value) && value is ISolidColorBrush brush)
                    resources[key] = new SolidColorBrush(brush.Color, 1 - through);
            if (!variantWatched)
            {
                // The system switched between light and dark: the see-through colours follow.
                variantWatched = true;
                app.ActualThemeVariantChanged += (_, _) => { if (SeeThrough(Current) > 0) { applied = null; Apply(Current); } };
            }
        }
        Changed?.Invoke(preferences);
    }

    static SolidColorBrush Brush(string color) => new(Color.Parse(color));

    static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    static string Mix(string a, string b, double t)
    {
        var x = Color.Parse(a);
        var y = Color.Parse(b);
        byte M(byte p, byte q) => (byte)Math.Round(p + (q - p) * t);
        return $"#{M(x.R, y.R):X2}{M(x.G, y.G):X2}{M(x.B, y.B):X2}";
    }
}
