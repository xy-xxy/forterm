package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
)

// formViewport owns its vertical range. Fyne's scrollbar overlays its content;
// the padded content reserves a separate hit gutter and breathing room at both
// ends. Width-dependent rows must reflow before the scroll range is measured.
type formViewport struct {
	widget.BaseWidget
	scroll *container.Scroll
	body   fyne.CanvasObject
	padded *fyne.Container
}

func newFormViewport(body fyne.CanvasObject) *formViewport {
	v := &formViewport{body: body}
	v.padded = container.New(&formInsets{}, body)
	v.scroll = container.NewVScroll(v.padded)
	v.ExtendBaseWidget(v)
	return v
}
func (v *formViewport) CreateRenderer() fyne.WidgetRenderer { return &viewportRenderer{v: v} }
func (v *formViewport) Reflow() {
	// Layout at the available width, then measure again: a row can switch from
	// side-by-side to stacked without changing the outer popup's size.
	for i := 0; i < 3; i++ {
		v.padded.Resize(fyne.NewSize(v.scroll.Size().Width, max(v.scroll.Size().Height, v.padded.MinSize().Height)))
		v.padded.Refresh()
		v.scroll.Refresh()
	}
}
func (v *formViewport) Reveal(o fyne.CanvasObject) {
	v.Reflow()
	driver := fyne.CurrentApp().Driver()
	top := driver.AbsolutePositionForObject(o).Y - driver.AbsolutePositionForObject(v.scroll).Y + v.scroll.Offset.Y
	bottom := top + o.Size().Height
	offset := v.scroll.Offset.Y
	if top < offset+8 {
		offset = top - 8
	} else if bottom > offset+v.scroll.Size().Height-8 {
		offset = bottom - v.scroll.Size().Height + 8
	}
	v.scroll.ScrollToOffset(fyne.NewPos(0, max(0, min(offset, max(0, v.padded.MinSize().Height-v.scroll.Size().Height)))))
}

type viewportRenderer struct{ v *formViewport }

func (r *viewportRenderer) Layout(s fyne.Size)           { r.v.scroll.Resize(s); r.v.Reflow() }
func (r *viewportRenderer) MinSize() fyne.Size           { return fyne.NewSize(220, 48) }
func (r *viewportRenderer) Refresh()                     { r.Layout(r.v.Size()) }
func (r *viewportRenderer) Objects() []fyne.CanvasObject { return []fyne.CanvasObject{r.v.scroll} }
func (r *viewportRenderer) Destroy()                     {}

type formInsets struct{}

func (*formInsets) MinSize(o []fyne.CanvasObject) fyne.Size {
	return fyne.NewSize(220, o[0].MinSize().Height+32)
}
func (*formInsets) Layout(o []fyne.CanvasObject, s fyne.Size) {
	o[0].Move(fyne.NewPos(16, 16))
	o[0].Resize(fyne.NewSize(max(1, s.Width-40), max(0, o[0].MinSize().Height)))
}

// Outer-scroll focus reveal belongs to the controls, so native Tab dispatch,
// mouse focus and programmatic focus use the same path without polling/timers.
type formEntry struct {
	widget.Entry
	reveal   func(fyne.CanvasObject)
	viewport *formViewport
}

func newFormEntry(password bool) *formEntry {
	e := &formEntry{}
	e.Password = password
	e.Wrapping = fyne.TextWrap(fyne.TextTruncateClip)
	e.ExtendBaseWidget(e)
	return e
}
func (e *formEntry) FocusGained() {
	e.Entry.FocusGained()
	if e.reveal != nil {
		e.reveal(e)
	}
}

type formSelect struct {
	widget.Select
	reveal func(fyne.CanvasObject)
}

func newFormSelect(options []string) *formSelect {
	s := &formSelect{}
	s.Options = options
	s.PlaceHolder = "请选择"
	s.ExtendBaseWidget(s)
	return s
}
func (s *formSelect) FocusGained() {
	s.Select.FocusGained()
	if s.reveal != nil {
		s.reveal(s)
	}
}

type formCheck struct {
	widget.Check
	reveal func(fyne.CanvasObject)
}

func newFormCheck(text string) *formCheck {
	c := &formCheck{}
	c.Text = text
	c.ExtendBaseWidget(c)
	return c
}
func (c *formCheck) FocusGained() {
	c.Check.FocusGained()
	if c.reveal != nil {
		c.reveal(c)
	}
}

// An Entry has its own horizontal Scroll. Route vertical wheel events to the
// enclosing form, even when the pointer is directly over an input field.
func (e *formEntry) CreateRenderer() fyne.WidgetRenderer {
	base := e.Entry.CreateRenderer()
	objects := append([]fyne.CanvasObject(nil), base.Objects()...)
	for i, o := range objects {
		if s, ok := o.(*container.Scroll); ok {
			objects[i] = &formEntryScroll{Scroll: s, entry: e}
		}
	}
	return &formEntryRenderer{WidgetRenderer: base, objects: objects}
}

type formEntryRenderer struct {
	fyne.WidgetRenderer
	objects []fyne.CanvasObject
}

func (r *formEntryRenderer) Objects() []fyne.CanvasObject { return r.objects }

type formEntryScroll struct {
	*container.Scroll
	entry *formEntry
}

func (s *formEntryScroll) Scrolled(ev *fyne.ScrollEvent) {
	if ev.Scrolled.DY != 0 && s.entry.viewport != nil {
		s.entry.viewport.scroll.Scrolled(ev)
		return
	}
	s.Scroll.Scrolled(ev)
}

// The event-routing wrapper shares Scroll's live geometry. Its renderer is
// separate from Entry's cached renderer, so synchronize geometry before the
// object tree is walked for painting or hit testing.
func (s *formEntryScroll) CreateRenderer() fyne.WidgetRenderer {
	r := s.Scroll.CreateRenderer()
	r.Refresh()
	r.Layout(s.Size())
	return &formWheelRenderer{WidgetRenderer: r, scroll: s}
}

type formWheelRenderer struct {
	fyne.WidgetRenderer
	scroll *formEntryScroll
}

func (r *formWheelRenderer) Objects() []fyne.CanvasObject {
	r.WidgetRenderer.Layout(r.scroll.Size())
	return r.WidgetRenderer.Objects()
}

func (s *formSelect) CreateRenderer() fyne.WidgetRenderer {
	base := s.Select.CreateRenderer()
	r := &formSelectRenderer{WidgetRenderer: base, selectWidget: s}
	if bg, ok := base.Objects()[0].(*canvas.Rectangle); ok {
		r.border = bg
	}
	r.Refresh()
	return r
}

type formSelectRenderer struct {
	fyne.WidgetRenderer
	selectWidget *formSelect
	border       *canvas.Rectangle
}

func (r *formSelectRenderer) Refresh() {
	r.WidgetRenderer.Refresh()
	if r.border != nil {
		th := theme.CurrentForWidget(r.selectWidget)
		r.border.StrokeColor = th.Color(theme.ColorNameInputBorder, fyne.CurrentApp().Settings().ThemeVariant())
		r.border.StrokeWidth = 1
		r.border.CornerRadius = 4
		r.border.Refresh()
	}
}
