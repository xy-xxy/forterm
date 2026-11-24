package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
)

type meter struct {
	widget.BaseWidget
	value float64
}

func newMeter() *meter              { m := &meter{}; m.ExtendBaseWidget(m); return m }
func (m *meter) SetValue(v float64) { m.value = max(0, min(1, v)); m.Refresh() }
func (m *meter) CreateRenderer() fyne.WidgetRenderer {
	return &meterRenderer{m: m, bg: canvas.NewRectangle(theme.Color(theme.ColorNameSeparator)), fill: canvas.NewRectangle(theme.Color(theme.ColorNamePrimary))}
}

type meterRenderer struct {
	m        *meter
	bg, fill *canvas.Rectangle
}

func (r *meterRenderer) MinSize() fyne.Size           { return fyne.NewSize(20, 8) }
func (r *meterRenderer) Objects() []fyne.CanvasObject { return []fyne.CanvasObject{r.bg, r.fill} }
func (r *meterRenderer) Destroy()                     {}
func (r *meterRenderer) Layout(s fyne.Size) {
	r.bg.Move(fyne.NewPos(0, 2))
	r.bg.Resize(fyne.NewSize(s.Width, 4))
	r.fill.Move(fyne.NewPos(0, 2))
	r.fill.Resize(fyne.NewSize(s.Width*float32(r.m.value), 4))
}
func (r *meterRenderer) Refresh() {
	r.bg.FillColor = r.m.Theme().Color(theme.ColorNameSeparator, fyne.CurrentApp().Settings().ThemeVariant())
	r.fill.FillColor = r.m.Theme().Color(theme.ColorNamePrimary, fyne.CurrentApp().Settings().ThemeVariant())
	r.Layout(r.m.Size())
	r.bg.Refresh()
	r.fill.Refresh()
}
