using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Forterm.Core;
using Forterm.Desktop;
using Xunit;
namespace Forterm.Tests;
public sealed class LayoutUiTests
{
    [AvaloniaFact] public void ThreePlacementsRetainControlsAndDraftsAndTogglesPreserveWeights()
    {
        var preference = new UserPreferences { Workspace = new() { TerminalWeight = 2, FilesWeight = 1, EditorWeight = 1.5, EditorVisible = true } };
        var owner = new Window { Width = 1100, Height = 750 }; using var queue = new TransferQueue(); using var pane = new SessionPane(owner, null, transfers: queue, preferences: () => preference); owner.Content = pane; owner.Show();
        try
        {
            var terminal = pane.Terminal; var picker = pane.GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "工作区排列");
            var editor = pane.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().FirstOrDefault(); if (editor != null) editor.Text = "preserved draft";
            foreach (int index in new[] { 0, 1, 2, 0 })
            {
                picker.SelectedIndex = index; owner.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Assert.Same(terminal, pane.Terminal); Assert.Equal(index == 1 ? "editorLeft" : index == 2 ? "editorBottom" : "columns", pane.CurrentLayout.Placement);
                if (editor != null) Assert.Equal("preserved draft", editor.Text);
                pane.ToggleCapability(1); pane.ToggleCapability(1); Assert.Equal(2, pane.CurrentLayout.TerminalWeight); Assert.Equal(1.5, pane.CurrentLayout.EditorWeight);
                owner.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "workspace-layout-" + pane.CurrentLayout.Placement + ".png"));
            }
            var storeDir = Path.Combine(Path.GetTempPath(), "forterm-layout-" + Guid.NewGuid().ToString("N"));
            try { var store = new PreferenceStore(storeDir); store.Save(preference with { Workspace = pane.CurrentLayout }); Assert.Equal(pane.CurrentLayout, store.Load().Workspace); } finally { Directory.Delete(storeDir, true); }
        }
        finally { owner.Close(); }
    }
    [AvaloniaFact] public async Task ActualDividerDragAndKeyboardPersistAndSurvivePanelHide()
    {
        string dir = Path.Combine(Path.GetTempPath(), "forterm-divider-" + Guid.NewGuid().ToString("N")); var store = new PreferenceStore(dir);
        var owner = new Window { Width = 1000, Height = 700 }; using var pane = new SessionPane(owner, null); owner.Content = pane; owner.Show(); pane.LayoutChanged += value => store.Save(new() { Workspace = value });
        try
        {
            owner.UpdateLayout(); var split = pane.GetVisualDescendants().OfType<GridSplitter>().Single(s => AutomationProperties.GetName(s) == "终端与文件分隔");
            var point = split.TranslatePoint(new Point(2, 40), owner)!.Value; double oldWidth = pane.Terminal.Bounds.Width;
            owner.MouseDown(point, MouseButton.Left, RawInputModifiers.None); owner.MouseMove(point + new Vector(100, 0), RawInputModifiers.LeftMouseButton); owner.MouseUp(point + new Vector(100, 0), MouseButton.Left, RawInputModifiers.None); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.True(pane.Terminal.Bounds.Width < oldWidth - 50); var dragged = store.Load().Workspace; Assert.True(dragged.FilesWeight > .6);
            pane.ToggleCapability(1); pane.ToggleCapability(1); owner.UpdateLayout(); Assert.Equal(dragged.TerminalWeight, pane.CurrentLayout.TerminalWeight); Assert.Equal(dragged.FilesWeight, pane.CurrentLayout.FilesWeight);
            split.Focus(); owner.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null); owner.KeyRelease(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs(); await Task.Delay(20); Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(dragged.FilesWeight, store.Load().Workspace.FilesWeight);
            pane.ToggleCapability(0); pane.ToggleCapability(1); pane.ToggleCapability(3); owner.UpdateLayout(); var panel = pane.GetVisualDescendants().OfType<MonitorPanel>().Single(); Assert.True(panel.IsEffectivelyVisible); Assert.True(((Control)panel.Parent!).Bounds.Height > pane.Bounds.Height * .8);
            Assert.False(pane.Terminal.IsEffectivelyVisible); Assert.False(pane.CurrentLayout.TerminalVisible);
        }
        finally { owner.Close(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [AvaloniaFact] public void SmallToolbarAtLargestUiFontWrapsAndShortcutDoesNotConsumeOrdinaryTerminalKeys()
    {
        string dir = Path.Combine(Path.GetTempPath(), "forterm-shortcuts-" + Guid.NewGuid().ToString("N")); new PreferenceStore(dir).Save(new() { UiFontSize = 20 }); var window = new MainWindow(dir, new CredentialStoreTests.MemoryVault()) { Width = 700, Height = 433 }; window.Show();
        try
        {
            window.UpdateLayout(); foreach (var label in new[] { "连接库", "新建", "本地终端", "验证器", "导航", "工作区", "快捷键" })
            {
                var button = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label)); var pos = button.TranslatePoint(default, window)!.Value; Assert.InRange(pos.X, 0, window.Bounds.Width - button.Bounds.Width); Assert.InRange(pos.Y, 0, window.Bounds.Height - button.Bounds.Height);
            }
            bool before = window.GetVisualDescendants().OfType<TreeView>().Single(t => AutomationProperties.GetName(t) == "连接分组").IsEffectivelyVisible;
            window.GetVisualDescendants().OfType<TextBox>().First().Focus(); window.KeyPress(Key.B, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.B, null); window.KeyRelease(Key.B, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.B, null); Assert.NotEqual(before, window.GetVisualDescendants().OfType<TreeView>().Single(t => AutomationProperties.GetName(t) == "连接分组").IsEffectivelyVisible);
            window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "快捷键")).Focus(); window.KeyPress(Key.F1, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.F1, null); window.KeyRelease(Key.F1, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.F1, null); Assert.Single(window.OwnedWindows); window.OwnedWindows.Single().Close();
            window.UpdateLayout(); window.CaptureRenderedFrame()!.Save(Path.Combine(UiEvidence.DirectoryPath, "toolbar-large-font-small.png"));
            var plain = new Window(); var field = new TextBox(); plain.Content = field; plain.Show(); int shortcuts = 0; WorkspaceShortcuts.Install(plain, _ => shortcuts++); field.Focus(); plain.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); plain.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null); plain.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, null); plain.KeyRelease(Key.C, RawInputModifiers.Control, PhysicalKey.C, null); Assert.Equal(0, shortcuts); plain.Close();
        }
        finally { window.Close(); Directory.Delete(dir, true); }
    }
}
