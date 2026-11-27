using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Studio.Controls;
using Forterm.Studio.Mvvm;
using Forterm.Studio.Services;
using Forterm.Studio.ViewModels;
using Iciclecreek.Terminal;

namespace Forterm.Studio.Views;

/// <summary>Lays out the three columns. The visible centre column (terminal, or files when
/// the terminal is hidden) takes the remaining width; the side columns keep their own widths.</summary>
public sealed partial class WorkspaceView : UserControl
{
    enum Pane { None, Files, Terminal, Monitor }

    const double MinSide = 200, MinCentre = 260, SplitterWidth = 4;
    readonly Grid columns;
    readonly StudioPane files, terminal, monitor;
    readonly Thumb leftSplitter, rightSplitter;
    readonly TextBlock allHidden;
    WorkspaceViewModel? model;
    /// <summary>The session whose files the file column shows: the same one, or the one it is linked to.</summary>
    WorkspaceViewModel? fileModel;
    Flyout? quickFlyout;
    double filesWidth = Appearance.Current.FilesWidth, monitorWidth = Appearance.Current.MonitorWidth;
    Pane maximized = Pane.None, star = Pane.Terminal;

    public WorkspaceView()
    {
        AvaloniaXamlLoader.Load(this);
        columns = this.FindControl<Grid>("Columns")!;
        files = this.FindControl<StudioPane>("FilesPane")!;
        // The header holds the title, its ×, the link button and the column's own two buttons; at the narrowest the title
        // gives way, so that none of them is pushed under the next column.
        var filesTitle = this.FindControl<TextBlock>("FilesTitleText")!;
        files.SizeChanged += (_, e) => filesTitle.MaxWidth = Math.Clamp(e.NewSize.Width - 131, 30, 96);
        terminal = this.FindControl<StudioPane>("TerminalPane")!;
        monitor = this.FindControl<StudioPane>("MonitorPane")!;
        leftSplitter = this.FindControl<Thumb>("LeftSplitter")!;
        rightSplitter = this.FindControl<Thumb>("RightSplitter")!;
        allHidden = this.FindControl<TextBlock>("AllHidden")!;
        files.MaximizeCommand = new RelayCommand(() => ToggleMaximize(Pane.Files));
        terminal.MaximizeCommand = new RelayCommand(() => ToggleMaximize(Pane.Terminal));
        monitor.MaximizeCommand = new RelayCommand(() => ToggleMaximize(Pane.Monitor));
        leftSplitter.DragDelta += (_, e) => Resize(left: true, e.Vector.X);
        rightSplitter.DragDelta += (_, e) => Resize(left: false, e.Vector.X);
        foreach (var splitter in new[] { leftSplitter, rightSplitter })
        {
            splitter.DragCompleted += (_, _) => RememberWidths();
            splitter.DoubleTapped += (_, _) =>
            {
                var standard = new StudioPreferences();
                if (ReferenceEquals(splitter, leftSplitter)) filesWidth = standard.FilesWidth; else monitorWidth = standard.MonitorWidth;
                Layout();
                RememberWidths();
            };
        }
        DataContextChanged += (_, _) => Bind(DataContext as WorkspaceViewModel);
        // The × that had the keyboard is gone with its column: the keys are for what is left.
        AddHandler(Button.ClickEvent, (_, e) =>
        {
            if (e.Source is not Button b || !b.Classes.Contains("collapse")) return;
            Dispatcher.UIThread.Post(() => { if (model is { IsActive: true } && KeyboardFocus.IsFree(this)) FocusContent(); }, DispatcherPriority.Background);
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        var tree = this.FindControl<TreeView>("FileTree")!;
        // Right-click selects the row under the pointer (or clears the selection on empty space),
        // so the context menu acts on what the user clicked.
        tree.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(tree).Properties.IsRightButtonPressed || fileModel == null) return;
            var item = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
            if (item?.DataContext is FileNodeViewModel { IsPlaceholder: false } node) fileModel.SelectedNode = node;
            else if (item == null) fileModel.SelectNothing();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        // While the context menu is open, pwd follow must not move the selection it acts on.
        if (tree.ContextMenu is { } menu)
        {
            menu.Opened += (_, _) => fileModel?.SetFileMenuOpen(true);
            menu.Closed += (_, _) => fileModel?.SetFileMenuOpen(false);
        }
        tree.DoubleTapped += (_, e) =>
        {
            var item = (e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
            if (item?.DataContext is FileNodeViewModel { IsDirectory: true } node && e.Source is not ToggleButton) node.IsExpanded = !node.IsExpanded;
            else if (item?.DataContext is FileNodeViewModel { IsFile: true } file && fileModel != null)
            {
                fileModel.SelectedNode = file;
                if (fileModel.OpenEditorCommand.CanExecute(null)) fileModel.OpenEditorCommand.Execute(null);
            }
        };
        SizeChanged += (_, _) => Layout();
        void Details(StudioPreferences p)
        {
            double width = tree.Bounds.Width;
            tree.Classes.Set("sizes", p.FileDetails && width >= 280);
            tree.Classes.Set("times", p.FileDetails && width >= 380);
        }
        tree.SizeChanged += (_, _) => Details(Appearance.Current);
        // "页签位置" of the terminal column.
        var terminalPane = this.FindControl<StudioPane>("TerminalPane")!;
        void Tabs(StudioPreferences p) => terminalPane.HeaderAtBottom = p.TabsAtBottom;
        AttachedToVisualTree += (_, _) => { Appearance.Changed += Details; Details(Appearance.Current); Appearance.Changed += Tabs; Tabs(Appearance.Current); };
        DetachedFromVisualTree += (_, _) => { Appearance.Changed -= Details; Appearance.Changed -= Tabs; };
        WireTerminal();
        WireFiles(tree);
        WireLinks();
        // The middle button closes a tab of the centre column.
        terminal.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(terminal).Properties.IsMiddleButtonPressed) return;
            if ((e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("chip-tab"))?.DataContext is not CenterTabViewModel tab) return;
            e.Handled = true;
            if (tab.CloseCommand?.CanExecute(null) == true && !CloseGuard.Blocks(e)) tab.CloseCommand.Execute(null);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    void RememberWidths() => model?.SavePreferences?.Invoke(p => p with { FilesWidth = filesWidth, MonitorWidth = monitorWidth });

    // ---------- files: keys, and files dropped from outside ----------

    bool showingSelection;

    /// <summary>The tree shows the selection of the session; it does not have one of its own.</summary>
    void ShowSelection()
    {
        if (fileModel == null) return;
        var tree = this.FindControl<TreeView>("FileTree")!;
        if (!ReferenceEquals(tree.ItemsSource, fileModel.FileRoots) || ReferenceEquals(tree.SelectedItem, fileModel.SelectedNode)) return;
        // "空目录" may be where the keys are, although it stands for its directory.
        if (fileModel.SelectedNode == null && tree.SelectedItem is FileNodeViewModel { IsPlaceholder: true, Parent: { } parent } note && parent.Path == fileModel.CurrentDirectory && fileModel.Owns(note)) return;
        showingSelection = true;
        try { tree.SelectedItem = fileModel.SelectedNode; }
        finally { showingSelection = false; }
    }

    /// <summary>The user chose a row. "Nothing" is never chosen in the tree: it drops what it shows when rows are
    /// exchanged and when another session comes to the front, and is then told again.</summary>
    void OnTreeSelection(TreeView tree)
    {
        if (showingSelection || fileModel == null || !ReferenceEquals(tree.ItemsSource, fileModel.FileRoots)) return;
        if (tree.SelectedItem is FileNodeViewModel node && fileModel.Owns(node)) fileModel.SelectedNode = node;
        Dispatcher.UIThread.Post(ShowSelection, DispatcherPriority.Background);
    }

    bool rowsHadFocus;
    void OnRowsChanging() => rowsHadFocus |= this.FindControl<TreeView>("FileTree")!.IsKeyboardFocusWithin;

    /// <summary>The row that had the keyboard was exchanged: the one that took its place gets it, or the directory's.
    /// Otherwise the next arrow key would be for a button somewhere.</summary>
    void OnRowsChanged()
    {
        if (!rowsHadFocus) return;
        rowsHadFocus = false;
        Dispatcher.UIThread.Post(() =>
        {
            var tree = this.FindControl<TreeView>("FileTree")!;
            if (fileModel != null && !tree.IsKeyboardFocusWithin && KeyboardFocus.IsFree(this)) FocusRow(tree);
        }, DispatcherPriority.Background);
    }

    void FocusRow(TreeView tree)
    {
        var node = fileModel!.SelectedNode ?? fileModel.RestingNode ?? fileModel.FileRoots.FirstOrDefault();
        if (node != null && tree.TreeContainerFromItem(node) is TreeViewItem row) row.Focus();
    }

    void WireFiles(TreeView tree)
    {
        tree.SelectionChanged += (_, _) => OnTreeSelection(tree);
        // From a place without a row the arrow keys go to the row of its directory, not to the top of the list.
        tree.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is not (Key.Up or Key.Down) || e.KeyModifiers != KeyModifiers.None || tree.SelectedItem != null || fileModel?.RestingNode is not { } directory) return;
            e.Handled = true;
            fileModel.SelectedNode = directory;
            FocusRow(tree);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        tree.KeyDown += (_, e) =>
        {
            if (fileModel == null || e.KeyModifiers != KeyModifiers.None) return;
            var command = e.Key switch
            {
                Key.F2 => fileModel.RenameCommand,
                Key.Delete => fileModel.DeleteCommand,
                Key.F5 => fileModel.RefreshDirectoryCommand,
                Key.Enter when fileModel.SelectedNode is { IsFile: true } => fileModel.OpenEditorCommand,
                _ => null,
            };
            if (command?.CanExecute(null) != true || fileModel.SelectedNode == null && e.Key != Key.F5) return;
            e.Handled = true;
            command.Execute(null);
        };
        // The order of the list and what it shows; the same as in the settings.
        if (this.FindControl<Button>("FileViewButton")!.Flyout is MenuFlyout view)
            view.Opening += (_, _) =>
            {
                var p = Appearance.Current;
                MenuItem Item(string title, bool on, Func<StudioPreferences, StudioPreferences> change) => new()
                {
                    Header = title,
                    Icon = new StrokeIcon { Data = Application.Current!.TryGetResource("Icon.Check", null, out object? icon) ? icon as Avalonia.Media.Geometry : null, Width = 12, Height = 12, IsVisible = on },
                    Command = new RelayCommand(() => model?.SavePreferences?.Invoke(change)),
                };
                view.Items.Clear();
                foreach (var (key, title) in Appearance.FileSorts) view.Items.Add(Item("按" + title, p.FileSort == key, x => x with { FileSort = key }));
                view.Items.Add(new Separator());
                view.Items.Add(Item("从大到小、从新到旧", p.FileSortDescending, x => x with { FileSortDescending = !x.FileSortDescending }));
                view.Items.Add(new Separator());
                view.Items.Add(Item("显示隐藏文件", p.ShowHidden, x => x with { ShowHidden = !x.ShowHidden }));
                view.Items.Add(Item("显示大小和时间", p.FileDetails, x => x with { FileDetails = !x.FileDetails }));
            };
        var hint = this.FindControl<Border>("DropHint")!;
        var name = this.FindControl<TextBlock>("DropTarget")!;
        DragDrop.SetAllowDrop(files, true);
        string Target(DragEventArgs e) => fileModel!.DropDirectory((e.Source as Visual)?.FindAncestorOfType<TreeViewItem>(includeSelf: true)?.DataContext as FileNodeViewModel);
        void Over(DragEventArgs e)
        {
            bool able = fileModel is { HasFiles: true } && e.DataTransfer.Contains(DataFormat.File);
            e.DragEffects = able ? DragDropEffects.Copy : DragDropEffects.None;
            hint.IsVisible = able;
            if (able) name.Text = Target(e);
            e.Handled = true;
        }
        DragDrop.AddDragEnterHandler(files, (_, e) => Over(e));
        DragDrop.AddDragOverHandler(files, (_, e) => Over(e));
        DragDrop.AddDragLeaveHandler(files, (_, _) => hint.IsVisible = false);
        DragDrop.AddDropHandler(files, (_, e) =>
        {
            hint.IsVisible = false;
            if (fileModel is not { HasFiles: true }) return;
            e.Handled = true;
            string target = Target(e);
            string?[] paths = e.DataTransfer.TryGetFiles()?.Select(f => { string? path = f.TryGetLocalPath(); f.Dispose(); return path; }).ToArray() ?? [];
            if (paths.Length > 0) _ = fileModel.DropAsync(target, paths.OfType<string>().ToArray(), paths.Count(p => p == null));
        });
    }

    const int MenuCommands = 30;

    void WireTerminal()
    {
        var menu = this.FindControl<ContextMenu>("TerminalMenu")!;
        var quick = this.FindControl<MenuItem>("MenuQuick")!;
        this.FindControl<MenuItem>("MenuCopy")!.InputGesture = TerminalKeyGestures.Copy[0];
        this.FindControl<MenuItem>("MenuPaste")!.InputGesture = TerminalKeyGestures.Paste[0];
        menu.Opening += (_, e) =>
        {
            // The panel also shows editors and the connecting overlay; the menu is the terminal's.
            if (model is not { IsTerminalShown: true, ShowTerminalOverlay: false }) { e.Cancel = true; return; }
            model.RefreshTerminalMenu();
            this.FindControl<MenuItem>("MenuFind")!.InputGesture = Shortcuts.Find.IsOff ? null : Shortcuts.Find.Gesture;
            var commands = model.QuickCommands?.Commands ?? [];
            quick.Items.Clear();
            foreach (var command in commands.Take(MenuCommands))
                quick.Items.Add(new MenuItem { Header = new TextBlock { Text = command.Name }, Command = model.RunQuickCommand, CommandParameter = command });
            if (commands.Count > MenuCommands) quick.Items.Add(new MenuItem { Header = new TextBlock { Text = $"其余 {commands.Count - MenuCommands} 条见工具栏的快速命令" }, IsEnabled = false });
            if (commands.Count > 0) quick.Items.Add(new Separator());
            quick.Items.Add(new MenuItem { Header = new TextBlock { Text = "管理快速命令…" }, Command = model.ManageQuickCommandsCommand });
        };

        if (this.FindControl<Button>("QuickButton")!.Flyout is Flyout { Content: Control content } flyout)
        {
            quickFlyout = flyout;
            flyout.Opened += (_, _) => Dispatcher.UIThread.Post(() => content.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus());
            flyout.Closed += (_, _) => { if (model != null) model.QuickFilter = ""; };
            // Choosing a command, or "管理", closes the list — after the button has run its command, which
            // it would lose with its data context.
            content.AddHandler(Button.ClickEvent, (_, _) => Dispatcher.UIThread.Post(flyout.Hide));
        }

        var box = this.FindControl<TextBox>("FindBox")!;
        box.KeyDown += (_, e) =>
        {
            if (model == null) return;
            if (e.Key == Key.Enter) (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? model.Find.NextCommand : model.Find.PreviousCommand).Execute(null);
            else if (e.Key == Key.Escape) model.Find.Close();
            else if (Shortcuts.Matches(Shortcuts.Find, e)) box.SelectAll();
            else return;
            e.Handled = true;
        };
    }

    void OnFindOpened() => Dispatcher.UIThread.Post(() =>
    {
        var box = this.FindControl<TextBox>("FindBox")!;
        box.Focus();
        box.SelectAll();
    });

    void Bind(WorkspaceViewModel? next)
    {
        if (model != null) { model.PropertyChanged -= OnModelChanged; model.Find.Opened -= OnFindOpened; }
        // Another session is shown: a list opened for the last one must not type into this one.
        quickFlyout?.Hide();
        model = next;
        if (model != null) { model.PropertyChanged += OnModelChanged; model.Find.Opened += OnFindOpened; }
        BindFiles();
        maximized = Pane.None;
        lastTab = model?.ActiveTab;
        Layout();
        Dispatcher.UIThread.Post(ShowSelection, DispatcherPriority.Background);
    }

    /// <summary>Another session's files are shown: those of the session in front, or of the one it is linked to.</summary>
    void BindFiles()
    {
        var next = model?.FileView;
        if (ReferenceEquals(next, fileModel)) return;
        if (fileModel != null)
        {
            fileModel.PropertyChanged -= OnFilesChanged;
            fileModel.RowsChanging -= OnRowsChanging;
            fileModel.RowsChanged -= OnRowsChanged;
            // Its menu is not the one that is open now.
            fileModel.SetFileMenuOpen(false);
        }
        linkFlyout?.Hide();
        fileModel = next;
        if (fileModel == null) return;
        fileModel.PropertyChanged += OnFilesChanged;
        fileModel.RowsChanging += OnRowsChanging;
        fileModel.RowsChanged += OnRowsChanged;
        Dispatcher.UIThread.Post(ShowSelection, DispatcherPriority.Background);
    }

    void OnFilesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.SelectedNode)) ShowSelection();
    }

    MenuFlyout? linkFlyout;

    /// <summary>The file sessions that are open, to choose from.</summary>
    void WireLinks()
    {
        foreach (string name in new[] { "LinkButton", "OfferLink" })
            if (this.FindControl<Button>(name)!.Flyout is MenuFlyout menu)
                menu.Opening += (_, _) =>
                {
                    linkFlyout = menu;
                    menu.Items.Clear();
                    if (model is not { } desk) return;
                    var choices = desk.LinkChoices;
                    foreach (var source in choices)
                    {
                        var item = new MenuItem
                        {
                            Header = new TextBlock { Text = desk.LinkLabel(source), MaxWidth = 420, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis },
                            Icon = new StrokeIcon { Data = Application.Current!.TryGetResource("Icon.Check", null, out object? icon) ? icon as Avalonia.Media.Geometry : null, Width = 12, Height = 12, IsVisible = ReferenceEquals(source, desk.FileView) },
                            Command = new RelayCommand(() => desk.Link(source)),
                        };
                        // What a screen reader (and the Windows check) reads: the header is a text block, not text.
                        Avalonia.Automation.AutomationProperties.SetName(item, desk.LinkLabel(source));
                        menu.Items.Add(item);
                    }
                    if (choices.Count == 0) menu.Items.Add(new MenuItem { Header = new TextBlock { Text = "没有打开的文件会话。请先从控制器用 xftp 打开，或在连接库打开 SFTP / FTP 连接。", MaxWidth = 320, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, IsEnabled = false });
                    if (!desk.ShowsLinkedFiles) return;
                    menu.Items.Add(new Separator());
                    var unlink = new MenuItem { Header = new TextBlock { Text = "解除关联，恢复为单独的页签" }, Command = new RelayCommand(desk.Unlink) };
                    Avalonia.Automation.AutomationProperties.SetName(unlink, "解除关联，恢复为单独的页签");
                    menu.Items.Add(unlink);
                };
    }

    CenterTabViewModel? lastTab;

    /// <summary>Another terminal or editor is in front: the keyboard follows when it was in the one before (or on
    /// its tab). Not to the first terminal of a session the controller opened — nobody asked for that one here.</summary>
    void FollowTab()
    {
        var (previous, now) = (lastTab, model!.ActiveTab);
        lastTab = now;
        if (now == null || previous == null && model.IsLaunchedSession || !model.IsActive) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (model == null || !model.IsActive || !ReferenceEquals(model.ActiveTab, now)) return;
            if (KeyboardFocus.IsFree(this, terminal.GetVisualDescendants().OfType<Button>().ToArray())) FocusContent();
        }, DispatcherPriority.Background);
    }

    /// <summary>The keyboard goes to what is shown: the terminal or editor in front, or the files. The caller has
    /// made sure that it may (<see cref="KeyboardFocus"/>).</summary>
    public void FocusContent()
    {
        if (model == null) return;
        if (terminal.IsVisible && model.ActiveTab is TerminalTabViewModel tab) tab.Control.FocusTerminal();
        else if (terminal.IsVisible && terminal.GetVisualDescendants().OfType<EditorView>().FirstOrDefault() is { } editor) editor.FocusText();
        else if (files.IsVisible)
        {
            var rows = this.FindControl<TreeView>("FileTree")!.GetVisualDescendants().OfType<TreeViewItem>().ToList();
            (rows.FirstOrDefault(r => r.IsSelected) ?? rows.FirstOrDefault())?.Focus();
        }
    }

    void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.ActiveTab)) FollowTab();
        if (e.PropertyName == nameof(WorkspaceViewModel.FileView)) BindFiles();
        if (e.PropertyName == nameof(WorkspaceViewModel.Status)) Motion.Flash(this.FindControl<TextBlock>("StatusText")!);
        if (e.PropertyName == nameof(WorkspaceViewModel.ShowFiles) && model!.ShowFiles) Motion.Enter(files);
        if (e.PropertyName == nameof(WorkspaceViewModel.ShowTerminal) && model!.ShowTerminal) Motion.Enter(terminal);
        if (e.PropertyName == nameof(WorkspaceViewModel.ShowMonitor) && model!.ShowMonitor) Motion.Enter(monitor);
        if (e.PropertyName is nameof(WorkspaceViewModel.ShowFiles) or nameof(WorkspaceViewModel.ShowTerminal) or nameof(WorkspaceViewModel.ShowMonitor))
        {
            maximized = Pane.None;
            Layout();
        }
    }

    void ToggleMaximize(Pane pane)
    {
        maximized = maximized == pane ? Pane.None : pane;
        Layout();
    }

    void Layout()
    {
        if (model == null) return;
        bool f = model.ShowFiles, t = model.ShowTerminal, m = model.ShowMonitor;
        model.IsCentreCovered = maximized is Pane.Files or Pane.Monitor;
        if (maximized != Pane.None) { f = maximized == Pane.Files; t = maximized == Pane.Terminal; m = maximized == Pane.Monitor; }
        star = t ? Pane.Terminal : f ? Pane.Files : m ? Pane.Monitor : Pane.None;
        ClampWidths(f, t, m);
        Column(0, files, f, star == Pane.Files ? GridLength.Star : new GridLength(filesWidth));
        Column(2, terminal, t, GridLength.Star);
        Column(4, monitor, m, star == Pane.Monitor ? GridLength.Star : new GridLength(monitorWidth));
        bool showLeft = f && (t || m), showRight = m && (t || f);
        leftSplitter.IsVisible = showLeft;
        rightSplitter.IsVisible = showRight;
        columns.ColumnDefinitions[1].Width = new GridLength(showLeft ? SplitterWidth : 0);
        columns.ColumnDefinitions[3].Width = new GridLength(showRight ? SplitterWidth : 0);
        files.IsMaximized = maximized == Pane.Files;
        terminal.IsMaximized = maximized == Pane.Terminal;
        monitor.IsMaximized = maximized == Pane.Monitor;
        allHidden.IsVisible = star == Pane.None;
    }

    void Column(int index, StudioPane pane, bool visible, GridLength width)
    {
        pane.IsVisible = visible;
        columns.ColumnDefinitions[index].Width = visible ? width : new GridLength(0);
    }

    /// <summary>A splitter moves the side column next to it; if that side column is the one
    /// filling the space (terminal hidden), it moves the opposite side column instead.</summary>
    void Resize(bool left, double delta)
    {
        if (left) { if (star == Pane.Files) monitorWidth -= delta; else filesWidth += delta; }
        else { if (star == Pane.Monitor) filesWidth += delta; else monitorWidth -= delta; }
        Layout();
    }

    void ClampWidths(bool f, bool t, bool m)
    {
        double total = columns.Bounds.Width;
        if (total <= 0) return;
        double side = Math.Max(MinSide, total - MinCentre - 2 * SplitterWidth);
        filesWidth = Math.Clamp(filesWidth, MinSide, side);
        monitorWidth = Math.Clamp(monitorWidth, MinSide, side);
        if (f && m && t && filesWidth + monitorWidth > total - MinCentre - 2 * SplitterWidth)
        {
            double room = Math.Max(2 * MinSide, total - MinCentre - 2 * SplitterWidth);
            double ratio = filesWidth / (filesWidth + monitorWidth);
            filesWidth = Math.Max(MinSide, room * ratio);
            monitorWidth = Math.Max(MinSide, room - filesWidth);
        }
    }
}
