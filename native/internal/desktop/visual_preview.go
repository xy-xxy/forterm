package desktop

import (
	"io"
	"strings"

	"forterm/native/internal/settings"
	"forterm/native/internal/sshconn"
	"fyne.io/fyne/v2"
)

// AddSyntheticWorkspacePreview is available only to an explicitly offline app.
// It renders fixtures, never launches a shell, executes commands, fetches
// metrics, reads credentials or creates a network connection.
func (a *App) AddSyntheticWorkspacePreview() *Workspace {
	if !a.diagnostic {
		return nil
	}
	w := NewWorkspace(a, "合成会话 · 预览", "ssh")
	a.Tabs.Append(w.Tab)
	a.Tabs.Select(w.Tab)
	w.Path.SetText("/preview")
	w.directory = "/preview"
	w.allEntries = []sshconn.Entry{{Name: "documents", Directory: true}, {Name: "settings.yaml", Size: 84}, {Name: "README.md", Size: 256}, {Name: "notes.txt", Size: 128}}
	w.applyFileFilter()
	w.showTerminal()
	w.editorDock = "bottom"
	w.editorDockRight = false
	w.rebuildEditorDock()
	w.addDocument(nil, sshconn.TextSnapshot{Path: "/preview/settings.yaml", Text: "# 合成预览，不对应远程文件\nworkspace:\n  terminal: true\n  files: true\n  editor: true\n  monitor: manual\n"}, false, w.ID, w.Title, true)
	w.documents[0].Editor.ReadOnly = true
	w.Monitor.SetAutomatic(false)
	w.MonitorToggle.SetChecked(true)
	w.Status.SetText("合成预览 · 未连接远程主机，指标尚未获取")
	output := "\x1b[32mForterm\x1b[0m · 原生会话工作区\r\n\r\n终端、文件、在线编辑和资源监控同时显示。\r\n\r\n这是合成显示数据，未建立远程连接。\r\n监控由用户按需刷新；此处没有指标样本。\r\n"
	go func() { _ = w.Term.RunWithConnection(previewInput{}, strings.NewReader(output)) }()
	return w
}

type previewInput struct{}

func (previewInput) Write(data []byte) (int, error) { return len(data), nil }
func (previewInput) Close() error                   { return nil }

var _ io.WriteCloser = previewInput{}

func (a *App) ConnectionPageSnapshot() map[string]any {
	m := a.connections
	if m == nil {
		return map[string]any{"version": Version}
	}
	return map[string]any{"version": Version, "profiles": len(m.profiles), "filteredRows": len(m.rows), "createdCellTemplates": m.createdCells, "navigationWidth": a.navigationWidth, "tableWidth": m.table.Size().Width, "tableHeight": m.table.Size().Height, "fonts": SystemUIFontSnapshot(), "pageSize": fyne.NewSize(m.Size().Width, m.Size().Height)}
}

// LoadSyntheticPreviewMetadata keeps the disabled credential service when the
// visual tool replaces its empty diagnostic store with generated metadata.
func (a *App) LoadSyntheticPreviewMetadata(directory string) bool {
	if !a.diagnostic {
		return false
	}
	a.Store = settings.Open(directory, diagnosticVault{})
	a.ShowLibrary()
	return true
}
