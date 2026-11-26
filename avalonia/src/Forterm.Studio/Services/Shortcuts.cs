using System.Collections.Concurrent;
using Avalonia.Input;
using Iciclecreek.Terminal;

namespace Forterm.Studio.Services;

/// <summary>A keyboard shortcut of the window. On macOS they use Cmd; elsewhere Ctrl+Shift with a letter, because
/// plain Ctrl belongs to the program in the terminal (Ctrl+W, Ctrl+K and Ctrl+T mean something to a shell), and so
/// do Ctrl+Shift with a digit or "-" (Ctrl+_ is undo) and Ctrl+Alt (which is AltGr on Windows). The user may choose
/// another one ("快捷键"), within the same limits.</summary>
public sealed class Shortcut(string id, string title, KeyGesture standard, IReadOnlyList<KeyGesture>? also = null)
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    /// <summary>As it comes.</summary>
    public KeyGesture Standard { get; } = standard;
    /// <summary>More keys for the same; a shortcut that has them cannot be changed.</summary>
    public IReadOnlyList<KeyGesture>? Also { get; } = also;
    public bool IsFixed => Also != null;
    /// <summary>As it is now. <see cref="Key.None"/> = off.</summary>
    public KeyGesture Gesture => !IsFixed && Appearance.Current.Shortcuts is { } chosen && chosen.TryGetValue(Id, out string? text) ? Shortcuts.Parse(text) ?? Shortcuts.Off : Standard;
    public bool IsOff => Gesture.Key == Key.None;
    public bool IsChanged => !Shortcuts.Same(Gesture, Standard);
    public string Keys => IsOff ? "" : Shortcuts.Show(Gesture);
}

public static class Shortcuts
{
    static readonly bool Mac = OperatingSystem.IsMacOS();
    static KeyModifiers Main => Mac ? KeyModifiers.Meta : KeyModifiers.Control | KeyModifiers.Shift;
    public static readonly KeyGesture Off = new(Key.None);

    public static readonly Shortcut Palette = new("palette", "命令面板", new(Mac ? Key.K : Key.P, Main));
    public static readonly Shortcut NewTerminal = new("newTerminal", "新建终端页签", new(Key.T, Main));
    public static readonly Shortcut CloseTab = new("closeTab", "关闭当前页签", new(Key.W, Main));
    public static readonly Shortcut Home = new("home", "连接库", new(Key.L, Main));
    public static readonly Shortcut Settings = new("settings", "系统配置", new(Key.OemComma, Main));
    public static readonly Shortcut NextSession = new("next", "下一个会话", new(Key.Tab, KeyModifiers.Control));
    public static readonly Shortcut PreviousSession = new("previous", "上一个会话", new(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift));
    /// <summary>In terminals and in the editor. Cmd+F; elsewhere Ctrl+Shift+F, as Ctrl+F belongs to the program.</summary>
    public static readonly Shortcut Find = new("find", "查找（终端、编辑器）", new(Key.F, Main));
    /// <summary>The terminal's own zoom keys, which never reach a program anyway: they change the setting instead.</summary>
    public static readonly Shortcut ZoomIn = Zoom("zoomIn", "放大终端文字", TerminalKeyGestures.ZoomIn);
    public static readonly Shortcut ZoomOut = Zoom("zoomOut", "缩小终端文字", TerminalKeyGestures.ZoomOut);
    public static readonly Shortcut ZoomReset = Zoom("zoomReset", "终端文字恢复默认大小", TerminalKeyGestures.ResetZoom);
    static Shortcut Zoom(string id, string title, IReadOnlyList<KeyGesture> gestures) => new(id, title, gestures[0], gestures);

    /// <summary>"锁屏". ⌘⇧L on macOS (⌘L is the library); Ctrl+Shift+K elsewhere (Ctrl+Shift+L is).</summary>
    public static readonly Shortcut Lock = new("lock", "锁定 Forterm", new(Mac ? Key.L : Key.K, Mac ? KeyModifiers.Meta | KeyModifiers.Shift : Main));

    public static readonly Shortcut[] All = [Palette, NewTerminal, CloseTab, Home, Settings, NextSession, PreviousSession, Find, Lock, ZoomIn, ZoomOut, ZoomReset];

    /// <summary>Cmd+1 … Cmd+9 on macOS: the first to the ninth session; 0 when it is not one of them. Other
    /// systems have no modifier left for digits: Ctrl+Alt writes characters there, Ctrl+Shift and Alt are the
    /// shell's.</summary>
    public static int SessionNumber(KeyEventArgs e) => Mac && e.KeyModifiers == KeyModifiers.Meta && e.Key is >= Key.D1 and <= Key.D9 ? e.Key - Key.D0 : 0;

    public static bool Matches(Shortcut shortcut, KeyEventArgs e) => !shortcut.IsOff && (shortcut.Gesture.Matches(e) || shortcut.Also?.Any(g => g.Matches(e)) == true);

    /// <summary>"⌘K" on macOS, "Ctrl+Shift+P" elsewhere.</summary>
    public static string Show(KeyGesture gesture)
    {
        string key = gesture.Key switch
        {
            Key.OemComma => ",", Key.OemPeriod => ".", Key.OemPlus => "+", Key.OemMinus => "-", Key.OemSemicolon => ";", Key.OemQuestion => "/", Key.OemOpenBrackets => "[", Key.OemCloseBrackets => "]",
            >= Key.D0 and <= Key.D9 => ((char)('0' + (gesture.Key - Key.D0))).ToString(),
            Key.Tab => Mac ? "⇥" : "Tab", Key.Up => "↑", Key.Down => "↓", Key.Left => "←", Key.Right => "→", _ => gesture.Key.ToString(),
        };
        var m = gesture.KeyModifiers;
        return Mac
            ? (m.HasFlag(KeyModifiers.Control) ? "⌃" : "") + (m.HasFlag(KeyModifiers.Alt) ? "⌥" : "") + (m.HasFlag(KeyModifiers.Shift) ? "⇧" : "") + (m.HasFlag(KeyModifiers.Meta) ? "⌘" : "") + key
            : (m.HasFlag(KeyModifiers.Control) ? "Ctrl+" : "") + (m.HasFlag(KeyModifiers.Alt) ? "Alt+" : "") + (m.HasFlag(KeyModifiers.Shift) ? "Shift+" : "") + key;
    }

    // ---------- chosen by the user ----------

    public static bool Same(KeyGesture a, KeyGesture b) => a.Key == b.Key && a.KeyModifiers == b.KeyModifiers;

    static readonly ConcurrentDictionary<string, KeyGesture?> parsed = new();
    /// <summary>null when it cannot be read; "" is off.</summary>
    public static KeyGesture? Parse(string? text) => text is not { Length: <= 64 } ? null : text.Length == 0 ? Off : parsed.GetOrAdd(text, t =>
    {
        try { return KeyGesture.Parse(t); }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException) { return null; }
    });

    static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.CapsLock or Key.None or Key.ImeProcessed or Key.DeadCharProcessed or Key.System;
    /// <summary>Nothing but modifiers so far: the user is not done.</summary>
    public static bool IsUnfinished(Key key) => IsModifier(key);

    static bool Function(Key key) => key is >= Key.F1 and <= Key.F24;
    static bool Letter(Key key) => key is >= Key.A and <= Key.Z;

    /// <summary>Why a gesture cannot be a shortcut of the window; null when it can.</summary>
    public static string? Check(KeyGesture gesture) => Check(gesture, Mac);

    internal static string? Check(KeyGesture gesture, bool mac)
    {
        var (key, m) = (gesture.Key, gesture.KeyModifiers);
        if (IsModifier(key)) return "还缺一个主键";
        bool control = m.HasFlag(KeyModifiers.Control), alt = m.HasFlag(KeyModifiers.Alt), shift = m.HasFlag(KeyModifiers.Shift), meta = m.HasFlag(KeyModifiers.Meta);
        if (!control && !alt && !meta) return "要带上 " + (mac ? "⌘" : "Ctrl+Shift") + "：单独的按键属于正在运行的程序";
        if (alt && control && !mac) return "Ctrl+Alt 就是 AltGr，部分键盘要用它输入字符";
        if (alt && !meta) return "Alt 组合属于终端里的程序，也用来输入特殊字符";
        if (meta && !mac) return "Windows 徽标键组合属于系统";
        if (control && !meta)
        {
            bool tab = key == Key.Tab;
            if (!shift && !tab) return "Ctrl 组合属于终端里的程序（例如 Ctrl+C、Ctrl+W）；请再加上 Shift";
            if (shift && !tab && !Letter(key) && !Function(key) && key is not (Key.OemComma or Key.OemPeriod or Key.OemSemicolon)) return "这个组合在终端里另有含义（例如 Ctrl+Shift+- 就是 Ctrl+_）；请用字母";
        }
        foreach (var taken in TerminalKeyGestures.Copy.Concat(TerminalKeyGestures.Paste).Concat(TerminalKeyGestures.SelectAll))
            if (Same(taken, gesture)) return "已用于终端的复制、粘贴或全选";
        foreach (var zoom in All.Where(s => s.IsFixed))
            if (zoom.Also!.Any(g => Same(g, gesture))) return $"已用于“{zoom.Title}”";
        if (!mac) return null;
        if (meta && !control && !alt && key is >= Key.D0 and <= Key.D9) return "⌘数字用于切换会话";
        if (!meta) return null;
        if (!Letter(key) && !Function(key) && key is not (Key.OemComma or Key.OemPeriod or Key.OemSemicolon or Key.OemOpenBrackets or Key.OemCloseBrackets or Key.OemQuestion)) return "这个键用于编辑文字或属于系统；请用字母或功能键";
        if (key is Key.Q or Key.H or Key.M or Key.Z or Key.X or Key.S) return "属于系统、编辑或保存";
        if (key == Key.G && !control && !alt || key == Key.F && alt && !control && !shift) return "用于编辑器的查找和替换";
        return null;
    }

    /// <summary>The shortcut that has this gesture now.</summary>
    public static Shortcut? Owner(KeyGesture gesture) => gesture.Key == Key.None ? null : All.FirstOrDefault(s => Same(s.Gesture, gesture) || s.Also?.Any(g => Same(g, gesture)) == true);

    /// <summary>Known, possible, and no two the same; otherwise it stays as it comes.</summary>
    public static IReadOnlyDictionary<string, string>? Validated(IReadOnlyDictionary<string, string>? chosen)
    {
        if (chosen == null || chosen.Count == 0) return null;
        var kept = new Dictionary<string, string>();
        foreach (var s in All.Where(s => !s.IsFixed))
        {
            if (!chosen.TryGetValue(s.Id, out string? text) || Parse(text) is not { } gesture) continue;
            if (gesture.Key != Key.None && (Check(gesture) != null || Same(gesture, s.Standard))) continue;
            kept[s.Id] = gesture.Key == Key.None ? "" : gesture.ToString();
        }
        // Two with the same keys: the changed ones give way, the later first.
        KeyGesture Now(Shortcut s) => kept.TryGetValue(s.Id, out string? t) ? Parse(t)! : s.Standard;
        for (bool again = true; again;)
        {
            again = false;
            foreach (var pair in All.Where(s => !s.IsFixed && Now(s).Key != Key.None).GroupBy(s => (Now(s).Key, Now(s).KeyModifiers)).Where(g => g.Count() > 1))
            {
                kept.Remove(pair.Last(s => kept.ContainsKey(s.Id)).Id);
                again = true;
                break;
            }
        }
        return kept.Count == 0 ? null : kept;
    }
}
