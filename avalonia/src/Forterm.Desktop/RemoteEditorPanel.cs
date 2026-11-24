using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using Forterm.Core;
namespace Forterm.Desktop;

/// <summary>Drafts remain bound to their original source and version. Never stores text on local disk.</summary>
public sealed class RemoteEditorPanel : DockPanel, IDisposable
{
    readonly Window owner; readonly TransferQueue queue; readonly SafeTransfers safe = new();
    readonly TabControl tabs = new(); readonly List<Draft> drafts = new(); readonly TextBlock state = UI.Label("双击远程普通文件以编辑。UTF-8 · 每份最多1MiB · 最多12份草稿", true);
    bool disposed;
    public RemoteEditorPanel(Window owner, TransferQueue queue)
    {
        this.owner = owner; this.queue = queue; Margin = new Thickness(6); tabs.Classes.Add("remote-drafts"); tabs.Padding = new Thickness(0);
        state.FontSize = 10; state.TextWrapping = TextWrapping.NoWrap; state.TextTrimming = TextTrimming.CharacterEllipsis; state.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) ToolTip.SetTip(state, state.Text); };
        SizeChanged += (_, _) => { foreach (var draft in drafts) draft.Title.MaxWidth = Math.Clamp(Bounds.Width - 60, 50, 170); };
        var bar = new WrapPanel();
        var save = UI.Button("保存", () => { if (tabs.SelectedItem is TabItem { Tag: Draft d }) _ = SaveAsync(d); });
        var saveAs = UI.Button("另存新名", () => { if (tabs.SelectedItem is TabItem { Tag: Draft d }) _ = SaveAsync(d, true); });
        save.MinWidth = save.Width = 60; saveAs.MinWidth = saveAs.Width = 84;
        foreach (var button in new[] { save, saveAs }) { button.Height = 26; button.FontSize = 12; button.Padding = new Thickness(4, 0); bar.Children.Add(button); }
        ToolTip.SetTip(save, "保存 · Ctrl / ⌘ + S"); SetDock(bar, Dock.Top); Children.Add(bar);
        SetDock(state, Dock.Bottom); Children.Add(state); Children.Add(tabs); tabs.SelectionChanged += (_, e) => { if (e.Source == tabs && tabs.SelectedItem is TabItem { Tag: Draft d }) Update(d); };
    }
    public bool HasUnsaved => drafts.Any(d => d.Dirty || d.Saving);
    public async Task OpenAsync(RemoteDirectoryBinding directory, string name, CancellationToken ct, string sourceTitle = "")
    {
        string path = directory.Path.TrimEnd('/') + "/" + name;
        var found = drafts.FirstOrDefault(d => d.Original.Source.SourceId == directory.SourceId && d.Original.Path == path);
        if (found != null) { tabs.SelectedItem = found.Tab; return; }
        if (drafts.Count >= 12) throw new IOException("最多12份草稿，请先关闭已保存文件");
        var document = await safe.OpenTextAsync(directory, name, ct); ct.ThrowIfCancellationRequested(); if (disposed) return;
        if (drafts.Count >= 12) throw new IOException("最多12份草稿"); Add(new SftpTextDocument(document), false, sourceTitle);
    }
    public async Task NewAsync(RemoteDirectoryBinding directory, CancellationToken ct, string sourceTitle = "")
    {
        if (drafts.Count >= 12) throw new IOException("最多12份草稿，请先关闭已保存文件");
        string? name = await AskName(ct); if (name == null) return;
        var version = await safe.CaptureRemoteAsync(directory, name, ct, RemoteText.MaxBytes);
        if (version.Stamp != null) throw new IOException("新建名称已存在，请打开现有文件或选择其他名称");
        ct.ThrowIfCancellationRequested(); if (!disposed) Add(new SftpTextDocument(new(directory, version, "", false)), true, sourceTitle);
    }
    public async Task OpenFtpAsync(FtpDirectory directory, string name, CancellationToken ct, string sourceTitle = "")
    {
        string path = directory.Path.TrimEnd('/') + "/" + name;
        var found = drafts.FirstOrDefault(d => d.Original.Source.SourceId == directory.SourceId && d.Original.Path == path);
        if (found != null) { tabs.SelectedItem = found.Tab; return; }
        if (drafts.Count >= 12) throw new IOException("最多12份草稿");
        var document = await directory.Source.OpenTextAsync(directory, name, ct); ct.ThrowIfCancellationRequested(); if (disposed) return;
        Add(new FtpTextDocument(document), false, sourceTitle);
    }
    public async Task NewFtpAsync(FtpDirectory directory, CancellationToken ct, string sourceTitle = "")
    {
        if (drafts.Count >= 12) throw new IOException("最多12份草稿");
        string? name = await AskName(ct); if (name == null) return;
        var version = await directory.Source.CaptureAsync(directory, name, ct, RemoteText.MaxBytes); if (version.Stamp != null) throw new IOException("新名称已存在");
        ct.ThrowIfCancellationRequested(); if (!disposed) Add(new FtpTextDocument(new(directory, version, "", false)), true, sourceTitle);
    }
    void Add(IRemoteTextDocument original, bool isNew, string sourceTitle)
    {
        if (disposed) return; if (drafts.Count >= 12) throw new IOException("最多12份草稿");
        var d = new Draft(original, isNew, sourceTitle); var editor = d.Editor;
        editor.ShowLineNumbers = true; editor.FontFamily = new FontFamily("Menlo,Consolas,monospace"); editor.FontSize = 14; editor.Text = original.Text;
        editor.Background = Brushes.Transparent;
        editor.Options.ConvertTabsToSpaces = false;
        // An attached property style supplies theme-specific text color without altering contents.
        editor.Classes.Add("remote-editor");
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; header.Children.Add(d.Title);
        var close = UI.Button("×", () => _ = CloseDraftAsync(d)); close.Classes.Add("icon"); close.Width = close.Height = close.MinWidth = close.MinHeight = 22; close.Padding = new Thickness(0); header.Children.Add(close); d.Title.FontSize = 12; d.Title.MaxWidth = Math.Clamp(Bounds.Width - 60, 50, 170);
        d.Tab.MinHeight = 0; d.Tab.Height = 28; d.Tab.Header = header; d.Tab.Tag = d; d.Tab.Content = editor;
        editor.TextChanged += (_, _) => Update(d);
        editor.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (e.Key == Key.S && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0) { e.Handled = true; _ = SaveAsync(d); } }, RoutingStrategies.Tunnel, handledEventsToo: true);
        d.Closed = original.Source.Closed.Register(() => Dispatcher.UIThread.Post(() => { if (!disposed && drafts.Contains(d)) { editor.IsReadOnly = true; Update(d); state.Text = "原文件来源已关闭；草稿保留为只读，不自动改投或重新认证。"; } }));
        if (!original.Source.IsAvailable) editor.IsReadOnly = true;
        drafts.Add(d); tabs.Items.Add(d.Tab); tabs.SelectedItem = d.Tab; Update(d); editor.TextArea.Focus();
    }
    static string Caption(Draft d) => (d.Dirty ? "● " : "") + Path.GetFileName(d.Original.Path) + (d.SourceTitle.Length > 0 ? " · " + d.SourceTitle : "") + (d.Saving ? " · 保存中" : "") + (d.Editor.IsReadOnly ? " · 已离线" : "");
    void Update(Draft d)
    {
        ToolTip.SetTip(d.Title, d.SourceTitle + " · " + d.Original.Path);
        d.Title.Text = Caption(d);
        if (tabs.SelectedItem != d.Tab) return;
        try { state.Text = $"{d.SourceTitle} · {d.Original.Path} · UTF-8{(d.Original.HasBom ? " BOM" : "")} · {RemoteText.DescribeNewlines(d.Editor.Text)} · {RemoteText.Encode(d.Editor.Text, d.Original.HasBom).Length} B · {(d.Dirty ? "未保存" : "已保存")}"; }
        catch (Exception e) { state.Text = e.Message; }
    }
    async Task SaveAsync(Draft d, bool saveAs = false)
    {
        if (disposed || d.Saving || !saveAs && !d.Dirty) return;
        if (d.Editor.IsReadOnly || !d.Original.Source.IsAvailable) { state.Text = "原来源已关闭，不能保存；草稿仍保留。"; return; }
        if (!saveAs && d.Original.Exists && !d.Original.CanReplace) { state.Text = "此来源未确认安全原子覆盖，请使用另存为新名称；草稿保留。"; return; }
        d.Saving = true; string text = d.Editor.Text; var original = d.Original; Update(d);
        try
        {
            var bytes = RemoteText.Encode(text, original.HasBom);
            string? newName = saveAs ? await AskName(original.Source.Closed) : null;
            if (saveAs && newName == null) return;
            string destination = newName == null ? original.Path : original.Path[..Math.Max(1, original.Path.LastIndexOf('/'))].TrimEnd('/') + "/" + newName;
            string policy = original.Source.Protocol == WorkspaceProtocol.Ftp ? "FTP仅新名称，不主动覆盖；标准协议没有原子排他保证" : newName != null || !original.Exists ? "新名称排他创建" : "仅原子替换已打开的版本，保留权限";
            bool confirmed = await UI.Confirm(owner, "确认保存远程文件", $"来源：{d.SourceTitle}\n{destination}\n{bytes.Length} B · 固定原文件来源\n{policy}\n远端变化时停止并保留草稿。", original.Source.Closed);
            if (!confirmed || disposed || !drafts.Contains(d)) return;
            IRemoteTextDocument? saved = null;
            var ticket = queue.Add(Path.GetFileName(destination) + " · 编辑保存", d.SourceTitle + " · " + destination,
                async (progress, token) => saved = newName == null ? await original.SaveAsync(text, progress, token) : await original.SaveAsNewAsync(newName, text, progress, token), original.Source.Closed);
            var result = await ticket.Completion;
            if (disposed || !drafts.Contains(d)) return;
            if (result.State != TransferState.Completed || saved == null) { state.Text = result.Message + "；未清除草稿。"; return; }
            d.Original = saved; d.New = false;
            Update(d); state.Text += " · 保存提交已确认";
        }
        catch (OperationCanceledException) { if (!disposed) state.Text = "保存取消或原来源已关闭；草稿保留。"; }
        catch (Exception e) { if (!disposed) state.Text = e.Message + "；草稿保留。"; }
        finally { d.Saving = false; if (!disposed) { d.Title.Text = Caption(d); } }
    }
    async Task CloseDraftAsync(Draft d)
    {
        if (d.Saving) { state.Text = "此草稿正在保存，请等待提交结果或在传输队列取消。"; return; }
        if (d.Dirty && !await UI.Confirm(owner, "放弃未保存草稿？", d.SourceTitle + " · " + d.Original.Path + "\n关闭后无法恢复此内存草稿。")) return;
        if (!drafts.Remove(d)) return; d.Closed.Dispose(); tabs.Items.Remove(d.Tab); d.Editor.Document = new AvaloniaEdit.Document.TextDocument();
    }
    public async Task<bool> ConfirmCloseAsync()
    {
        if (drafts.Any(d => d.Saving)) { state.Text = "有文件正在保存，请先等待结果或取消队列任务。"; return false; }
        if (!drafts.Any(d => d.Dirty)) return true;
        return await UI.Confirm(owner, "关闭会话并放弃未保存草稿？", string.Join('\n', drafts.Where(d => d.Dirty).Select(d => d.SourceTitle + " · " + d.Original.Path)) + "\n这些内存草稿将无法恢复。");
    }
    async Task<string?> AskName(CancellationToken ct)
    {
        var box = new TextBox { PlaceholderText = "例如 notes.txt" }; var window = new Window { Title = "新建远程文本文件", Width = 430, Height = 210, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.Content = new StackPanel { Spacing = 12, Margin = new Thickness(20), Children = { UI.Label("新名称（固定原来源目录；不会覆盖已存在名称）"), box, UI.Button("新建草稿", () => window.Close(box.Text)) } };
        using var cancel = ct.Register(() => Dispatcher.UIThread.Post(() => window.Close())); return await window.ShowDialog<string?>(owner);
    }
    public void Dispose() { if (disposed) return; disposed = true; foreach (var d in drafts) { d.Closed.Dispose(); d.Editor.Document = new AvaloniaEdit.Document.TextDocument(); } drafts.Clear(); tabs.Items.Clear(); }
    sealed class Draft(IRemoteTextDocument original, bool isNew, string sourceTitle)
    {
        public readonly string SourceTitle = sourceTitle; public IRemoteTextDocument Original = original; public bool New = isNew, Saving; public bool Dirty => New || Editor.Text != Original.Text;
        public readonly TextEditor Editor = new(); public readonly TextBlock Title = new() { MaxWidth = 170, TextTrimming = TextTrimming.CharacterEllipsis }; public readonly TabItem Tab = new(); public CancellationTokenRegistration Closed;
    }
}
