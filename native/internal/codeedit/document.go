// Package codeedit provides an editing model and native code viewport. It does
// not normalize file bytes implicitly and does not execute source code.
package codeedit

import (
	"github.com/rivo/uniseg"
	"strings"
	"time"
	"unicode"
	"unicode/utf8"
)

type Format struct {
	BOM, Mixed bool
	EOL        string
}
type state struct {
	text          []rune
	caret, anchor int
	format        Format
	raw           string
}
type Document struct {
	cacheValid    bool
	cacheVersion  uint64
	cacheRaw      string
	text          []rune
	Caret, Anchor int
	Format        Format
	raw           string
	saved         string
	undo, redo    []state
	historyBytes  int
	lines         []string
	starts        []int
	Version       uint64
	OnChange      func()
	lastInsert    time.Time
	lastCaret     int
}

func ParseFormat(raw string) Format {
	f := Format{BOM: strings.HasPrefix(raw, "\ufeff"), EOL: "\n"}
	crlf, lf, cr := 0, 0, 0
	for i := 0; i < len(raw); i++ {
		if raw[i] == '\r' {
			if i+1 < len(raw) && raw[i+1] == '\n' {
				crlf++
				i++
			} else {
				cr++
			}
		} else if raw[i] == '\n' {
			lf++
		}
	}
	kinds := 0
	for _, n := range []int{crlf, lf, cr} {
		if n > 0 {
			kinds++
		}
	}
	f.Mixed = kinds > 1 || cr > 0
	if crlf > 0 && lf == 0 && cr == 0 {
		f.EOL = "\r\n"
	}
	return f
}
func NewDocument(raw string) *Document {
	f := ParseFormat(raw)
	visible := strings.TrimPrefix(raw, "\ufeff")
	visible = strings.ReplaceAll(strings.ReplaceAll(visible, "\r\n", "\n"), "\r", "\n")
	d := &Document{text: []rune(visible), Format: f, raw: raw, saved: raw}
	d.reindex()
	return d
}
func (d *Document) Text() string { return string(d.text) }
func (d *Document) Serialize() string {
	if d.cacheValid && d.cacheVersion == d.Version {
		return d.cacheRaw
	}
	defer func() { d.cacheVersion = d.Version; d.cacheValid = true }()

	if d.Format.Mixed {
		d.cacheRaw = d.raw
		return d.raw
	}
	text := string(d.text)
	if d.Format.EOL == "\r\n" {
		text = strings.ReplaceAll(text, "\n", "\r\n")
	}
	if d.Format.BOM {
		text = "\ufeff" + text
	}
	d.cacheRaw = text
	return text
}
func (d *Document) Dirty() bool          { return d.Serialize() != d.saved }
func (d *Document) Bytes() int           { return len(d.Serialize()) }
func (d *Document) MarkSaved(raw string) { d.saved = raw; d.notify() }
func (d *Document) Lines() []string      { return d.lines }
func (d *Document) Bounds() (int, int) {
	a, b := d.Anchor, d.Caret
	if a > b {
		a, b = b, a
	}
	return a, b
}
func (d *Document) Selected() string { a, b := d.Bounds(); return string(d.text[a:b]) }
func (d *Document) snapshot() state {
	return state{text: append([]rune(nil), d.text...), caret: d.Caret, anchor: d.Anchor, format: d.Format, raw: d.raw}
}
func (d *Document) remember() {
	s := d.snapshot()
	d.undo = append(d.undo, s)
	d.historyBytes += 4 * len(s.text)
	d.redo = nil
	for len(d.undo) > 200 || (d.historyBytes > 8<<20 && len(d.undo) > 1) {
		d.historyBytes -= 4 * len(d.undo[0].text)
		d.undo = d.undo[1:]
	}
}
func (d *Document) restore(s state) {
	d.text = append([]rune(nil), s.text...)
	d.Caret, d.Anchor = s.caret, s.anchor
	d.Format, d.raw = s.format, s.raw
	d.lastInsert = time.Time{}
	d.changed()
}
func (d *Document) Undo() {
	if len(d.undo) == 0 {
		return
	}
	s := d.undo[len(d.undo)-1]
	d.undo = d.undo[:len(d.undo)-1]
	d.historyBytes -= 4 * len(s.text)
	d.redo = append(d.redo, d.snapshot())
	d.restore(s)
}
func (d *Document) Redo() {
	if len(d.redo) == 0 {
		return
	}
	s := d.redo[len(d.redo)-1]
	d.redo = d.redo[:len(d.redo)-1]
	d.undo = append(d.undo, d.snapshot())
	d.historyBytes += 4 * len(d.text)
	d.restore(s)
}
func (d *Document) notify() {
	if d.OnChange != nil {
		d.OnChange()
	}
}
func (d *Document) reindex() {
	d.lines = strings.Split(string(d.text), "\n")
	d.starts = make([]int, len(d.lines))
	offset := 0
	for i, line := range d.lines {
		d.starts[i] = offset
		offset += utf8.RuneCountInString(line) + 1
	}
}
func (d *Document) changed()            { d.Version++; d.reindex(); d.notify() }
func (d *Document) Replace(text string) { d.replace(text, false) }
func (d *Document) InsertRune(r rune)   { d.replace(string(r), r != '\n' && r != '\r') }
func (d *Document) replace(text string, group bool) {
	if d.Format.Mixed {
		return
	}
	text = strings.ReplaceAll(strings.ReplaceAll(text, "\r\n", "\n"), "\r", "\n")
	a, b := d.Bounds()
	now := time.Now()
	if !group || a != b || d.lastCaret != a || now.Sub(d.lastInsert) > 750*time.Millisecond {
		d.remember()
	}
	if a == 0 {
		text = strings.TrimPrefix(text, "\ufeff")
	}
	replacement := []rune(text)
	next := make([]rune, 0, len(d.text)-(b-a)+len(replacement))
	next = append(next, d.text[:a]...)
	next = append(next, replacement...)
	next = append(next, d.text[b:]...)
	d.text = next
	d.Caret = a + len(replacement)
	d.Anchor = d.Caret
	d.lastCaret = d.Caret
	if group {
		d.lastInsert = now
	} else {
		d.lastInsert = time.Time{}
	}
	d.changed()
}
func (d *Document) ConvertEOL(eol string) {
	if eol != "\n" && eol != "\r\n" {
		return
	}
	d.remember()
	d.Format.Mixed = false
	d.Format.EOL = eol
	d.lastInsert = time.Time{}
	d.changed()
}
func (d *Document) Position() (row, col int) {
	for i, start := range d.starts {
		if start > d.Caret {
			break
		}
		row = i
	}
	return row, d.Caret - d.starts[row]
}
func (d *Document) SetPosition(row, col int, extend bool) {
	row = max(0, min(len(d.lines)-1, row))
	col = max(0, min(utf8.RuneCountInString(d.lines[row]), col))
	position := 0
	g := uniseg.NewGraphemes(d.lines[row])
	for g.Next() {
		next := position + utf8.RuneCountInString(g.Str())
		if col > position && col < next {
			col = position
			break
		}
		position = next
	}
	d.Caret = d.starts[row] + col
	if !extend {
		d.Anchor = d.Caret
	}
	d.lastInsert = time.Time{}
	d.notify()
}
func (d *Document) MoveVertical(delta int, extend bool) {
	row, col := d.Position()
	d.SetPosition(row+delta, col, extend)
}
func (d *Document) MoveHorizontal(delta int, word, extend bool) {
	a, b := d.Bounds()
	if a != b && !extend {
		if delta < 0 {
			d.Caret = a
		} else {
			d.Caret = b
		}
		d.Anchor = d.Caret
		d.notify()
		return
	}
	pos := d.Caret
	if word {
		if delta < 0 {
			for pos > 0 && unicode.IsSpace(d.text[pos-1]) {
				pos--
			}
			for pos > 0 && !unicode.IsSpace(d.text[pos-1]) {
				pos--
			}
		} else {
			for pos < len(d.text) && !unicode.IsSpace(d.text[pos]) {
				pos++
			}
			for pos < len(d.text) && unicode.IsSpace(d.text[pos]) {
				pos++
			}
		}
	} else if delta < 0 && pos > 0 {
		row, _ := d.Position()
		start := d.starts[row]
		if pos == start {
			pos--
		} else {
			g := uniseg.NewGraphemes(string(d.text[start:pos]))
			last := 0
			for g.Next() {
				last = utf8.RuneCountInString(g.Str())
			}
			pos -= max(1, last)
		}
	} else if delta > 0 && pos < len(d.text) {
		g := uniseg.NewGraphemes(string(d.text[pos:]))
		if g.Next() {
			pos += utf8.RuneCountInString(g.Str())
		}
	}
	d.Caret = max(0, min(len(d.text), pos))
	if !extend {
		d.Anchor = d.Caret
	}
	d.lastInsert = time.Time{}
	d.notify()
}
func (d *Document) Delete(backwards bool) {
	if d.Format.Mixed {
		return
	}
	a, b := d.Bounds()
	if a == b {
		if backwards {
			d.MoveHorizontal(-1, false, true)
		} else {
			d.MoveHorizontal(1, false, true)
		}
	}
	d.Replace("")
}
func (d *Document) Newline() {
	row, col := d.Position()
	prefix := []rune(d.lines[row])
	indent := []rune{}
	for _, r := range prefix[:min(col, len(prefix))] {
		if r != ' ' && r != '\t' {
			break
		}
		indent = append(indent, r)
	}
	d.Replace("\n" + string(indent))
}
func (d *Document) SelectAll() { d.Anchor = 0; d.Caret = len(d.text); d.notify() }
