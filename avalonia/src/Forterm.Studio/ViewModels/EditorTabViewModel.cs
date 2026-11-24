using System.Windows.Input;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using Forterm.Core;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;

namespace Forterm.Studio.ViewModels;

/// <summary>Built-in editor for one remote UTF-8 text file (at most 1 MiB). The draft lives only in
/// memory. Saving is confirmed, goes through the connection's serial queue and re-checks the remote
/// version before an atomic replace; a conflict or failure keeps the draft. When the source closes
/// the draft turns read-only and is never re-targeted to another connection.</summary>
public sealed class EditorTabViewModel : CenterTabViewModel
{
    readonly TransferQueue queue;
    readonly IStudioInteraction ui;
    readonly Action<Guid, TransferKind> register;
    readonly Action<string> refresh;
    readonly CancellationTokenRegistration sourceClosed;
    IRemoteTextDocument original;
    bool saving, readOnly, dirty;
    string status = "";

    public EditorTabViewModel(IRemoteTextDocument document, string sourceTitle, TransferQueue queue, IStudioInteraction ui, Action<Guid, TransferKind> register, Action<string> refresh)
    {
        original = document;
        SourceTitle = sourceTitle;
        this.queue = queue;
        this.ui = ui;
        this.register = register;
        this.refresh = refresh;
        Document = new TextDocument(document.Text);
        stopped = EditorSyntax.TooLong(document.Text) ? EditorSyntax.LongLineText : "";
        language = EditorSyntax.Chosen(document.Path) ?? EditorSyntax.Detect(document.Path, document.Text);
        Find = new EditorFindViewModel(this);
        Document.TextChanged += (_, _) => UpdateDirty();
        readOnly = !document.Source.IsAvailable;
        sourceClosed = document.Source.Closed.Register(() => Dispatcher.UIThread.Post(() =>
        {
            IsReadOnly = true;
            Status = "原文件来源已关闭；草稿保留为只读，不会改投其他连接或重新认证。";
        }));
        SaveCommand = new AsyncCommand(() => SaveAsync(saveAs: false), e => Status = WorkspaceViewModel.Short(e.Message) + "；草稿保留。");
        SaveAsCommand = new AsyncCommand(() => SaveAsync(saveAs: true), e => Status = WorkspaceViewModel.Short(e.Message) + "；草稿保留。");
    }

    public TextDocument Document { get; }
    public string SourceTitle { get; }
    public string Path => original.Path;
    EditorLanguage language;
    string stopped;
    public EditorLanguage[] Languages => EditorSyntax.All;
    /// <summary>Found by name and content; the user may choose another. null is a ComboBox losing its list.</summary>
    public EditorLanguage? Language
    {
        get => language;
        set
        {
            if (value == null || !Set(ref language, value)) return;
            SavePreferences?.Invoke(p => EditorSyntax.Remember(p, original.Path, Document.Text, value));
            // Chosen by hand: worth another try, unless the reason is still there.
            StopColouring(EditorSyntax.TooLong(Document.Text) ? EditorSyntax.LongLineText : "");
            Raise(nameof(ColouredLanguage));
        }
    }
    /// <summary>What is coloured: nothing when colouring would stall the window.</summary>
    public EditorLanguage ColouredLanguage => stopped.Length > 0 || !Appearance.Current.EditorColours ? EditorSyntax.Plain : language;
    public string LanguageTip => stopped.Length > 0 ? stopped + "，没有着色" : !Appearance.Current.EditorColours ? "关键字颜色在系统配置里关闭了" : "关键字颜色按这种语言显示（只影响显示）；手动选的会记住，用于同类文件";
    /// <summary>Find, replace, go to line.</summary>
    public EditorFindViewModel Find { get; }
    /// <summary>Set by the workspace; null keeps choices to this tab.</summary>
    public Action<Func<StudioPreferences, StudioPreferences>>? SavePreferences { get; set; }
    internal void Report(string text) => Status = text;
    internal void FollowSettings() { Raise(nameof(ColouredLanguage)); Raise(nameof(LanguageTip)); }

    /// <summary>Ends colouring for this editor, with the reason; "" = colour again.</summary>
    public void StopColouring(string reason)
    {
        if (stopped == reason) return;
        stopped = reason;
        if (reason.Length > 0) Status = reason + "，已关闭这个文件的着色；内容不受影响。";
        Raise(nameof(ColouredLanguage));
        Raise(nameof(LanguageTip));
    }

    public string FileName => System.IO.Path.GetFileName(original.Path);
    public string Extension => System.IO.Path.GetExtension(original.Path);
    public override string Title => (dirty ? "● " : "") + FileName;
    public override object Content => this;
    public IRemoteFileSource Source => original.Source;
    public bool IsDirty => dirty;
    public bool IsSaving { get => saving; private set { if (Set(ref saving, value)) Raise(nameof(CanEdit)); } }
    public bool IsReadOnly { get => readOnly; private set { if (Set(ref readOnly, value)) Raise(nameof(CanEdit)); } }
    public bool CanEdit => !readOnly && !saving;
    public string Status { get => status; private set => Set(ref status, value); }
    public string Details => $"{(original.HasBom ? "UTF-8 BOM" : "UTF-8")} · {RemoteText.DescribeNewlines(Document.Text)} · {SourceTitle}";
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }

    void UpdateDirty()
    {
        bool now = Document.Text != original.Text;
        if (now == dirty) return;
        dirty = now;
        Raise(nameof(IsDirty));
        Raise(nameof(Title));
    }

    async Task SaveAsync(bool saveAs)
    {
        if (saving || !saveAs && !dirty) return;
        if (readOnly || !original.Source.IsAvailable) { Status = "原来源已关闭，不能保存；草稿仍保留。"; return; }
        string? newName = null;
        if (saveAs)
        {
            newName = await ui.PromptTextAsync("另存为", "新文件名（同一目录，不覆盖已有文件）", FileName);
            if (string.IsNullOrWhiteSpace(newName)) return;
            newName = FileActions.ValidName(newName);
        }
        else if (original.Exists && !original.CanReplace)
        {
            Status = original.Source.Protocol == WorkspaceProtocol.Ftp
                ? "FTP 没有安全的原子覆盖：请使用“另存为”新名称；草稿保留。"
                : "服务器不支持原子替换（posix-rename）：请使用“另存为”新名称；草稿保留。";
            return;
        }
        string text = Document.Text;
        string folder = original.Path[..Math.Max(1, original.Path.LastIndexOf('/'))];
        string destination = newName == null ? original.Path : FileActions.Join(folder, newName);
        int bytes = RemoteText.Encode(text, original.HasBom).Length;
        string policy = newName != null || !original.Exists ? "以新名称排他创建，不覆盖已有文件" : "只替换打开时的那个版本；远程已变化则停止";
        if (!await ui.ConfirmAsync("保存远程文件", $"{destination}\n{bytes} 字节 · {SourceTitle}\n{policy}。", "保存", danger: false)) return;
        IsSaving = true;
        Status = "保存中…";
        try
        {
            var current = original;
            IRemoteTextDocument? saved = null;
            var ticket = queue.Add(System.IO.Path.GetFileName(destination) + " · 编辑保存", SourceTitle + " · " + destination, async (progress, ct) =>
            {
                saved = newName == null ? await current.SaveAsync(text, progress, ct) : await current.SaveAsNewAsync(newName, text, progress, ct);
            }, current.Source.Closed);
            register(ticket.Id, TransferKind.Operation);
            var result = await ticket.Completion;
            if (result.State != TransferState.Completed || saved == null)
            {
                Status = "保存未完成：" + WorkspaceViewModel.Short(result.Message) + "；草稿保留。";
                return;
            }
            if (newName == null) original = saved;
            refresh(folder);
            Status = newName == null ? "已保存，远程版本已核对" : $"已另存为 {newName}；当前草稿仍对应原文件";
            UpdateDirty();
            Raise(nameof(Details));
        }
        finally { IsSaving = false; }
    }

    /// <summary>True when the tab may close (saved, or the user chose to discard the draft).</summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (saving) { Status = "正在保存，请等待结果或在传输管理中取消。"; return false; }
        return !dirty || await ui.ConfirmAsync("放弃未保存的修改？", $"{SourceTitle} · {Path}\n关闭后无法恢复这份内存草稿。", "放弃修改", danger: true);
    }

    public override void Dispose()
    {
        sourceClosed.Dispose();
        Document.Text = "";
    }
}
