package codeedit

import (
	"fmt"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/driver/desktop"
	"fyne.io/fyne/v2/widget"
	"github.com/alecthomas/chroma/v2"
	"github.com/alecthomas/chroma/v2/lexers"
	"github.com/rivo/uniseg"
	"math"
	"strings"
	"unicode/utf8"
)

type syntaxJob struct {
	language   string
	text, path string
	version    uint64
}
type Editor struct {
	Language       string
	queuedLanguage string
	OnMessage      func(string)
	post           func(func())
	syntaxApplied  chan uint64
	hasQueued      bool
	queuedVersion  uint64
	widget.BaseWidget
	Document                *Document
	Path                    string
	TabSize                 int
	UseTabs, ShowWhitespace bool
	FontSize                float32
	OnSave                  func()
	OnDirty                 func(bool)
	ReadOnly                bool
	styles                  []chroma.TokenType
	firstLine, firstColumn  int
	focused, dragging       bool
	shift, control, alt     bool
	jobs                    chan syntaxJob
	done                    chan struct{}
	closed                  bool
}

func New(path, raw string) *Editor { return newEditor(path, raw, fyne.Do) }
func newEditor(path, raw string, post func(func())) *Editor {
	e := &Editor{post: post, Path: path, Document: NewDocument(raw), TabSize: 2, jobs: make(chan syntaxJob, 1), done: make(chan struct{}), syntaxApplied: make(chan uint64, 1)}
	e.ExtendBaseWidget(e)
	tabs, spaces := 0, 0
	for _, line := range e.Document.Lines() {
		if strings.HasPrefix(line, "\t") {
			tabs++
		} else if strings.HasPrefix(line, "  ") {
			spaces++
		}
	}
	e.UseTabs = tabs > spaces
	e.Document.OnChange = func() {
		e.ensureCaret()
		e.queueSyntax()
		e.Refresh()
		if e.OnDirty != nil {
			e.OnDirty(e.Document.Dirty())
		}
	}
	go e.syntaxWorker()
	e.queueSyntax()
	return e
}
func (e *Editor) Close() {
	if !e.closed {
		e.closed = true
		close(e.done)
	}
}
func (e *Editor) queueSyntax() {
	if e.closed {
		return
	}
	if e.hasQueued && e.queuedVersion == e.Document.Version && e.queuedLanguage == e.Language {
		return
	}
	e.hasQueued = true
	e.queuedVersion = e.Document.Version
	e.queuedLanguage = e.Language
	job := syntaxJob{language: e.Language, text: e.Document.Text(), path: e.Path, version: e.Document.Version}
	select {
	case <-e.jobs:
	default:
	}
	e.jobs <- job
}
func (e *Editor) syntaxWorker() {
	for {
		select {
		case <-e.done:
			return
		case job := <-e.jobs:
			lexer := lexers.Match(job.path)
			if job.language != "" {
				lexer = lexers.Get(job.language)
			}
			if lexer == nil {
				lexer = lexers.Fallback
			}
			iterator, err := lexer.Tokenise(nil, job.text)
			if err != nil {
				continue
			}
			styles := make([]chroma.TokenType, 0, utf8.RuneCountInString(job.text))
			for token := iterator(); token != chroma.EOF; token = iterator() {
				for range []rune(token.Value) {
					styles = append(styles, token.Type)
				}
				if len(styles) > 1<<20 {
					break
				}
			}
			e.post(func() {
				if !e.closed && e.Document.Version == job.version && e.Language == job.language {
					e.styles = styles
					e.Refresh()
					select {
					case <-e.syntaxApplied:
					default:
					}
					e.syntaxApplied <- job.version
				}
			})
		}
	}
}
func (e *Editor) Editable() bool        { return !e.ReadOnly && !e.Document.Format.Mixed }
func (e *Editor) MinSize() fyne.Size    { return fyne.NewSize(160, 100) }
func (e *Editor) Resize(size fyne.Size) { e.BaseWidget.Resize(size); e.ensureCaret() }
func (e *Editor) FocusGained()          { e.focused = true; e.Refresh() }
func (e *Editor) FocusLost() {
	e.focused = false
	e.shift, e.control, e.alt = false, false, false
	e.Refresh()
}
func (e *Editor) TypedRune(r rune) {
	if e.Editable() {
		e.Document.InsertRune(r)
	}
}
func (e *Editor) modifier(key fyne.KeyName, down bool) {
	switch key {
	case desktop.KeyShiftLeft, desktop.KeyShiftRight:
		e.shift = down
	case desktop.KeyControlLeft, desktop.KeyControlRight:
		e.control = down
	case desktop.KeyAltLeft, desktop.KeyAltRight:
		e.alt = down
	}
}
func (e *Editor) KeyDown(k *fyne.KeyEvent) { e.modifier(k.Name, true) }
func (e *Editor) KeyUp(k *fyne.KeyEvent)   { e.modifier(k.Name, false) }
func (e *Editor) TypedKey(k *fyne.KeyEvent) {
	if driver, ok := fyne.CurrentApp().Driver().(desktop.Driver); ok {
		mods := driver.CurrentKeyModifiers()
		e.shift = mods&fyne.KeyModifierShift != 0
		e.control = mods&fyne.KeyModifierControl != 0
		e.alt = mods&fyne.KeyModifierAlt != 0
	}
	d := e.Document
	switch k.Name {
	case fyne.KeyLeft:
		d.MoveHorizontal(-1, e.control || e.alt, e.shift)
	case fyne.KeyRight:
		d.MoveHorizontal(1, e.control || e.alt, e.shift)
	case fyne.KeyUp:
		d.MoveVertical(-1, e.shift)
	case fyne.KeyDown:
		d.MoveVertical(1, e.shift)
	case fyne.KeyPageUp:
		d.MoveVertical(-e.visibleRows(), e.shift)
	case fyne.KeyPageDown:
		d.MoveVertical(e.visibleRows(), e.shift)
	case fyne.KeyHome:
		row, _ := d.Position()
		d.SetPosition(row, 0, e.shift)
	case fyne.KeyEnd:
		row, _ := d.Position()
		d.SetPosition(row, utf8.RuneCountInString(d.Lines()[row]), e.shift)
	case fyne.KeyBackspace:
		if e.Editable() {
			d.Delete(true)
		}
	case fyne.KeyDelete:
		if e.Editable() {
			d.Delete(false)
		}
	case fyne.KeyEnter, fyne.KeyReturn:
		if e.Editable() {
			d.Newline()
		}
	case fyne.KeyTab:
		if e.Editable() {
			e.indent(e.shift)
		}
	}
	e.ensureCaret()
	e.Refresh()
}
func (e *Editor) TypedShortcut(shortcut fyne.Shortcut) {
	clipboard := fyne.CurrentApp().Clipboard()
	switch s := shortcut.(type) {
	case *fyne.ShortcutSelectAll:
		e.Document.SelectAll()
	case *fyne.ShortcutCopy:
		if s.Clipboard != nil {
			clipboard = s.Clipboard
		}
		clipboard.SetContent(e.Document.Selected())
	case *fyne.ShortcutCut:
		if e.Editable() {
			if s.Clipboard != nil {
				clipboard = s.Clipboard
			}
			clipboard.SetContent(e.Document.Selected())
			e.Document.Replace("")
		}
	case *fyne.ShortcutPaste:
		if e.Editable() {
			if s.Clipboard != nil {
				clipboard = s.Clipboard
			}
			text := clipboard.Content()
			if len(text) > 1<<20 {
				if e.OnMessage != nil {
					e.OnMessage("粘贴文本超过1MiB")
				}
				return
			}
			if utf8.ValidString(text) && !strings.ContainsRune(text, 0) {
				e.Document.Replace(text)
			}
		}
	case *fyne.ShortcutUndo:
		if e.Editable() {
			e.Document.Undo()
		}
	case *fyne.ShortcutRedo:
		if e.Editable() {
			e.Document.Redo()
		}
	case *desktop.CustomShortcut:
		switch s.KeyName {
		case fyne.KeyS:
			if e.OnSave != nil {
				e.OnSave()
			}
		case fyne.KeyZ:
			if e.Editable() {
				if s.Modifier&fyne.KeyModifierShift != 0 {
					e.Document.Redo()
				} else {
					e.Document.Undo()
				}
			}
		case fyne.KeyY:
			if e.Editable() {
				e.Document.Redo()
			}
		}
	}
}
func (e *Editor) indent(reverse bool) {
	d := e.Document
	a, b := d.Bounds()
	if a == b && !reverse {
		if e.UseTabs {
			d.Replace("\t")
		} else {
			_, col := d.Position()
			d.Replace(strings.Repeat(" ", e.TabSize-col%e.TabSize))
		}
		return
	}
	start := a
	for start > 0 && d.text[start-1] != '\n' {
		start--
	}
	end := b
	for end < len(d.text) && d.text[end] != '\n' {
		end++
	}
	selected := strings.Split(string(d.text[start:end]), "\n")
	indent := "\t"
	if !e.UseTabs {
		indent = strings.Repeat(" ", e.TabSize)
	}
	for i, line := range selected {
		if reverse {
			if strings.HasPrefix(line, "\t") {
				line = line[1:]
			} else {
				count := 0
				for count < e.TabSize && count < len(line) && line[count] == ' ' {
					count++
				}
				line = line[count:]
			}
			selected[i] = line
		} else {
			selected[i] = indent + line
		}
	}
	d.Anchor = start
	d.Caret = end
	d.Replace(strings.Join(selected, "\n"))
	d.Anchor = start
	d.notify()
}
func (e *Editor) Tapped(event *fyne.PointEvent) {
	if c := fyne.CurrentApp().Driver().CanvasForObject(e); c != nil {
		c.Focus(e)
	}
	e.clickPosition(event.Position, e.shift)
}
func (e *Editor) MouseDown(event *desktop.MouseEvent) {
	if event.Button != desktop.MouseButtonPrimary {
		return
	}
	e.Tapped(&event.PointEvent)
	e.dragging = true
}
func (e *Editor) MouseUp(*desktop.MouseEvent) { e.dragging = false }
func (e *Editor) MouseMoved(event *desktop.MouseEvent) {
	if e.dragging {
		e.clickPosition(event.Position, true)
	}
}
func (e *Editor) MouseIn(*desktop.MouseEvent) {}
func (e *Editor) MouseOut()                   {}
func (e *Editor) Scrolled(event *fyne.ScrollEvent) {
	if event.Scrolled.DY != 0 {
		e.firstLine = max(0, min(max(0, len(e.Document.Lines())-e.visibleRows()), e.firstLine-int(math.Copysign(3, float64(event.Scrolled.DY)))))
	}
	if event.Scrolled.DX != 0 {
		e.firstColumn = max(0, e.firstColumn-int(event.Scrolled.DX))
	}
	e.Refresh()
}
func (e *Editor) clickPosition(pos fyne.Position, extend bool) {
	cell := e.cellSize()
	row := max(0, min(len(e.Document.Lines())-1, e.firstLine+int(pos.Y/cell.Height)))
	target := max(0, e.firstColumn+int((pos.X-e.gutter())/cell.Width))
	col, display := 0, 0
	g := uniseg.NewGraphemes(e.Document.Lines()[row])
	for g.Next() {
		width := g.Width()
		if g.Str() == "\t" {
			width = e.TabSize - display%e.TabSize
		}
		if display+width > target {
			break
		}
		col += utf8.RuneCountInString(g.Str())
		display += width
	}
	e.Document.SetPosition(row, col, extend)
}
func (e *Editor) visibleRows() int { return max(1, int(e.Size().Height/e.cellSize().Height)) }
func (e *Editor) gutter() float32 {
	return float32(len(fmt.Sprint(len(e.Document.Lines())))+3) * e.cellSize().Width
}
func (e *Editor) ensureCaret() {
	row, col := e.Document.Position()
	height := e.visibleRows()
	if row < e.firstLine {
		e.firstLine = row
	}
	if row >= e.firstLine+height {
		e.firstLine = row - height + 1
	}
	display := 0
	g := uniseg.NewGraphemes(string([]rune(e.Document.Lines()[row])[:col]))
	for g.Next() {
		if g.Str() == "\t" {
			display += e.TabSize - display%e.TabSize
		} else {
			display += g.Width()
		}
	}
	width := max(2, int((e.Size().Width-e.gutter())/e.cellSize().Width))
	if display < e.firstColumn {
		e.firstColumn = display
	}
	if display >= e.firstColumn+width {
		e.firstColumn = display - width + 1
	}
}

func (e *Editor) Dragged(event *fyne.DragEvent) {
	if event.Position.Y < 0 {
		e.firstLine = max(0, e.firstLine-1)
	} else if event.Position.Y > e.Size().Height {
		e.firstLine = min(max(0, len(e.Document.lines)-e.visibleRows()), e.firstLine+1)
	}
	e.clickPosition(event.Position, true)
}
func (e *Editor) DragEnd() { e.dragging = false }

func (e *Editor) SetLanguage(language string) { e.Language = language; e.queueSyntax(); e.Refresh() }

func (e *Editor) ViewState() map[string]int {
	return map[string]int{"firstLine": e.firstLine, "firstColumn": e.firstColumn, "caret": e.Document.Caret, "anchor": e.Document.Anchor}
}

// Editable code receives Tab through the desktop dispatcher for its existing
// indent/unindent logic; ordinary entries and read-only previews still traverse.
func (e *Editor) AcceptsTab() bool { return e.Editable() }

var _ fyne.Tabbable = (*Editor)(nil)
