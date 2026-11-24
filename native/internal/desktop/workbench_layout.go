package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/driver/desktop"
	"fyne.io/fyne/v2/layout"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
)

type paneDivider struct {
	axisY            bool
	minimum, maximum func()
	widget.BaseWidget
	change                func(float32)
	reset                 func()
	finish                func()
	focused, hover, shift bool
}

func newPaneDivider(change func(float32), reset, finish func()) *paneDivider {
	d := &paneDivider{change: change, reset: reset, finish: finish}
	d.ExtendBaseWidget(d)
	return d
}
func (d *paneDivider) CreateRenderer() fyne.WidgetRenderer {
	return &dividerRenderer{d: d, line: canvas.NewRectangle(theme.Color(theme.ColorNameSeparator))}
}
func (d *paneDivider) FocusGained()   { d.focused = true; d.Refresh() }
func (d *paneDivider) FocusLost()     { d.focused = false; d.shift = false; d.Refresh() }
func (d *paneDivider) TypedRune(rune) {}
func (d *paneDivider) TypedKey(e *fyne.KeyEvent) {
	step := float32(8)
	shift := d.shift
	if driver, ok := fyne.CurrentApp().Driver().(desktop.Driver); ok {
		shift = driver.CurrentKeyModifiers()&fyne.KeyModifierShift != 0
	}
	if shift {
		step = 32
	}
	switch e.Name {
	case fyne.KeyLeft, fyne.KeyUp:
		d.change(-step)
	case fyne.KeyRight, fyne.KeyDown:
		d.change(step)
	case fyne.KeyHome:
		if d.minimum != nil {
			d.minimum()
		} else {
			d.reset()
		}
	case fyne.KeyEnd:
		if d.maximum != nil {
			d.maximum()
		} else {
			d.reset()
		}
	case fyne.KeyReturn, fyne.KeyEnter:
		d.reset()
	}
	if d.finish != nil {
		d.finish()
	}
}
func (d *paneDivider) KeyDown(e *fyne.KeyEvent) {
	if e.Name == desktop.KeyShiftLeft || e.Name == desktop.KeyShiftRight {
		d.shift = true
	}
}
func (d *paneDivider) KeyUp(e *fyne.KeyEvent) {
	if e.Name == desktop.KeyShiftLeft || e.Name == desktop.KeyShiftRight {
		d.shift = false
	}
}

// Focus on pointer-down. Waiting for Tapped delays focus until the native
// double-tap timeout, which can send an immediate arrow key to the old widget.
func (d *paneDivider) MouseDown(*desktop.MouseEvent) {
	if c := fyne.CurrentApp().Driver().CanvasForObject(d); c != nil {
		c.Focus(d)
	}
}
func (d *paneDivider) MouseUp(*desktop.MouseEvent) {}
func (d *paneDivider) Tapped(*fyne.PointEvent) {
	if c := fyne.CurrentApp().Driver().CanvasForObject(d); c != nil {
		c.Focus(d)
	}
}
func (d *paneDivider) DoubleTapped(*fyne.PointEvent) {
	d.reset()
	if d.finish != nil {
		d.finish()
	}
}
func (d *paneDivider) Dragged(e *fyne.DragEvent) {
	if d.axisY {
		d.change(e.Dragged.DY)
	} else {
		d.change(e.Dragged.DX)
	}
}
func (d *paneDivider) DragEnd() {
	if d.finish != nil {
		d.finish()
	}
}
func (d *paneDivider) MouseIn(*desktop.MouseEvent)    { d.hover = true; d.Refresh() }
func (d *paneDivider) MouseMoved(*desktop.MouseEvent) {}
func (d *paneDivider) MouseOut()                      { d.hover = false; d.Refresh() }
func (d *paneDivider) Cursor() desktop.Cursor {
	if d.axisY {
		return desktop.VResizeCursor
	}
	return desktop.HResizeCursor
}

type dividerRenderer struct {
	d    *paneDivider
	line *canvas.Rectangle
}

func (r *dividerRenderer) MinSize() fyne.Size {
	if r.d.axisY {
		return fyne.NewSize(0, 6)
	}
	return fyne.NewSize(6, 0)
}
func (r *dividerRenderer) Objects() []fyne.CanvasObject { return []fyne.CanvasObject{r.line} }
func (r *dividerRenderer) Destroy()                     {}
func (r *dividerRenderer) Layout(size fyne.Size) {
	width := float32(1)
	if r.d.focused || r.d.hover {
		width = 3
	}
	if r.d.axisY {
		r.line.Move(fyne.NewPos(0, (size.Height-width)/2))
		r.line.Resize(fyne.NewSize(size.Width, width))
	} else {
		r.line.Move(fyne.NewPos((size.Width-width)/2, 0))
		r.line.Resize(fyne.NewSize(width, size.Height))
	}
}
func (r *dividerRenderer) Refresh() {
	name := theme.ColorNameSeparator
	if r.d.focused || r.d.hover {
		name = theme.ColorNamePrimary
	}
	r.line.FillColor = r.d.Theme().Color(name, fyne.CurrentApp().Settings().ThemeVariant())
	r.Layout(r.d.Size())
	r.line.Refresh()
}

type workbenchLayout struct {
	w           *Workspace
	left, right *paneDivider
}

func (l *workbenchLayout) MinSize([]fyne.CanvasObject) fyne.Size { return fyne.NewSize(420, 260) }
func (l *workbenchLayout) Layout(_ []fyne.CanvasObject, size fyne.Size) {
	w := l.w
	left, right := float32(0), float32(0)
	gap := float32(6)
	if w.filesPaneEnabled {
		left = w.fileWidth
	}
	if w.monitorEnabled && size.Width >= 540 {
		right = w.monitorWidth
	}
	if size.Width < 1000 {
		left = min(left, max(170, size.Width*.28))
		right = min(right, max(190, size.Width*.24))
	}
	budget := max(0, size.Width-240)
	requested := left + right
	if requested > budget && requested > 0 {
		factor := budget / requested
		left *= factor
		right *= factor
	}
	if left > 0 {
		w.fileSourcesRoot.Show()
		w.fileSourcesRoot.Move(fyne.NewPos(0, 0))
		w.fileSourcesRoot.Resize(fyne.NewSize(left, size.Height))
		l.left.Show()
		l.left.Move(fyne.NewPos(left, 0))
		l.left.Resize(fyne.NewSize(gap, size.Height))
	} else {
		w.fileSourcesRoot.Hide()
		l.left.Hide()
	}
	x := float32(0)
	if left > 0 {
		x = left + gap
	}
	centerWidth := size.Width - x
	if right > 0 {
		centerWidth -= right + gap
		w.Monitor.Content.Show()
		w.Monitor.Content.Move(fyne.NewPos(size.Width-right, 0))
		w.Monitor.Content.Resize(fyne.NewSize(right, size.Height))
		l.right.Show()
		l.right.Move(fyne.NewPos(size.Width-right-gap, 0))
		l.right.Resize(fyne.NewSize(gap, size.Height))
	} else {
		w.Monitor.Content.Hide()
		l.right.Hide()
	}
	w.centerCell.Move(fyne.NewPos(x, 0))
	w.centerCell.Resize(fyne.NewSize(max(0, centerWidth), size.Height))
	w.Monitor.SetVisible(right > 0 && w.workspaceVisible)
}
func (w *Workspace) initWorkbench() {
	w.fileWidth = float32(w.App.UI.Preferences().FloatWithFallback("filesWidth", 280))
	w.monitorWidth = float32(w.App.UI.Preferences().FloatWithFallback("monitorWidth", 242))
	w.filesPaneEnabled = w.App.UI.Preferences().BoolWithFallback("filesVisible", true)
	if w.Protocol == "sftp" || w.Protocol == "ftp" {
		w.filesPaneEnabled = true
	}
	save := func() {
		w.App.UI.Preferences().SetFloat("filesWidth", float64(w.fileWidth))
		w.App.UI.Preferences().SetFloat("monitorWidth", float64(w.monitorWidth))
	}
	left := newPaneDivider(func(delta float32) { w.fileWidth = max(170, min(480, w.fileWidth+delta)); w.bodyHost.Refresh() }, func() { w.fileWidth = 280; w.bodyHost.Refresh() }, save)
	right := newPaneDivider(func(delta float32) { w.monitorWidth = max(190, min(400, w.monitorWidth-delta)); w.bodyHost.Refresh() }, func() { w.monitorWidth = 242; w.bodyHost.Refresh() }, save)
	left.minimum = func() { w.fileWidth = 170; w.bodyHost.Refresh() }
	left.maximum = func() { w.fileWidth = 480; w.bodyHost.Refresh() }
	right.minimum = func() { w.monitorWidth = 190; w.bodyHost.Refresh() }
	right.maximum = func() { w.monitorWidth = 400; w.bodyHost.Refresh() }
	w.bodyHost = container.New(&workbenchLayout{w: w, left: left, right: right}, w.fileSourcesRoot, w.centerCell, w.Monitor.Content, left, right)
	w.MonitorToggle = widget.NewCheck("监控", func(v bool) { w.monitorEnabled = v; w.bodyHost.Refresh() })
	filesToggle := action("文件", theme.FolderIcon(), w.toggleFiles)
	toolbar := container.NewHBox(layout.NewSpacer(), filesToggle, action("分屏", theme.GridIcon(), func() {
		if w.secondTerminal != nil {
			w.SetTerminalSplit(nil, true)
		} else {
			w.ChooseTerminalSplit()
		}
	}), action("编辑", theme.DocumentCreateIcon(), func() {
		if len(w.documents) > 0 {
			w.showEditor()
		} else {
			w.ActiveFileView().OpenSelectedText()
		}
	}), w.MonitorToggle)
	w.Content = container.NewBorder(surface(toolbar, theme.ColorNameBackground), surface(w.Status, theme.ColorNameBackground), nil, nil, w.bodyHost)
}

func (w *Workspace) LayoutSnapshot() map[string]any {
	state := map[string]any{"fileWidth": w.fileWidth, "monitorWidth": w.monitorWidth, "filesVisible": w.filesPaneEnabled, "monitorVisible": w.monitorEnabled, "editorDock": w.editorDock, "editorRight": w.editorDockRight, "editorBottomRatio": w.editorBottomRatio, "editorRightRatio": w.editorRightRatio}
	view := w.ActiveFileView()
	if scroll, ok := view.filePane.(*container.Scroll); ok {
		state["fileScrollOffsetY"] = scroll.Offset.Y
		state["fileScrollHeight"] = scroll.Size().Height
		state["fileScrollContentHeight"] = scroll.Content.Size().Height
	}
	if view.Files != nil {
		state["fileListHeight"] = view.Files.Size().Height
		state["selectedFileIndex"] = view.selected
		state["currentDirectory"] = view.directory
		state["directoryReady"] = view.directoryReady
		state["directoryError"] = view.directoryError
	}
	if view.transferList != nil {
		state["queueListHeight"] = view.transferList.Size().Height
	}
	if d := w.ActiveDocument(); d != nil {
		state["editorBodyHeight"] = d.Editor.Size().Height
		state["editorHostHeight"] = w.editorHost.Size().Height
	}
	return state
}

func (d *paneDivider) AccessibilityLabel() string {
	return "调整面板大小；方向键微调，Shift加速，Home/End到边界，Enter重置"
}
func (d *paneDivider) AccessibilityRole() fyne.AccessibleRole { return fyne.AccessibleRoleContainer }
