package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/driver/desktop"
	ftest "fyne.io/fyne/v2/test"
	"testing"
)

func TestDividerPointerDownCapturesKeyboardBeforeDoubleTapTimeout(t *testing.T) {
	app := ftest.NewApp()
	defer app.Quit()
	value := float32(280)
	divider := newPaneDivider(func(delta float32) { value += delta }, func() { value = 280 }, nil)
	window := app.NewWindow("divider keyboard")
	window.SetContent(divider)
	window.Resize(fyne.NewSize(100, 300))
	divider.MouseDown(&desktop.MouseEvent{Button: desktop.MouseButtonPrimary})
	if window.Canvas().Focused() != divider {
		t.Fatal("divider did not capture focus on pointer-down")
	}
	window.Canvas().Focused().TypedKey(&fyne.KeyEvent{Name: fyne.KeyRight})
	if value != 288 {
		t.Fatal("arrow key not routed to divider")
	}
	divider.KeyDown(&fyne.KeyEvent{Name: desktop.KeyShiftLeft})
	divider.TypedKey(&fyne.KeyEvent{Name: fyne.KeyLeft})
	if value != 256 {
		t.Fatal("shift step incorrect")
	}
	divider.TypedKey(&fyne.KeyEvent{Name: fyne.KeyHome})
	if value != 280 {
		t.Fatal("reset failed")
	}
}
