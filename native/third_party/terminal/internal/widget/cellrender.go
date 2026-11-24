package widget

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/theme"
	"github.com/mattn/go-runewidth"
	"image/color"
	"math"
)

// cellRenderer paints all backgrounds before glyphs. TextGrid's interleaved
// background/glyph order clips the right half of wide glyphs on colored cells.
// The terminal owns scrolling; only the visible viewport is rendered here.
type cellRenderer struct {
	grid        *TermGrid
	objects     []fyne.CanvasObject
	backgrounds []*canvas.Rectangle
	texts       []*canvas.Text
	cell        fyne.Size
	rows, cols  int
}

func (r *cellRenderer) Layout(size fyne.Size) { r.Refresh() }
func (r *cellRenderer) MinSize() fyne.Size    { return fyne.NewSize(0, 0) }
func (r *cellRenderer) Destroy() {
	if r.grid.tickerCancel != nil {
		r.grid.tickerCancel()
	}
}
func (r *cellRenderer) Objects() []fyne.CanvasObject { return r.objects }
func (r *cellRenderer) Refresh() {
	th := r.grid.Theme()
	variant := fyne.CurrentApp().Settings().ThemeVariant()
	textSize := th.Size(theme.SizeNameText)
	cell := fyne.MeasureText("M", textSize, fyne.TextStyle{Monospace: true})
	cell.Width = float32(math.Round(float64(cell.Width)))
	cell.Height = float32(math.Round(float64(cell.Height)))
	rows := int(r.grid.Size().Height / cell.Height)
	cols := int(r.grid.Size().Width / cell.Width)
	if rows < 0 || cols < 0 {
		return
	}
	count := rows * cols
	if len(r.texts) != count {
		r.backgrounds = make([]*canvas.Rectangle, count)
		r.texts = make([]*canvas.Text, count)
		r.objects = make([]fyne.CanvasObject, count*2)
		for i := 0; i < count; i++ {
			r.backgrounds[i] = canvas.NewRectangle(color.Transparent)
			r.texts[i] = canvas.NewText("", color.Transparent)
			r.objects[i] = r.backgrounds[i]
			r.objects[count+i] = r.texts[i]
		}
	}
	r.rows, r.cols, r.cell = rows, cols, cell
	fgDefault := th.Color(theme.ColorNameForeground, variant)
	for row := 0; row < rows; row++ {
		cells := r.grid.Row(row).Cells
		for col := 0; col < cols; col++ {
			i := row*cols + col
			bg := color.Color(color.Transparent)
			fg := fgDefault
			style := fyne.TextStyle{Monospace: true}
			chr := rune(0)
			if col < len(cells) {
				c := cells[col]
				chr = c.Rune
				if c.Style != nil {
					style = c.Style.Style()
					style.Monospace = true
					if c.Style.BackgroundColor() != nil {
						bg = c.Style.BackgroundColor()
					}
					if c.Style.TextColor() != nil {
						fg = c.Style.TextColor()
					}
				}
			}
			pos := fyne.NewPos(float32(col)*cell.Width, float32(row)*cell.Height)
			rect := r.backgrounds[i]
			rect.FillColor = bg
			rect.Move(pos)
			rect.Resize(cell)
			rect.Refresh()
			text := r.texts[i]
			text.Text = ""
			if chr != 0 {
				text.Text = string(chr)
			}
			text.TextStyle = style
			text.TextSize = textSize
			text.Color = fg
			text.Move(pos)
			width := runewidth.RuneWidth(chr)
			if width < 1 {
				width = 1
			}
			text.Resize(fyne.NewSize(float32(width)*cell.Width, cell.Height))
			text.Refresh()
		}
	}
}
