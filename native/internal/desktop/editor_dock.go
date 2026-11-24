package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
)

type editorDockLayout struct {
	w       *Workspace
	divider *paneDivider
}

func (l *editorDockLayout) MinSize([]fyne.CanvasObject) fyne.Size { return fyne.NewSize(200, 220) }
func (l *editorDockLayout) Layout(_ []fyne.CanvasObject, size fyne.Size) {
	right := (l.w.editorDock == "left" || l.w.editorDock == "right") && size.Width >= 620
	l.divider.axisY = !right
	if right {
		split := size.Width * float32(l.w.editorRightRatio)
		split = max(180, min(size.Width-180, split))
		if l.w.editorDock == "left" {
			editorWidth := size.Width - split
			l.w.editorHost.Move(fyne.NewPos(0, 0))
			l.w.editorHost.Resize(fyne.NewSize(editorWidth-3, size.Height))
			l.divider.Move(fyne.NewPos(editorWidth-3, 0))
			l.divider.Resize(fyne.NewSize(6, size.Height))
			l.w.termBox.Move(fyne.NewPos(editorWidth+3, 0))
			l.w.termBox.Resize(fyne.NewSize(split-3, size.Height))
		} else {
			l.w.termBox.Move(fyne.NewPos(0, 0))
			l.w.termBox.Resize(fyne.NewSize(split-3, size.Height))
			l.divider.Move(fyne.NewPos(split-3, 0))
			l.divider.Resize(fyne.NewSize(6, size.Height))
			l.w.editorHost.Move(fyne.NewPos(split+3, 0))
			l.w.editorHost.Resize(fyne.NewSize(max(0, size.Width-split-3), size.Height))
		}
	} else {
		available := max(0, size.Height-6)
		terminalMin := min(float32(100), available*.35)
		editorMin := min(float32(190), available*.65)
		split := max(terminalMin, min(available-editorMin, available*float32(l.w.editorBottomRatio)))
		l.w.termBox.Move(fyne.NewPos(0, 0))
		l.w.termBox.Resize(fyne.NewSize(max(0, size.Width), split))
		l.divider.Move(fyne.NewPos(0, split))
		l.divider.Resize(fyne.NewSize(size.Width, 6))
		l.w.editorHost.Move(fyne.NewPos(0, split+6))
		l.w.editorHost.Resize(fyne.NewSize(max(0, size.Width), max(0, available-split)))
	}
}
func (w *Workspace) makeEditorDock() *fyne.Container {
	w.editorBottomRatio = w.App.UI.Preferences().FloatWithFallback("editorBottomRatio", .54)
	w.editorRightRatio = w.App.UI.Preferences().FloatWithFallback("editorRightRatio", .5)
	w.editorDock = w.App.UI.Preferences().StringWithFallback("editorDock", "right")
	if w.editorDock != "left" && w.editorDock != "right" && w.editorDock != "bottom" {
		w.editorDock = "right"
	}
	w.editorDockRight = w.editorDock != "bottom"
	var host *fyne.Container
	divider := newPaneDivider(func(delta float32) {
		right := (w.editorDock == "left" || w.editorDock == "right") && host.Size().Width >= 620
		if right {
			if w.editorDock == "left" {
				delta = -delta
			}
			w.editorRightRatio = max(.2, min(.8, w.editorRightRatio+float64(delta/host.Size().Width)))
		} else {
			w.editorBottomRatio = max(.2, min(.8, w.editorBottomRatio+float64(delta/host.Size().Height)))
		}
		host.Refresh()
	}, func() { w.editorBottomRatio = .54; w.editorRightRatio = .5; host.Refresh() }, func() {
		w.App.UI.Preferences().SetFloat("editorBottomRatio", w.editorBottomRatio)
		w.App.UI.Preferences().SetFloat("editorRightRatio", w.editorRightRatio)
		w.App.UI.Preferences().SetString("editorDock", w.editorDock)
	})
	host = container.New(&editorDockLayout{w: w, divider: divider}, w.termBox, w.editorHost, divider)
	return host
}

func (w *Workspace) SetEditorDock(side string) {
	if side != "left" && side != "right" && side != "bottom" {
		return
	}
	w.editorDock = side
	w.editorDockRight = side != "bottom"
	w.rebuildEditorDock()
}
func (w *Workspace) EffectiveEditorDock() string {
	if w.editorSplit.Size().Width < 620 {
		return "bottom"
	}
	return w.editorDock
}
