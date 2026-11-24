package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/driver/desktop"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"image/color"
)

// connectionButton retains native Button input/accessibility semantics, with a
// deliberately sized renderer. Its refresh affects only this control.
type connectionButton struct {
	widget.Button
	minWidth           float32
	iconOnly           bool
	outlined           bool
	name               string
	hover, focus, down bool
	tooltip            *connectionTooltip
	tooltipHost        *connectionTooltip
}

func newConnectionButton(text string, icon fyne.Resource, width float32, fn func()) *connectionButton {
	b := &connectionButton{minWidth: width, name: text}
	b.Text = text
	b.Icon = icon
	b.OnTapped = fn
	b.ExtendBaseWidget(b)
	return b
}
func newConnectionIcon(name string, icon fyne.Resource, fn func()) *connectionButton {
	b := newConnectionButton("", icon, 30, fn)
	b.name = name
	b.iconOnly = true
	return b
}
func (b *connectionButton) AccessibilityLabel() string {
	if b.Text != "" {
		return b.Text
	}
	return b.name
}
func (b *connectionButton) FocusGained() { b.focus = true; b.Refresh() }
func (b *connectionButton) FocusLost()   { b.focus = false; b.down = false; b.Refresh() }
func (b *connectionButton) MouseIn(*desktop.MouseEvent) {
	b.hover = true
	b.Refresh()
	if b.iconOnly && !b.Disabled() {
		b.showTooltip()
	}
}
func (b *connectionButton) MouseMoved(*desktop.MouseEvent) {}
func (b *connectionButton) MouseOut()                      { b.hover = false; b.down = false; b.hideTooltip(); b.Refresh() }
func (b *connectionButton) MouseDown(ev *desktop.MouseEvent) {
	if ev.Button == desktop.MouseButtonPrimary && !b.Disabled() {
		b.down = true
		b.hideTooltip()
		b.Refresh()
	}
}
func (b *connectionButton) MouseUp(*desktop.MouseEvent) { b.down = false; b.Refresh() }
func (b *connectionButton) Tapped(*fyne.PointEvent) {
	if b.Disabled() {
		return
	}
	b.down = false
	b.hideTooltip()
	b.Refresh()
	if b.OnTapped != nil {
		b.OnTapped()
	}
}
func (b *connectionButton) TypedKey(e *fyne.KeyEvent) {
	if e.Name == fyne.KeySpace || e.Name == fyne.KeyReturn || e.Name == fyne.KeyEnter {
		b.Tapped(nil)
	}
}
func (b *connectionButton) Hide()    { b.hideTooltip(); b.Button.Hide() }
func (b *connectionButton) Disable() { b.hideTooltip(); b.Button.Disable() }
func (b *connectionButton) hideTooltip() {
	if b.tooltip != nil {
		if b.tooltip.owner == b {
			b.tooltip.Hide()
			b.tooltip.owner = nil
		}
		b.tooltip = nil
	}
}
func (b *connectionButton) showTooltip() {
	if b.tooltipHost != nil {
		b.tooltipHost.showFor(b)
	}
}
func (b *connectionButton) CreateRenderer() fyne.WidgetRenderer {
	bg := canvas.NewRectangle(color.Transparent)
	bg.CornerRadius = 6
	bg.StrokeWidth = 1
	ring := canvas.NewRectangle(color.Transparent)
	ring.CornerRadius = 7
	ring.StrokeWidth = 2
	text := canvas.NewText(b.Text, color.Transparent)
	text.TextSize = 14
	icon := widget.NewIcon(b.Icon)
	r := &connectionButtonRenderer{button: b, bg: bg, ring: ring, text: text, icon: icon}
	r.Refresh()
	return r
}

type connectionButtonRenderer struct {
	button   *connectionButton
	bg, ring *canvas.Rectangle
	text     *canvas.Text
	icon     *widget.Icon
}

func (r *connectionButtonRenderer) MinSize() fyne.Size {
	b := r.button
	if b.iconOnly {
		return fyne.NewSize(30, 30)
	}
	width := fyne.CurrentApp().Driver()
	ts, _ := width.RenderedTextSize(b.Text, 14, fyne.TextStyle{}, theme.CurrentForWidget(b).Font(fyne.TextStyle{}))
	w := ts.Width + 32
	if b.Icon != nil {
		w += 24
	}
	return fyne.NewSize(max(b.minWidth, w), 34)
}
func (r *connectionButtonRenderer) Layout(s fyne.Size) {
	r.bg.Move(fyne.NewPos(1, 1))
	r.bg.Resize(fyne.NewSize(max(0, s.Width-2), max(0, s.Height-2)))
	r.ring.Resize(s)
	b := r.button
	textSize := r.text.MinSize()
	w := textSize.Width
	if b.Icon != nil {
		w += 24
	}
	if b.iconOnly {
		w = 16
	}
	x := (s.Width - w) / 2
	if b.Icon != nil {
		r.icon.Move(fyne.NewPos(x, (s.Height-16)/2))
		r.icon.Resize(fyne.NewSize(16, 16))
		x += 24
	}
	r.text.Move(fyne.NewPos(x, (s.Height-textSize.Height)/2))
	r.text.Resize(textSize)
}
func (r *connectionButtonRenderer) Refresh() {
	b := r.button
	th := theme.CurrentForWidget(b)
	v := fyne.CurrentApp().Settings().ThemeVariant()
	fg := th.Color(theme.ColorNameForeground, v)
	border := th.Color(theme.ColorNameInputBorder, v)
	bg := color.Color(color.Transparent)
	primary := b.Importance == widget.HighImportance
	if primary {
		bg = color.NRGBA{R: 37, G: 137, B: 82, A: 255}
		fg = color.White
		border = bg
	}
	if b.hover && !b.Disabled() {
		if primary {
			bg = color.NRGBA{R: 42, G: 155, B: 94, A: 255}
			border = bg
		} else {
			bg = th.Color(theme.ColorNameHover, v)
		}
	}
	if b.down && !b.Disabled() {
		if primary {
			bg = color.NRGBA{R: 27, G: 105, B: 62, A: 255}
			border = bg
		} else {
			bg = th.Color(theme.ColorNamePressed, v)
		}
	}
	if b.Disabled() {
		fg = th.Color(theme.ColorNameDisabled, v)
		border = th.Color(theme.ColorNameDisabledButton, v)
		if primary {
			bg = border
		}
	}
	if b.iconOnly && !b.outlined {
		border = color.Transparent
	}
	r.bg.FillColor = bg
	r.bg.StrokeColor = border
	if b.focus && !b.Disabled() {
		r.ring.StrokeColor = th.Color(theme.ColorNamePrimary, v)
	} else {
		r.ring.StrokeColor = color.Transparent
	}
	r.text.Text = b.Text
	r.text.FontSource = th.Font(fyne.TextStyle{})
	r.text.Color = fg
	if b.Icon != nil {
		resource := b.Icon
		if b.Disabled() {
			resource = theme.NewDisabledResource(resource)
		}
		r.icon.SetResource(resource)
		r.icon.Show()
	} else {
		r.icon.Hide()
	}
	if b.Text == "" {
		r.text.Hide()
	} else {
		r.text.Show()
	}
	r.Layout(b.Size())
	r.bg.Refresh()
	r.ring.Refresh()
	r.text.Refresh()
}
func (r *connectionButtonRenderer) Objects() []fyne.CanvasObject {
	return []fyne.CanvasObject{r.bg, r.text, r.icon, r.ring}
}
func (r *connectionButtonRenderer) Destroy() { r.button.hideTooltip() }

// Tooltips live inside the existing modal's object tree. A separate PopUp
// would install another focus manager and swallow typing while hovering.
type connectionTooltip struct {
	widget.BaseWidget
	label  *widget.Label
	parent fyne.CanvasObject
	owner  *connectionButton
}

func withConnectionTooltips(content fyne.CanvasObject, buttons ...*connectionButton) fyne.CanvasObject {
	tip := &connectionTooltip{label: widget.NewLabel("")}
	tip.ExtendBaseWidget(tip)
	tip.Hide()
	root := container.New(&connectionTooltipLayout{}, content, tip)
	tip.parent = root
	for _, b := range buttons {
		b.tooltipHost = tip
	}
	return root
}
func (t *connectionTooltip) showFor(b *connectionButton) {
	if t.owner != nil {
		t.owner.tooltip = nil
	}
	t.owner = b
	b.tooltip = t
	t.label.SetText(b.name)
	driver := fyne.CurrentApp().Driver()
	p := driver.AbsolutePositionForObject(b).Subtract(driver.AbsolutePositionForObject(t.parent)).Add(fyne.NewPos(0, b.Size().Height+4))
	t.Resize(t.MinSize())
	if p.Y+t.Size().Height > t.parent.Size().Height-4 {
		p.Y = driver.AbsolutePositionForObject(b).Y - driver.AbsolutePositionForObject(t.parent).Y - t.Size().Height - 6
	}
	t.Move(p)
	t.clamp(t.parent.Size())
	t.Show()
	t.Refresh()
}
func (t *connectionTooltip) clamp(s fyne.Size) {
	p := t.Position()
	p.X = max(4, min(p.X, s.Width-t.Size().Width-4))
	p.Y = max(4, min(p.Y, s.Height-t.Size().Height-4))
	t.Move(p)
}
func (t *connectionTooltip) CreateRenderer() fyne.WidgetRenderer {
	bg := canvas.NewRectangle(color.Transparent)
	bg.CornerRadius = 5
	bg.StrokeWidth = 1
	r := &connectionTooltipRenderer{tip: t, bg: bg, text: container.NewThemeOverride(t.label, connectionNoteTheme{})}
	r.Refresh()
	return r
}

type connectionTooltipRenderer struct {
	tip  *connectionTooltip
	bg   *canvas.Rectangle
	text fyne.CanvasObject
}

func (r *connectionTooltipRenderer) MinSize() fyne.Size {
	return r.text.MinSize().Add(fyne.NewSize(12, 8))
}
func (r *connectionTooltipRenderer) Layout(s fyne.Size) {
	r.bg.Resize(s)
	r.text.Move(fyne.NewPos(6, 4))
	r.text.Resize(fyne.NewSize(max(0, s.Width-12), max(0, s.Height-8)))
}
func (r *connectionTooltipRenderer) Refresh() {
	th := theme.CurrentForWidget(r.tip)
	v := fyne.CurrentApp().Settings().ThemeVariant()
	r.bg.FillColor = th.Color(theme.ColorNameOverlayBackground, v)
	r.bg.StrokeColor = th.Color(theme.ColorNameInputBorder, v)
	r.Layout(r.tip.Size())
	r.bg.Refresh()
	r.text.Refresh()
}
func (r *connectionTooltipRenderer) Objects() []fyne.CanvasObject {
	return []fyne.CanvasObject{r.bg, r.text}
}
func (r *connectionTooltipRenderer) Destroy() {}

type connectionTooltipLayout struct{}

func (*connectionTooltipLayout) MinSize(o []fyne.CanvasObject) fyne.Size { return o[0].MinSize() }
func (*connectionTooltipLayout) Layout(o []fyne.CanvasObject, s fyne.Size) {
	o[0].Move(fyne.Position{})
	o[0].Resize(s)
	tip := o[1].(*connectionTooltip)
	if tip.Visible() {
		tip.clamp(s)
	}
}
