package vtwidget_test

import (
	"bytes"
	"encoding/json"
	terminal "forterm/native/internal/vtwidget"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/driver/desktop"
	ftest "fyne.io/fyne/v2/test"
	"image/png"
	"io"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"
)

type capture struct {
	mu sync.Mutex
	b  bytes.Buffer
}

func (c *capture) Write(p []byte) (int, error) { c.mu.Lock(); defer c.mu.Unlock(); return c.b.Write(p) }
func (c *capture) Close() error                { return nil }
func (c *capture) take() string {
	c.mu.Lock()
	defer c.mu.Unlock()
	s := c.b.String()
	c.b.Reset()
	return s
}
func wait(t *testing.T, fn func() bool) {
	t.Helper()
	until := time.Now().Add(3 * time.Second)
	for !fn() {
		if time.Now().After(until) {
			t.Fatal("terminal probe timeout")
		}
		time.Sleep(5 * time.Millisecond)
	}
}

func TestNativeVTCapabilities(t *testing.T) {
	app := ftest.NewApp()
	defer app.Quit()
	term := terminal.New()
	w := app.NewWindow("native VT probe")
	w.SetContent(term)
	w.Resize(fyne.NewSize(800, 400))
	term.Resize(fyne.NewSize(800, 400))
	out, feed := io.Pipe()
	in := &capture{}
	boundary := out
	var expectedOutput uint64
	done := make(chan struct{})
	go func() { _ = term.RunWithConnection(in, boundary); close(done) }()
	defer func() { feed.Close(); out.Close(); term.Close(); <-done }()
	wait(t, func() bool { _, e := term.Write(nil); return e == nil })
	syncText := func(s string) string {
		in.take()
		_, _ = io.WriteString(feed, s+"\x1b[6n")
		expectedOutput += uint64(len(s) + 4)
		var reply string
		wait(t, func() bool { reply += in.take(); return strings.Contains(reply, "R") })
		wait(t, func() bool { _, processed, _ := term.OutputStats(); return processed >= expectedOutput })
		return reply
	}
	result := map[string]any{"engine": "Native grapheme VT canvas", "upstream": "charmbracelet/x/vt v0.1.0 + Forterm patches", "webEngine": false}
	assertInput := func(expected string) {
		var actual string
		wait(t, func() bool { actual += in.take(); return len(actual) >= len(expected) })
		if actual != expected {
			t.Fatalf("terminal input got %q want %q", actual, expected)
		}
	}
	reply := syncText("\x1b[2J\x1b[HAB")
	if reply != "\x1b[1;3R" {
		t.Fatalf("ASCII cursor reply %q", reply)
	}
	result["asciiCursor"] = true
	reply = syncText("\x1b[2J\x1b[H中文")
	result["cjkCursorActual"] = reply
	result["cjkCursorExpected"] = "\x1b[1;5R"
	result["cjkWideCellPass"] = reply == "\x1b[1;5R"
	if !strings.Contains(term.Text(), "中文") {
		t.Fatalf("UTF8 text lost: %q, cursor %q", term.Text(), reply)
	}
	result["utf8TextPreserved"] = true
	if reply != "\x1b[1;5R" {
		t.Fatalf("CJK cursor columns wrong: %q", reply)
	}
	// A UTF-8 character may arrive across separate SSH packets.
	_, _ = io.WriteString(feed, "\x1b[2J\x1b[H\xe4")
	expectedOutput += 8
	wait(t, func() bool { _, processed, _ := term.OutputStats(); return processed >= expectedOutput })
	syncText("\xb8\xad文")
	if term.Text() != "中文" {
		t.Fatalf("split UTF8 lost: %q", term.Text())
	}
	result["splitUTF8"] = true
	syncText("\x1b[2J\x1b[H中文\x1b[1;2HX")
	if term.Text() != " X文" {
		t.Fatalf("wide half overwrite: %q", term.Text())
	}
	result["wideHalfOverwrite"] = true
	dimensions := make(chan terminal.Config, 4)
	term.AddListener(dimensions)
	term.Resize(fyne.NewSize(80, 400))
	narrow := <-dimensions
	term.RemoveListener(dimensions)
	cjkWrap := syncText("\x1b[2J\x1b[H" + strings.Repeat("1", int(narrow.Columns)-1) + "中")
	if cjkWrap != "\x1b[2;3R" {
		t.Fatalf("wide edge wrapping: %q", cjkWrap)
	}
	result["wideEdgeWrap"] = true
	term.Resize(fyne.NewSize(800, 400))

	syncText("\x1b[2J\x1b[HMAIN")
	syncText("\x1b[?1049h\x1b[HALT")
	if !strings.Contains(term.Text(), "ALT") || strings.Contains(term.Text(), "MAIN") {
		t.Fatal("alternate buffer not isolated")
	}
	syncText("\x1b[?1049l")
	if !strings.Contains(term.Text(), "MAIN") || strings.Contains(term.Text(), "ALT") {
		t.Fatal("main buffer not restored")
	}
	result["alternateScreen"] = true
	syncText("\x1b[2J\x1b[H\x1b[31mRED\x1b[0m WHITE \x1b[41m中文双列\x1b[0m")
	img := w.Canvas().Capture()
	red := 0
	for y := 0; y < img.Bounds().Dy(); y++ {
		for x := 0; x < img.Bounds().Dx(); x++ {
			r, g, b, _ := img.At(x, y).RGBA()
			if r > 30000 && float64(r) > float64(g)*1.3 && float64(r) > float64(b)*1.3 {
				red++
			}
		}
	}
	result["redPixels"] = red
	if red == 0 {
		t.Fatal("ANSI color not rendered")
	}
	in.take()
	term.TypedShortcut(&desktop.CustomShortcut{KeyName: fyne.KeyC, Modifier: fyne.KeyModifierControl})
	assertInput("\x03")
	term.TypedKey(&fyne.KeyEvent{Name: fyne.KeyUp})
	assertInput("\x1b[A")
	result["controlAndArrow"] = true
	syncText("\x1b[?2004h")
	app.Clipboard().SetContent("one\ntwo\n")
	var pending string
	term.OnPaste = func(s string) { pending = s }
	term.TypedShortcut(&fyne.ShortcutPaste{Clipboard: app.Clipboard()})
	if pending != "one\ntwo\n" || in.take() != "" {
		t.Fatal("paste bypassed explicit hook")
	}
	term.Paste(pending)
	assertInput("\x1b[200~one\ntwo\n\x1b[201~")
	result["pasteGuardAndBracketed"] = true
	syncText("\x1b[2J\x1b[HSELECT word")
	term.DoubleTapped(&fyne.PointEvent{Position: fyne.NewPos(12, 8)})
	if !strings.Contains(term.SelectedText(), "SELECT") {
		t.Fatalf("selection failed: %q", term.SelectedText())
	}
	term.TypedShortcut(&fyne.ShortcutCopy{Clipboard: app.Clipboard()})
	if !strings.Contains(app.Clipboard().Content(), "SELECT") {
		t.Fatal("copy failed")
	}
	result["selectionCopy"] = true
	changes := make(chan terminal.Config, 8)
	term.AddListener(changes)
	term.Resize(fyne.NewSize(700, 300))
	select {
	case c := <-changes:
		result["resizeRows"] = c.Rows
		result["resizeColumns"] = c.Columns
	case <-time.After(time.Second):
		t.Fatal("resize notification missing")
	}
	term.RemoveListener(changes)
	dir := filepath.Join("..", "..", "evidence")
	os.MkdirAll(dir, 0755)
	b, _ := json.MarshalIndent(result, "", "  ")
	os.WriteFile(filepath.Join(dir, "vt-widget-capabilities.json"), b, 0600)
	f, e := os.Create(filepath.Join(dir, "vt-widget-render.png"))
	if e == nil {
		png.Encode(f, img)
		f.Close()
	}
	if result["cjkWideCellPass"] == false {
		t.Log("CONFIRMED GAP: CJK cursor width is not VT-compatible; prototype is not a full replacement")
	}
}

func TestScrollbackSearchAndSplitGraphemes(t *testing.T) {
	app := ftest.NewApp()
	defer app.Quit()
	term := terminal.New()
	defer term.Close()
	win := app.NewWindow("native terminal history")
	win.SetContent(term)
	win.Resize(fyne.NewSize(400, 120))
	out, feed := io.Pipe()
	in := &capture{}
	var expectedOutput uint64
	done := make(chan struct{})
	go func() { _ = term.RunWithConnection(in, out); close(done) }()
	defer func() { feed.Close(); out.Close(); <-done }()
	wait(t, func() bool { _, e := term.Write(nil); return e == nil })
	write := func(s string) {
		io.WriteString(feed, s)
		expectedOutput += uint64(len(s))
		wait(t, func() bool { _, processed, _ := term.OutputStats(); return processed >= expectedOutput })
	}
	for _, piece := range []string{"e", "\u0301", "👩", "\u200d", "💻"} {
		write(piece)
	}
	if !strings.Contains(term.Text(), "é👩‍💻") {
		t.Fatal("native renderer model lost split graphemes")
	}
	write("\r\nNeedle one\r\nsecond NEEDLE two\r\n")
	for i := 0; i < 30; i++ {
		write("history line\r\n")
	}
	in.take()
	if !term.Search("needle", false) || term.SelectedText() != "Needle" {
		t.Fatal("first history search failed", term.SelectedText())
	}
	if !term.Search("needle", false) || term.SelectedText() != "NEEDLE" {
		t.Fatal("next history search did not advance", term.SelectedText())
	}
	if !term.Search("needle", true) || term.SelectedText() != "Needle" {
		t.Fatal("previous history search failed", term.SelectedText())
	}
	if in.take() != "" {
		t.Fatal("history search wrote to remote terminal")
	}
}

func TestInputBeforeConnectionIsNotReplayed(t *testing.T) {
	app := ftest.NewApp()
	defer app.Quit()
	term := terminal.New()
	defer term.Close()
	win := app.NewWindow("readiness guard")
	win.SetContent(term)
	win.Resize(fyne.NewSize(400, 200))
	term.TypedRune('5')
	term.TypedKey(&fyne.KeyEvent{Name: fyne.KeyReturn})
	term.Paste("never queued")
	out, feed := io.Pipe()
	in := &capture{}
	var expectedOutput uint64
	done := make(chan struct{})
	go func() { _ = term.RunWithConnection(in, out); close(done) }()
	defer func() { feed.Close(); out.Close(); <-done }()
	wait(t, func() bool { _, e := term.Write(nil); return e == nil })
	io.WriteString(feed, "\x1b[6n")
	expectedOutput = 4
	wait(t, func() bool { _, processed, _ := term.OutputStats(); return processed >= expectedOutput })
	var response string
	wait(t, func() bool { response += in.take(); return strings.Contains(response, "R") })
	if response != "\x1b[1;1R" {
		t.Fatalf("input before readiness was replayed: %q", response)
	}
}
