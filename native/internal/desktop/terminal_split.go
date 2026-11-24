package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/widget"
)

func (w *Workspace) ChooseTerminalSplit() {
	sessions := []*Workspace{}
	names := []string{}
	for _, meta := range w.App.model.Visible() {
		other := w.App.session(meta.ID)
		if other == nil || other == w || other.Window != w.Window || other.Context.Err() != nil || !other.Term.Connected() {
			continue
		}
		sessions = append(sessions, other)
		names = append(names, other.Title)
	}
	if len(sessions) == 0 {
		dialog.ShowInformation("暂无第二终端", "请先打开另一个SSH或本地终端。隐藏的文件来源不参与双终端选择。", w.Window)
		return
	}
	selection := widget.NewSelect(names, nil)
	selection.SetSelectedIndex(0)
	sideBySide := widget.NewCheck("左右分屏（取消为上下）", nil)
	sideBySide.SetChecked(w.App.UI.Preferences().BoolWithFallback("terminalsSideBySide", true))
	dialog.ShowForm("双终端分屏", "分屏", "取消", []*widget.FormItem{widget.NewFormItem("第二终端", selection), widget.NewFormItem("", sideBySide)}, func(ok bool) {
		if ok && selection.SelectedIndex() >= 0 {
			w.SetTerminalSplit(sessions[selection.SelectedIndex()], sideBySide.Checked)
		}
	}, w.Window)
}
func (w *Workspace) SetTerminalSplit(second *Workspace, sideBySide bool) {
	if second == w {
		return
	}
	w.secondTerminal = second
	w.terminalsSideBySide = sideBySide
	w.App.UI.Preferences().SetBool("terminalsSideBySide", sideBySide)
	w.renderTerminals()
}
func (w *Workspace) renderTerminals() {
	if w.primaryTerminal == nil {
		return
	}
	if w.secondTerminal == nil || w.secondTerminal.Context.Err() != nil {
		w.termBox.Objects = []fyne.CanvasObject{w.primaryTerminal}
		w.termBox.Refresh()
		return
	}
	second := w.secondTerminal
	if second.primaryTerminal == nil {
		w.termBox.Objects = []fyne.CanvasObject{w.primaryTerminal}
		w.termBox.Refresh()
		return
	}
	var host *fyne.Container
	divider := newPaneDivider(func(delta float32) {
		dimension := host.Size().Width
		if !w.terminalsSideBySide {
			dimension = host.Size().Height
		}
		if dimension > 0 {
			w.terminalShare = max(.2, min(.8, w.terminalShare+float64(delta/dimension)))
		}
		host.Refresh()
	}, func() { w.terminalShare = .5; host.Refresh() }, func() { w.App.UI.Preferences().SetFloat("terminalShare", w.terminalShare) })
	divider.axisY = !w.terminalsSideBySide
	host = container.New(&terminalSplitLayout{w: w, divider: divider}, w.primaryTerminal, second.primaryTerminal, divider)
	w.termBox.Objects = []fyne.CanvasObject{host}
	w.termBox.Refresh()
}

type terminalSplitLayout struct {
	w       *Workspace
	divider *paneDivider
}

func (l *terminalSplitLayout) MinSize([]fyne.CanvasObject) fyne.Size { return fyne.NewSize(220, 140) }
func (l *terminalSplitLayout) Layout(_ []fyne.CanvasObject, size fyne.Size) {
	w := l.w
	first, second := w.primaryTerminal, w.secondTerminal.primaryTerminal
	if w.terminalShare < .2 || w.terminalShare > .8 {
		w.terminalShare = .5
	}
	if w.terminalsSideBySide {
		split := size.Width * float32(w.terminalShare)
		first.Move(fyne.NewPos(0, 0))
		first.Resize(fyne.NewSize(max(0, split-3), size.Height))
		second.Move(fyne.NewPos(split+3, 0))
		second.Resize(fyne.NewSize(max(0, size.Width-split-3), size.Height))
		l.divider.Move(fyne.NewPos(split-3, 0))
		l.divider.Resize(fyne.NewSize(6, size.Height))
	} else {
		split := size.Height * float32(w.terminalShare)
		first.Move(fyne.NewPos(0, 0))
		first.Resize(fyne.NewSize(size.Width, max(0, split-3)))
		second.Move(fyne.NewPos(0, split+3))
		second.Resize(fyne.NewSize(size.Width, max(0, size.Height-split-3)))
		l.divider.Move(fyne.NewPos(0, split-3))
		l.divider.Resize(fyne.NewSize(size.Width, 6))
	}
}
