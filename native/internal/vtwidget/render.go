package vtwidget

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/theme"
	uv "github.com/charmbracelet/ultraviolet"
	"image/color"
	"strings"
)

// RenderStats contains counters only, never terminal contents. Read on UI loop.
type RenderStats struct {
	Refreshes, ChangedObjects uint64
	Objects                   int
}
type renderer struct {
	term               *Terminal
	objects            []fyne.CanvasObject
	backgrounds        []*canvas.Rectangle
	texts              []*canvas.Text
	background, cursor *canvas.Rectangle
	nr, nt             int
	row                []cellPaint
}

func (t *Terminal) CreateRenderer() fyne.WidgetRenderer {
	r := &renderer{term: t, background: canvas.NewRectangle(color.Transparent), cursor: canvas.NewRectangle(color.Transparent)}
	r.Refresh()
	return r
}
func (r *renderer) Layout(fyne.Size)             { r.Refresh() }
func (r *renderer) MinSize() fyne.Size           { return r.term.MinSize() }
func (r *renderer) Destroy()                     {}
func (r *renderer) Objects() []fyne.CanvasObject { return r.objects }
func sameColor(a, b color.Color) bool {
	if a == nil || b == nil {
		return a == nil && b == nil
	}
	ar, ag, ab, aa := a.RGBA()
	br, bg, bb, ba := b.RGBA()
	return ar == br && ag == bg && ab == bb && aa == ba
}
func (r *renderer) rect(obj *canvas.Rectangle, pos fyne.Position, size fyne.Size, c color.Color) {
	changed := !sameColor(obj.FillColor, c)
	anyChange := changed || obj.Position() != pos || obj.Size() != size
	obj.FillColor = c
	obj.Move(pos)
	obj.Resize(size)
	if changed {
		obj.Refresh()
	}
	if anyChange {
		r.term.renderStats.ChangedObjects++
	}
}
func (r *renderer) fill(pos fyne.Position, size fyne.Size, c color.Color) {
	if r.nr == len(r.backgrounds) {
		r.backgrounds = append(r.backgrounds, canvas.NewRectangle(color.Transparent))
	}
	obj := r.backgrounds[r.nr]
	r.nr++
	r.rect(obj, pos, size, c)
}
func (r *renderer) text(content string, pos fyne.Position, size fyne.Size, c color.Color, style fyne.TextStyle) {
	if r.nt == len(r.texts) {
		r.texts = append(r.texts, canvas.NewText("", color.Transparent))
	}
	obj := r.texts[r.nt]
	r.nt++
	changed := obj.Text != content || obj.TextStyle != style || obj.TextSize != r.term.textSize() || !sameColor(obj.Color, c)
	anyChange := changed || obj.Position() != pos || obj.Size() != size
	obj.Text = content
	obj.TextStyle = style
	obj.TextSize = r.term.textSize()
	obj.Color = c
	obj.Move(pos)
	obj.Resize(size)
	if changed {
		obj.Refresh()
	}
	if anyChange {
		r.term.renderStats.ChangedObjects++
	}
}

type cellPaint struct {
	content string
	width   int
	fg, bg  color.Color
	style   fyne.TextStyle
}

func (t *Terminal) paintCell(x, y int, lead *uv.Cell, fg, bg, selection color.Color, light bool) cellPaint {
	c := t.visibleCell(x, y)
	p := cellPaint{width: 1, fg: fg, bg: bg, style: fyne.TextStyle{Monospace: true}}
	if c != nil {
		style := c.Style
		if c.Width == 0 && lead != nil {
			style = lead.Style
		}
		p.content = c.Content
		p.width = max(1, c.Width)
		if style.Fg != nil {
			p.fg = t.resolveColor(style.Fg, light)
		}
		if style.Bg != nil {
			p.bg = t.resolveColor(style.Bg, light)
		}
		if style.Attrs&uv.AttrReverse != 0 {
			p.fg, p.bg = p.bg, p.fg
		}
		p.style.Bold = style.Attrs&uv.AttrBold != 0
		p.style.Italic = style.Attrs&uv.AttrItalic != 0
		p.style.Strikethrough = style.Attrs&uv.AttrStrikethrough != 0
		p.style.Underline = style.Underline != 0
		if style.Attrs&uv.AttrConceal != 0 {
			p.fg = p.bg
		}
	}
	if t.selected(x, y) {
		p.bg = selection
	}
	return p
}
func asciiCell(p cellPaint) bool {
	return p.width == 1 && len(p.content) == 1 && p.content[0] >= 32 && p.content[0] <= 126
}
func (r *renderer) Refresh() {
	t := r.term
	t.renderStats.Refreshes++
	r.nr, r.nt = 0, 0
	rows, cols := int(t.config.Rows), int(t.config.Columns)
	cell := t.cellSize()
	variant := fyne.CurrentApp().Settings().ThemeVariant()
	th := t.Theme()
	fg, bg := th.Color(theme.ColorNameForeground, variant), th.Color(theme.ColorNameInputBackground, variant)
	br, bgLevel, bb, _ := th.Color(theme.ColorNameBackground, variant).RGBA()
	light := br+bgLevel+bb > 3*32768
	if t.foregroundOverride != nil {
		fg = t.foregroundOverride
	}
	if t.backgroundOverride != nil {
		bg = t.backgroundOverride
	}
	selection := th.Color(theme.ColorNameSelection, variant)
	r.rect(r.background, fyne.NewPos(0, 0), t.Size(), bg)
	if cap(r.row) < cols {
		r.row = make([]cellPaint, cols)
	} else {
		if len(r.row) > cols {
			clear(r.row[cols:])
		}
		r.row = r.row[:cols]
	}
	row := r.row
	for y := 0; y < rows; y++ {
		var lead *uv.Cell
		for x := 0; x < cols; x++ {
			c := t.visibleCell(x, y)
			if c != nil && c.Width > 0 {
				lead = c
			}
			row[x] = t.paintCell(x, y, lead, fg, bg, selection, light)
		}
		for x := 0; x < cols; {
			end := x + 1
			for end < cols && sameColor(row[end].bg, row[x].bg) {
				end++
			}
			if !sameColor(row[x].bg, bg) {
				r.fill(fyne.NewPos(float32(x)*cell.Width, float32(y)*cell.Height), fyne.NewSize(float32(end-x)*cell.Width, cell.Height), row[x].bg)
			}
			x = end
		}
		for x := 0; x < cols; {
			p := row[x]
			end := x + 1
			if p.content == "" || (p.content == " " && !p.style.Underline && !p.style.Strikethrough) {
				x++
				continue
			}
			content := p.content
			// ASCII glyphs have the actual monospace advance used by cellSize. Wide
			// graphemes remain separate fixed-cell objects, including their continuations.
			if asciiCell(p) && !p.style.Bold && !p.style.Italic {
				for end < cols && asciiCell(row[end]) && row[end].style == p.style && sameColor(row[end].fg, p.fg) {
					end++
				}
				visibleEnd := end
				if !p.style.Underline && !p.style.Strikethrough {
					for visibleEnd > x && row[visibleEnd-1].content == " " {
						visibleEnd--
					}
				}
				content = r.asciiRun(row, x, visibleEnd)
			}
			r.text(content, fyne.NewPos(float32(x)*cell.Width, float32(y)*cell.Height), fyne.NewSize(float32(max(p.width, end-x))*cell.Width, cell.Height), p.fg, p.style)
			x = end
		}
	}
	hidden := !t.focused || t.model.CursorHidden() || t.offset > 0
	if r.cursor.Hidden != hidden {
		r.cursor.Hidden = hidden
		r.cursor.Refresh()
	}
	cursor := t.model.CursorPosition()
	r.rect(r.cursor, fyne.NewPos(float32(cursor.X)*cell.Width, float32(cursor.Y)*cell.Height), fyne.NewSize(2, cell.Height), th.Color(theme.ColorNamePrimary, variant))
	r.objects = r.objects[:0]
	r.objects = append(r.objects, r.background)
	for _, obj := range r.backgrounds[:r.nr] {
		r.objects = append(r.objects, obj)
	}
	for _, obj := range r.texts[:r.nt] {
		r.objects = append(r.objects, obj)
	}
	r.objects = append(r.objects, r.cursor)
	t.renderStats.Objects = len(r.objects)
}
func (t *Terminal) RenderStats() RenderStats { return t.renderStats }

// Reuse the previous immutable text when a run is unchanged. Changed runs have
// exactly one sized allocation rather than repeatedly growing a builder.
func (r *renderer) asciiRun(row []cellPaint, start, end int) string {
	n := end - start
	if r.nt < len(r.texts) {
		previous := r.texts[r.nt].Text
		if len(previous) == n {
			same := true
			for i := 0; i < n; i++ {
				if previous[i] != row[start+i].content[0] {
					same = false
					break
				}
			}
			if same {
				return previous
			}
		}
	}
	var builder strings.Builder
	builder.Grow(n)
	for i := start; i < end; i++ {
		builder.WriteByte(row[i].content[0])
	}
	return builder.String()
}
