package termprobe

import (
	"bytes"
	"encoding/json"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/driver/desktop"
	ftest "fyne.io/fyne/v2/test"
	terminal "github.com/fyne-io/terminal"
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

// A read boundary signals that the previous chunk has finished its UI refresh.
// The Fyne test driver executes DoAndWait inline, unlike the real GUI event loop.
type boundaryReader struct {
	io.Reader
	idle chan struct{}
}

func (r boundaryReader) Read(p []byte) (int, error) { r.idle <- struct{}{}; return r.Reader.Read(p) }

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
	idle := make(chan struct{}, 1)
	boundary := boundaryReader{Reader: out, idle: idle}
	done := make(chan struct{})
	go func() { _ = term.RunWithConnection(in, boundary); close(done) }()
	defer func() { feed.Close(); out.Close(); term.Close(); <-done }()
	<-idle
	if _, e := term.Write(nil); e != nil {
		t.Fatal("legacy terminal did not initialize before first read")
	}
	syncText := func(s string) string {
		in.take()
		_, _ = io.WriteString(feed, s+"\x1b[6n")
		var reply string
		wait(t, func() bool { reply += in.take(); return strings.Contains(reply, "R") })
		select {
		case <-idle:
		case <-time.After(3 * time.Second):
			t.Fatal("parser refresh did not finish")
		}
		return reply
	}
	result := map[string]any{"engine": "Fyne Terminal native VT parser", "upstream": "45431670057e", "webEngine": false}
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
	<-idle
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
			if r > 20000 && r > g*2 && r > b*2 {
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
	if in.take() != "\x03" {
		t.Fatal("Ctrl-C not interrupt")
	}
	term.TypedKey(&fyne.KeyEvent{Name: fyne.KeyUp})
	if in.take() != "\x1b[A" {
		t.Fatal("cursor key encoding")
	}
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
	if in.take() != "\x1b[200~one\ntwo\n\x1b[201~" {
		t.Fatal("bracketed paste changed")
	}
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
	os.WriteFile(filepath.Join(dir, "terminal-capabilities.json"), b, 0600)
	f, e := os.Create(filepath.Join(dir, "terminal-native-render.png"))
	if e == nil {
		png.Encode(f, img)
		f.Close()
	}
	if result["cjkWideCellPass"] == false {
		t.Log("CONFIRMED GAP: CJK cursor width is not VT-compatible; prototype is not a full replacement")
	}
}
