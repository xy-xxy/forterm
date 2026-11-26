using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media;
using Forterm.Studio.Library;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;

namespace Forterm.Studio.ViewModels;

public sealed class PresetItem(ThemePreset preset) : ObservableObject
{
    bool selected;
    public ThemePreset Preset { get; } = preset;
    public string Name => Preset.Name;
    public bool IsSelected { get => selected; set => Set(ref selected, value); }
}

public sealed class AccentItem(string color) : ObservableObject
{
    bool selected;
    public string Color { get; } = color;
    public IBrush Brush { get; } = new SolidColorBrush(Avalonia.Media.Color.Parse(color));
    public bool IsSelected { get => selected; set => Set(ref selected, value); }
}

/// <summary>A "终端配色" choice with a small preview: background, foreground and six ANSI colours.</summary>
public sealed class SchemeItem(TerminalScheme scheme)
{
    static IBrush B(string? c) => new SolidColorBrush(Color.Parse(c ?? "#000000"));
    readonly XTerm.Options.ThemeOptions palette = scheme.Palette();
    public string Name { get; } = scheme.Name;
    public IBrush Background => B(palette.Background);
    public IBrush Foreground => B(palette.Foreground);
    public IBrush[] Dots => [B(palette.Red), B(palette.Green), B(palette.Yellow), B(palette.Blue), B(palette.Magenta), B(palette.Cyan)];
    public override string ToString() => Name;
}

/// <summary>A highlight colour: the terminal scheme's own red, yellow, …</summary>
public sealed record HighlightColor(string Key, string Title)
{
    public IBrush Brush { get; } = new SolidColorBrush(Highlighter.ColorOf(TerminalSchemes.StudioDark(), Key));
}

/// <summary>One editable "自定义高亮" rule.</summary>
public sealed class HighlightRuleItem : ObservableObject
{
    readonly SettingsViewModel owner;

    public HighlightRuleItem(SettingsViewModel owner, HighlightRule rule)
    {
        this.owner = owner;
        Rule = rule;
        RemoveCommand = new RelayCommand(() => owner.RemoveRule(this));
        UpCommand = new RelayCommand(() => owner.MoveRule(this, -1));
        DownCommand = new RelayCommand(() => owner.MoveRule(this, 1));
    }

    public HighlightRule Rule { get; private set; }
    public ICommand RemoveCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand DownCommand { get; }
    public HighlightColor[] Colors => SettingsViewModel.HighlightColors;
    public bool Enabled { get => Rule.Enabled; set => Update(Rule with { Enabled = value }); }
    public string Name { get => Rule.Name; set => Update(Rule with { Name = value ?? "" }); }
    public string Pattern { get => Rule.Pattern; set => Update(Rule with { Pattern = value ?? "" }); }
    public bool IgnoreCase { get => Rule.IgnoreCase; set => Update(Rule with { IgnoreCase = value }); }
    public HighlightColor? Color
    {
        get => Colors.FirstOrDefault(c => c.Key == Rule.Color) ?? Colors[0];
        set { if (value != null) Update(Rule with { Color = value.Key }); }
    }
    /// <summary>Why the pattern matches nothing; empty when it is usable.</summary>
    public string Error => Highlighter.Check(Rule.Pattern) is { } reason ? "此规则不生效：" + reason
        : Highlighter.IsSlow(Rule) ? "此规则匹配太慢，已停用到程序退出；请改成更简单的写法" : "";
    public bool HasError => Error.Length > 0;
    internal void RefreshError() { Raise(nameof(Error)); Raise(nameof(HasError)); }

    void Update(HighlightRule next)
    {
        if (next == Rule) return;
        Rule = next;
        RefreshError();
        owner.CommitRules();
    }
}

/// <summary>One shortcut on the settings page.</summary>
public sealed class ShortcutItem : ObservableObject
{
    bool capturing;
    string error = "";

    public ShortcutItem(SettingsViewModel owner, Shortcut shortcut)
    {
        Shortcut = shortcut;
        ChangeCommand = new RelayCommand(() => { if (capturing) owner.EndCapture(); else owner.BeginCapture(this); });
        ResetCommand = new RelayCommand(() => owner.ResetShortcut(this));
        OffCommand = new RelayCommand(() => owner.SetShortcut(shortcut, ""));
    }

    public Shortcut Shortcut { get; }
    public string Title => Shortcut.Title;
    public string Keys => capturing ? "按下新的组合…" : Shortcut.IsOff ? "未设置" : Shortcut.Keys;
    public string Standard => "默认 " + Shortcuts.Show(Shortcut.Standard);
    public bool CanChange => !Shortcut.IsFixed;
    public bool IsFixed => Shortcut.IsFixed;
    public bool IsCapturing => capturing;
    public bool IsChanged => Shortcut.IsChanged;
    public bool CanTurnOff => CanChange && !Shortcut.IsOff;
    public string Error => error;
    public bool HasError => error.Length > 0;
    public ICommand ChangeCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand OffCommand { get; }

    internal void Say(string problem) { error = problem; Raise(nameof(Error)); Raise(nameof(HasError)); }

    internal void Sync(bool capturingNow, string errorNow)
    {
        (capturing, error) = (capturingNow, capturingNow ? errorNow : "");
        foreach (string name in new[] { nameof(Keys), nameof(IsCapturing), nameof(IsChanged), nameof(CanTurnOff), nameof(Error), nameof(HasError) }) Raise(name);
    }
}

/// <summary>Which cards are shown, for bindings like <c>Shown[edfont]</c>.</summary>
public sealed class ShownCards(IReadOnlySet<string> keys)
{
    public bool this[string key] => keys.Contains(key);
}

/// <summary>One searchable settings card.</summary>
public sealed record SettingEntry(string Key, string Section, string Title, string Keywords);

/// <summary>"系统配置": appearance, terminal and local terminal preferences. Every change applies
/// immediately and is saved atomically to the Studio data directory. "搜索设置" narrows both the
/// category list and the cards inside a category.</summary>
public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    public const string DefaultFont = "默认";

    static readonly (string Key, string Title)[] SectionTitles = [("theme", "主题界面"), ("terminal", "通用终端"), ("highlight", "自定义高亮"), ("quick", "快速命令"), ("editor", "编辑器"), ("files", "文件与传输"), ("layout", "布局"), ("monitor", "监控阈值"), ("keys", "快捷键"), ("lock", "锁屏"), ("local", "本地终端"), ("access", "控制器接入"), ("backup", "备份恢复"), ("about", "关于与验证说明")];

    public static readonly SettingEntry[] Entries =
    [
        new("presets", "theme", "背景", "背景 预设 主题 皮肤 明亮 暗夜 恢复默认 重置"),
        new("navcolor", "theme", "导航栏颜色", "导航栏 标题栏 顶部 颜色"),
        new("accent", "theme", "主题色", "主题色 强调色 按钮 颜色"),
        new("follow", "theme", "跟随系统主题", "跟随 系统 明亮 暗夜 深色 浅色"),
        new("uifont", "theme", "系统字体", "系统字体 界面字体 字体"),
        new("scale", "theme", "界面缩放", "界面缩放 大小 缩放"),
        new("motion", "theme", "界面动画", "界面动画 动画 动效 过渡 减少动态效果"),
        new("bgimage", "theme", "自定义背景图片", "背景 图片 壁纸 照片 自定义 透明"),
        new("opacity", "theme", "全局不透明度", "不透明度 透明 半透明 窗口"),
        new("termfont", "terminal", "终端字体", "终端字体 等宽 字体"),
        new("termsize", "terminal", "终端字号", "终端字号 字号 字体大小"),
        new("scheme", "terminal", "终端配色", "终端配色 配色 颜色 主题"),
        new("cursor", "terminal", "光标", "光标 样式 竖线 方块 下划线 闪烁"),
        new("scrollback", "terminal", "回滚行数", "回滚 行数 缓冲 历史 滚动"),
        new("rightclick", "terminal", "右键菜单", "右键 菜单 鼠标 复制 粘贴"),
        new("paste", "terminal", "粘贴确认", "粘贴 确认 多行 剪贴板 预览"),
        new("copysel", "terminal", "选中即复制", "选中 复制 剪贴板 自动"),
        new("record", "terminal", "会话记录", "会话 记录 日志 录制 文件"),
        new("idle", "terminal", "空闲保活", "空闲保活 保活 心跳 键盘 输入 超时 断开 防踢"),
        new("protect", "terminal", "会话保护", "会话保护 心跳 保活 重连"),
        new("highlight", "highlight", "自定义高亮", "自定义高亮 高亮 规则 正则 关键字 颜色"),
        new("quick", "quick", "快速命令", "快速命令 常用命令 片段 脚本"),
        new("edfont", "editor", "编辑器字体和字号", "编辑器 字体 字号 大小"),
        new("edwrap", "editor", "自动换行", "编辑器 自动换行 折行"),
        new("edindent", "editor", "缩进", "编辑器 缩进 制表符 Tab 空格"),
        new("edshow", "editor", "显示", "编辑器 行号 空白 空格 制表符 显示"),
        new("edcolour", "editor", "关键字颜色", "编辑器 关键字 颜色 着色 高亮 语言"),
        new("fsort", "files", "排序", "文件 排序 名称 大小 时间 类型"),
        new("fhidden", "files", "隐藏文件", "文件 隐藏 点开头"),
        new("fdetails", "files", "大小和时间", "文件 大小 时间 详细"),
        new("fdownload", "files", "默认下载目录", "下载 目录 桌面 文件夹 保存"),
        new("fnotify", "files", "完成提示", "传输 完成 提示 通知"),
        new("columns", "layout", "新会话显示的栏", "布局 栏 文件 监控 默认 显示"),
        new("widths", "layout", "栏宽", "布局 栏宽 宽度 恢复默认"),
        new("tabpos", "layout", "页签位置", "页签 标签 位置 顶部 底部 终端栏 本地终端"),
        new("tabrender", "layout", "标签渲染", "标签 页签 渲染 后台 终端 初始化 输出 暂停 本地终端"),
        new("limits", "monitor", "监控阈值", "监控 阈值 磁盘 CPU 内存 负载 注意 严重 告警"),
        new("keys", "keys", "快捷键", "快捷键 键盘 命令面板 热键 按键 自定义"),
        new("lockpw", "lock", "锁屏密码", "锁屏 锁定 密码 离开 隐私"),
        new("lockauto", "lock", "自动锁定", "锁屏 锁定 自动 空闲 离开 分钟 启动"),
        new("entries", "access", "入口程序", "控制器 接入 入口 xshell xftp 唤醒 路径 位置 堡垒机 SSO"),
        new("wakes", "access", "最近的唤醒", "控制器 唤醒 记录 没反应"),
        new("backup", "backup", "备份", "备份 导出 保存 配置"),
        new("restore", "backup", "恢复", "恢复 还原 导入 配置"),
        new("shell", "local", "默认程序", "默认程序 shell zsh bash cmd powershell"),
        new("shellargs", "local", "默认程序参数", "默认程序参数 参数 启动参数"),
        new("localterm", "local", "终端类型", "终端类型 TERM xterm"),
        new("about", "about", "关于", "关于 版本 数据目录 验证"),
    ];

    public static readonly string[] LocalTerminalTypes = ["xterm-256color", "xterm", "xterm-color", "screen-256color", "tmux-256color", "vt100", "linux"];
    const string DefaultTermChoice = "默认（xterm-256color）";

    public static readonly HighlightColor[] HighlightColors = Highlighter.Colors.Select(c => new HighlightColor(c.Key, c.Title)).ToArray();
    static readonly int[] ScrollbackSteps = [1000, 5000, 10_000, 20_000, 50_000];

    readonly string? directory;
    readonly QuickCommandStore? quickStore;
    StudioPreferences prefs;
    string section = "theme", search = "", saveError = "";
    string? rejectedShell, rejectedArgs;
    HashSet<string> visible = Entries.Select(e => e.Key).ToHashSet();

    readonly LibraryStore? library;
    readonly IStudioInteraction? interaction;

    public SettingsViewModel(string? directory, QuickCommandStore? quickCommands = null, LibraryStore? library = null, IStudioInteraction? interaction = null, ControllerAccess? access = null)
    {
        this.directory = directory;
        this.library = library;
        this.interaction = interaction;
        this.access = access ?? new ControllerAccess();
        quickStore = quickCommands;
        prefs = Appearance.Current;
        // Another settings window, or the title bar toggle, changes the same preferences.
        Appearance.Changed += OnChanged;
        Appearance.Saved += OnSaved;
        Highlighter.SlowFound += OnSlowRule;
        Presets = Appearance.Presets.Select(p => new PresetItem(p)).ToArray();
        Accents = Appearance.Accents.Select(a => new AccentItem(a)).ToArray();
        NavColors = Appearance.TitleBarColors.Select(a => new AccentItem(a)).ToArray();
        var installed = Appearance.InstalledFonts();
        UiFonts = new[] { DefaultFont }.Concat(installed).Concat(Missing(prefs.UiFont, installed)).ToArray();
        var monospace = Appearance.MonospaceCandidates.Where(c => installed.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        TerminalFonts = new[] { DefaultFont }.Concat(monospace).Concat(Missing(prefs.TerminalFont, monospace)).ToArray();
        Schemes = TerminalSchemes.All.Select(s => new SchemeItem(s)).ToArray();
        TerminalTypes = new[] { DefaultTermChoice }.Concat(LocalTerminalTypes).Concat(prefs.LocalTerminalType.Length > 0 && !LocalTerminalTypes.Contains(prefs.LocalTerminalType) ? [prefs.LocalTerminalType] : []).ToArray();
        SelectThemeCommand = new RelayCommand(p => { if (p is PresetItem item) Commit(p0 => p0 with { Theme = item.Name, FollowSystem = false }); });
        SelectAccentCommand = new RelayCommand(p => { if (p is AccentItem item) Commit(p0 => p0 with { Accent = item.Color }); });
        SelectNavColorCommand = new RelayCommand(p => Commit(p0 => p0 with { TitleBarColor = (p as AccentItem)?.Color ?? "" }));
        // Appearance only: every other setting is kept.
        ResetCommand = new RelayCommand(() => Commit(p0 =>
        {
            var d = new StudioPreferences();
            return p0 with { Theme = d.Theme, FollowSystem = d.FollowSystem, Accent = d.Accent, UiScale = d.UiScale, TitleBarColor = d.TitleBarColor, UiFont = d.UiFont, Motion = d.Motion };
        }));
        IdleMinutesChoices = IdleKeeper.Minutes.Append(prefs.IdleMinutes).Distinct().Order().ToArray();
        IdleHoursChoices = IdleKeeper.Hours.Append(prefs.IdleHours).Distinct().Order().ToArray();
        EditorTabs = new[] { 2, 4, 8 }.Append(prefs.EditorTab).Distinct().Order().ToArray();
        Scrollbacks = ScrollbackSteps.Append(prefs.Scrollback).Distinct().Order().ToArray();
        AddRuleCommand = new RelayCommand(() =>
        {
            if (Rules.Count >= Highlighter.MaxRules) return;
            Rules.Add(new HighlightRuleItem(this, new HighlightRule { Name = "新规则" }));
            CommitRules();
        });
        ResetRulesCommand = new RelayCommand(() => Commit(p0 => p0 with { HighlightRules = null }));
        NewQuickCommand = new RelayCommand(() => EditQuick(new QuickCommand()));
        EditQuickCommand = new RelayCommand(p => { if (p is QuickCommand c) EditQuick(c); });
        QuickUpCommand = new RelayCommand(p => { if (p is QuickCommand c) Store(s => s.Move(c.Id, -1)); });
        QuickDownCommand = new RelayCommand(p => { if (p is QuickCommand c) Store(s => s.Move(c.Id, 1)); });
        SaveQuickCommand = new RelayCommand(() => { if (editing is { } c && Store(s => s.Upsert(c with { Name = QuickName, Text = QuickText, Enter = QuickEnter, Confirm = QuickConfirm }))) EndEdit(); });
        DeleteQuickCommand = new RelayCommand(() => { if (editing is { } c && Store(s => s.Remove(c.Id))) EndEdit(); });
        CancelQuickCommand = new RelayCommand(EndEdit);
        if (quickStore != null) quickStore.Changed += OnQuickChanged;
        OnQuickChanged();
        EditorFonts = new[] { SameAsTerminal }.Concat(monospace).Concat(Missing(prefs.EditorFont, monospace)).ToArray();
        ForgetLanguagesCommand = new RelayCommand(() => Commit(p0 => p0 with { EditorLanguages = null }));
        ChooseDownloadFolderCommand = new AsyncCommand(async () => { if (interaction != null && await interaction.PickFolderAsync("选择默认下载目录") is { } folder) Commit(p0 => p0 with { DownloadFolder = folder }); });
        DesktopDownloadCommand = new RelayCommand(() => Commit(p0 => p0 with { DownloadFolder = "" }));
        ResetWidthsCommand = new RelayCommand(() => { var d = new StudioPreferences(); Commit(p0 => p0 with { FilesWidth = d.FilesWidth, MonitorWidth = d.MonitorWidth }); });
        ResetThresholdsCommand = new RelayCommand(() => Commit(p0 => p0 with { Thresholds = new() }));
        ChooseBackgroundCommand = new AsyncCommand(ChooseBackgroundAsync, e => BackgroundStatus = "没有使用这张图片：" + WorkspaceViewModel.Short(e.Message));
        RemoveBackgroundCommand = new RelayCommand(() => { Commit(p0 => p0 with { BackgroundImage = "" }); Backdrop.Forget(directory, ""); BackgroundStatus = ""; });
        SaveLockCommand = new AsyncCommand(SaveLockAsync, e => LockStatus = "没有保存：" + WorkspaceViewModel.Short(e.Message));
        RemoveLockCommand = new AsyncCommand(RemoveLockAsync, e => LockStatus = "没有移除：" + WorkspaceViewModel.Short(e.Message));
        LockNowCommand = new AsyncCommand(async () => { if (await AppLock.LockAsync() is { } why) LockStatus = why; }, e => LockStatus = "没有锁定：" + WorkspaceViewModel.Short(e.Message));
        Keys = Shortcuts.All.Select(x => new ShortcutItem(this, x)).ToArray();
        ResetKeysCommand = new RelayCommand(() => { EndCapture(); Commit(p0 => p0 with { Shortcuts = null }); });
        EntryPrograms = this.access.Programs;
        this.access.WakeUps.CollectionChanged += OnWakeUps;
        CopyEntryCommand = new AsyncCommand<EntryProgram>(async e => { if (e != null && interaction != null) { await interaction.CopyTextAsync(e.Path); EntryStatus = "已复制：" + e.Path; } }, e => EntryStatus = "没有复制：" + WorkspaceViewModel.Short(e.Message));
        OpenEntryFolderCommand = new AsyncCommand(async () => { if (interaction != null) await interaction.OpenFolderAsync(this.access.Folder); }, e => EntryStatus = "没有打开：" + WorkspaceViewModel.Short(e.Message));
        BackupCommand = new AsyncCommand(BackupAsync, e => BackupStatus = "没有备份：" + WorkspaceViewModel.Short(e.Message));
        OpenBackupCommand = new AsyncCommand(OpenBackupAsync, e => BackupStatus = "没有读取：" + WorkspaceViewModel.Short(e.Message));
        RestoreCommand = new AsyncCommand(RestoreAsync, e => BackupStatus = "没有完成恢复：" + WorkspaceViewModel.Short(e.Message));
        CancelRestoreCommand = new RelayCommand(() => { opened = null; RaiseBackup(); });
        ShowSectionCommand = new RelayCommand(p => Section = p as string ?? "theme");
        ClearSearchCommand = new RelayCommand(() => SearchText = "");
        Sync();
    }

    /// <summary>A saved font that is not installed (or not offered) stays visible instead of vanishing.</summary>
    static IEnumerable<string> Missing(string saved, IReadOnlyCollection<string> offered) =>
        saved.Length > 0 && !offered.Contains(saved, StringComparer.OrdinalIgnoreCase) ? [saved] : [];

    /// <summary>The list's own spelling of a saved value (font names differ only in case on some systems).</summary>
    static string Pick(string[] list, string saved) =>
        saved.Length == 0 ? DefaultFont : list.FirstOrDefault(x => string.Equals(x, saved, StringComparison.OrdinalIgnoreCase)) ?? saved;

    public PresetItem[] Presets { get; }
    public AccentItem[] Accents { get; }
    public AccentItem[] NavColors { get; }
    public string[] UiFonts { get; }
    public string[] TerminalFonts { get; }
    public SchemeItem[] Schemes { get; }
    public string[] TerminalTypes { get; }
    public ICommand SelectThemeCommand { get; }
    public ICommand SelectAccentCommand { get; }
    public ICommand SelectNavColorCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand ShowSectionCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public int[] Scrollbacks { get; }
    public ICommand AddRuleCommand { get; }
    public ICommand ResetRulesCommand { get; }
    public ICommand NewQuickCommand { get; }
    public ICommand EditQuickCommand { get; }
    public ICommand QuickUpCommand { get; }
    public ICommand QuickDownCommand { get; }
    public ICommand SaveQuickCommand { get; }
    public ICommand DeleteQuickCommand { get; }
    public ICommand CancelQuickCommand { get; }

    public string Section
    {
        get => section;
        set
        {
            if (!Set(ref section, SectionTitles.Any(s => s.Key == value) ? value : "theme")) return;
            EndCapture();
            foreach (string name in new[] { nameof(IsTheme), nameof(IsTerminal), nameof(IsHighlight), nameof(IsQuick), nameof(IsKeys), nameof(IsLocal), nameof(IsAbout), nameof(IsEditor), nameof(IsFiles), nameof(IsLayout), nameof(IsMonitor), nameof(IsBackup), nameof(IsAccess), nameof(IsLock) }) Raise(name);
            if (section == "lock") _ = ReadLockAsync();
        }
    }
    public bool IsHighlight => section == "highlight";
    public bool IsQuick => section == "quick";
    public bool IsKeys => section == "keys";
    public bool IsEditor => section == "editor";
    public bool IsFiles => section == "files";
    public bool IsLayout => section == "layout";
    public bool IsMonitor => section == "monitor";
    public bool IsBackup => section == "backup";
    public bool IsAccess => section == "access";
    public bool IsLock => section == "lock";
    public bool IsTheme => section == "theme";
    public bool IsTerminal => section == "terminal";
    public bool IsLocal => section == "local";
    public bool IsAbout => section == "about";

    // 搜索设置
    public string SearchText
    {
        get => search;
        set
        {
            if (!Set(ref search, value ?? "")) return;
            string q = search.Trim();
            visible = Entries.Where(e => q.Length == 0 || Matches(e, q)).Select(e => e.Key).ToHashSet();
            if (q.Length > 0 && !Entries.Any(e => e.Section == section && visible.Contains(e.Key)) && Entries.FirstOrDefault(e => visible.Contains(e.Key)) is { } first)
                Section = first.Section;
            foreach (string name in new[]
            {
                nameof(ShowThemeSection), nameof(ShowTerminalSection), nameof(ShowLocalSection), nameof(ShowAboutSection), nameof(NoMatches), nameof(IsSearching),
                nameof(ShowPresets), nameof(ShowNavColor), nameof(ShowAccent), nameof(ShowFollow), nameof(ShowUiFont), nameof(ShowScale),
                nameof(ShowTermFont), nameof(ShowTermSize), nameof(ShowScheme), nameof(ShowProtect), nameof(ShowShell), nameof(ShowShellArgs), nameof(ShowLocalTerm), nameof(ShowAbout),
                nameof(Shown), nameof(ShowEditorSection), nameof(ShowFilesSection), nameof(ShowLayoutSection), nameof(ShowMonitorSection), nameof(ShowBackupSection), nameof(ShowAccessSection),
                nameof(ShowHighlightSection), nameof(ShowQuickSection), nameof(ShowKeysSection), nameof(ShowLockSection), nameof(ShowMotion), nameof(ShowCursor), nameof(ShowScrollback), nameof(ShowRightClick), nameof(ShowPaste), nameof(ShowCopySelect), nameof(ShowRecord),
            }) Raise(name);
        }
    }

    static bool Matches(SettingEntry e, string q)
    {
        string section = SectionTitles.First(s => s.Key == e.Section).Title;
        string text = e.Title + " " + e.Keywords + " " + section;
        // "终端 字体": every word must match, in any order.
        return q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    bool SectionHas(string key) => Entries.Any(e => e.Section == key && visible.Contains(e.Key));
    public bool IsSearching => search.Trim().Length > 0;
    public bool NoMatches => visible.Count == 0;
    public bool ShowThemeSection => SectionHas("theme");
    public bool ShowTerminalSection => SectionHas("terminal");
    public bool ShowHighlightSection => SectionHas("highlight");
    public bool ShowQuickSection => SectionHas("quick");
    public bool ShowKeysSection => SectionHas("keys");
    public bool ShowEditorSection => SectionHas("editor");
    public bool ShowFilesSection => SectionHas("files");
    public bool ShowLayoutSection => SectionHas("layout");
    public bool ShowMonitorSection => SectionHas("monitor");
    public bool ShowBackupSection => SectionHas("backup");
    public bool ShowAccessSection => SectionHas("access");
    public bool ShowLockSection => SectionHas("lock");
    /// <summary>Which cards the search leaves, by key.</summary>
    public ShownCards Shown => new(visible);
    public bool ShowMotion => visible.Contains("motion");
    public bool ShowLocalSection => SectionHas("local");
    public bool ShowAboutSection => SectionHas("about");
    public bool ShowPresets => visible.Contains("presets");
    public bool ShowNavColor => visible.Contains("navcolor");
    public bool ShowAccent => visible.Contains("accent");
    public bool ShowFollow => visible.Contains("follow");
    public bool ShowUiFont => visible.Contains("uifont");
    public bool ShowScale => visible.Contains("scale");
    public bool ShowTermFont => visible.Contains("termfont");
    public bool ShowTermSize => visible.Contains("termsize");
    public bool ShowScheme => visible.Contains("scheme");
    public bool ShowProtect => visible.Contains("protect");
    public bool ShowCursor => visible.Contains("cursor");
    public bool ShowScrollback => visible.Contains("scrollback");
    public bool ShowRightClick => visible.Contains("rightclick");
    public bool ShowPaste => visible.Contains("paste");
    public bool ShowCopySelect => visible.Contains("copysel");
    public bool ShowRecord => visible.Contains("record");
    public bool ShowShell => visible.Contains("shell");
    public bool ShowShellArgs => visible.Contains("shellargs");
    public bool ShowLocalTerm => visible.Contains("localterm");
    public bool ShowAbout => visible.Contains("about");

    // 主题界面
    public bool UseMotion { get => prefs.Motion; set => Commit(p0 => p0 with { Motion = value }); }
    public bool FollowSystem { get => prefs.FollowSystem; set => Commit(p0 => p0 with { FollowSystem = value }); }
    public double UiScalePercent { get => Math.Round(prefs.UiScale * 100); set => Commit(p0 => p0 with { UiScale = Math.Round(value / 10) / 10 }); }
    public string UiScaleText => $"{UiScalePercent:0}%";
    public bool NavColorIsDefault => prefs.TitleBarColor.Length == 0;
    public string UiFont
    {
        get => Pick(UiFonts, prefs.UiFont);
        // null is a ComboBox that cannot find the value in its list, never a choice of the user's.
        set { if (value != null) Commit(p0 => p0 with { UiFont = value == DefaultFont ? "" : value }); }
    }

    // 通用终端
    public double TerminalFontSize { get => prefs.TerminalFontSize; set => Commit(p0 => p0 with { TerminalFontSize = Math.Round(value) }); }
    public string TerminalFont
    {
        get => Pick(TerminalFonts, prefs.TerminalFont);
        set { if (value != null) Commit(p0 => p0 with { TerminalFont = value == DefaultFont ? "" : value }); }
    }
    public SchemeItem? Scheme
    {
        get => Schemes.FirstOrDefault(s => s.Name == prefs.TerminalScheme) ?? Schemes[0];
        set { if (value != null) Commit(p0 => p0 with { TerminalScheme = value.Name }); }
    }

    public string[] CursorStyles { get; } = Appearance.CursorStyles.Select(c => c.Title).ToArray();
    public string? CursorStyle
    {
        get => Appearance.CursorStyles.First(c => c.Key == prefs.CursorStyle).Title;
        set { if (value != null) Commit(p0 => p0 with { CursorStyle = Appearance.CursorStyles.First(c => c.Title == value).Key }); }
    }
    public bool CursorBlink { get => prefs.CursorBlink; set => Commit(p0 => p0 with { CursorBlink = value }); }
    public int? Scrollback
    {
        get => prefs.Scrollback;
        set { if (value is { } lines) Commit(p0 => p0 with { Scrollback = lines }); }
    }
    static readonly string[] RightClicks = ["显示菜单", "复制 / 粘贴"];
    public string[] RightClickChoices => RightClicks;
    public string? RightClick
    {
        get => prefs.RightClickMenu ? RightClicks[0] : RightClicks[1];
        set { if (value != null) Commit(p0 => p0 with { RightClickMenu = value == RightClicks[0] }); }
    }
    public bool ConfirmPaste { get => prefs.ConfirmPaste; set => Commit(p0 => p0 with { ConfirmPaste = value }); }
    public bool CopyOnSelect { get => prefs.CopyOnSelect; set => Commit(p0 => p0 with { CopyOnSelect = value }); }

    // 空闲保活
    public bool IdleKeepAlive { get => prefs.IdleKeepAlive; set => Commit(p0 => p0 with { IdleKeepAlive = value }); }
    public int[] IdleMinutesChoices { get; }
    public int? IdleMinutes { get => prefs.IdleMinutes; set { if (value is { } m && m != prefs.IdleMinutes) Commit(p0 => p0 with { IdleMinutes = m }); } }
    public string[] IdleInputs { get; } = IdleKeeper.Inputs.Select(i => i.Title).ToArray();
    public string? IdleInput { get => IdleKeeper.Inputs.First(i => i.Key == prefs.IdleInput).Title; set { if (value != null) Commit(p0 => p0 with { IdleInput = IdleKeeper.Inputs.First(i => i.Title == value).Key }); } }
    public string IdleInputNote => IdleKeeper.Inputs.First(i => i.Key == prefs.IdleInput).Note;
    public int[] IdleHoursChoices { get; }
    public int? IdleHours { get => prefs.IdleHours; set { if (value is { } h && h != prefs.IdleHours) Commit(p0 => p0 with { IdleHours = h }); } }
    public bool IdleFullScreen { get => prefs.IdleFullScreen; set => Commit(p0 => p0 with { IdleFullScreen = value }); }

    // 自定义高亮
    public bool Highlight { get => prefs.Highlight; set => Commit(p0 => p0 with { Highlight = value }); }
    public ObservableCollection<HighlightRuleItem> Rules { get; } = new();
    public bool CanAddRule => Rules.Count < Highlighter.MaxRules;

    internal void CommitRules() => Commit(p0 => p0 with { HighlightRules = Rules.Select(r => r.Rule).ToArray() });
    internal void RemoveRule(HighlightRuleItem item) { if (Rules.Remove(item)) CommitRules(); }
    internal void MoveRule(HighlightRuleItem item, int by)
    {
        int from = Rules.IndexOf(item), to = from + by;
        if (from < 0 || to < 0 || to >= Rules.Count) return;
        Rules.Move(from, to);
        CommitRules();
    }

    /// <summary>Follows rules changed elsewhere. The rows being edited here are left alone: rebuilding them
    /// would take the focus out of the box on every keystroke.</summary>
    void SyncRules()
    {
        var rules = prefs.HighlightRules ?? Highlighter.Defaults();
        if (!Highlighter.Same(Highlighter.Validated(Rules.Select(r => r.Rule).ToArray()), Highlighter.Validated(rules)))
        {
            Rules.Clear();
            foreach (var rule in rules) Rules.Add(new HighlightRuleItem(this, rule));
        }
        Raise(nameof(CanAddRule));
    }

    // 快速命令
    QuickCommand? editing;
    string quickName = "", quickText = "", quickError = "";
    bool quickEnter = true, quickConfirm;
    public IReadOnlyList<QuickCommand> QuickCommands => quickStore?.Commands.ToArray() ?? [];
    public bool HasQuickStore => quickStore != null;
    public bool NoQuickCommands => QuickCommands.Count == 0 && editing == null;
    public bool IsEditingQuick => editing != null;
    public bool IsExistingQuick => editing is { } c && QuickCommands.Any(x => x.Id == c.Id);
    public string QuickTitle => IsExistingQuick ? "编辑快速命令" : "新建快速命令";
    public string QuickName { get => quickName; set => Set(ref quickName, value ?? ""); }
    public string QuickText { get => quickText; set => Set(ref quickText, value ?? ""); }
    public bool QuickEnter { get => quickEnter; set => Set(ref quickEnter, value); }
    public bool QuickConfirm { get => quickConfirm; set => Set(ref quickConfirm, value); }
    public string QuickError { get => quickError; private set { if (Set(ref quickError, value)) Raise(nameof(HasQuickError)); } }
    public bool HasQuickError => QuickError.Length > 0;

    void EditQuick(QuickCommand command)
    {
        editing = command;
        QuickName = command.Name;
        QuickText = command.Text.Replace("\n", Environment.NewLine);
        QuickEnter = command.Enter;
        QuickConfirm = command.Confirm;
        QuickError = "";
        RaiseQuick();
    }

    void EndEdit()
    {
        editing = null;
        QuickError = "";
        RaiseQuick();
    }

    /// <summary>Runs a change of the stored list; false (and the reason on the page) when it was refused or
    /// could not be written.</summary>
    bool Store(Action<QuickCommandStore> change)
    {
        if (quickStore == null) return false;
        try { change(quickStore); QuickError = ""; return true; }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            QuickError = "没有保存：" + WorkspaceViewModel.Short(e.Message);
            return false;
        }
    }

    void OnQuickChanged()
    {
        // Deleted in another window while its form is open here: saving would bring it back unnoticed.
        if (editing is { } c && wasStored && quickStore!.Commands.All(x => x.Id != c.Id)) editing = null;
        RaiseQuick();
    }

    bool wasStored;

    void RaiseQuick()
    {
        wasStored = IsExistingQuick;
        foreach (string name in new[] { nameof(QuickCommands), nameof(NoQuickCommands), nameof(IsEditingQuick), nameof(IsExistingQuick), nameof(QuickTitle) }) Raise(name);
    }

    // 本地终端
    const string RejectedNote = "包含控制字符或超过 1024 个字符，没有保存";
    static bool Accepts(string value) => value.Length <= 1024 && !value.Any(char.IsControl);

    public string LocalShell
    {
        get => rejectedShell ?? prefs.LocalShell;
        set
        {
            if (!Accepts(value ?? "")) { rejectedShell = value; Raise(nameof(LocalShellStatus)); return; }
            rejectedShell = null;
            Commit(p0 => p0 with { LocalShell = value ?? "" });
        }
    }
    public string LocalShellStatus
    {
        get
        {
            if (rejectedShell != null) return RejectedNote;
            string shell = WorkspaceViewModel.ResolveLocalShell(prefs.LocalShell);
            return prefs.LocalShell.Trim().Length > 0 && !File.Exists(prefs.LocalShell.Trim())
                ? $"找不到 {prefs.LocalShell.Trim()}，将使用 {shell}"
                : $"已找到可用 shell：{shell}";
        }
    }
    public string LocalShellArgs
    {
        get => rejectedArgs ?? prefs.LocalShellArgs;
        set
        {
            if (!Accepts(value ?? "")) { rejectedArgs = value; Raise(nameof(LocalShellArgsStatus)); Raise(nameof(LocalShellArgsInvalid)); return; }
            rejectedArgs = null;
            Commit(p0 => p0 with { LocalShellArgs = value ?? "" });
        }
    }
    WorkspaceViewModel.LocalLaunch Plan => WorkspaceViewModel.PlanLocal(prefs, OperatingSystem.IsWindows());
    public string LocalShellArgsStatus
    {
        get
        {
            if (rejectedArgs != null) return RejectedNote;
            var plan = Plan;
            if (plan.Note != null) return plan.Note;
            if (plan.Args.Length > 0 && OperatingSystem.IsWindows()) return "将原样附加到程序的命令行（不经过 shell）：" + plan.Args[0];
            return plan.Args.Length == 0 ? "不传参数" : "将按 " + plan.Args.Length + " 个参数直接传给程序（不经过 shell）：" + string.Join(" ", plan.Args.Select(a => "[" + a + "]"));
        }
    }
    public bool LocalShellArgsInvalid => rejectedArgs != null || Plan.Note != null;
    public string LocalTerminalType
    {
        get => prefs.LocalTerminalType.Length > 0 ? prefs.LocalTerminalType : DefaultTermChoice;
        set { if (value != null) Commit(p0 => p0 with { LocalTerminalType = value == DefaultTermChoice ? "" : value }); }
    }

    // 编辑器
    public const string SameAsTerminal = "与终端相同";
    public string[] EditorFonts { get; }
    public string EditorFont
    {
        get => prefs.EditorFont.Length == 0 ? SameAsTerminal : EditorFonts.FirstOrDefault(x => string.Equals(x, prefs.EditorFont, StringComparison.OrdinalIgnoreCase)) ?? prefs.EditorFont;
        set { if (value != null) Commit(p0 => p0 with { EditorFont = value == SameAsTerminal ? "" : value }); }
    }
    public double EditorFontSize { get => prefs.EditorFontSize; set => Commit(p0 => p0 with { EditorFontSize = Math.Round(value) }); }
    public bool EditorWrap { get => prefs.EditorWrap; set => Commit(p0 => p0 with { EditorWrap = value }); }
    public bool EditorWhitespace { get => prefs.EditorWhitespace; set => Commit(p0 => p0 with { EditorWhitespace = value }); }
    public bool EditorLineNumbers { get => prefs.EditorLineNumbers; set => Commit(p0 => p0 with { EditorLineNumbers = value }); }
    public bool EditorColours { get => prefs.EditorColours; set => Commit(p0 => p0 with { EditorColours = value }); }
    public bool EditorSpaces { get => prefs.EditorSpaces; set => Commit(p0 => p0 with { EditorSpaces = value }); }
    /// <summary>The same list as long as the window is open: a ComboBox writes its choice back when it gets another.</summary>
    public int[] EditorTabs { get; }
    public int? EditorTab { get => prefs.EditorTab; set { if (value is { } columns && columns != prefs.EditorTab) Commit(p0 => p0 with { EditorTab = columns }); } }
    public bool HasLanguages => prefs.EditorLanguages is { Count: > 0 };
    public string LanguagesText => prefs.EditorLanguages is { Count: > 0 } l ? "手动选过语言的文件：" + string.Join("、", l.TakeLast(6).Select(x => $"{x.Key} → {EditorSyntax.Find(x.Value).Title}")) + (l.Count > 6 ? $" 等 {l.Count} 种" : "") : "在编辑器里手动选的语言会记住，用于同类文件。";
    public ICommand ForgetLanguagesCommand { get; }

    // 文件与传输
    public string[] FileSorts { get; } = Appearance.FileSorts.Select(x => x.Title).ToArray();
    public string? FileSort { get => Appearance.FileSorts.First(x => x.Key == prefs.FileSort).Title; set { if (value != null) Commit(p0 => p0 with { FileSort = Appearance.FileSorts.First(x => x.Title == value).Key }); } }
    public bool FileSortDescending { get => prefs.FileSortDescending; set => Commit(p0 => p0 with { FileSortDescending = value }); }
    public bool ShowHiddenFiles { get => prefs.ShowHidden; set => Commit(p0 => p0 with { ShowHidden = value }); }
    public bool FileDetails { get => prefs.FileDetails; set => Commit(p0 => p0 with { FileDetails = value }); }
    public bool NotifyTransfers { get => prefs.NotifyTransfers; set => Commit(p0 => p0 with { NotifyTransfers = value }); }
    public bool HasDownloadFolder => prefs.DownloadFolder.Length > 0;
    public string DownloadFolderText => prefs.DownloadFolder.Length == 0 ? "桌面" : folderMissing == prefs.DownloadFolder ? prefs.DownloadFolder + "（现在找不到，下载时会提示）" : prefs.DownloadFolder;
    string? folderMissing, folderAsked;

    /// <summary>Not on the UI thread: a network folder that is gone takes its time to say so.</summary>
    async void LookForFolder()
    {
        string folder = prefs.DownloadFolder;
        if (folder == folderAsked) return;
        folderAsked = folder;
        bool missing = folder.Length > 0 && await Task.Run(() => !Directory.Exists(folder));
        if (folder != folderAsked) return;
        folderMissing = missing ? folder : null;
        Raise(nameof(DownloadFolderText));
    }
    public ICommand ChooseDownloadFolderCommand { get; }
    public ICommand DesktopDownloadCommand { get; }

    // 主题界面: "自定义背景图片", "全局不透明度"
    string backgroundStatus = "";
    public bool HasBackgroundImage => prefs.BackgroundImage.Length > 0;
    public double BackgroundPercent { get => Math.Round(prefs.BackgroundStrength * 100); set => Commit(p0 => p0 with { BackgroundStrength = Math.Round(value) / 100 }); }
    public string BackgroundPercentText => $"{BackgroundPercent:0}%";
    public double OpacityPercent { get => Math.Round(prefs.WindowOpacity * 100); set => Commit(p0 => p0 with { WindowOpacity = Math.Round(value) / 100 }); }
    public string OpacityText => $"{OpacityPercent:0}%";
    public string BackgroundStatus { get => backgroundStatus; private set { if (Set(ref backgroundStatus, value)) Raise(nameof(HasBackgroundStatus)); } }
    public bool HasBackgroundStatus => backgroundStatus.Length > 0;
    public ICommand ChooseBackgroundCommand { get; private set; } = null!;
    public ICommand RemoveBackgroundCommand { get; private set; } = null!;

    async Task ChooseBackgroundAsync()
    {
        if (interaction == null || directory == null) { BackgroundStatus = "没有数据目录，不能保存图片"; return; }
        if (await interaction.PickOpenFileAsync("选择背景图片") is not { } file) return;
        BackgroundStatus = "正在读取图片…";
        try
        {
            string name = await Backdrop.ImportAsync(file, directory);
            Commit(p0 => p0 with { BackgroundImage = name });
            Backdrop.Forget(directory, name);
            BackgroundStatus = Backdrop.Load(directory, prefs) == null ? "图片已复制，但读不出来；请换一张" : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            BackgroundStatus = "没有使用这张图片：" + WorkspaceViewModel.Short(e.Message);
        }
    }

    // 布局
    public bool OpenFiles { get => prefs.OpenFiles; set => Commit(p0 => p0 with { OpenFiles = value }); }
    public static readonly string[] TabPositions = ["顶部", "底部"];
    public string TabPosition { get => TabPositions[prefs.TabsAtBottom ? 1 : 0]; set { if (value != null) Commit(p0 => p0 with { TabsAtBottom = value == TabPositions[1] }); } }
    public static readonly string[] TabRenders = ["显示时再开始（默认）", "立即开始"];
    public string TabRender { get => TabRenders[prefs.StartHiddenTerminals ? 1 : 0]; set { if (value != null) Commit(p0 => p0 with { StartHiddenTerminals = value == TabRenders[1] }); } }

    // 锁屏
    public static readonly string[] LockChoices = ["不自动锁定", "5 分钟", "10 分钟", "15 分钟", "30 分钟", "60 分钟"];
    static readonly int[] LockMinutesOf = [0, 5, 10, 15, 30, 60];
    string lockCurrent = "", lockNew = "", lockRepeat = "", lockStatus = "";
    bool? lockSet;
    public string LockChoice
    {
        get => Array.IndexOf(LockMinutesOf, prefs.LockMinutes) is >= 0 and var i ? LockChoices[i] : $"{prefs.LockMinutes} 分钟";
        set { if (value != null && Array.IndexOf(LockChoices, value) is >= 0 and var i) Commit(p0 => p0 with { LockMinutes = LockMinutesOf[i] }); }
    }
    public bool LockAtStart { get => prefs.LockAtStart; set => Commit(p0 => p0 with { LockAtStart = value }); }
    public string LockCurrent { get => lockCurrent; set => Set(ref lockCurrent, value ?? ""); }
    public string LockNew { get => lockNew; set => Set(ref lockNew, value ?? ""); }
    public string LockRepeat { get => lockRepeat; set => Set(ref lockRepeat, value ?? ""); }
    public string LockStatus { get => lockStatus; private set { if (Set(ref lockStatus, value)) Raise(nameof(HasLockStatus)); } }
    public bool HasLockStatus => lockStatus.Length > 0;
    /// <summary>Whether a lock password is set; null while that is not known.</summary>
    public bool HasLockPassword => lockSet == true;
    public bool NoLockPassword => lockSet == false;
    public string LockState => lockSet switch { true => "已设置锁屏密码。", false => "还没有设置锁屏密码：设置后才能锁定。", _ => "正在读取…" };
    public string LockKeys => Shortcuts.Lock.IsOff ? "" : "快捷键 " + Shortcuts.Lock.Keys + "，也可以在命令面板里选“锁定 Forterm”。";
    public ICommand SaveLockCommand { get; private set; } = null!;
    public ICommand RemoveLockCommand { get; private set; } = null!;
    public ICommand LockNowCommand { get; private set; } = null!;

    void RaiseLock() { foreach (string name in new[] { nameof(HasLockPassword), nameof(NoLockPassword), nameof(LockState) }) Raise(name); }

    async Task ReadLockAsync()
    {
        try { lockSet = await AppLock.HasPasswordAsync(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException) { lockSet = null; LockStatus = "读不到锁屏密码：" + WorkspaceViewModel.Short(e.Message); }
        RaiseLock();
    }

    async Task SaveLockAsync()
    {
        if (lockNew != lockRepeat) { LockStatus = "两次输入的新密码不一样"; return; }
        string? problem = await AppLock.SetPasswordAsync(lockCurrent, lockNew);
        LockCurrent = LockNew = LockRepeat = "";
        LockStatus = problem ?? "锁屏密码已保存";
        await ReadLockAsync();
    }

    async Task RemoveLockAsync()
    {
        string? problem = await AppLock.RemovePasswordAsync(lockCurrent);
        LockCurrent = LockNew = LockRepeat = "";
        LockStatus = problem ?? "锁屏密码已移除，不会再锁定";
        await ReadLockAsync();
    }
    public bool OpenMonitor { get => prefs.OpenMonitor; set => Commit(p0 => p0 with { OpenMonitor = value }); }
    public ICommand ResetWidthsCommand { get; }

    // 监控阈值
    void Limit(Func<MonitorThresholds, MonitorThresholds> change) => Commit(p0 => p0 with { Thresholds = change(p0.Thresholds) });
    public double DiskWarning { get => prefs.Thresholds.DiskWarning; set => Limit(t => t with { DiskWarning = Math.Round(value), DiskDanger = Math.Max(t.DiskDanger, Math.Round(value)) }); }
    public double DiskDanger { get => prefs.Thresholds.DiskDanger; set => Limit(t => t with { DiskDanger = Math.Round(value), DiskWarning = Math.Min(t.DiskWarning, Math.Round(value)) }); }
    public double CpuWarning { get => prefs.Thresholds.CpuWarning; set => Limit(t => t with { CpuWarning = Math.Round(value), CpuDanger = Math.Max(t.CpuDanger, Math.Round(value)) }); }
    public double CpuDanger { get => prefs.Thresholds.CpuDanger; set => Limit(t => t with { CpuDanger = Math.Round(value), CpuWarning = Math.Min(t.CpuWarning, Math.Round(value)) }); }
    public double MemoryWarning { get => prefs.Thresholds.MemoryWarning; set => Limit(t => t with { MemoryWarning = Math.Round(value), MemoryDanger = Math.Min(t.MemoryDanger, Math.Round(value)) }); }
    public double MemoryDanger { get => prefs.Thresholds.MemoryDanger; set => Limit(t => t with { MemoryDanger = Math.Round(value), MemoryWarning = Math.Max(t.MemoryWarning, Math.Round(value)) }); }
    public double LoadWarning { get => prefs.Thresholds.LoadWarning; set => Limit(t => t with { LoadWarning = Math.Round(value, 1), LoadDanger = Math.Max(t.LoadDanger, Math.Round(value, 1)) }); }
    public double LoadDanger { get => prefs.Thresholds.LoadDanger; set => Limit(t => t with { LoadDanger = Math.Round(value, 1), LoadWarning = Math.Min(t.LoadWarning, Math.Round(value, 1)) }); }
    public string LimitsText => $"磁盘 ≥{DiskWarning:0}% / {DiskDanger:0}%，CPU ≥{CpuWarning:0}% / {CpuDanger:0}%，可用内存 <{MemoryWarning:0}% / {MemoryDanger:0}%，负载 ≥ 核数的 {LoadWarning:0.0} / {LoadDanger:0.0} 倍";
    public bool LimitsChanged => prefs.Thresholds != new MonitorThresholds();
    public ICommand ResetThresholdsCommand { get; }

    // 快捷键
    public ShortcutItem[] Keys { get; }
    public ICommand ResetKeysCommand { get; }
    public bool KeysChanged => prefs.Shortcuts != null;
    ShortcutItem? capturing;
    public bool IsCapturing => capturing != null;

    internal void BeginCapture(ShortcutItem item) { capturing = item; foreach (var k in Keys) k.Sync(k == item, ""); }
    internal void EndCapture() { if (capturing == null) return; capturing = null; foreach (var k in Keys) k.Sync(false, ""); }

    /// <summary>A key pressed while the user chooses a new combination. True when it was used for that.</summary>
    public bool Press(Avalonia.Input.Key key, Avalonia.Input.KeyModifiers modifiers)
    {
        if (capturing is not { } item) return false;
        if (key == Avalonia.Input.Key.Escape && modifiers == Avalonia.Input.KeyModifiers.None) { EndCapture(); return true; }
        if (Shortcuts.IsUnfinished(key)) return true;
        var gesture = new Avalonia.Input.KeyGesture(key, modifiers);
        string? problem = Shortcuts.Check(gesture) ?? (Shortcuts.Owner(gesture) is { } other && other != item.Shortcut ? $"已用于“{other.Title}”" : null);
        if (problem != null) { item.Sync(true, $"{Shortcuts.Show(gesture)} 不能用：{problem}"); return true; }
        SetShortcut(item.Shortcut, Shortcuts.Same(gesture, item.Shortcut.Standard) ? null : gesture.ToString());
        return true;
    }

    /// <summary>Back to as it comes — unless another one has taken those keys meanwhile.</summary>
    internal void ResetShortcut(ShortcutItem item)
    {
        EndCapture();
        if (Shortcuts.Owner(item.Shortcut.Standard) is { } other && other != item.Shortcut) { item.Say($"默认的 {Shortcuts.Show(item.Shortcut.Standard)} 已用于“{other.Title}”，请先改它"); return; }
        SetShortcut(item.Shortcut, null);
    }

    /// <summary>null = as it comes; "" = off.</summary>
    internal void SetShortcut(Shortcut shortcut, string? gesture)
    {
        capturing = null;
        Commit(p0 =>
        {
            var next = new Dictionary<string, string>(p0.Shortcuts ?? new Dictionary<string, string>());
            if (gesture == null) next.Remove(shortcut.Id); else next[shortcut.Id] = gesture;
            return p0 with { Shortcuts = next };
        });
    }

    // 备份恢复
    StudioBackup? opened;
    string backupStatus = "";
    bool restoreSettings = true, restoreQuick = true, restoreConnections = true, restoreTrusted;
    // 控制器接入
    readonly ControllerAccess access;
    string entryStatus = "";
    public EntryProgram[] EntryPrograms { get; }
    public bool EntriesMissing => EntryPrograms.Any(e => !e.IsThere);
    public string EntryStatus { get => entryStatus; private set { if (Set(ref entryStatus, value)) Raise(nameof(HasEntryStatus)); } }
    public bool HasEntryStatus => EntryStatus.Length > 0;
    public ICommand CopyEntryCommand { get; }
    public ICommand OpenEntryFolderCommand { get; }
    public ObservableCollection<WakeUp> WakeUps => access.WakeUps;
    public bool HasWakeUps => WakeUps.Count > 0;
    void OnWakeUps(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Raise(nameof(HasWakeUps));

    public ICommand BackupCommand { get; }
    public ICommand OpenBackupCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand CancelRestoreCommand { get; }
    public bool CanBackup => library != null && quickStore != null && interaction != null;
    public string BackupStatus { get => backupStatus; private set { if (Set(ref backupStatus, value)) Raise(nameof(HasBackupStatus)); } }
    public bool HasBackupStatus => BackupStatus.Length > 0;
    public bool HasOpened => opened != null;
    public bool RestoreSettings { get => restoreSettings; set { if (Set(ref restoreSettings, value)) RaiseBackup(); } }
    public bool RestoreQuick { get => restoreQuick; set { if (Set(ref restoreQuick, value)) RaiseBackup(); } }
    public bool RestoreConnections { get => restoreConnections; set { if (Set(ref restoreConnections, value)) RaiseBackup(); } }
    /// <summary>The user says the backup is their own.</summary>
    public bool RestoreTrusted { get => restoreTrusted; set { if (Set(ref restoreTrusted, value)) RaiseBackup(); } }
    RestoreChoice Choice => new(restoreSettings, restoreQuick, restoreConnections, restoreTrusted);
    public string OpenedText => opened is not { } b ? "" : $"备份于 {b.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)}{(b.App.Length > 0 ? "，Forterm Studio " + b.App : "")}：{(b.HasSettings ? "设置，" : "")}{b.QuickCommandCount} 条快速命令，{b.ConnectionCount} 个连接，{b.TunnelCount} 个隧道";
    public string PlanText => opened is not { } b || quickStore == null || library == null ? "" : Say(b.Preview(Choice, quickStore, library), "将");

    static string Say(RestoreSummary s, string verb)
    {
        var parts = new List<string>();
        if (s.Settings) parts.Add("替换全部设置");
        if (s.QuickCommands + s.QuickKnown + s.QuickLeft > 0) parts.Add($"添加 {s.QuickCommands} 条快速命令" + (s.QuickKnown > 0 ? $"（{s.QuickKnown} 条已有）" : "") + (s.QuickLeft > 0 ? $"（{s.QuickLeft} 条超过 {QuickCommandStore.MaxCommands} 条的上限，不添加）" : ""));
        if (s.Connections + s.ConnectionsKnown > 0) parts.Add($"添加 {s.Connections} 个连接" + (s.ConnectionsKnown > 0 ? $"（{s.ConnectionsKnown} 个已有，保持不变）" : ""));
        if (s.Groups > 0) parts.Add($"添加 {s.Groups} 个分组");
        if (s.Tunnels + s.TunnelsLeft > 0) parts.Add($"添加 {s.Tunnels} 个隧道" + (s.TunnelsLeft > 0 ? $"（{s.TunnelsLeft} 个本地端口已被占用，不添加）" : ""));
        return parts.Count == 0 ? "没有要恢复的内容" : verb + string.Join("，", parts) + (s.Stripped > 0 ? $"。{s.Stripped} 项会自动执行或影响本机的内容不恢复" : "");
    }

    async Task BackupAsync()
    {
        if (!CanBackup || await interaction!.PickSaveFileAsync("备份到", $"forterm-studio-backup-{DateTime.Now:yyyyMMdd}.json") is not { } path) return;
        byte[] bytes = StudioBackup.Create(Appearance.Current, quickStore!, library!, DateTimeOffset.Now);
        await Task.Run(() => StudioBackup.Write(path, bytes));
        BackupStatus = $"已备份到 {path}（{bytes.Length / 1024.0:0.#} KB，不含密码、口令、动态令牌种子和会话环境变量）";
    }

    async Task OpenBackupAsync()
    {
        if (!CanBackup || await interaction!.PickOpenFileAsync("选择备份文件") is not { } path) return;
        opened = null;
        opened = await Task.Run(() => StudioBackup.Read(path));
        (restoreSettings, restoreQuick, restoreConnections, restoreTrusted) = (opened.HasSettings, opened.QuickCommandCount > 0, opened.ConnectionCount > 0, false);
        BackupStatus = "";
        RaiseBackup();
    }

    async Task RestoreAsync()
    {
        if (opened is not { } backup || !CanBackup) return;
        if (!await interaction!.ConfirmAsync("恢复备份", PlanText + "。\n密码和口令不在备份里，需要重新输入。" + (restoreTrusted ? "\n你确认这是自己做的备份：其中会自动执行的命令也会恢复。" : ""), "恢复", danger: restoreTrusted || restoreSettings)) return;
        var summary = backup.Restore(Choice, quickStore!, library!, directory, DateTime.Now);
        opened = null;
        BackupStatus = Say(summary, "已") + (summary.Settings ? "。原来的设置保存在数据目录的 preferences.before-restore-*.json" : "");
        RaiseBackup();
    }

    void RaiseBackup() { foreach (string name in new[] { nameof(HasOpened), nameof(OpenedText), nameof(PlanText), nameof(RestoreSettings), nameof(RestoreQuick), nameof(RestoreConnections), nameof(RestoreTrusted) }) Raise(name); }

    public string Version => StudioPaths.Version;
    public string DataDirectory => directory ?? "（未保存）";

    void Commit(Func<StudioPreferences, StudioPreferences> change)
    {
        // Always from the live preferences: this window may be older than another change.
        prefs = change(Appearance.Current).Validated();
        Appearance.Apply(prefs);
        try
        {
            Appearance.Save(directory, prefs);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            SaveError = "设置已生效，但没有保存到磁盘（重启后会丢失）：" + WorkspaceViewModel.Short(e.Message);
        }
        Sync();
    }

    void OnChanged(StudioPreferences next)
    {
        prefs = next;
        Sync();
    }

    void OnSlowRule() { foreach (var rule in Rules) rule.RefreshError(); }

    /// <summary>Stops following preference changes (called when the window closes).</summary>
    public void Dispose()
    {
        Appearance.Changed -= OnChanged;
        Appearance.Saved -= OnSaved;
        Highlighter.SlowFound -= OnSlowRule;
        access.WakeUps.CollectionChanged -= OnWakeUps;
        if (quickStore != null) quickStore.Changed -= OnQuickChanged;
    }

    /// <summary>Every save writes all preferences, so one from another window also covers a change that
    /// this window could not save.</summary>
    void OnSaved(string savedTo) { if (savedTo == directory) SaveError = ""; }

    /// <summary>Why the last change could not be written to disk; empty when it was.</summary>
    public string SaveError { get => saveError; private set { if (Set(ref saveError, value)) Raise(nameof(HasSaveError)); } }
    public bool HasSaveError => SaveError.Length > 0;

    void Sync()
    {
        foreach (var p in Presets) p.IsSelected = !prefs.FollowSystem && p.Name == prefs.Theme;
        foreach (var a in Accents) a.IsSelected = a.Color == prefs.Accent;
        foreach (var n in NavColors) n.IsSelected = n.Color == prefs.TitleBarColor;
        foreach (string name in new[]
        {
            nameof(FollowSystem), nameof(UseMotion), nameof(UiScalePercent), nameof(UiScaleText), nameof(TerminalFontSize), nameof(LocalShell), nameof(LocalShellStatus),
            nameof(NavColorIsDefault), nameof(UiFont), nameof(TerminalFont), nameof(Scheme), nameof(LocalShellArgs), nameof(LocalShellArgsStatus),
            nameof(LocalShellArgsInvalid), nameof(LocalTerminalType), nameof(CursorStyle), nameof(CursorBlink), nameof(Scrollback), nameof(RightClick),
            nameof(ConfirmPaste), nameof(CopyOnSelect), nameof(Highlight),
            nameof(IdleKeepAlive), nameof(IdleMinutes), nameof(IdleInput), nameof(IdleInputNote), nameof(IdleHours), nameof(IdleFullScreen),
            nameof(EditorFont), nameof(EditorFontSize), nameof(EditorWrap), nameof(EditorWhitespace), nameof(EditorLineNumbers), nameof(EditorColours), nameof(EditorSpaces), nameof(EditorTab), nameof(HasLanguages), nameof(LanguagesText),
            nameof(FileSort), nameof(FileSortDescending), nameof(ShowHiddenFiles), nameof(FileDetails), nameof(NotifyTransfers), nameof(HasDownloadFolder), nameof(DownloadFolderText),
            nameof(HasBackgroundImage), nameof(BackgroundPercent), nameof(BackgroundPercentText), nameof(OpacityPercent), nameof(OpacityText), nameof(TabPosition), nameof(TabRender), nameof(LockChoice), nameof(LockAtStart), nameof(LockKeys),
            nameof(OpenFiles), nameof(OpenMonitor), nameof(DiskWarning), nameof(DiskDanger), nameof(CpuWarning), nameof(CpuDanger), nameof(MemoryWarning), nameof(MemoryDanger), nameof(LoadWarning), nameof(LoadDanger), nameof(LimitsText), nameof(LimitsChanged), nameof(KeysChanged),
        }) Raise(name);
        foreach (var k in Keys) k.Sync(k == capturing, k.Error);
        LookForFolder();
        SyncRules();
    }
}
