package desktop

import (
	"fmt"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/layout"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
)

func (w *Workspace) editorTools(d *RemoteDocument) fyne.CanvasObject {
	editor := d.Editor
	var more *widget.Button
	more = action("", theme.MoreHorizontalIcon(), func() {
		menu := fyne.NewMenu("编辑设置", fyne.NewMenuItem("语言模式…", func() {
			choice := widget.NewSelect([]string{"自动", "纯文本", "Go", "JavaScript", "JSON", "YAML", "XML", "Bash", "Python", "SQL", "Java"}, nil)
			choice.SetSelected("自动")
			dialog.ShowForm("代码语言", "应用", "取消", []*widget.FormItem{widget.NewFormItem("语言", choice)}, func(ok bool) {
				if ok {
					language := choice.Selected
					if language == "自动" {
						language = ""
					} else if language == "纯文本" {
						language = "plaintext"
					}
					editor.SetLanguage(language)
				}
			}, w.Window)
		}),
			fyne.NewMenuItem("换行为 LF…", func() { w.convertDocument(d, "\n") }), fyne.NewMenuItem("换行为 CRLF…", func() { w.convertDocument(d, "\r\n") }), fyne.NewMenuItemSeparator(), fyne.NewMenuItem("缩进 2 空格", func() { editor.TabSize = 2; editor.UseTabs = false; editor.Refresh() }), fyne.NewMenuItem("缩进 4 空格", func() { editor.TabSize = 4; editor.UseTabs = false; editor.Refresh() }), fyne.NewMenuItem("使用制表符缩进", func() { editor.UseTabs = true; editor.Refresh() }), fyne.NewMenuItem("显示 / 隐藏空白", func() { editor.ShowWhitespace = !editor.ShowWhitespace; editor.Refresh() }), fyne.NewMenuItemSeparator(), fyne.NewMenuItem("编辑区在下方", func() { w.editorDock = "bottom"; w.editorDockRight = false; w.rebuildEditorDock() }), fyne.NewMenuItem("编辑区在左侧", func() { w.editorDock = "left"; w.editorDockRight = true; w.rebuildEditorDock() }), fyne.NewMenuItem("编辑区在右侧", func() { w.editorDock = "right"; w.editorDockRight = true; w.rebuildEditorDock() }))
		position := w.App.UI.Driver().AbsolutePositionForObject(more).Add(fyne.NewPos(0, more.Size().Height))
		widget.ShowPopUpMenuAtPosition(menu, w.Window.Canvas(), position)
	})
	source := widget.NewLabel(d.SourceTitle)
	source.Importance = widget.LowImportance
	source.Truncation = fyne.TextTruncateEllipsis
	return container.NewHScroll(container.NewHBox(action("保存", theme.DocumentSaveIcon(), func() { w.SaveDocument(d) }), action("", theme.ContentUndoIcon(), func() {
		if editor.Editable() {
			editor.Document.Undo()
		}
	}), action("", theme.ContentRedoIcon(), func() {
		if editor.Editable() {
			editor.Document.Redo()
		}
	}), source, layout.NewSpacer(), more, action("", theme.VisibilityOffIcon(), w.hideEditor)))
}
func (w *Workspace) rebuildEditorDock() {
	w.App.UI.Preferences().SetString("editorDock", w.editorDock)
	w.editorSplit.Refresh()
	w.showEditor()
}

func editorFormat(d *RemoteDocument) string {
	f := d.Editor.Document.Format
	eol := "LF"
	if f.Mixed {
		eol = "混合"
	} else if f.EOL == "\r\n" {
		eol = "CRLF"
	}
	bom := ""
	if f.BOM {
		bom = " BOM"
	}
	row, col := d.Editor.Document.Position()
	return fmt.Sprintf("UTF-8%s · %s · %d:%d", bom, eol, row+1, col+1)
}
