package uiresponse

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/widget"
)

type measuredButton struct {
	widget.Button
	pairer  *eventPairer
	phase   func() int
	context activation
}

func newMeasuredButton(label string, p *eventPairer, phase func() int, run func(activation)) *measuredButton {
	b := &measuredButton{pairer: p, phase: phase}
	b.Text = label
	b.OnTapped = func() { a := b.context; b.context = activation{}; run(a) }
	b.ExtendBaseWidget(b)
	return b
}
func (b *measuredButton) Tapped(e *fyne.PointEvent) {
	if b.Disabled() {
		return
	}
	b.context = b.pairer.take(b.phase(), e != nil)
	b.Button.Tapped(e)
	b.context = activation{}
}
func (b *measuredButton) TypedKey(e *fyne.KeyEvent) {
	b.context = activation{Status: "unmatched", Reason: "keyboard_activation"}
	b.Button.TypedKey(e)
	b.context = activation{}
}
