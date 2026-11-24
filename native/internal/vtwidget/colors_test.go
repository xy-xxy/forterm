package vtwidget

import (
	ftest "fyne.io/fyne/v2/test"
	"github.com/charmbracelet/x/ansi"
	"image/color"
	"testing"
)

func TestDefaultPaletteChangesDoNotRecolorApplicationOverrides(t *testing.T) {
	app := ftest.NewApp()
	defer app.Quit()
	term := New()
	defer term.Close()
	dark := term.resolveColor(ansi.BasicColor(2), false)
	light := term.resolveColor(ansi.BasicColor(2), true)
	if dark == light {
		t.Fatal("light/dark defaults identical")
	}
	r, g, b, _ := dark.RGBA()
	if g < 45000 || g <= r || g <= b {
		t.Fatal("dark normal green still VGA-dark")
	}
	explicit := color.NRGBA{R: 63, G: 143, B: 191, A: 255}
	if term.resolveColor(explicit, false) != explicit || term.resolveColor(explicit, true) != explicit {
		t.Fatal("true-color changed by theme")
	}
	term.model.SetIndexedColor(2, explicit)
	if term.resolveColor(ansi.BasicColor(2), false) != explicit || term.resolveColor(ansi.BasicColor(2), true) != explicit {
		t.Fatal("OSC palette override changed by theme")
	}
}
