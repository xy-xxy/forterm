using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using XTerm.Options;

namespace Forterm.Studio.Services;

/// <summary>A language the editor can colour. <see cref="Resource"/> is one of Forterm's own definitions
/// (Syntax/*.xshd) or, with <see cref="Builtin"/>, one that AvaloniaEdit ships.</summary>
public sealed record EditorLanguage(string Id, string Title, string Resource, bool Builtin, string[] Extensions, string[] FileNames)
{
    public override string ToString() => Title;
}

/// <summary>"关键字颜色" of the built-in editor: which language a file is in, and its colours. A definition only
/// names roles (comment, keyword, key, …); the colours come from the terminal scheme and are moved until they can
/// be read on its background. Colouring never keeps a file from being shown.</summary>
public static partial class EditorSyntax
{
    /// <summary>A file with a line longer than this (minified files) is not coloured: it would stall the window.</summary>
    public const int LongestLine = 10_000;
    public static readonly string LongLineText = $"有超过 {LongestLine} 个字符的行";
    public const double LeastContrast = 4.0;

    static EditorLanguage Own(string id, string title, string extensions, string names = "") => new(id, title, id + ".xshd", false, Split(extensions), Split(names));
    static EditorLanguage Ships(string id, string title, string resource, string extensions, string names = "") => new(id, title, resource, true, Split(extensions), Split(names));
    static string[] Split(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public static readonly EditorLanguage Plain = new("Plain", "纯文本", "", false, [".txt", ".csv", ".tsv"], []);

    public static readonly EditorLanguage[] All =
    [
        Plain,
        Own("Shell", "Shell", ".sh .bash .zsh .ksh .profile .bashrc .zshrc .bash_profile .bash_aliases .zprofile .command .install", ".bashrc .zshrc .profile .bash_profile .bash_login .bash_logout .bash_aliases .zprofile .zshenv .zlogin profile bashrc bash.bashrc zshrc environment rc.local"),
        Own("YAML", "YAML", ".yml .yaml", ".clang-format .yamllint"),
        Own("Json", "JSON", ".json .jsonc .json5 .jsonl .ndjson .webmanifest .geojson", "composer.lock .babelrc .eslintrc .prettierrc"),
        Own("INI", "INI / 配置", ".ini .conf .cnf .cfg .properties .env .service .socket .timer .target .mount .desktop .repo .network .netdev .rules .editorconfig .gitconfig .npmrc .inf .reg .sysctl", ".env .gitconfig .editorconfig .npmrc .wgetrc .curlrc my.cnf config sshd_config ssh_config sudoers fstab resolv.conf nsswitch.conf login.defs redis.conf php.ini supervisord.conf"),
        Own("TOML", "TOML", ".toml", "Cargo.lock Pipfile"),
        Own("Nginx", "Nginx", ".nginx .vhost", "nginx.conf mime.types fastcgi_params fastcgi.conf proxy_params uwsgi_params"),
        Own("Dockerfile", "Dockerfile", ".dockerfile", "Dockerfile Containerfile"),
        Own("Makefile", "Makefile", ".mk .mak .make", "Makefile makefile GNUmakefile"),
        Own("Log", "日志", ".log .out .err .trace", "syslog messages dmesg catalina.out"),
        Own("SQL", "SQL", ".sql .ddl .dml .psql .mysql"),
        Ships("XML", "XML", "XML-Mode.xshd", ".xml .xsd .xsl .xslt .svg .plist .xaml .axaml .csproj .config .wsdl .pom .iml .jnlp .resx .props .targets", "pom.xml web.config"),
        Ships("HTML", "HTML", "HTML-Mode.xshd", ".html .htm .xhtml .vue .tmpl .jinja .j2"),
        Ships("CSS", "CSS", "CSS-Mode.xshd", ".css .less .scss"),
        Ships("JavaScript", "JavaScript / TypeScript", "JavaScript-Mode.xshd", ".js .mjs .cjs .jsx .ts .tsx"),
        Ships("Python", "Python", "Python-Mode.xshd", ".py .pyw .pyi", "SConstruct wscript"),
        Ships("Java", "Java / Kotlin", "Java-Mode.xshd", ".java .kt .kts .groovy .gradle .scala"),
        Own("Go", "Go", ".go"),
        Own("Rust", "Rust", ".rs"),
        Ships("C++", "C / C++", "CPP-Mode.xshd", ".c .h .cc .cpp .cxx .hpp .hxx .ino"),
        Ships("C#", "C#", "CSharp-Mode.xshd", ".cs .csx"),
        Ships("PHP", "PHP", "PHP-Mode.xshd", ".php .phtml"),
        Own("Ruby", "Ruby", ".rb .rake .gemspec .ru", "Gemfile Rakefile Vagrantfile"),
        Own("Perl", "Perl", ".pl .pm .t"),
        Own("Lua", "Lua", ".lua"),
        Ships("PowerShell", "PowerShell", "PowerShell.xshd", ".ps1 .psm1 .psd1"),
        Ships("MarkDown", "Markdown", "MarkDown-Mode.xshd", ".md .markdown", "README CHANGELOG"),
        Ships("Patch", "补丁 / Diff", "Patch-Mode.xshd", ".patch .diff"),
        Own("Crontab", "Crontab", ".cron .crontab", "crontab"),
        Own("Hosts", "Hosts", ".hosts", "hosts hosts.allow hosts.deny"),
    ];

    public static EditorLanguage Find(string id) => All.FirstOrDefault(l => l.Id == id) ?? Plain;

    /// <summary>By name, then by where it lies, then by extension, then by its first line.</summary>
    public static EditorLanguage Detect(string path, string text)
    {
        string name = path[(path.LastIndexOf('/') + 1)..], lower = name.ToLowerInvariant(), directory = path.Length > name.Length ? path[..^name.Length].ToLowerInvariant() : "/";
        string kept = Kept(lower);
        if (All.FirstOrDefault(l => l.FileNames.Any(f => f.Equals(name, StringComparison.OrdinalIgnoreCase) || f.Equals(kept, StringComparison.OrdinalIgnoreCase))) is { } named) return named;
        if (kept.StartsWith("dockerfile.", StringComparison.Ordinal)) return Find("Dockerfile");
        if (directory.Contains("/nginx/") && (kept.EndsWith(".conf", StringComparison.Ordinal) || directory.Contains("/sites-") || directory.Contains("/conf.d/") || directory.Contains("/vhost"))) return Find("Nginx");
        if (directory.Contains("/cron") && !kept.Contains('.')) return Find(directory.EndsWith("/cron.d/", StringComparison.Ordinal) || directory.Contains("/spool/") ? "Crontab" : "Shell");
        int dot = kept.LastIndexOf('.');
        if (dot >= 0 && All.FirstOrDefault(l => l.Extensions.Contains(kept[dot..])) is { } byExtension) return byExtension;
        string first = FirstLine(text);
        if (first.StartsWith("#!", StringComparison.Ordinal))
            foreach (var (word, id) in new[] { ("python", "Python"), ("node", "JavaScript"), ("perl", "Perl"), ("ruby", "Ruby"), ("php", "PHP"), ("lua", "Lua"), ("pwsh", "PowerShell"), ("make", "Makefile"), ("sh", "Shell") })
                if (first.Contains(word, StringComparison.Ordinal)) return Find(id);
        if (first.StartsWith("<?xml", StringComparison.Ordinal)) return Find("XML");
        if (first.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) || first.StartsWith("<html", StringComparison.OrdinalIgnoreCase)) return Find("HTML");
        if (first.StartsWith("<?php", StringComparison.Ordinal)) return Find("PHP");
        if (first is ['{', ..] or "[" || first.StartsWith("[{", StringComparison.Ordinal)) return Find("Json");
        if (first == "---") return Find("YAML");
        if (first.StartsWith("diff --git", StringComparison.Ordinal) || first.StartsWith("--- a/", StringComparison.Ordinal)) return Find("Patch");
        if (directory.Contains("/log/") || directory.Contains("/logs/")) return Find("Log");
        if (directory.StartsWith("/etc/", StringComparison.Ordinal) && dot < 0) return Find("INI");
        return Plain;
    }

    /// <summary>What a choice made for this file is kept under: its extension, or its whole name when it has none.</summary>
    static string KindOf(string path)
    {
        string name = Kept(path[(path.LastIndexOf('/') + 1)..].ToLowerInvariant());
        int dot = name.LastIndexOf('.');
        string kind = dot > 0 ? name[dot..] : name;
        return kind.Length <= 128 && !kind.Any(char.IsControl) ? kind : "";
    }

    /// <summary>The language the user chose by hand for files of this kind.</summary>
    public static EditorLanguage? Chosen(string path) =>
        Appearance.Current.EditorLanguages is { } chosen && chosen.TryGetValue(KindOf(path), out string? id) ? All.FirstOrDefault(l => l.Id == id) : null;

    /// <summary>Keeps a choice; choosing what would be recognised anyway forgets it.</summary>
    public static StudioPreferences Remember(StudioPreferences preferences, string path, string text, EditorLanguage language)
    {
        string kind = KindOf(path);
        if (kind.Length == 0) return preferences;
        var next = new Dictionary<string, string>(preferences.EditorLanguages ?? new Dictionary<string, string>());
        next.Remove(kind); // the newest is last
        if (Detect(path, text) != language) next[kind] = language.Id;
        return preferences with { EditorLanguages = next.Count == 0 ? null : next };
    }

    static readonly string[] KeptSuffixes = [".bak", ".old", ".orig", ".save", ".rpmsave", ".rpmnew", ".dpkg-old", ".dpkg-dist", ".dist", ".default", ".sample", ".example", ".template", ".tpl", ".swp", "~"];

    /// <summary>"nginx.conf.bak", "app.log.1", "my.cnf.2026-11-24": the name before the file was kept. A name comes
    /// from a server, so only a few endings of a bounded length are looked at.</summary>
    static string Kept(string name)
    {
        for (int i = 0; i < 4; i++)
        {
            string tail = name.Length > 32 ? name[^32..] : name;
            int cut = KeptSuffixes.FirstOrDefault(s => tail.EndsWith(s, StringComparison.Ordinal))?.Length ?? KeptDate().Match(tail).Length;
            if (cut == 0 || cut >= name.Length) return name;
            name = name[..^cut];
        }
        return name;
    }

    [GeneratedRegex(@"([._-]\d{4}-?\d{2}-?\d{2}([._-]?\d{2,6})?|\.\d{1,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex KeptDate();

    static string FirstLine(string text)
    {
        foreach (string line in text.AsSpan(0, Math.Min(text.Length, 4096)).ToString().Split('\n').Take(20))
            if (line.Trim() is { Length: > 0 } found) return found;
        return "";
    }

    public static bool TooLong(string text)
    {
        int since = 0;
        foreach (char c in text) { if (c == '\n') since = 0; else if (++since > LongestLine) return true; }
        return false;
    }

    // ---------- colours ----------

    /// <summary>The colour of a role in a scheme, readable on the scheme's background.</summary>
    public static Color ColorOf(ThemeOptions palette, string role)
    {
        Color P(string? c) => Color.Parse(c ?? "#888888");
        var color = role switch
        {
            "Comment" => P(palette.BrightBlack),
            "Keyword" => P(palette.Magenta),
            "String" or "Good" => P(palette.Green),
            "Number" or "Constant" or "Warning" => P(palette.Yellow),
            "Key" or "Function" => P(palette.Blue),
            "Type" or "Operator" => P(palette.Cyan),
            "Variable" => P(palette.Cyan),
            "Heading" => P(palette.Magenta),
            "Bad" => P(palette.Red),
            _ => P(palette.Foreground),
        };
        return Readable(color, P(palette.Foreground), P(palette.Background));
    }

    /// <summary>Moves a colour towards black or white — away from the background — until it can be read.</summary>
    public static Color Readable(Color color, Color foreground, Color background, double least = LeastContrast)
    {
        var target = TerminalSchemes.Contrast(Colors.White, background) >= TerminalSchemes.Contrast(Colors.Black, background) ? Colors.White : Colors.Black;
        for (double share = 0; share <= 1; share += 0.05)
        {
            byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * share);
            var mixed = Color.FromRgb(Mix(color.R, target.R), Mix(color.G, target.G), Mix(color.B, target.B));
            if (TerminalSchemes.Contrast(mixed, background) >= least) return mixed;
        }
        return foreground;
    }

    /// <summary>The role behind a colour name of the definitions AvaloniaEdit ships.</summary>
    public static string RoleOf(string? name)
    {
        string n = (name ?? "").ToLowerInvariant();
        bool Has(params string[] parts) => parts.Any(p => n.Contains(p, StringComparison.Ordinal));
        if (Has("comment", "docline")) return "Comment";
        if (Has("removed")) return "Bad";
        if (Has("added")) return "Good";
        if (Has("char", "string", "regex", "attributevalue", "value", "cdata", "blockquote")) return "String";
        if (Has("digit", "number", "bool", "null", "constant", "truefalse", "entity", "position")) return "Number";
        if (Has("method", "function", "command", "link", "image")) return "Function";
        if (Has("attribute", "property", "fieldname", "selector", "field")) return "Key";
        if (Has("heading", "header", "filename")) return "Heading";
        if (Has("preprocessor", "type", "class", "variable", "operator", "punctuation", "emphasis", "code")) return Has("variable") ? "Variable" : "Type";
        if (Has("tag", "keyword", "reserved", "statement", "modifier", "visibility", "access", "namespace", "strong", "this", "goto", "exception", "context", "jump", "loop", "selection", "unsafe", "parameter", "getset", "checked", "void", "global", "compound", "friend", "javascript")) return "Keyword";
        return "Keyword";
    }

    static readonly Dictionary<(string Language, string Scheme), IHighlightingDefinition?> loaded = new();

    /// <summary>The definition of a language in a scheme's colours; null = no colours. UI thread only.</summary>
    public static IHighlightingDefinition? Definition(EditorLanguage language, ThemeOptions palette, string scheme)
    {
        if (language.Resource.Length == 0) return null;
        if (loaded.TryGetValue((language.Id, scheme), out var ready)) return ready;
        IHighlightingDefinition? built = null;
        try { built = Limit(new Coloured(palette).Load(language)); }
        catch (Exception) { } // a definition that cannot be loaded costs the colours, not the file
        loaded[(language.Id, scheme)] = built;
        return built;
    }

    /// <summary>Gives every pattern of a definition a time limit; AvaloniaEdit sets none.</summary>
    static IHighlightingDefinition Limit(IHighlightingDefinition definition)
    {
        static Regex? Timed(Regex? regex) => regex == null || regex.MatchTimeout != Regex.InfiniteMatchTimeout ? regex : new Regex(regex.ToString(), regex.Options, GuardedHighlighter.LongestMatch);
        var seen = new HashSet<HighlightingRuleSet>();
        void Walk(HighlightingRuleSet? set)
        {
            if (set == null || !seen.Add(set)) return;
            foreach (var rule in set.Rules) rule.Regex = Timed(rule.Regex);
            foreach (var span in set.Spans)
            {
                span.StartExpression = Timed(span.StartExpression);
                span.EndExpression = Timed(span.EndExpression);
                Walk(span.RuleSet);
            }
        }
        Walk(definition.MainRuleSet);
        return definition;
    }

    /// <summary>Loads a definition and what it imports, with every colour replaced.</summary>
    sealed class Coloured(ThemeOptions palette) : IXshdVisitor, IHighlightingDefinitionReferenceResolver
    {
        readonly Dictionary<string, IHighlightingDefinition> imported = new(StringComparer.Ordinal);

        public IHighlightingDefinition Load(EditorLanguage language) => Load(language.Builtin ? typeof(HighlightingManager).Assembly : typeof(EditorSyntax).Assembly,
            (language.Builtin ? "AvaloniaEdit.Highlighting.Resources." : "Forterm.Studio.Syntax.") + language.Resource);

        IHighlightingDefinition Load(Assembly assembly, string resource)
        {
            using var stream = assembly.GetManifestResourceStream(resource) ?? throw new FileNotFoundException(resource);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            var xshd = HighlightingLoader.LoadXshd(reader);
            foreach (var element in xshd.Elements) element.AcceptVisitor(this);
            return HighlightingLoader.Load(xshd, this);
        }

        IHighlightingDefinition IHighlightingDefinitionReferenceResolver.GetDefinition(string name)
        {
            if (imported.TryGetValue(name, out var known)) return known;
            string resource = name switch { "XmlDoc" => "XmlDoc.xshd", "JavaScript" => "JavaScript-Mode.xshd", "CSS" => "CSS-Mode.xshd", "XML" => "XML-Mode.xshd", "HTML" => "HTML-Mode.xshd", _ => "" };
            return imported[name] = resource.Length > 0 ? Load(typeof(HighlightingManager).Assembly, "AvaloniaEdit.Highlighting.Resources." + resource) : HighlightingManager.Instance.GetDefinition(name);
        }

        object? IXshdVisitor.VisitColor(XshdColor color)
        {
            // What begins or ends something and has no colour of its own has that of the whole.
            if (color.Name == null && color.Foreground == null) return null;
            color.Foreground = new SimpleHighlightingBrush(ColorOf(palette, Roles.Contains(color.Name ?? "") ? color.Name! : RoleOf(color.Name)));
            color.Background = null;
            return null;
        }

        static readonly HashSet<string> Roles = ["Comment", "Keyword", "String", "Number", "Key", "Function", "Type", "Constant", "Variable", "Operator", "Heading", "Good", "Warning", "Bad"];

        object? IXshdVisitor.VisitRuleSet(XshdRuleSet set) { set.AcceptElements(this); return null; }
        object? IXshdVisitor.VisitRule(XshdRule rule) { rule.ColorReference.AcceptVisitor(this); return null; }
        object? IXshdVisitor.VisitKeywords(XshdKeywords keywords) { keywords.ColorReference.AcceptVisitor(this); return null; }
        object? IXshdVisitor.VisitImport(XshdImport import) { import.RuleSetReference.AcceptVisitor(this); return null; }
        object? IXshdVisitor.VisitSpan(XshdSpan span)
        {
            span.SpanColorReference.AcceptVisitor(this);
            span.BeginColorReference.AcceptVisitor(this);
            span.EndColorReference.AcceptVisitor(this);
            span.RuleSetReference.AcceptVisitor(this);
            return null;
        }
    }
}
