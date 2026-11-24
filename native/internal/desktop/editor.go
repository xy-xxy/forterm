package desktop

import (
	"fmt"
	"forterm/native/internal/codeedit"
	"forterm/native/internal/sshconn"
	"forterm/native/internal/transfers"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"path"
	"strings"
)

type RemoteDocument struct {
	SourceMoved           bool
	SourceID, SourceTitle string
	Confirming            bool
	Editor                *codeedit.Editor
	Snapshot              sshconn.TextSnapshot
	Source                *sshconn.Connection
	New, Busy             bool
	Tab                   *container.TabItem
	Label                 *widget.Label
}

func (w *Workspace) OpenSelectedText() {
	entry, remote, ok := w.selectedRemoteEntry()
	if !ok {
		return
	}
	if entry.Directory {
		w.EnterDirectory()
		return
	}
	w.OpenText(remote)
}
func (w *Workspace) OpenText(file string) {
	view := w
	w = w.editorOwner()
	sourceID, sourceTitle := view.ID, view.sourceTitle()
	key := sourceID + "\x00" + file
	for _, d := range w.documents {
		if d.SourceID == sourceID && d.Snapshot.Path == file {
			w.editorTabs.Select(d.Tab)
			w.showEditor()
			if w.active() {
				w.Window.Canvas().Focus(d.Editor)
			}
			return
		}
	}
	if w.openingText[key] || !view.fileAvailable() || w.Context.Err() != nil {
		return
	}
	if len(w.documents) >= 12 {
		dialog.ShowInformation("编辑标签已满", "每个会话最多12份远程草稿，请先关闭不需要的标签。", w.Window)
		return
	}
	w.openingText[key] = true
	source, ok := view.FileConn.(*sshconn.Connection)
	if !ok {
		delete(w.openingText, key)
		dialog.ShowInformation("此文件协议不支持安全在线编辑", "FTP缺少已验证的安全原子写入能力，请下载后编辑。不会自动执行或下载。", w.Window)
		return
	}
	w.editorRequestEpoch++
	epoch := w.editorRequestEpoch
	focusAtRequest := w.Window.Canvas().Focused()
	w.Status.SetText("读取文本…")
	go func() {
		snapshot, err := source.ReadText(w.Context, file)
		fyne.Do(func() {
			delete(w.openingText, key)
			if w.Context.Err() != nil {
				return
			}
			if err != nil {
				w.Status.SetText(err.Error())
				return
			}
			activate := w.active() && w.editorRequestEpoch == epoch && w.Window.Canvas().Focused() == focusAtRequest
			w.addDocument(source, snapshot, false, sourceID, sourceTitle, activate)
		})
	}()
}
func (w *Workspace) NewText() {
	view := w
	w = w.editorOwner()
	sourceID, sourceTitle := view.ID, view.sourceTitle()
	source, ok := view.FileConn.(*sshconn.Connection)
	if !ok {
		dialog.ShowInformation("此文件协议不支持安全新建文本", "FTP不提供已验证的排他首次保存，请在SFTP来源中新建。", w.Window)
		return
	}
	name := widget.NewEntry()
	name.PlaceHolder = "例如 notes.txt"
	dialog.ShowForm("新建文本草稿", "新建", "取消", []*widget.FormItem{widget.NewFormItem("文件名", name)}, func(ok bool) {
		if !ok {
			return
		}
		value := name.Text
		if value == "" || value == "." || value == ".." || strings.ContainsAny(value, "/\\\x00\r\n") {
			dialog.ShowInformation("文件名无效", "请输入不含路径分隔符的文件名。", w.Window)
			return
		}
		if len(w.documents) >= 12 {
			return
		}
		file := sshconn.RemoteJoin(view.directory, value)
		for _, d := range w.documents {
			if d.SourceID == sourceID && d.Snapshot.Path == file {
				w.editorTabs.Select(d.Tab)
				w.showEditor()
				return
			}
		}
		w.addDocument(source, sshconn.TextSnapshot{Path: file}, true, sourceID, sourceTitle, true)
	}, w.Window)
}
func (w *Workspace) addDocument(source *sshconn.Connection, snapshot sshconn.TextSnapshot, isNew bool, sourceID, sourceTitle string, activate bool) {
	editor := codeedit.New(snapshot.Path, snapshot.Text)
	editor.FontSize = float32(w.App.UI.Preferences().IntWithFallback("editorFontSize", 13))
	doc := &RemoteDocument{SourceID: sourceID, SourceTitle: sourceTitle, Editor: editor, Snapshot: snapshot, Source: source, New: isNew}
	doc.Label = widget.NewLabel("")
	doc.Label.Wrapping = fyne.TextWrapWord
	doc.Label.Importance = widget.LowImportance
	doc.Label.Hide()
	refresh := func() {
		label := doc.SourceTitle + " · " + path.Base(doc.Snapshot.Path)
		if doc.New || editor.Document.Dirty() {
			label = "● " + label
		}
		if doc.Tab.Text != label {
			doc.Tab.Text = label
			w.editorTabs.Refresh()
		}
	}
	editor.OnMessage = func(message string) { doc.setMessage(message) }
	editor.OnSave = func() { w.SaveDocument(doc) }
	editor.OnDirty = func(bool) { refresh() }
	tools := w.editorTools(doc)
	format := widget.NewLabel(editorFormat(doc))
	format.Importance = widget.LowImportance
	pathLabel := widget.NewLabel(snapshot.Path)
	pathLabel.Truncation = fyne.TextTruncateEllipsis
	pathLabel.Importance = widget.LowImportance
	editor.OnDirty = func(bool) { refresh(); format.SetText(editorFormat(doc)) }
	if editor.Document.Format.Mixed {
		doc.setMessage("混合换行只读，选择转换后编辑。")
	}
	status := container.NewBorder(nil, nil, nil, format, pathLabel)
	// Keep the code viewport's minimum height inside a clipped, scrollable
	// document, rather than letting fixed chrome consume a negative center.
	content := paneSurface(container.NewVScroll(container.NewBorder(tools, container.NewVBox(doc.Label, status), nil, nil, editor)), theme.ColorNameInputBackground)

	doc.Tab = container.NewTabItem(path.Base(snapshot.Path), content)
	w.documents = append(w.documents, doc)
	previous := w.editorTabs.Selected()
	w.editorTabs.Append(doc.Tab)
	if activate {
		w.editorTabs.Select(doc.Tab)
		w.showEditor()
		w.Window.Canvas().Focus(editor)
	} else if previous != nil {
		w.editorTabs.Select(previous)
	}
	refresh()
	w.Status.SetText("文本已打开，修改后请确认保存")
}
func (w *Workspace) convertDocument(d *RemoteDocument, eol string) {
	if d.Busy {
		return
	}
	name := "LF"
	if eol == "\r\n" {
		name = "CRLF"
	}
	dialog.ShowConfirm("转换换行", "明确将此草稿全部换行转换为 "+name+"？这会产生未保存修改。", func(ok bool) {
		if ok && !d.Busy && w.Context.Err() == nil {
			d.Editor.Document.ConvertEOL(eol)
			d.setMessage("换行已转换，保存前仍可撤销。")
		}
	}, w.Window)
}
func (w *Workspace) SaveDocument(d *RemoteDocument) {
	if d.SourceMoved {
		d.setMessage("文件已移动，此标签保持原路径只读；请从新路径重新打开。")
		return
	}
	if d.Busy || d.Confirming || w.Context.Err() != nil {
		return
	}
	if original := w.App.session(d.SourceID); original != nil && original.Context.Err() != nil {
		d.setMessage("原文件来源已关闭；草稿保持原路径，不自动绑定其他会话。")
		return
	}
	if d.Source == nil || d.Source.Closed() {
		d.setMessage("连接已断开，草稿仍保留；请复制内容后重新授权连接。")
		return
	}
	if d.Editor.Document.Format.Mixed {
		d.setMessage("混合换行保持只读，请先明确转换。")
		return
	}
	text := d.Editor.Document.Serialize()
	if len(text) > sshconn.TextLimit {
		d.setMessage("在线编辑限1MiB，请缩减后保存。")
		return
	}
	version := d.Editor.Document.Version
	message := "将当前草稿保存到远程文件？\n" + d.Snapshot.Path
	if d.New {
		message = "首次保存将排他创建新文件，绝不覆盖同名文件。\n" + d.Snapshot.Path
	}
	d.Confirming = true
	dialog.ShowConfirm("确认远程保存", message, func(ok bool) {
		d.Confirming = false
		if !ok || w.Context.Err() != nil {
			return
		}
		if d.Editor.Document.Version != version {
			d.setMessage("确认期间草稿已变化，请重新确认保存。")
			return
		}
		d.Busy = true
		d.Editor.ReadOnly = true
		d.setMessage("保存中…")
		go func() {
			var saved sshconn.TextSnapshot
			ctx, release, err := transfers.AcquireResources(w.Context, transfers.ResourceKey(d.Source))
			defer release()
			if err == nil && d.New {
				saved, err = d.Source.CreateText(ctx, d.Snapshot.Path, text)
			} else if err == nil {
				saved, err = d.Source.SaveText(ctx, d.Snapshot.Path, text, d.Snapshot.Revision)
			}
			fyne.Do(func() {
				d.Busy = false
				d.Editor.ReadOnly = false
				if w.Context.Err() != nil {
					return
				}
				if err != nil {
					d.setMessage(err.Error())
					return
				}
				d.Snapshot = saved
				d.New = false
				d.Editor.Document.MarkSaved(text)
				d.setMessage("保存已确认 · 草稿与本次写入一致")
				w.refreshSource(d.SourceID)
			})
		}()
	}, w.Window)
}
func (w *Workspace) showEditor() {
	w.editorHost.Show()
	w.centerCell.Objects = []fyne.CanvasObject{w.editorSplit}
	w.centerCell.Refresh()
}
func (w *Workspace) hideEditor() {
	w.centerCell.Objects = []fyne.CanvasObject{w.termBox}
	w.centerCell.Refresh()
}
func (w *Workspace) closeDocument(tab *container.TabItem) {
	for i, d := range w.documents {
		if d.Tab != tab {
			continue
		}
		remove := func() {
			d.Editor.Close()
			w.documents = append(w.documents[:i], w.documents[i+1:]...)
			w.editorTabs.Remove(tab)
			if len(w.documents) == 0 {
				w.hideEditor()
			}
		}
		if d.New || d.Busy || d.Editor.Document.Dirty() {
			dialog.ShowConfirm("放弃未保存草稿？", d.Snapshot.Path+"\n关闭后此草稿将丢失；远程文件不会自动保存。", func(ok bool) {
				if ok {
					remove()
				}
			}, w.Window)
		} else {
			remove()
		}
		return
	}
}
func (w *Workspace) DirtyCount() int {
	count := 0
	for _, d := range w.documents {
		if d.New || d.Busy || d.Editor.Document.Dirty() {
			count++
		}
	}
	return count
}
func (w *Workspace) RequestClose(after func()) {
	count := w.DirtyCount()
	transfers := w.ActiveTransfers()
	if count == 0 && transfers == 0 {
		after()
		return
	}
	dialog.ShowConfirm("关闭会话并放弃草稿？", fmt.Sprintf("此会话有%d份未保存或保存中草稿、%d项活动/排队传输。关闭会结束连接，草稿丢失，传输可能留下部分远程文件。", count, transfers), func(ok bool) {
		if ok {
			after()
		}
	}, w.Window)
}

func (w *Workspace) ActiveDocument() *RemoteDocument {
	selected := w.editorTabs.Selected()
	for _, d := range w.documents {
		if d.Tab == selected {
			return d
		}
	}
	return nil
}

func (a *App) RequestExit() {
	count, transfers := 0, 0
	for _, w := range a.sessions {
		count += w.DirtyCount()
		transfers += w.ActiveTransfers()
	}
	close := func() {
		for _, w := range a.sessions {
			w.Window.SetCloseIntercept(nil)
		}
		a.Window.SetCloseIntercept(nil)
		a.Close()
		a.UI.Quit()
	}
	if count > 0 || transfers > 0 {
		dialog.ShowConfirm("确认退出？", fmt.Sprintf("共有%d份未保存/保存中草稿、%d项活动/排队传输，退出将结束连接并停止传输。", count, transfers), func(ok bool) {
			if ok {
				close()
			}
		}, a.Window)
	} else {
		close()
	}
}

func (d *RemoteDocument) setMessage(message string) {
	d.Label.SetText(message)
	if message == "" {
		d.Label.Hide()
	} else {
		d.Label.Show()
	}
}
