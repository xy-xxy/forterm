package desktop

import (
	"context"
	"fmt"
	"forterm/native/internal/transfers"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/storage"
	ftest "fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

type missingTargetFS struct{ directoryFixture }

func (*missingTargetFS) Stat(context.Context, string) (os.FileInfo, error) {
	return nil, os.ErrNotExist
}
func TestSoftwarePickerCancelAndUnicodePercentFileUseCapturedSourceTarget(t *testing.T) {
	app := ftest.NewApp()
	defer app.Quit()
	app.Settings().SetTheme(theme.DefaultTheme())
	local := t.TempDir()
	name := "中文 空格%20.txt"
	data := []byte("synthetic picker payload\n")
	if e := os.WriteFile(filepath.Join(local, name), data, 0600); e != nil {
		t.Fatal(e)
	}
	start, e := storage.ListerForURI(storage.NewFileURI(local))
	if e != nil {
		t.Fatal(e)
	}
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	sent := make(chan transfers.Spec, 1)
	q := transfers.New(ctx, func(_ context.Context, spec transfers.Spec, _ func(int64, int64)) error {
		got, e := os.ReadFile(spec.Local)
		if e != nil {
			return e
		}
		if string(got) != string(data) {
			return fmt.Errorf("wrong selected bytes")
		}
		sent <- spec
		return nil
	}, nil)
	defer q.Close()
	window := app.NewWindow("software picker integration")
	status := widget.NewLabel("")
	window.SetContent(status)
	window.Resize(fyne.NewSize(900, 700))
	path := widget.NewEntry()
	path.SetText("/original-destination")
	source := &missingTargetFS{}
	w := &Workspace{Title: "original-source", FileConn: source, Transfers: q, Window: window, Context: ctx, Status: status, Path: path, directory: "/original-destination", directoryReady: true}
	find := func(predicate func(fyne.CanvasObject) bool) fyne.CanvasObject {
		return findPickerObject(window.Canvas().Overlays().Top(), predicate)
	}
	cancelButton := func() *widget.Button {
		obj := find(func(o fyne.CanvasObject) bool {
			b, ok := o.(*widget.Button)
			return ok && (strings.EqualFold(b.Text, "cancel") || b.Text == "取消")
		})
		if obj == nil {
			t.Fatal("picker cancel button missing")
		}
		return obj.(*widget.Button)
	}
	w.uploadUsingFyne(start, "/original-destination")
	ftest.Tap(cancelButton())
	if len(q.Snapshot()) != 0 {
		t.Fatal("cancel opened upload queue")
	}
	w.uploadUsingFyne(start, "/original-destination")
	// A directory edit while the modal is open must not retarget the chosen file.
	w.directory = "/different-directory"
	w.Path.SetText("/different-directory")
	item := find(func(o fyne.CanvasObject) bool {
		if fmt.Sprintf("%T", o) != "*dialog.fileDialogItem" {
			return false
		}
		return findPickerObject(o, func(child fyne.CanvasObject) bool {
			l, ok := child.(*widget.Label)
			return ok && (l.Text == name || l.Text == strings.TrimSuffix(name, ".txt"))
		}) != nil
	})
	if item == nil {
		t.Fatal("synthetic Unicode file was not listed in picker")
	}
	ftest.Tap(item.(fyne.Tappable))
	open := find(func(o fyne.CanvasObject) bool {
		b, ok := o.(*widget.Button)
		return ok && (strings.EqualFold(b.Text, "open") || b.Text == "打开")
	})
	if open == nil {
		t.Fatal("picker open button missing")
	}
	ftest.Tap(open.(*widget.Button))
	select {
	case spec := <-sent:
		if spec.Local != filepath.Join(local, name) || spec.Remote != "/original-destination/"+name || w.FileConn != source {
			t.Fatalf("file URI/source/destination changed: %+v", spec)
		}
	case <-time.After(3 * time.Second):
		t.Fatal("file choice did not enter and run queue")
	}
}
func findPickerObject(root fyne.CanvasObject, predicate func(fyne.CanvasObject) bool) fyne.CanvasObject {
	seen := map[fyne.CanvasObject]bool{}
	var walk func(fyne.CanvasObject, int) fyne.CanvasObject
	walk = func(obj fyne.CanvasObject, depth int) fyne.CanvasObject {
		if obj == nil || depth > 36 || seen[obj] {
			return nil
		}
		seen[obj] = true
		if predicate(obj) {
			return obj
		}
		if c, ok := obj.(*fyne.Container); ok {
			for _, child := range c.Objects {
				if found := walk(child, depth+1); found != nil {
					return found
				}
			}
		}
		if scroll, ok := obj.(*container.Scroll); ok {
			if found := walk(scroll.Content, depth+1); found != nil {
				return found
			}
		}
		if w, ok := obj.(fyne.Widget); ok {
			for _, child := range ftest.WidgetRenderer(w).Objects() {
				if found := walk(child, depth+1); found != nil {
					return found
				}
			}
		}
		return nil
	}
	return walk(root, 0)
}
