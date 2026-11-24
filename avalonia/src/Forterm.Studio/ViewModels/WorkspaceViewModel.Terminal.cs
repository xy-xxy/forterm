using System.Globalization;
using System.Windows.Input;
using Forterm.Studio.Launch;
using Forterm.Studio.Library;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;

namespace Forterm.Studio.ViewModels;

/// <summary>Terminal commands: the right-click menu, paste review, find, quick commands and recording.
/// Everything here acts on the terminal tab that is shown.</summary>
public sealed partial class WorkspaceViewModel
{
    QuickCommandStore? quickCommands;
    string quickFilter = "";
    bool pasteAsking, quickTypingConfirmed;

    public TerminalFindViewModel Find { get; private set; } = null!;
    public ICommand CopyTerminalCommand { get; private set; } = null!;
    public ICommand PasteTerminalCommand { get; private set; } = null!;
    public ICommand SelectAllTerminalCommand { get; private set; } = null!;
    public ICommand FindInTerminalCommand { get; private set; } = null!;
    public ICommand ClearTerminalCommand { get; private set; } = null!;
    public ICommand ScrollToBottomCommand { get; private set; } = null!;
    public ICommand RecordTerminalCommand { get; private set; } = null!;
    public ICommand RunQuickCommand { get; private set; } = null!;
    public ICommand ManageQuickCommandsCommand { get; private set; } = null!;
    public ICommand TerminalSettingsCommand { get; private set; } = null!;

    /// <summary>Opens the settings window at a category; set by the main window.</summary>
    public Action<string>? ShowSettings { get; set; }
    /// <summary>Changes and stores the preferences; set by the main window.</summary>
    public Action<Func<StudioPreferences, StudioPreferences>>? SavePreferences { get; set; }

    void InitTerminalCommands()
    {
        Find = new TerminalFindViewModel(() => ShownTerminal?.Control);
        AsyncCommand Command(Func<TerminalTabViewModel, Task> run) => new(async () => { if (ShownTerminal is { } tab) await run(tab); }, e => Status = Short(e.Message));
        CopyTerminalCommand = Command(async tab => await tab.Control.CopyAsync());
        PasteTerminalCommand = Command(tab => tab.Control.PasteAsync());
        SelectAllTerminalCommand = Command(tab => { tab.Control.SelectAll(); return Task.CompletedTask; });
        FindInTerminalCommand = new RelayCommand(() => Find.Open());
        ClearTerminalCommand = Command(tab => { tab.Control.ClearScreen(); return Task.CompletedTask; });
        ScrollToBottomCommand = Command(tab => { tab.Control.ScrollToBottom(); return Task.CompletedTask; });
        RecordTerminalCommand = Command(ToggleRecordingAsync);
        RunQuickCommand = new AsyncCommand<QuickCommand>(RunQuickAsync, e => Status = "快速命令没有发送：" + Short(e.Message));
        ManageQuickCommandsCommand = new RelayCommand(() => ShowSettings?.Invoke("quick"));
        TerminalSettingsCommand = new RelayCommand(() => ShowSettings?.Invoke("terminal"));
        ToggleIdleCommand = new RelayCommand(() => { if (!CanIdle) return; idleChoice = !IsIdleOn; RaiseIdle(); });
        if (CanIdle) Appearance.Changed += OnIdlePreferences;
    }

    /// <summary>The terminal tab the centre column shows; null while an editor is shown.</summary>
    TerminalTabViewModel? ShownTerminal => activeTab as TerminalTabViewModel;
    public bool IsTerminalShown => ShownTerminal != null;
    /// <summary>The shown terminal has a running shell.</summary>
    public bool IsTerminalLive => ShownTerminal?.Control.IsLive == true;
    public bool HasTerminalSelection => ShownTerminal?.Control.HasSelection == true;
    public bool IsRecording => ShownTerminal?.IsRecording == true;
    public string RecordHeader => IsRecording ? "停止记录会话" : "记录会话到文件…";

    void OnActiveTabChanged()
    {
        Raise(nameof(IsTerminalShown));
        Raise(nameof(IsRecording));
        Find.Retarget();
    }

    /// <summary>The right-click menu is about to open: its state is read once, here.</summary>
    public void RefreshTerminalMenu()
    {
        foreach (string name in new[] { nameof(IsTerminalLive), nameof(HasTerminalSelection), nameof(IsRecording), nameof(RecordHeader), nameof(IdleHeader) }) Raise(name);
    }

    // ---------- "空闲保活" ----------

    bool? idleChoice;
    TimeProvider time = TimeProvider.System;
    internal TimeProvider Time { get => time; set { time = value; foreach (var tab in Terminals) tab.Time = value; } }
    public ICommand ToggleIdleCommand { get; private set; } = null!;
    /// <summary>A terminal on a server. The local shell is never kept alive.</summary>
    public bool CanIdle => Kind is not (WorkspaceKind.Local or WorkspaceKind.Ftp);
    /// <summary>By the settings, unless the user decided for this session.</summary>
    public bool IsIdleOn => CanIdle && (idleChoice ?? Appearance.Current.IdleKeepAlive);
    public bool ShowsIdle => IsIdleOn && IsConnected && Kept.Any();
    public string IdleHeader => IsIdleOn ? "关闭空闲保活（本会话）" : "打开空闲保活（本会话）";
    /// <summary>The terminals there is something to keep in: not those whose shell has ended.</summary>
    IEnumerable<TerminalTabViewModel> Kept => Terminals.Where(t => t.IsIdleKept);
    int IdleCount => Terminals.Sum(t => t.Idle?.Count ?? 0);
    bool IsIdleOver => Kept.All(t => t.Idle?.IsOver(Appearance.Current) == true);
    /// <summary>Why a terminal is not kept although it is time; "" when nothing holds back.</summary>
    string IdleHeld => IsIdleOn ? Kept.Select(t => t.IdleHeld).FirstOrDefault(h => h.Length > 0) ?? "" : "";
    public string IdleText => IsIdleOver ? "空闲保活：已到时限" : $"空闲保活 {Appearance.Current.IdleMinutes} 分钟" + (IdleCount is > 0 and var n ? $" · {n} 次" : "") + (IdleHeld.Length > 0 ? " · 暂缓" : "");
    public string IdleTip
    {
        get
        {
            var p = Appearance.Current;
            var last = Terminals.Select(t => t.Idle?.Last).Where(t => t != null).Max();
            return $"{p.IdleMinutes} 分钟没有键盘输入时自动发送“{(Services.IdleKeeper.Inputs.FirstOrDefault(i => i.Key == p.IdleInput) ?? Services.IdleKeeper.Inputs[0]).Title.Replace("（推荐）", "")}”，最长 {p.IdleHours} 小时"
                + (last is { } at ? $"\n本会话已发送 {IdleCount} 次，最近 {at.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}" : "") + (IsIdleOver ? "\n已超过最长时间，按任意键后重新计时" : "") + (IdleHeld is { Length: > 0 } held ? "\n现在没有发送：" + held + (Terminals.Count > 1 ? "（其中一个终端）" : "") : "") + "\n点击：只在本会话关闭";
        }
    }

    void RaiseIdle() { foreach (string name in new[] { nameof(IsIdleOn), nameof(ShowsIdle), nameof(IdleHeader), nameof(IdleText), nameof(IdleTip) }) Raise(name); }
    void OnIdlePreferences(StudioPreferences _) => RaiseIdle();

    /// <summary>Looks after the terminals of this session; called every few seconds.</summary>
    internal void IdleTick()
    {
        if (!IsIdleOn || !IsConnected || disposed != 0) return;
        foreach (var tab in Terminals.ToArray()) _ = IdleAsync(tab);
        RaiseIdle(); // a shell may have ended, too
    }

    async Task IdleAsync(TerminalTabViewModel tab)
    {
        try { if (await tab.KeepAliveWhenIdleAsync(Appearance.Current, lifetime.Token)) RaiseIdle(); }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException) { }
        catch (Exception e) { if (disposed == 0 && IsConnected && Terminals.Contains(tab)) Status = "空闲保活已停止（这个终端）：" + Short(e.Message); RaiseIdle(); }
    }

    // ---------- paste ----------

    /// <summary>Every paste into a terminal comes through here (shortcut, menu, right click). Control
    /// characters are always removed; "粘贴确认" decides whether the user sees the text first.</summary>
    async Task<string?> ReviewPasteAsync(TerminalTabViewModel tab, string text)
    {
        if (text.Length > PasteGuard.MaxLength) { Status = "剪贴板内容超过 1 MiB，没有粘贴"; return null; }
        var review = PasteGuard.Review(text);
        if (review.Send.Length == 0) { Status = "剪贴板里只有控制字符，没有粘贴"; return null; }
        if (Appearance.Current.ConfirmPaste && review.NeedsConfirmation)
        {
            if (pasteAsking) return null;
            pasteAsking = true;
            try { if (!await interaction.ReviewPasteAsync(review)) return null; }
            finally { pasteAsking = false; }
        }
        else if (review.Removed > 0) Status = $"粘贴时去掉了 {review.Removed} 个控制字符";
        return disposed == 0 && Terminals.Contains(tab) ? review.Send : null;
    }

    // ---------- quick commands ----------

    public QuickCommandStore? QuickCommands
    {
        get => quickCommands;
        set
        {
            if (quickCommands != null) quickCommands.Changed -= OnQuickCommandsChanged;
            quickCommands = value;
            if (quickCommands != null) quickCommands.Changed += OnQuickCommandsChanged;
            OnQuickCommandsChanged();
        }
    }

    void OnQuickCommandsChanged() { Raise(nameof(VisibleQuickCommands)); Raise(nameof(QuickCommandsHint)); Raise(nameof(HasQuickCommands)); }

    public string QuickFilter { get => quickFilter; set { if (Set(ref quickFilter, value ?? "")) OnQuickCommandsChanged(); } }

    /// <summary>Every word of the filter must appear in the name or the text.</summary>
    public IReadOnlyList<QuickCommand> VisibleQuickCommands =>
        (quickCommands?.Commands ?? []).Where(c => quickFilter.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(w => c.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || c.Text.Contains(w, StringComparison.OrdinalIgnoreCase))).ToArray();
    public bool HasQuickCommands => quickCommands?.Commands.Count > 0;
    public string QuickCommandsHint => VisibleQuickCommands.Count > 0 ? "" : HasQuickCommands ? "没有匹配的快速命令" : "还没有快速命令";

    /// <summary>Types a quick command into the shown terminal (user action). A controller-launched session
    /// asks first, once per workspace, as for "执行 CD 命令到终端".</summary>
    async Task RunQuickAsync(QuickCommand? command)
    {
        if (command == null) return;
        if (ShownTerminal is not { Control.IsLive: true } tab) { Status = "没有可输入的终端，快速命令没有发送"; return; }
        long shell = tab.Control.SessionId;
        bool first = IsLaunchedSession && !quickTypingConfirmed;
        if (first || command.Confirm)
        {
            string shown = command.Text.Length > 600 ? command.Text[..600] + "…" : command.Text;
            string intro = first ? "这是控制器唤醒的临时会话，Studio 不会自动向它写入任何内容" + (IsIdleOn ? "（你打开的空闲保活除外）" : "") + "。\n现在将按你的操作输入下面的快速命令（堡垒机可能会审计；若终端是堡垒机菜单而不是 shell，请取消）。\n本会话中之后的快速命令不再询问（设为“每次执行前确认”的除外）。\n\n" : "";
            if (!await interaction.ConfirmAsync($"执行“{command.Name}”", intro + shown, command.Enter ? "执行" : "输入", danger: false)) return;
            if (first) quickTypingConfirmed = true;
        }
        // Confirmed for the shell the user was looking at, not for one started since.
        if (disposed != 0 || !Terminals.Contains(tab) || !tab.Control.IsLive || tab.Control.SessionId != shell) { Status = "终端已关闭，快速命令没有发送"; return; }
        await tab.Control.SendInputAsync(command.Typed, lifetime.Token);
        // Writing to a shell that is going away fails silently.
        if (disposed != 0 || !tab.Control.IsLive || tab.Control.SessionId != shell) { Status = "终端已关闭，快速命令可能没有发送"; return; }
        Status = $"已输入快速命令“{command.Name}”";
        if (!KeyboardFocus.IsInField(tab.Control)) tab.Control.FocusTerminal();
    }

    // ---------- recording ----------

    /// <summary>"会话记录": starts writing the shown terminal's output to a file the user picks, or stops.</summary>
    async Task ToggleRecordingAsync(TerminalTabViewModel tab)
    {
        if (tab.IsRecording)
        {
            string path = tab.RecordingPath;
            Status = (tab.StopRecording() ? "已停止记录：" : "已停止记录，剩余内容仍在写入：") + path;
            string folder = Path.GetDirectoryName(path) ?? "";
            interaction.Toast(new ToastMessage(ToastKind.Success, "已停止记录：" + Path.GetFileName(path), "打开文件夹", () => _ = interaction.OpenFolderAsync(folder)));
            return;
        }
        if (!tab.Control.IsLive) { Status = "终端没有运行，无法记录"; return; }
        if (IsLaunchedSession && !await interaction.ConfirmAsync("记录会话到文件",
                "这是控制器唤醒的临时会话。从现在起，这个终端的输出会以明文写入你选择的本机文件，直到停止记录或关闭终端。\n请先确认允许在本机保留堡垒机会话内容。", "继续", danger: false)) return;
        string name = new(Title.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());
        string? file = await interaction.PickSaveFileAsync("记录终端输出到新文件", $"{(name.Length > 60 ? name[..60] : name)}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        if (file == null) return;
        if (disposed != 0 || !Terminals.Contains(tab) || tab.IsRecording) return;
        string header = $"# Forterm 会话记录 {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {OneLine(Title)} {OneLine(Endpoint)} {tab.Title}（只含输出；明文）";
        try
        {
            tab.StartRecording(file, header, reason => Status = "记录已停止：" + Short(reason));
            Status = "正在记录到 " + file;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { Status = "无法开始记录：" + Short(e.Message); }
    }

    /// <summary>Closing: every recording takes no more output, so that they all finish writing side by side.</summary>
    public void EndRecordings() { foreach (var tab in Terminals) tab.EndRecording(); }

    static string OneLine(string text) => new(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
}
