package desktop

import (
	"context"
	"fmt"
	"forterm/native/internal/fileops"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	ftest "fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/widget"
	"image/png"
	"math"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func saveDev10(t *testing.T, a *App, name string) {
	t.Helper()
	out := os.Getenv("FORTERM_DEV10_EVIDENCE")
	if out == "" {
		return
	}
	if e := os.MkdirAll(out, 0700); e != nil {
		t.Fatal(e)
	}
	f, e := os.Create(filepath.Join(out, name+".png"))
	if e != nil {
		t.Fatal(e)
	}
	defer f.Close()
	if e = png.Encode(f, a.Window.Canvas().Capture()); e != nil {
		t.Fatal(e)
	}
}
func objectPos(o fyne.CanvasObject) fyne.Position {
	return fyne.CurrentApp().Driver().AbsolutePositionForObject(o)
}
func assertInViewport(t *testing.T, v *formViewport, o fyne.CanvasObject) {
	t.Helper()
	p, base := objectPos(o), objectPos(v.scroll)
	if p.Y < base.Y-1 || p.Y+o.Size().Height > base.Y+v.scroll.Size().Height+1 {
		t.Fatalf("not reachable: %T y%.1f..%.1f viewport%.1f..%.1f offset%.1f min%v content%v", o, p.Y, p.Y+o.Size().Height, base.Y, base.Y+v.scroll.Size().Height, v.scroll.Offset.Y, v.padded.MinSize(), v.padded.Size())
	}
	if p.X+o.Size().Width > base.X+v.scroll.Size().Width-15 {
		t.Fatalf("control overlaps dedicated scrollbar gutter: %T", o)
	}
}
func TestDev10ConnectionCardScrollReachability(t *testing.T) {
	a, _, _ := formFixture(t)
	for _, tc := range []struct {
		name, mode string
		size       fyne.Size
		scale      float32
	}{
		{"saved-dark", "dark", fyne.NewSize(1150, 850), 1},
		{"saved-light", "light", fyne.NewSize(1050, 650), 1},
		{"saved-short", "dark", fyne.NewSize(800, 500), 1},
		{"saved-150", "dark", fyne.NewSize(700, 433), 1.5},
		{"saved-narrow", "dark", fyne.NewSize(560, 500), 1},
		{"saved-stacked", "light", fyne.NewSize(480, 500), 1},
	} {
		t.Run(tc.name, func(t *testing.T) {
			a.SetAppearance(tc.mode)
			a.Window.Canvas().(interface{ SetScale(float32) }).SetScale(tc.scale)
			a.Window.Resize(tc.size)
			f := a.newConnectionForm(nil, false, nil)
			f.show()
			a.Window.Canvas().Capture()
			f.viewport.Reflow()
			saveDev10(t, a, tc.name+"-top")
			v := f.viewport
			s := v.scroll
			old := v.padded.MinSize().Height
			f.togglePrivate(true)
			a.Window.Canvas().Capture()
			v.Reflow()
			if v.padded.MinSize().Height <= old+60 {
				t.Fatalf("expanded card did not grow range %.1f->%.1f", old, v.padded.MinSize().Height)
			}
			p := objectPos(s).Add(fyne.NewPos(s.Size().Width/2, 10))
			ftest.Scroll(a.Window.Canvas(), p, 0, -10000)
			if s.Offset.Y < 1 {
				t.Fatal("wheel hit did not reach viewport")
			}
			assertInViewport(t, v, f.jump)
			footerY := objectPos(f.footer).Y
			if objectPos(s).Y+s.Size().Height > footerY {
				t.Fatal("fixed footer overlaps viewport")
			}
			if objectPos(f.connect).Y+f.connect.Size().Height > tc.size.Height {
				t.Fatalf("save action clipped: %v size%v", objectPos(f.connect), f.connect.Size())
			}
			saveDev10(t, a, tc.name+"-bottom")
			// Drag the actual scrollbar hit area; do not assign offsets to simulate a drag.
			s.ScrollToTop()
			a.Window.Canvas().Capture()
			hit := objectPos(s).Add(fyne.NewPos(s.Size().Width-2, 3))
			ftest.Drag(a.Window.Canvas(), hit, 0, s.Size().Height*2)
			if s.Offset.Y < 1 {
				t.Fatal("scrollbar thumb drag failed")
			}
			assertInViewport(t, v, f.jump)
			// Native focus traversal must reveal the last control, then return upward.
			s.ScrollToTop()
			a.Window.Canvas().Focus(f.phrase)
			assertInViewport(t, v, f.phrase)
			a.Window.Canvas().FocusNext()
			a.Window.Canvas().FocusNext()
			assertInViewport(t, v, f.jump)
			a.Window.Canvas().Focus(f.host)
			assertInViewport(t, v, f.host)
			// Expanded -> collapsed -> expanded at the same outer size cannot retain stale extent.
			f.togglePrivate(false)
			v.Reflow()
			f.togglePrivate(true)
			v.Reflow()
			ftest.Tap(f.categories[3])
			assertInViewport(t, v, f.jump)
			// Every visible input keeps its content width inside the viewport at short/high DPI sizes.
			if f.host.Size().Width < 60 || f.port.Size().Width < 40 {
				t.Fatalf("address fields squeezed: %v %v", f.host.Size(), f.port.Size())
			}
			f.popup.Hide()
		})
	}
	// Same popup shrinks and grows; neither transition loses the bottom anchor.
	a.Window.Canvas().(interface{ SetScale(float32) }).SetScale(1)
	a.Window.Resize(fyne.NewSize(1150, 850))
	f := a.newConnectionForm(nil, false, nil)
	f.show()
	f.togglePrivate(true)
	for _, size := range []fyne.Size{fyne.NewSize(700, 433), fyne.NewSize(1150, 850)} {
		a.Window.Resize(size)
		a.Window.Canvas().Capture()
		f.viewport.Reflow()
		ftest.Tap(f.categories[3])
		assertInViewport(t, f.viewport, f.jump)
		saveDev10(t, a, fmt.Sprintf("resize-%.0f", size.Width))
	}
	f.popup.Hide()
	f = a.newConnectionForm(nil, true, nil)
	f.show()
	saveDev10(t, a, "quick-dark")
	f.popup.Hide()
}
func TestDev10GenericViewportExpansionAndKeyboard(t *testing.T) {
	a, _, _ := formFixture(t)
	body := container.NewVBox()
	v := newFormViewport(body)
	for i := 0; i < 16; i++ {
		e := newFormEntry(false)
		e.PlaceHolder = fmt.Sprint("question ", i)
		e.reveal = v.Reveal
		e.viewport = v
		body.Add(e)
	}
	last := body.Objects[15].(*formEntry)
	accordion := widget.NewAccordion(widget.NewAccordionItem("More", widget.NewLabel("last expanded content")))
	body.Add(accordion)
	root := container.NewBorder(nil, widget.NewButton("Fixed footer", func() {}), nil, nil, v)
	a.Window.SetContent(root)
	a.Window.Resize(fyne.NewSize(500, 350))
	a.Window.Canvas().Capture()
	old := v.padded.MinSize().Height
	accordion.Open(0)
	v.Reflow()
	if v.padded.MinSize().Height <= old {
		t.Fatal("accordion expansion retained old range")
	}
	first := body.Objects[0].(*formEntry)
	a.Window.Canvas().Focus(first)
	ftest.Type(first, strings.Repeat("long-address-", 20))
	before := v.scroll.Offset.Y
	ftest.Scroll(a.Window.Canvas(), objectPos(first).Add(fyne.NewPos(30, 10)), 0, -60)
	if v.scroll.Offset.Y <= before {
		t.Fatal("vertical wheel over Entry was swallowed by its internal scroll")
	}
	a.Window.Canvas().Focus(first)
	for i := 0; i < 15; i++ {
		a.Window.Canvas().FocusNext()
	}
	if a.Window.Canvas().Focused() != last {
		t.Fatal("Tab traversal did not reach last entry")
	}
	assertInViewport(t, v, last)
	ftest.Scroll(a.Window.Canvas(), objectPos(v.scroll).Add(fyne.NewPos(100, 30)), 0, -10000)
	want := max(float32(0), v.padded.MinSize().Height-v.scroll.Size().Height)
	if math.Abs(float64(v.scroll.Offset.Y-want)) > 1 {
		t.Fatalf("wheel stopped early %.1f want%.1f", v.scroll.Offset.Y, want)
	}
}

func TestDev10AuthenticationAndPermissionsUseBoundedViewport(t *testing.T) {
	a, _, pump := formFixture(t)
	a.Window.Resize(fyne.NewSize(700, 433))
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	done := make(chan error, 1)
	questions := make([]string, 16)
	for i := range questions {
		questions[i] = fmt.Sprintf("synthetic verification question %d", i)
	}
	go func() { _, e := a.credentialQuestions(ctx, "Synthetic authentication", questions, nil); done <- e }()
	pump(func() bool { return a.Window.Canvas().Overlays().Top() != nil })
	popup := a.Window.Canvas().Overlays().Top()
	entries := []*formEntry{}
	findPickerObject(popup, func(o fyne.CanvasObject) bool {
		if e, ok := o.(*formEntry); ok {
			entries = append(entries, e)
		}
		return false
	})
	if len(entries) != 16 {
		t.Fatalf("authentication entries %d", len(entries))
	}
	last := entries[15]
	a.Window.Canvas().Focus(last)
	assertInViewport(t, last.viewport, last)
	saveDev10(t, a, "auth-16-last")
	cancel()
	pump(func() bool { return a.Window.Canvas().Overlays().Top() == nil })
	if e := <-done; e == nil {
		t.Fatal("cancel authenticated")
	}
	for _, e := range entries {
		if e.Text != "" {
			t.Fatal("cancel retained secret")
		}
	}
	fs := &directoryFixture{}
	w := &Workspace{App: a, Window: a.Window, Context: context.Background(), FileConn: fs, Title: "synthetic permissions"}
	w.showPermissions(fileops.Permissions{Path: "/synthetic", Mode: 0755, Known: true, Supported: true})
	popup = a.Window.Canvas().Overlays().Top()
	var viewport *formViewport
	findPickerObject(popup, func(o fyne.CanvasObject) bool {
		if v, ok := o.(*formViewport); ok {
			viewport = v
		}
		return false
	})
	if viewport == nil {
		t.Fatal("permissions missing shared viewport")
	}
	var lastCheck *formCheck
	findPickerObject(popup, func(o fyne.CanvasObject) bool {
		if c, ok := o.(*formCheck); ok {
			lastCheck = c
		}
		return false
	})
	ftest.Scroll(a.Window.Canvas(), objectPos(viewport.scroll).Add(fyne.NewPos(10, 10)), 0, -10000)
	assertInViewport(t, viewport, lastCheck)
	saveDev10(t, a, "permissions-bottom")
	if fs.statCalls != 0 || fs.listCalls != 0 {
		t.Fatal("opening permission form performed remote operations")
	}
	uploadRouteButton(t, popup, "关闭").OnTapped()
}
