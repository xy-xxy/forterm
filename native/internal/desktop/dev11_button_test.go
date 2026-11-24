package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/driver/desktop"
	ftest "fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"image/color"
	"image/png"
	"os"
	"path/filepath"
	"testing"
)

func saveDev11(t *testing.T, a *App, name string) {
	t.Helper()
	out := os.Getenv("FORTERM_DEV11_EVIDENCE")
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
func TestDev11ButtonBarSoftwareReview(t *testing.T) {
	a, _, _ := formFixture(t)
	for _, tc := range []struct {
		name, mode string
		size       fyne.Size
	}{{"dark", "dark", fyne.NewSize(1150, 850)}, {"light", "light", fyne.NewSize(1050, 650)}, {"narrow", "dark", fyne.NewSize(560, 500)}, {"stacked", "light", fyne.NewSize(480, 500)}} {
		a.SetAppearance(tc.mode)
		a.Window.Resize(tc.size)
		f := a.newConnectionForm(nil, false, nil)
		f.show()
		a.Window.Canvas().Capture()
		saveDev11(t, a, tc.name+"-top")
		for _, b := range []*connectionButton{f.test, f.closeButton, f.connect} {
			if b.Size().Height != 34 {
				t.Fatalf("%s: %s height%v", tc.name, b.Text, b.Size())
			}
			if b.Size().Width < b.minWidth {
				t.Fatal("lost horizontal padding")
			}
			p := objectPos(b)
			if p.X < 0 || p.X+b.Size().Width > tc.size.Width || p.Y+b.Size().Height > tc.size.Height {
				t.Fatal("button clipped", tc.name, b.Text, p)
			}
		}
		if f.more.AccessibilityLabel() == "" || f.more.Size().Height != 30 {
			t.Fatal("icon has no accessible name or hit area")
		}
		f.togglePrivate(true)
		ftest.Tap(f.categories[3])
		assertInViewport(t, f.viewport, f.jump)
		saveDev11(t, a, tc.name+"-bottom")
		f.setBusy(true, false)
		a.Window.Canvas().Capture()
		saveDev11(t, a, tc.name+"-testing")
		if !f.stop.Visible() || f.stop.Disabled() || !f.more.Disabled() {
			t.Fatal("test cancel or save menu busy state lost")
		}
		for _, b := range []*connectionButton{f.stop, f.connect} {
			p := objectPos(b)
			if p.X < 0 || p.X+b.Size().Width > tc.size.Width || p.Y+b.Size().Height > tc.size.Height {
				t.Fatal("busy buttons clipped")
			}
		}
		f.setBusy(false, false)
		f.popup.Hide()
	}
}
func TestDev11ButtonStatesKeyboardTooltipAndSaveMenu(t *testing.T) {
	a, _, _ := formFixture(t)
	a.SetAppearance("dark")
	count := 0
	b := newConnectionButton("保存并连接", nil, 144, func() { count++ })
	b.Importance = widget.HighImportance
	icon := newConnectionIcon("更多保存方式：仅保存信息", theme.MenuDropDownIcon(), func() {})
	box := container.NewCenter(container.NewHBox(b, icon))
	a.Window.SetContent(container.NewThemeOverride(withConnectionTooltips(box, icon), connectionDialogTheme{}))
	a.Window.Resize(fyne.NewSize(500, 200))
	a.Window.Canvas().Capture()
	bg := ftest.WidgetRenderer(b).Objects()[0].(*canvas.Rectangle)
	rgba := func(c color.Color) color.NRGBA { return color.NRGBAModel.Convert(c).(color.NRGBA) }
	initial := rgba(bg.FillColor)
	b.MouseIn(nil)
	hover := rgba(bg.FillColor)
	b.MouseDown(&desktop.MouseEvent{Button: desktop.MouseButtonPrimary})
	pressed := rgba(bg.FillColor)
	if initial == hover || hover == pressed {
		t.Fatal("hover/pressed not distinguishable")
	}
	b.MouseUp(nil)
	b.MouseOut()
	a.Window.Canvas().Focus(b)
	ring := ftest.WidgetRenderer(b).Objects()[3].(*canvas.Rectangle)
	_, _, _, alpha := ring.StrokeColor.RGBA()
	if alpha == 0 {
		t.Fatal("keyboard focus invisible")
	}
	b.TypedKey(&fyne.KeyEvent{Name: fyne.KeyReturn})
	b.TypedKey(&fyne.KeyEvent{Name: fyne.KeySpace})
	if count != 2 || a.Window.Canvas().Focused() != b {
		t.Fatal("Enter/Space lost callback or keyboard focus")
	}
	b.Disable()
	b.TypedKey(&fyne.KeyEvent{Name: fyne.KeyReturn})
	ftest.Tap(b)
	if count != 2 {
		t.Fatal("disabled action fired")
	}
	focusBeforeTooltip := a.Window.Canvas().Focused()
	icon.MouseIn(nil)
	if a.Window.Canvas().Focused() != focusBeforeTooltip {
		t.Fatal("tooltip stole keyboard focus")
	}
	if icon.tooltip == nil || icon.AccessibilityLabel() == "" {
		t.Fatal("icon tooltip or accessible name missing")
	}
	icon.MouseOut()
	if icon.tooltip != nil {
		t.Fatal("tooltip survived mouse-out")
	}
	b.Enable()
	states := container.NewGridWithColumns(5)
	buttons := []*connectionButton{}
	for _, name := range []string{"默认", "悬停", "按下", "禁用", "键盘焦点"} {
		button := newConnectionButton("保存并连接", nil, 144, func() {})
		button.Importance = widget.HighImportance
		buttons = append(buttons, button)
		states.Add(container.NewVBox(widget.NewLabel(name), container.NewCenter(button)))
	}
	a.Window.SetContent(container.NewThemeOverride(container.NewCenter(states), connectionDialogTheme{}))
	a.Window.Resize(fyne.NewSize(1000, 180))
	a.Window.Canvas().Capture()
	buttons[1].MouseIn(nil)
	buttons[2].MouseDown(&desktop.MouseEvent{Button: desktop.MouseButtonPrimary})
	buttons[3].Disable()
	a.Window.Canvas().Focus(buttons[4])
	saveDev11(t, a, "button-states")
	// A real menu keyboard selection invokes the original only-save action and
	// retains the one-time-password discard confirmation, without connecting.
	a.initChrome()
	a.Window.Resize(fyne.NewSize(1050, 650))
	f := a.newConnectionForm(nil, false, nil)
	f.show()
	defer f.popup.Hide()
	f.host.SetText("synthetic.invalid")
	f.user.SetText("synthetic")
	f.password.SetText("synthetic-one-time")
	beforeHover := a.Window.Canvas().Focused()
	f.more.MouseIn(nil)
	if a.Window.Canvas().Focused() != beforeHover {
		t.Fatal("save tooltip stole Entry focus")
	}
	if f.more.tooltip.Position().Y+f.more.tooltip.Size().Height > objectPos(f.more).Y-objectPos(f.more.tooltip.parent).Y {
		t.Fatal("footer tooltip covered its trigger")
	}
	saveDev11(t, a, "save-options-tooltip")
	f.more.MouseOut()
	formOverlay := a.Window.Canvas().Overlays().Top()
	ftest.Tap(f.more)
	top := a.Window.Canvas().Overlays().Top()
	obj := findPickerObject(top, func(o fyne.CanvasObject) bool { _, ok := o.(*widget.PopUpMenu); return ok })
	if obj == nil {
		t.Fatal("discoverable save menu missing")
	}
	menu := obj.(*widget.PopUpMenu)
	pos := objectPos(menu)
	if pos.X < 0 || pos.Y < 0 || pos.X+menu.Size().Width > a.Window.Canvas().Size().Width || pos.Y+menu.Size().Height > a.Window.Canvas().Size().Height {
		t.Fatal("save menu outside canvas", pos, menu.Size())
	}
	menu.TypedKey(&fyne.KeyEvent{Name: fyne.KeyEscape})
	if a.Window.Canvas().Overlays().Top() != formOverlay || f.password.Text == "" {
		t.Fatal("Escape did not preserve form input")
	}
	ftest.Tap(f.more)
	top = a.Window.Canvas().Overlays().Top()
	obj = findPickerObject(top, func(o fyne.CanvasObject) bool { _, ok := o.(*widget.PopUpMenu); return ok })
	menu = obj.(*widget.PopUpMenu)
	saveDev11(t, a, "save-menu")
	menu.TypedKey(&fyne.KeyEvent{Name: fyne.KeyDown})
	menu.TypedKey(&fyne.KeyEvent{Name: fyne.KeyReturn})
	if f.closed || f.password.Text == "" || len(a.Store.Snapshot().Profiles) != 0 {
		t.Fatal("menu silently saved/discarded one-time password")
	}
	if a.Window.Canvas().Overlays().Top() == top || a.Window.Canvas().Overlays().Top() == nil {
		t.Fatal("only-save discard confirmation missing")
	}
}
