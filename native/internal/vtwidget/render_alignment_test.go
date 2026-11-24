package vtwidget

import (
	"fmt"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	ftest "fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/theme"
	"image/png"
	"math"
	"os"
	"strings"
	"testing"
)

func TestRunsUseExactAdvanceAcrossFontsColorsAndWideCells(t *testing.T) {
	for _, size := range []float32{13, 14, 16} {
		t.Run(fmt.Sprint(size), func(t *testing.T) {
			app := ftest.NewApp()
			defer app.Quit()
			app.Settings().SetTheme(theme.DefaultTheme())
			term := New()
			defer term.Close()
			term.SetFontSize(size)
			cell := term.cellSize()
			win := app.NewWindow("alignment")
			win.SetContent(term)
			win.Resize(fyne.NewSize(cell.Width*170, cell.Height*4))
			term.Resize(fyne.NewSize(cell.Width*170, cell.Height*4))
			run := strings.Repeat("A", 120)
			term.model.Write([]byte(run + "\x1b[31mBC\x1b[0m中文END"))
			term.Refresh()
			render := ftest.WidgetRenderer(term).(*renderer)
			want := map[string]float32{run: 0, "BC": 120 * cell.Width, "中": 122 * cell.Width, "文": 124 * cell.Width, "END": 126 * cell.Width}
			found := map[string]bool{}
			for _, obj := range render.Objects() {
				text, ok := obj.(*canvas.Text)
				if !ok {
					continue
				}
				if x, exists := want[text.Text]; exists {
					if math.Abs(float64(text.Position().X-x)) > .01 {
						t.Fatalf("%q at %.3f want %.3f", text.Text, text.Position().X, x)
					}
					found[text.Text] = true
					if text.Text == run {
						advance := fyne.MeasureText(text.Text, text.TextSize, text.TextStyle).Width
						if math.Abs(float64(advance-120*cell.Width)) > .02 {
							t.Fatalf("120glyph run drifts %.3f pixels", advance-120*cell.Width)
						}
					}
				}
			}
			if len(found) != len(want) {
				t.Fatalf("style/CJK run boundaries missing: found %d", len(found))
			}
			cursor := term.model.CursorPosition()
			if cursor.X != 129 {
				t.Fatal("cursor column changed", cursor)
			}
			if math.Abs(float64(render.cursor.Position().X-129*cell.Width)) > .01 {
				t.Fatal("rendered cursor is off grid")
			}
			if col := term.location(fyne.NewPos(123.4*cell.Width, cell.Height*.5)).x; col != 123 {
				t.Fatal("pointer hit grid differs from run grid", col)
			}
			image := win.Canvas().Capture()
			os.MkdirAll("../../evidence/performance-0.2.2/alignment", 0700)
			if f, e := os.Create(fmt.Sprintf("../../evidence/performance-0.2.2/alignment/font-%v.png", size)); e == nil {
				png.Encode(f, image)
				f.Close()
			}
			red := 0
			for y := 0; y < min(image.Bounds().Dy(), int(math.Ceil(float64(cell.Height)))); y++ {
				for x := int(math.Floor(float64(120 * cell.Width))); x < min(image.Bounds().Dx(), int(math.Ceil(float64(122*cell.Width)))); x++ {
					r, g, b, _ := image.At(x, y).RGBA()
					if r > 15000 && float64(r) > float64(g)*1.3 && float64(r) > float64(b)*1.3 {
						red++
					}
				}
			}
			if red == 0 {
				t.Fatal("actual raster does not show red style at expected run boundary")
			}
			if len(render.Objects()) > 20 {
				t.Fatal("ASCII output rebuilt cell-per-object grid")
			}
		})
	}
}
