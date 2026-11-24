package codeedit

import (
	"fyne.io/fyne/v2"
	ftest "fyne.io/fyne/v2/test"
	"github.com/alecthomas/chroma/v2"
	"image/png"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestNativeCodeViewport(t *testing.T) {
	app := ftest.NewApp()
	defer app.Quit()
	uiJobs := make(chan func(), 16)
	editor := newEditor("sample.go", "\ufeffpackage main\r\n\r\nfunc main() {\r\n  // 中文  \r\n  println(\"hello\")\r\n}\r\n", func(f func()) { uiJobs <- f })
	defer editor.Close()
	window := app.NewWindow("native code viewport")
	window.SetContent(editor)
	window.Resize(fyne.NewSize(700, 360))
	await := func(version uint64) {
		t.Helper()
		for {
			select {
			case job := <-uiJobs:
				job()
			case got := <-editor.syntaxApplied:
				if got == version {
					return
				}
			case <-time.After(3 * time.Second):
				t.Fatal("syntax update timeout")
			}
		}
	}
	await(0)
	colored := false
	for _, style := range editor.styles {
		if style == chroma.KeywordNamespace || style == chroma.Keyword {
			colored = true
			break
		}
	}
	if !colored {
		t.Fatal("syntax highlighting missing")
	}
	editor.Document.SetPosition(3, 2, false)
	editor.TypedRune('X')
	await(editor.Document.Version)
	if editor.Document.Serialize() != "\ufeffpackage main\r\n\r\nfunc main() {\r\n  X// 中文  \r\n  println(\"hello\")\r\n}\r\n" {
		t.Fatal("native editing changed unrelated bytes")
	}
	editor.Document.Undo()
	await(editor.Document.Version)
	if editor.Document.Dirty() {
		t.Fatal("undo did not restore exact byte snapshot")
	}
	// Wait for the last syntax callback before raster capture in Fyne's inline
	// test driver. Real native windows serialize these operations on the UI loop.

	img := window.Canvas().Capture()
	dir := filepath.Join("..", "..", "evidence")
	_ = os.MkdirAll(dir, 0755)
	out, e := os.Create(filepath.Join(dir, "native-code-viewport.png"))
	if e != nil {
		t.Fatal(e)
	}
	if e = png.Encode(out, img); e != nil {
		t.Fatal(e)
	}
	out.Close()
}
