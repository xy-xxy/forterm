// Package vtwidget renders a native VT emulator, including grapheme cells and
// scrollback, on a Fyne canvas. It contains no browser/DOM layer.
package vtwidget

import (
	"errors"
	"fmt"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/driver/desktop"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	uv "github.com/charmbracelet/ultraviolet"
	"github.com/charmbracelet/x/ansi"
	vt "github.com/charmbracelet/x/vt"
	"image/color"
	"io"
	"math"
	"runtime"
	"strings"
	"sync"
	"sync/atomic"
	"unicode"
)

type Config struct {
	Rows, Columns uint
	Title, PWD    string
}
type point struct{ x, y int }
type Terminal struct {
	outputRead, outputRendered, outputBatches atomic.Uint64
	renderStats                               RenderStats
	OnFocus                                   func()
	foregroundOverride, backgroundOverride    color.Color
	searchColumn, searchTotal                 int
	modifiers                                 uv.KeyMod
	searchQuery                               string
	searchRow                                 int
	widget.BaseWidget
	model      *vt.Emulator
	OnReady    func()
	OnPaste    func(string)
	OnError    func(error)
	config     Config
	listeners  []chan Config
	mu         sync.Mutex
	in         io.WriteCloser
	out        io.Reader
	queue      chan []byte
	done       chan struct{}
	closeOnce  sync.Once
	local      io.Closer
	focused    bool
	fontSize   float32
	offset     int
	start, end *point
	selecting  bool
	mouseModes map[int]bool
}

func New() *Terminal {
	t := &Terminal{model: vt.NewEmulator(80, 24), config: Config{Rows: 24, Columns: 80}, queue: make(chan []byte, 64), done: make(chan struct{}), mouseModes: map[int]bool{}}
	t.ExtendBaseWidget(t)
	t.model.SetScrollbackSize(10000)
	t.model.SetCallbacks(vt.Callbacks{ForegroundColor: func(c color.Color) { t.foregroundOverride = c }, BackgroundColor: func(c color.Color) { t.backgroundOverride = c }, Title: func(s string) { t.config.Title = s }, EnableMode: func(mode ansi.Mode) { t.mouseModes[mode.Mode()] = true }, DisableMode: func(mode ansi.Mode) { delete(t.mouseModes, mode.Mode()) }})
	go t.collectInput()
	return t
}
func (t *Terminal) connected() bool {
	t.mu.Lock()
	ready := t.in != nil
	t.mu.Unlock()
	return ready && !t.stopped()
}
func (t *Terminal) stopped() bool {
	select {
	case <-t.done:
		return true
	default:
		return false
	}
}
func (t *Terminal) fail(err error) {
	fyne.Do(func() {
		if !t.stopped() && t.OnError != nil {
			t.OnError(err)
		}
		t.Close()
	})
}
func (t *Terminal) collectInput() {
	buf := make([]byte, 32768)
	for {
		n, e := t.model.Read(buf)
		if n > 0 {
			data := append([]byte(nil), buf[:n]...)
			select {
			case t.queue <- data:
			case <-t.done:
				return
			default:
				t.fail(errors.New("终端输入队列已满，已停止输入；请检查网络"))
				return
			}
		}
		if e != nil {
			return
		}
	}
}
func (t *Terminal) RunWithConnection(in io.WriteCloser, out io.Reader) error {
	t.mu.Lock()
	if t.in != nil || t.stopped() {
		t.mu.Unlock()
		return errors.New("终端已连接或已关闭")
	}
	t.in, t.out = in, out
	t.mu.Unlock()
	defer t.Close()
	if t.OnReady != nil {
		fyne.Do(t.OnReady)
	}
	go func() {
		for {
			select {
			case <-t.done:
				return
			case data := <-t.queue:
				if t.stopped() {
					return
				}
				if _, e := in.Write(data); e != nil {
					t.fail(errors.New("终端写入失败，未自动重连"))
					return
				}
			}
		}
	}()
	return t.consumeOutput(out)
}

func (t *Terminal) Write(data []byte) (int, error) {
	t.mu.Lock()
	ready := t.in != nil
	t.mu.Unlock()
	if !ready || t.stopped() {
		return 0, io.ErrClosedPipe
	}
	if len(data) == 0 {
		return 0, nil
	}
	copy := append([]byte(nil), data...)
	select {
	case t.queue <- copy:
		return len(data), nil
	default:
		return 0, errors.New("终端输入队列已满")
	}
}
func (t *Terminal) Close() {
	t.closeOnce.Do(func() {
		close(t.done)
		if c, ok := t.model.InputPipe().(io.Closer); ok {
			_ = c.Close()
		}
		t.mu.Lock()
		in, out, local := t.in, t.out, t.local
		t.mu.Unlock()
		go func() {
			if in != nil {
				_ = in.Close()
			}
			if c, ok := out.(io.Closer); ok {
				_ = c.Close()
			}
			if local != nil {
				_ = local.Close()
			}
		}()
	})
}
func (t *Terminal) AddListener(ch chan Config) {
	t.mu.Lock()
	defer t.mu.Unlock()
	t.listeners = append(t.listeners, ch)
}
func (t *Terminal) RemoveListener(ch chan Config) {
	t.mu.Lock()
	defer t.mu.Unlock()
	for i, c := range t.listeners {
		if c == ch {
			t.listeners = append(t.listeners[:i], t.listeners[i+1:]...)
			close(c)
			return
		}
	}
}
func (t *Terminal) MinSize() fyne.Size { return fyne.NewSize(30, 25) }
func (t *Terminal) cellSize() fyne.Size {
	s := fyne.MeasureText("M", t.textSize(), fyne.TextStyle{Monospace: true})
	return fyne.NewSize(s.Width, float32(math.Round(float64(s.Height))))
}
func (t *Terminal) textSize() float32 {
	if t.fontSize > 0 {
		return t.fontSize
	}
	return t.Theme().Size(theme.SizeNameText)
}
func (t *Terminal) SetFontSize(size float32) {
	if size < 9 {
		size = 9
	}
	if size > 32 {
		size = 32
	}
	t.fontSize = size
	t.Resize(t.Size())
	t.Refresh()
}
func (t *Terminal) Resize(size fyne.Size) {
	t.BaseWidget.Resize(size)
	cell := t.cellSize()
	cols, rows := max(2, int(size.Width/cell.Width)), max(1, int(size.Height/cell.Height))
	if int(t.config.Rows) == rows && int(t.config.Columns) == cols {
		return
	}
	t.model.Resize(cols, rows)
	t.config.Columns, t.config.Rows = uint(cols), uint(rows)
	t.mu.Lock()
	for _, ch := range t.listeners {
		select {
		case ch <- t.config:
		default:
		}
	}
	local := t.local
	t.mu.Unlock()
	if resizer, ok := local.(interface{ Resize(uint16, uint16) error }); ok {
		_ = resizer.Resize(uint16(cols), uint16(rows))
	}
	t.Refresh()
}
func (t *Terminal) keyModifier(e *fyne.KeyEvent, down bool) {
	var mod uv.KeyMod
	switch e.Name {
	case desktop.KeyShiftLeft, desktop.KeyShiftRight:
		mod = uv.ModShift
	case desktop.KeyAltLeft, desktop.KeyAltRight:
		mod = uv.ModAlt
	case desktop.KeyControlLeft, desktop.KeyControlRight:
		mod = uv.ModCtrl
	}
	if down {
		t.modifiers |= mod
	} else {
		t.modifiers &^= mod
	}
}
func (t *Terminal) KeyDown(e *fyne.KeyEvent) { t.keyModifier(e, true) }
func (t *Terminal) KeyUp(e *fyne.KeyEvent)   { t.keyModifier(e, false) }
func (t *Terminal) FocusGained() {
	t.focused = true
	if t.OnFocus != nil {
		t.OnFocus()
	}
	if !t.stopped() {
		t.model.Focus()
	}
	t.Refresh()
}
func (t *Terminal) FocusLost() {
	t.focused = false
	t.modifiers = 0
	if !t.stopped() {
		t.model.Blur()
	}
	t.Refresh()
}
func (t *Terminal) TypedRune(r rune) {
	if t.connected() {
		t.offset = 0
		t.model.SendText(string(r))
	}
}

var keys = map[fyne.KeyName]rune{fyne.KeyReturn: uv.KeyEnter, fyne.KeyEnter: uv.KeyEnter, fyne.KeyTab: uv.KeyTab, fyne.KeyEscape: uv.KeyEscape, fyne.KeyBackspace: uv.KeyBackspace, fyne.KeyDelete: uv.KeyDelete, fyne.KeyUp: uv.KeyUp, fyne.KeyDown: uv.KeyDown, fyne.KeyLeft: uv.KeyLeft, fyne.KeyRight: uv.KeyRight, fyne.KeyHome: uv.KeyHome, fyne.KeyEnd: uv.KeyEnd, fyne.KeyPageUp: uv.KeyPgUp, fyne.KeyPageDown: uv.KeyPgDown, fyne.KeyInsert: uv.KeyInsert, fyne.KeyF1: uv.KeyF1, fyne.KeyF2: uv.KeyF2, fyne.KeyF3: uv.KeyF3, fyne.KeyF4: uv.KeyF4, fyne.KeyF5: uv.KeyF5, fyne.KeyF6: uv.KeyF6, fyne.KeyF7: uv.KeyF7, fyne.KeyF8: uv.KeyF8, fyne.KeyF9: uv.KeyF9, fyne.KeyF10: uv.KeyF10, fyne.KeyF11: uv.KeyF11, fyne.KeyF12: uv.KeyF12}

func (t *Terminal) TypedKey(e *fyne.KeyEvent) {
	if driver, ok := fyne.CurrentApp().Driver().(desktop.Driver); ok {
		mods := driver.CurrentKeyModifiers()
		t.modifiers = 0
		if mods&fyne.KeyModifierShift != 0 {
			t.modifiers |= uv.ModShift
		}
		if mods&fyne.KeyModifierControl != 0 {
			t.modifiers |= uv.ModCtrl
		}
		if mods&fyne.KeyModifierAlt != 0 {
			t.modifiers |= uv.ModAlt
		}
	}
	if t.connected() {
		if code, ok := keys[e.Name]; ok {
			t.offset = 0
			t.model.SendKey(uv.KeyPressEvent{Code: code, Mod: t.modifiers})
		}
	}
}
func (t *Terminal) TypedShortcut(s fyne.Shortcut) {
	if !t.connected() {
		return
	}
	clipboard := fyne.CurrentApp().Clipboard()
	switch e := s.(type) {
	case *fyne.ShortcutPaste:
		if e.Clipboard != nil {
			clipboard = e.Clipboard
		}
		if t.OnPaste != nil {
			t.OnPaste(clipboard.Content())
		} else {
			t.Paste(clipboard.Content())
		}
	case *fyne.ShortcutCopy:
		if e.Clipboard != nil {
			clipboard = e.Clipboard
		}
		if t.start == nil && runtime.GOOS != "darwin" && !e.Secondary {
			t.model.SendKey(uv.KeyPressEvent{Code: 'c', Mod: uv.ModCtrl})
		} else {
			clipboard.SetContent(t.SelectedText())
		}
	case *fyne.ShortcutCut:
		if runtime.GOOS != "darwin" && !e.Secondary {
			t.model.SendKey(uv.KeyPressEvent{Code: 'x', Mod: uv.ModCtrl})
		}
	case *fyne.ShortcutSelectAll:
		if runtime.GOOS != "darwin" {
			t.model.SendKey(uv.KeyPressEvent{Code: 'a', Mod: uv.ModCtrl})
		}
	case *desktop.CustomShortcut:
		if e.KeyName == fyne.KeyV && (e.Modifier&fyne.KeyModifierControl != 0 || e.Modifier&fyne.KeyModifierSuper != 0) {
			t.TypedShortcut(&fyne.ShortcutPaste{Clipboard: clipboard})
			return
		}
		if e.KeyName == fyne.KeyC && t.start != nil {
			clipboard.SetContent(t.SelectedText())
			return
		}
		name := strings.ToLower(string(e.KeyName))
		if len([]rune(name)) == 1 {
			mod := uv.KeyMod(0)
			if e.Modifier&fyne.KeyModifierControl != 0 {
				mod |= uv.ModCtrl
			}
			if e.Modifier&fyne.KeyModifierAlt != 0 {
				mod |= uv.ModAlt
			}
			t.model.SendKey(uv.KeyPressEvent{Code: []rune(name)[0], Mod: mod})
		}
	}
}
func (t *Terminal) Paste(text string) {
	if t.connected() {
		t.offset = 0
		t.model.Paste(text)
	}
}
func (t *Terminal) Tapped(*fyne.PointEvent) {
	if c := fyne.CurrentApp().Driver().CanvasForObject(t); c != nil {
		c.Focus(t)
	}
}
func (t *Terminal) location(p fyne.Position) point {
	c := t.cellSize()
	return point{max(0, min(int(t.config.Columns)-1, int(p.X/c.Width))), max(0, min(int(t.config.Rows)-1, int(p.Y/c.Height)))}
}
func (t *Terminal) hasMouseReporting() bool {
	for _, mode := range []int{9, 1000, 1002, 1003} {
		if t.mouseModes[mode] {
			return true
		}
	}
	return false
}
func (t *Terminal) MouseDown(e *desktop.MouseEvent) {
	t.Tapped(&e.PointEvent)
	p := t.location(e.Position)
	if t.hasMouseReporting() && e.Modifier&fyne.KeyModifierShift == 0 {
		t.model.SendMouse(uv.MouseClickEvent{X: p.x, Y: p.y, Button: uv.MouseLeft})
		return
	}
	if e.Button == desktop.MouseButtonPrimary {
		t.start = &p
		t.end = &p
		t.selecting = true
		t.Refresh()
	}
}
func (t *Terminal) MouseUp(e *desktop.MouseEvent) {
	p := t.location(e.Position)
	if t.hasMouseReporting() && e.Modifier&fyne.KeyModifierShift == 0 {
		t.model.SendMouse(uv.MouseReleaseEvent{X: p.x, Y: p.y, Button: uv.MouseLeft})
	}
	t.selecting = false
}
func (t *Terminal) MouseMoved(e *desktop.MouseEvent) {
	if t.selecting {
		p := t.location(e.Position)
		t.end = &p
		t.Refresh()
	} else if t.hasMouseReporting() {
		p := t.location(e.Position)
		t.model.SendMouse(uv.MouseMotionEvent{X: p.x, Y: p.y})
	}
}
func (t *Terminal) MouseIn(*desktop.MouseEvent) {}
func (t *Terminal) MouseOut()                   {}
func (t *Terminal) Scrolled(e *fyne.ScrollEvent) {
	if t.hasMouseReporting() && t.offset == 0 {
		p := t.location(e.Position)
		button := uv.MouseWheelUp
		if e.Scrolled.DY < 0 {
			button = uv.MouseWheelDown
		}
		t.model.SendMouse(uv.MouseWheelEvent{X: p.x, Y: p.y, Button: button})
		return
	}
	if t.model.IsAltScreen() {
		return
	}
	t.offset = max(0, min(t.model.ScrollbackLen(), t.offset+int(math.Copysign(3, float64(e.Scrolled.DY)))))
	t.start = nil
	t.end = nil
	t.Refresh()
}
func (t *Terminal) visibleCell(x, y int) *uv.Cell {
	history := t.model.ScrollbackLen()
	index := history - t.offset + y
	if index < history {
		return t.model.ScrollbackCellAt(x, index)
	}
	return t.model.CellAt(x, index-history)
}
func (t *Terminal) Text() string {
	var lines []string
	for y := 0; y < t.model.Height(); y++ {
		var line strings.Builder
		for x := 0; x < t.model.Width(); x++ {
			if c := t.model.CellAt(x, y); c != nil {
				line.WriteString(c.Content)
			}
		}
		lines = append(lines, strings.TrimRight(line.String(), " "))
	}
	return strings.TrimRight(strings.Join(lines, "\n"), "\n")
}

func (t *Terminal) selection() (point, point, bool) {
	if t.start == nil || t.end == nil {
		return point{}, point{}, false
	}
	a, b := *t.start, *t.end
	if a.y > b.y || (a.y == b.y && a.x > b.x) {
		a, b = b, a
	}
	return a, b, true
}
func (t *Terminal) selected(x, y int) bool {
	a, b, ok := t.selection()
	return ok && y >= a.y && y <= b.y && (y > a.y || x >= a.x) && (y < b.y || x <= b.x)
}
func (t *Terminal) SelectedText() string {
	a, b, ok := t.selection()
	if !ok {
		return ""
	}
	var out strings.Builder
	for y := a.y; y <= b.y; y++ {
		start, end := 0, int(t.config.Columns)-1
		if y == a.y {
			start = a.x
		}
		if y == b.y {
			end = b.x
		}
		for x := start; x <= end; x++ {
			if c := t.visibleCell(x, y); c != nil {
				out.WriteString(c.Content)
			}
		}
		if y < b.y {
			out.WriteByte('\n')
		}
	}
	return out.String()
}
func (t *Terminal) DoubleTapped(e *fyne.PointEvent) {
	p := t.location(e.Position)
	a, b := p, p
	isWord := func(x int) bool {
		c := t.visibleCell(x, p.y)
		if c == nil || c.Content == "" {
			return false
		}
		for _, r := range c.Content {
			return unicode.IsLetter(r) || unicode.IsDigit(r) || r == '_'
		}
		return false
	}
	for a.x > 0 && isWord(a.x-1) {
		a.x--
	}
	for b.x < int(t.config.Columns)-1 && isWord(b.x+1) {
		b.x++
	}
	t.start, t.end = &a, &b
	t.Refresh()
}

// Search locates a literal query in the bounded scrollback and visible screen.
func (t *Terminal) Search(query string, backwards bool) bool {
	if query == "" {
		return false
	}
	needle := strings.ToLower(query)
	history := t.model.ScrollbackLen()
	total := history + t.model.Height()
	type match struct{ row, start, end int }
	var first, last, next *match
	same := query == t.searchQuery && total == t.searchTotal
	for y := 0; y < total; y++ {
		var line strings.Builder
		type span struct{ offset, col, width int }
		var spans []span
		runes := 0
		for x := 0; x < t.model.Width(); x++ {
			var c *uv.Cell
			if y < history {
				c = t.model.ScrollbackCellAt(x, y)
			} else {
				c = t.model.CellAt(x, y-history)
			}
			if c != nil && c.Content != "" {
				spans = append(spans, span{runes, x, max(1, c.Width)})
				line.WriteString(c.Content)
				runes += len([]rune(c.Content))
			}
		}
		lower := strings.ToLower(line.String())
		offset := 0
		for offset <= len(lower) {
			index := strings.Index(lower[offset:], needle)
			if index < 0 {
				break
			}
			index += offset
			runeStart := len([]rune(lower[:index]))
			runeEnd := runeStart + len([]rune(needle)) - 1
			candidate := match{row: y}
			for _, span := range spans {
				if span.offset <= runeStart {
					candidate.start = span.col
				}
				if span.offset <= runeEnd {
					candidate.end = span.col + span.width - 1
				}
			}
			if first == nil {
				copy := candidate
				first = &copy
			}
			copy := candidate
			last = &copy
			before := candidate.row < t.searchRow || (candidate.row == t.searchRow && candidate.start < t.searchColumn)
			after := candidate.row > t.searchRow || (candidate.row == t.searchRow && candidate.start > t.searchColumn)
			if !backwards && next == nil && (!same || after) {
				copy := candidate
				next = &copy
			}
			if backwards && (!same || before) {
				copy := candidate
				next = &copy
			}
			offset = index + len(needle)
		}
	}
	if next == nil {
		if backwards {
			next = last
		} else {
			next = first
		}
	}
	if next == nil {
		return false
	}
	t.searchQuery, t.searchRow, t.searchColumn, t.searchTotal = query, next.row, next.start, total
	t.offset = max(0, history-next.row)
	row := next.row - (history - t.offset)
	a, b := point{next.start, row}, point{next.end, row}
	t.start, t.end = &a, &b
	t.Refresh()
	return true
}

func (t *Terminal) ExitCode() int { return -1 }
func (t *Terminal) String() string {
	return fmt.Sprintf("native VT %dx%d", t.config.Columns, t.config.Rows)
}

func (t *Terminal) Dragged(event *fyne.DragEvent) {
	if t.hasMouseReporting() {
		p := t.location(event.Position)
		t.model.SendMouse(uv.MouseMotionEvent{X: p.x, Y: p.y, Button: uv.MouseLeft})
		return
	}
	p := t.location(event.Position)
	if t.start == nil {
		t.start = &p
	}
	t.end = &p
	t.Refresh()
}
func (t *Terminal) DragEnd() { t.selecting = false }

func (t *Terminal) Connected() bool { return t.connected() }

// The desktop driver otherwise consumes Tab for focus traversal before TypedKey.
// Only a live terminal captures it; forms and disconnected terminals retain
// normal traversal. VT decides the Tab/Shift-Tab byte sequence.
func (t *Terminal) AcceptsTab() bool { return t.connected() }

var _ fyne.Tabbable = (*Terminal)(nil)
