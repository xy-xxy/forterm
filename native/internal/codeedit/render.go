package codeedit

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/theme"
	"github.com/alecthomas/chroma/v2"
	"github.com/rivo/uniseg"
	"image/color"
	"math"
	"strings"
	"unicode/utf8"
)

func (e *Editor) textSize() float32 {
	if e.FontSize > 0 {
		return e.FontSize
	}
	return e.Theme().Size(theme.SizeNameText)
}
func (e *Editor) cellSize() fyne.Size {
	s := fyne.MeasureText("M", e.textSize(), fyne.TextStyle{Monospace: true})
	return fyne.NewSize(float32(math.Round(float64(s.Width))), float32(math.Ceil(float64(s.Height+2))))
}

type editorRenderer struct {
	editor     *Editor
	objects    []fyne.CanvasObject
	texts      []*canvas.Text
	rects      []*canvas.Rectangle
	guides     []*canvas.Line
	nt, nr, nl int
}

func (e *Editor) CreateRenderer() fyne.WidgetRenderer {
	r := &editorRenderer{editor: e}
	r.Refresh()
	return r
}
func (r *editorRenderer) Layout(fyne.Size)             { r.Refresh() }
func (r *editorRenderer) MinSize() fyne.Size           { return r.editor.MinSize() }
func (r *editorRenderer) Destroy()                     {}
func (r *editorRenderer) Objects() []fyne.CanvasObject { return r.objects }
func (r *editorRenderer) rect(pos fyne.Position, size fyne.Size, c color.Color) {
	if r.nr >= len(r.rects) {
		r.rects = append(r.rects, canvas.NewRectangle(c))
	}
	obj := r.rects[r.nr]
	r.nr++
	obj.FillColor = c
	obj.Move(pos)
	obj.Resize(size)
	obj.Refresh()
	r.objects = append(r.objects, obj)
}
func (r *editorRenderer) text(text string, pos fyne.Position, c color.Color, bold bool) {
	if r.nt >= len(r.texts) {
		r.texts = append(r.texts, canvas.NewText("", c))
	}
	obj := r.texts[r.nt]
	r.nt++
	obj.Text = text
	obj.Color = c
	obj.TextSize = r.editor.textSize()
	obj.TextStyle = fyne.TextStyle{Monospace: true, Bold: bold}
	obj.Move(pos)
	obj.Refresh()
	r.objects = append(r.objects, obj)
}
func (r *editorRenderer) guide(x, y, height float32, c color.Color) {
	if r.nl >= len(r.guides) {
		r.guides = append(r.guides, canvas.NewLine(c))
	}
	line := r.guides[r.nl]
	r.nl++
	line.StrokeColor = c
	line.StrokeWidth = 1
	line.Position1 = fyne.NewPos(x, y)
	line.Position2 = fyne.NewPos(x, y+height)
	line.Refresh()
	r.objects = append(r.objects, line)
}
func syntaxColor(token chroma.TokenType, light bool, fallback color.Color) color.Color {
	if token >= 3200 && token < 3300 {
		if light {
			return color.NRGBA{R: 160, G: 92, B: 24, A: 255}
		}
		return color.NRGBA{R: 226, G: 183, B: 123, A: 255}
	}
	group := int(token) / 1000
	if light {
		switch group {
		case 1:
			return color.NRGBA{R: 136, G: 46, B: 180, A: 255}
		case 3:
			return color.NRGBA{R: 9, G: 116, B: 76, A: 255}
		case 4:
			return color.NRGBA{R: 20, G: 94, B: 163, A: 255}
		case 6:
			return color.NRGBA{R: 109, G: 120, B: 129, A: 255}
		}
	} else {
		switch group {
		case 1:
			return color.NRGBA{R: 205, G: 140, B: 235, A: 255}
		case 3:
			return color.NRGBA{R: 155, G: 208, B: 139, A: 255}
		case 4:
			return color.NRGBA{R: 126, G: 180, B: 235, A: 255}
		case 6:
			return color.NRGBA{R: 128, G: 143, B: 159, A: 255}
		}
	}
	return fallback
}
func (r *editorRenderer) Refresh() {
	e := r.editor
	d := e.Document
	th := e.Theme()
	variant := fyne.CurrentApp().Settings().ThemeVariant()
	background := th.Color(theme.ColorNameBackground, variant)
	br, bgValue, bb, _ := background.RGBA()
	light := br+bgValue+bb > 3*32768
	fg, bg, muted, selection := th.Color(theme.ColorNameForeground, variant), th.Color(theme.ColorNameInputBackground, variant), th.Color(theme.ColorNamePlaceHolder, variant), th.Color(theme.ColorNameSelection, variant)
	cell, gutter := e.cellSize(), e.gutter()
	r.nt, r.nr, r.nl = 0, 0, 0
	r.objects = r.objects[:0]
	r.rect(fyne.NewPos(0, 0), e.Size(), bg)
	r.rect(fyne.NewPos(0, 0), fyne.NewSize(gutter, e.Size().Height), th.Color(theme.ColorNameMenuBackground, variant))
	cursorRow, cursorCol := d.Position()
	a, b := d.Bounds()
	cols := max(1, int((e.Size().Width-gutter)/cell.Width))
	rows := e.visibleRows()
	for view := 0; view < rows; view++ {
		row := e.firstLine + view
		if row >= len(d.lines) {
			break
		}
		y := float32(view) * cell.Height
		if row == cursorRow {
			shade := color.Color(color.NRGBA{R: 29, G: 43, B: 55, A: 255})
			if light {
				shade = color.NRGBA{R: 234, G: 243, B: 246, A: 255}
			}
			r.rect(fyne.NewPos(0, y), fyne.NewSize(e.Size().Width, cell.Height), shade)
		}
		r.text(formatLine(row+1), fyne.NewPos(4, y), muted, false)
		line := d.lines[row]
		display, index := 0, d.starts[row]
		leading := true
		graphemes := uniseg.NewGraphemes(line)
		for graphemes.Next() {
			text := graphemes.Str()
			width := graphemes.Width()
			if text == "\t" {
				width = e.TabSize - display%e.TabSize
			}
			start := display
			display += width
			n := utf8.RuneCountInString(text)
			if start >= e.firstColumn+cols {
				break
			}
			if start >= e.firstColumn {
				x := gutter + float32(start-e.firstColumn)*cell.Width
				if index < b && index+n > a {
					r.rect(fyne.NewPos(x, y), fyne.NewSize(float32(width)*cell.Width, cell.Height), selection)
				}
				if text != " " && text != "\t" {
					leading = false
				}
				if leading && start > 0 && start%e.TabSize == 0 {
					r.guide(x, y, cell.Height, th.Color(theme.ColorNameSeparator, variant))
				}
				token := chroma.Text
				if index < len(e.styles) {
					token = e.styles[index]
				}
				if text == "\t" {
					if e.ShowWhitespace {
						r.text("→", fyne.NewPos(x, y), muted, false)
					}
				} else if text == " " {
					if e.ShowWhitespace {
						r.text("·", fyne.NewPos(x, y), muted, false)
					}
				} else {
					r.text(text, fyne.NewPos(x, y), syntaxColor(token, light, fg), false)
				}
			}
			index += n
		}
		if e.focused && row == cursorRow {
			displayCol := 0
			cluster := uniseg.NewGraphemes(string([]rune(line)[:cursorCol]))
			for cluster.Next() {
				if cluster.Str() == "\t" {
					displayCol += e.TabSize - displayCol%e.TabSize
				} else {
					displayCol += cluster.Width()
				}
			}
			if displayCol >= e.firstColumn && displayCol < e.firstColumn+cols {
				r.rect(fyne.NewPos(gutter+float32(displayCol-e.firstColumn)*cell.Width, y), fyne.NewSize(2, cell.Height), th.Color(theme.ColorNamePrimary, variant))
			}
		}
	}
	// Native gutter divider, independent from document bytes.
	r.guide(gutter-3, 0, e.Size().Height, th.Color(theme.ColorNameSeparator, variant))
}
func formatLine(n int) string {
	const digits = "0123456789"
	if n == 0 {
		return "0"
	}
	var b [20]byte
	i := len(b)
	for n > 0 {
		i--
		b[i] = digits[n%10]
		n /= 10
	}
	return strings.TrimSpace(string(b[i:]))
}
