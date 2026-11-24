using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Iciclecreek.Terminal;
using XTerm.Options;

namespace Forterm.Studio.Services;

/// <summary>One "自定义高亮" rule. <see cref="Color"/> is one of <see cref="Highlighter.Colors"/>: a colour of
/// the terminal scheme, so a rule stays readable in every scheme.</summary>
public sealed record HighlightRule
{
    public string Name { get; init; } = "";
    public string Pattern { get; init; } = "";
    public string Color { get; init; } = "yellow";
    public bool IgnoreCase { get; init; } = true;
    public bool Enabled { get; init; } = true;
}

/// <summary>Colours matching text in the terminal's normal screen. Only text the program printed in the
/// default colours is touched, and only how it is drawn: nothing is sent anywhere. Patterns run on the
/// non-backtracking engine, whose time is linear in the row; a rule that is slow all the same is switched
/// off after a few rows, so no pattern can hold up the window for long.</summary>
public sealed class Highlighter
{
    public const int MaxRules = 40, MaxPattern = 400, MaxName = 40;
    static readonly TimeSpan DefaultRowLimit = TimeSpan.FromMilliseconds(50);
    /// <summary>One slow row may be a busy machine; this many in a row are the pattern.</summary>
    const int SlowRows = 3;

    public static readonly (string Key, string Title)[] Colors =
        [("red", "红"), ("yellow", "黄"), ("green", "绿"), ("cyan", "青"), ("blue", "蓝"), ("magenta", "紫")];

    /// <summary>A pattern may name the part to colour <c>(?&lt;k&gt;…)</c>; without it the whole match is coloured.</summary>
    const string PartGroup = "k";

    /// <summary>The built-in set: what the reference screenshots colour (URLs, IPv4, version numbers), the usual
    /// status words, and the parts of a command line. Earlier rules win where they overlap.</summary>
    public static HighlightRule[] Defaults() =>
    [
        new() { Name = "网址", Pattern = @"\b(https?|ftp|ssh|sftp|redis|mysql|jdbc:\w+)://[^\s'""<>()]+", Color = "yellow" },
        new() { Name = "IPv4 地址", Pattern = @"\b(25[0-5]|2[0-4]\d|1?\d?\d)(\.(25[0-5]|2[0-4]\d|1?\d?\d)){3}(:\d{1,5}|/\d{1,2})?\b", Color = "magenta" },
        new() { Name = "日期时间", Pattern = @"\b\d{4}[-/]\d{2}[-/]\d{2}([T ]\d{2}:\d{2}(:\d{2}([.,]\d+)?)?)?\b|\b\d{2}:\d{2}:\d{2}\b|\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) +\d{1,2}\b", Color = "magenta" },
        new() { Name = "版本号", Pattern = @"\bv?\d+\.\d+\.\d+([-+][\w.]+)?\b", Color = "cyan" },
        new() { Name = "错误", Pattern = @"\b(error|errors|failed|failure|fatal|denied|refused|panic|critical|cannot|unable|exception|traceback|segfault|killed|unreachable|timeout|timed out|not found|no such file or directory|inactive|dead)\b|错误|失败|拒绝|超时|异常", Color = "red" },
        new() { Name = "警告", Pattern = @"\b(warn|warning|warnings|deprecated|disabled|stopped|skipped|retry|retrying)\b|警告|注意", Color = "yellow" },
        new() { Name = "成功", Pattern = @"\b(ok|success|successful|successfully|succeeded|passed|done|complete|completed|active \(running\)|running|enabled|listening|established|healthy|connected|started)\b|成功|通过|完成|正常", Color = "green" },
        new() { Name = "日志级别", Pattern = @"\b(INFO|NOTICE|DEBUG|TRACE|VERBOSE)\b", Color = "blue", IgnoreCase = false },
        new() { Name = "用户@主机", Pattern = @"\b[A-Za-z_][\w.-]{0,31}@[A-Za-z0-9][\w.-]{0,63}", Color = "green" },
        new() { Name = "命令", Pattern = @"^(\([^)]{1,40}\) ?)?((\[[^\]]{1,80}\] ?|[\w.-]{1,32}@[\w.-]{1,64}[^$#%]{0,80})[$#%]|\$|❯|➜) +(sudo +)?(?<k>[A-Za-z_./~][\w./+-]*)", Color = "blue" },
        new() { Name = "命令选项", Pattern = @"(^|\s)(?<k>--?[A-Za-z][\w-]*)", Color = "yellow" },
        new() { Name = "路径", Pattern = @"(^|[\s=:'""(])(?<k>(~|\.{1,2})?(/[\w.@%+-]+)+/?|~/)", Color = "cyan" },
        new() { Name = "引号内容", Pattern = @"""[^""]{1,200}""|'[^']{1,200}'", Color = "green" },
        new() { Name = "百分比和大小", Pattern = @"\b\d+(\.\d+)?( ?[KMGT]i?B?\b|%)", Color = "blue", IgnoreCase = false },
    ];

    sealed class Rule((string, bool) key, Regex regex, IBrush brush)
    {
        /// <summary>The group that is coloured; 0 is the whole match.</summary>
        public int Part { get; } = Math.Max(0, regex.GroupNumberFromName(PartGroup));
        public (string, bool) Key { get; } = key;
        public Regex Regex { get; } = regex;
        public IBrush Brush { get; } = brush;
        public int Slow { get; set; }
        public bool Off { get; set; }
    }

    readonly Rule[] rules;
    internal TimeSpan RowLimit { get; set; }

    public Highlighter(IEnumerable<HighlightRule> rules, ThemeOptions palette) : this(rules, palette, DefaultRowLimit) { }

    internal Highlighter(IEnumerable<HighlightRule> rules, ThemeOptions palette, TimeSpan rowLimit)
    {
        RowLimit = rowLimit;
        var brushes = Colors.ToDictionary(c => c.Key, c => (IBrush)new ImmutableSolidColorBrush(ColorOf(palette, c.Key)));
        this.rules = rules.Where(r => r.Enabled && !IsSlow(r)).Select(r => (Key: KeyOf(r), Regex: Compile(r), Brush: brushes.GetValueOrDefault(r.Color) ?? brushes["yellow"]))
            .Where(r => r.Regex != null).Select(r => new Rule(r.Key, r.Regex!, r.Brush)).ToArray();
    }

    /// <summary>Patterns switched off because they were slow, until the program ends. UI thread only.</summary>
    static readonly HashSet<(string, bool)> slow = new();
    static (string, bool) KeyOf(HighlightRule rule) => (rule.Pattern, rule.IgnoreCase);
    public static bool IsSlow(HighlightRule rule) => slow.Contains(KeyOf(rule));
    /// <summary>A rule was switched off; the settings page shows it.</summary>
    public static event Action? SlowFound;

    static (StudioPreferences Preferences, string Scheme, Highlighter? Highlighter)? last;

    /// <summary>The highlighter for these preferences, shared by every terminal showing the same scheme;
    /// null when highlighting is off or has no usable rule. UI thread only.</summary>
    public static Highlighter? For(StudioPreferences preferences, string schemeKey, ThemeOptions palette)
    {
        if (last is { } cached && cached.Scheme == schemeKey && cached.Preferences.Highlight == preferences.Highlight && Same(cached.Preferences.HighlightRules, preferences.HighlightRules)) return cached.Highlighter;
        Highlighter? next = preferences.Highlight ? new Highlighter(preferences.HighlightRules ?? Defaults(), palette) : null;
        if (next is { IsEmpty: true }) next = null;
        last = (preferences, schemeKey, next);
        return next;
    }

    /// <summary>Same rules in the same order; null means the built-in set.</summary>
    public static bool Same(HighlightRule[]? a, HighlightRule[]? b) => a == null || b == null ? a == null && b == null : a.SequenceEqual(b);

    /// <summary>Rules as they may be stored: bounded and free of control characters. A pattern that does not
    /// compile is kept (the settings page shows why) and simply matches nothing.</summary>
    public static HighlightRule[]? Validated(HighlightRule[]? rules)
    {
        static string Clean(string? text, int max)
        {
            string clean = new((text ?? "").Where(c => !char.IsControl(c)).ToArray());
            return clean.Length > max ? clean[..max] : clean;
        }
        return rules?.Where(r => r is not null).Take(MaxRules).Select(r => r with
        {
            Name = Clean(r.Name, MaxName),
            Pattern = Clean(r.Pattern, MaxPattern),
            Color = Colors.Any(c => c.Key == r.Color) ? r.Color : "yellow",
        }).ToArray();
    }

    public bool IsEmpty => rules.Length == 0;

    /// <summary>The spans to recolour in one row of text; the first rule wins where matches overlap.</summary>
    public IReadOnlyList<TextHighlight> Match(string text)
    {
        List<TextHighlight>? found = null;
        foreach (var rule in rules)
        {
            if (rule.Off) continue;
            long start = Stopwatch.GetTimestamp();
            try
            {
                if (rule.Part == 0)
                {
                    foreach (var m in rule.Regex.EnumerateMatches(text))
                        if (m.Length > 0) (found ??= new()).Add(new TextHighlight(m.Index, m.Length, rule.Brush));
                }
                else
                {
                    for (var m = rule.Regex.Match(text); m.Success; m = m.NextMatch())
                        if (m.Groups[rule.Part] is { Success: true, Length: > 0 } part) (found ??= new()).Add(new TextHighlight(part.Index, part.Length, rule.Brush));
                }
            }
            catch (RegexMatchTimeoutException) { } // counted as slow below
            // The engine's own limit is only checked now and then, and not at all on short rows.
            if (Stopwatch.GetElapsedTime(start) <= RowLimit) { rule.Slow = 0; continue; }
            if (++rule.Slow < SlowRows) continue;
            rule.Off = true;
            slow.Add(rule.Key);
            // Rows are matched while the window is drawn: nothing may change on it before that is over.
            Dispatcher.UIThread.Post(() => SlowFound?.Invoke());
        }
        return found ?? (IReadOnlyList<TextHighlight>)Array.Empty<TextHighlight>();
    }

    static Regex? Compile(HighlightRule rule) => TryCompile(rule.Pattern, rule.IgnoreCase, out var regex) == null ? regex : null;

    /// <summary>Null when the pattern is usable, otherwise why not (shown next to the box).</summary>
    public static string? Check(string pattern) => TryCompile(pattern, true, out _);

    static string? TryCompile(string pattern, bool ignoreCase, out Regex? regex)
    {
        regex = null;
        if (pattern.Length == 0) return "不能为空";
        if (pattern.Length > MaxPattern) return $"最长 {MaxPattern} 个字符";
        if (pattern.Any(char.IsControl)) return "不能包含控制字符";
        try
        {
            regex = new Regex(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : 0), DefaultRowLimit);
            return null;
        }
        catch (NotSupportedException) { return "不支持的写法（环视、反向引用，或过于复杂）"; }
        catch (ArgumentException) { return "正则表达式写法不正确"; }
    }

    public static Color ColorOf(ThemeOptions palette, string key) => Color.Parse(key switch
    {
        "red" => palette.Red,
        "green" => palette.Green,
        "cyan" => palette.Cyan,
        "blue" => palette.Blue,
        "magenta" => palette.Magenta,
        _ => palette.Yellow,
    } ?? "#E2B93D");
}
