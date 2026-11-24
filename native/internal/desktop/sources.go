package desktop

import (
	"context"
	"fmt"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"strings"
)

func (a *App) session(id string) *Workspace {
	for _, w := range a.sessions {
		if w.ID == id {
			return w
		}
	}
	return nil
}
func (w *Workspace) editorOwner() *Workspace {
	if w.owner != nil {
		return w.owner
	}
	return w
}
func (w *Workspace) sourceTitle() string {
	if w.source != nil {
		return w.source.Title
	}
	return w.Title
}
func (w *Workspace) groupActive() string {
	group, ok := w.App.model.Groups[w.ID]
	if !ok || group.Active == "" {
		return w.ID
	}
	return group.Active
}
func (w *Workspace) renderSourceTabs() {
	if w.owner != nil {
		return
	}
	group := w.App.model.Groups[w.ID]
	if len(group.Sources) == 0 {
		group.Sources = []string{w.ID}
		group.Active = w.ID
	}
	objects := []fyne.CanvasObject{}
	for _, id := range group.Sources {
		source := w.App.session(id)
		if source == nil {
			continue
		}
		button := w.sourceButtons[id]
		if button == nil {
			sourceID := id
			button = widget.NewButton("", func() { w.SelectFileSource(sourceID) })
			w.sourceButtons[id] = button
		}
		protocol := strings.ToUpper(source.Protocol)
		if protocol == "SSH" {
			protocol = "SFTP"
		}
		title := source.Title
		runes := []rune(title)
		if len(runes) > 18 {
			title = string(runes[:18]) + "…"
		}
		status := ""
		if source.FileConn != nil && source.FileConn.Closed() {
			status = " · 离线"
		}
		button.SetText(protocol + " · " + title + status)
		if id == group.Active {
			button.Importance = widget.MediumImportance
			button.SetIcon(theme.ConfirmIcon())
		} else {
			button.Importance = widget.LowImportance
			button.SetIcon(nil)
		}
		button.Refresh()
		objects = append(objects, button)
		if id != w.ID {
			sourceID := id
			objects = append(objects, widget.NewButton("×", func() { w.DetachFileSource(sourceID) }))
		}
	}
	w.sourceTabs.Objects = objects
	w.sourceTabs.Refresh()
}
func (w *Workspace) newFileView(source *Workspace) *Workspace {
	ctx, cancel := context.WithCancel(w.Context)
	view := &Workspace{ID: source.ID, Title: source.Title, Protocol: source.Protocol, App: w.App, Window: w.Window, Context: ctx, cancel: cancel, owner: w, source: source, FileConn: source.FileConn, Transfers: source.Transfers, Operations: source.Operations, directory: ".", selected: -1, filesVisible: false, Tab: w.Tab}
	view.Status = widget.NewLabel("")
	view.operationStatus = widget.NewLabel("")
	view.operationStatus.Wrapping = fyne.TextWrapWord
	view.operationCancel = widget.NewButton("取消目录操作", func() {
		if view.Operations != nil {
			view.Operations.Cancel()
		}
	})
	view.operationCancel.Hide()
	view.initFilePane()
	if source.directoryReady {
		view.directory = source.directory
		view.Path.SetText(source.directory)
		view.exactDirectory = source.exactDirectory
	}
	if view.Transfers != nil {
		view.unsubscribeQueue = view.Transfers.Subscribe(view.updateTransferView)
	}
	if view.Operations != nil {
		view.unsubscribeOperations = view.Operations.Subscribe(view.updateOperationView)
	}
	return view
}
func (w *Workspace) SelectFileSource(sourceID string) {
	if e := w.App.model.Select(w.ID, sourceID); e != nil {
		w.Status.SetText(e.Error())
		return
	}
	view := w.sourceViews[sourceID]
	if view == nil {
		source := w.App.session(sourceID)
		if source == nil || source.FileConn == nil {
			w.Status.SetText("文件来源尚未就绪，请稍后再选")
			return
		}
		view = w.newFileView(source)
		w.sourceViews[sourceID] = view
	}
	for id, v := range w.sourceViews {
		v.filesVisible = id == sourceID && w.workspaceVisible && w.filesPaneEnabled
	}
	w.fileViewHost.Objects = []fyne.CanvasObject{view.filePane}
	w.fileViewHost.Refresh()
	if view.transferList != nil {
		view.transferList.Refresh()
	}
	w.renderSourceTabs()
	if view.refreshPending || len(view.entries) == 0 {
		view.refreshPending = false
		view.Refresh()
	}
}
func (w *Workspace) AttachFileSource(sourceID string) error {
	if e := w.App.model.Attach(w.ID, sourceID); e != nil {
		return e
	}
	// Explicitly choosing a file session means showing it alongside the current
	// terminal, even when the user previously collapsed the file pane.
	w.filesPaneEnabled = true
	w.App.UI.Preferences().SetBool("filesVisible", true)
	w.SelectFileSource(sourceID)
	w.bodyHost.Refresh()
	w.App.rebuildSessionTabs(w.ID)
	return nil
}
func (w *Workspace) DetachFileSource(sourceID string) {
	if sourceID == w.ID {
		return
	}
	detach := func() {
		w.App.model.Detach(w.ID, sourceID)
		if view := w.sourceViews[sourceID]; view != nil {
			view.closeBorrowedView()
			delete(w.sourceViews, sourceID)
		}
		w.SelectFileSource(w.groupActive())
		w.App.rebuildSessionTabs(w.ID)
	}
	references := 0
	for _, d := range w.documents {
		if d.SourceID == sourceID {
			references++
		}
	}
	source := w.App.session(sourceID)
	transfers := 0
	if source != nil {
		transfers = source.ActiveTransfers()
	}
	if references > 0 || transfers > 0 {
		dialog.ShowConfirm("解除文件来源关联？", fmt.Sprintf("有%d份关联草稿、%d项活动/排队传输。仅解除此文件标签；草稿和传输保留，不会关闭来源连接。", references, transfers), func(ok bool) {
			if ok {
				detach()
			}
		}, w.Window)
	} else {
		detach()
	}
}
func (w *Workspace) ChooseFileSource() {
	candidates := []*Workspace{}
	labels := []string{}
	for _, source := range w.App.sessions {
		meta, ok := w.App.model.Get(source.ID)
		if !ok || !meta.Open || source.Protocol == "local" || source.FileConn == nil || source.FileConn.Closed() {
			continue
		}
		candidates = append(candidates, source)
		labels = append(labels, fmt.Sprintf("%s · %s · 会话%d", strings.ToUpper(source.Protocol), source.Title, len(candidates)))
	}
	if len(candidates) == 0 {
		dialog.ShowInformation("暂无可关联来源", "请先新建SSH、SFTP或独立FTP文件连接，再在此处明确关联。", w.Window)
		return
	}
	selectSource := widget.NewSelect(labels, nil)
	selectSource.SetSelectedIndex(0)
	dialog.ShowForm("关联文件来源（不切换当前终端）", "关联", "取消", []*widget.FormItem{widget.NewFormItem("来源", selectSource)}, func(ok bool) {
		if !ok || w.Context.Err() != nil {
			return
		}
		index := selectSource.SelectedIndex()
		if index >= 0 {
			if e := w.AttachFileSource(candidates[index].ID); e != nil {
				w.Status.SetText(e.Error())
			}
		}
	}, w.Window)
}
func (w *Workspace) closeBorrowedView() {
	if w.owner == nil {
		return
	}
	w.cancel()
	if w.unsubscribeQueue != nil {
		w.unsubscribeQueue()
	}
	if w.unsubscribeOperations != nil {
		w.unsubscribeOperations()
	}
}
func (w *Workspace) refreshSource(sourceID string) {
	if view := w.sourceViews[sourceID]; view != nil {
		view.Refresh()
	}
}
func (a *App) rebuildSessionTabs(prefer string) {
	items := append([]*container.TabItem(nil), a.miscTabs...)
	for _, meta := range a.model.Visible() {
		if w := a.session(meta.ID); w != nil && w.Window == a.Window {
			items = append(items, w.Tab)
		}
	}
	a.Tabs.SetItems(items)
	owner := a.model.OwnerFor(prefer)
	if w := a.session(owner); w != nil && w.Window == a.Window {
		a.Tabs.Select(w.Tab)
	}
}

func (w *Workspace) setWorkspaceVisible(visible bool) {
	w.workspaceVisible = visible
	active := w.groupActive()
	for id, view := range w.sourceViews {
		view.filesVisible = visible && w.filesPaneEnabled && id == active
		if !view.filesVisible {
			view.stopTransferAnimations()
		}
		if view.filesVisible && view.refreshPending {
			view.refreshPending = false
			view.Refresh()
		}
	}
	if w.Monitor != nil {
		w.Monitor.SetVisible(visible && w.monitorEnabled)
	}
}
func (a *App) CloseWorkspace(w *Workspace) {
	close := func() {
		a.model.Close(w.ID)
		w.Close()
		if a.connections != nil {
			a.connections.refreshStates()
		}
		for _, owner := range a.sessions {
			if owner != w && owner.secondTerminal == w {
				owner.secondTerminal = nil
				owner.focusedTerminal = owner
				a.SetStatus("当前输入终端 · " + owner.Title)
				owner.renderTerminals()
			}
		}
		a.rebuildSessionTabs("")
		for _, owner := range a.sessions {
			if owner.Context.Err() == nil {
				owner.renderSourceTabs()
			}
		}
	}
	refs := a.model.Referencing(w.ID)
	if len(refs) > 0 {
		dialog.ShowConfirm("关闭共享文件来源？", fmt.Sprintf("此会话被%d个工作区引用。关闭会使这些来源离线，已有草稿保留，传输将停止。", len(refs)), func(ok bool) {
			if ok {
				w.RequestClose(close)
			}
		}, w.Window)
	} else {
		w.RequestClose(close)
	}
}

func (w *Workspace) ActiveFileView() *Workspace {
	if view := w.sourceViews[w.groupActive()]; view != nil {
		return view
	}
	return w
}
