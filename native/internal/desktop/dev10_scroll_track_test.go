package desktop

import (
	"fyne.io/fyne/v2"
	ftest "fyne.io/fyne/v2/test"
	"testing"
)

func TestDev10ScrollbarTrackMovesAndBottomStaysReachable(t *testing.T) {
	a, _, _ := formFixture(t)
	a.Window.Resize(fyne.NewSize(700, 433))
	f := a.newConnectionForm(nil, false, nil)
	f.show()
	defer f.popup.Hide()
	f.togglePrivate(true)
	v := f.viewport
	v.Reflow()
	s := v.scroll
	s.ScrollToTop()
	track := objectPos(s).Add(fyne.NewPos(s.Size().Width-2, s.Size().Height-3))
	ftest.TapCanvas(a.Window.Canvas(), track)
	if s.Offset.Y <= 0 {
		t.Fatal("actual scrollbar track tap did not move")
	}
	for i := 0; i < 20; i++ {
		ftest.TapCanvas(a.Window.Canvas(), track)
	}
	assertInViewport(t, v, f.jump)
	// Mouse wheel directly over a form Entry also scrolls the parent, while a
	// long value still follows its cursor horizontally in the native Entry.
	a.Window.Canvas().Focus(f.host)
	ftest.Type(f.host, "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz")
	inner := ftest.WidgetRenderer(f.host).Objects()
	horizontal := false
	for _, o := range inner {
		if scroll, ok := o.(*formEntryScroll); ok && scroll.Offset.X > 0 {
			horizontal = true
		}
	}
	if !horizontal {
		t.Fatal("long single-line input lost horizontal cursor reveal")
	}
	before := s.Offset.Y
	ftest.Scroll(a.Window.Canvas(), objectPos(f.host).Add(fyne.NewPos(20, 12)), 0, -50)
	if s.Offset.Y <= before {
		t.Fatal("wheel over long input did not reach outer viewport")
	}
}
