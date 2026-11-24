package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	ftest "fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/widget"
	"testing"
)

func TestEditorThreeDirectionsAndNarrowPreference(t *testing.T) {
	app := ftest.NewApp()
	defer app.Quit()
	a := &App{UI: app}
	terminal := container.NewStack(widget.NewLabel("terminal"))
	editor := container.NewStack(widget.NewLabel("editor"))
	w := &Workspace{App: a, termBox: terminal, editorHost: editor, centerCell: container.NewStack()}
	w.editorSplit = w.makeEditorDock()
	w.editorSplit.Resize(fyne.NewSize(1000, 600))
	for _, side := range []string{"left", "right", "bottom"} {
		w.SetEditorDock(side)
		w.editorSplit.Resize(fyne.NewSize(1000, 600))
		if w.termBox != terminal || w.editorHost != editor {
			t.Fatal("dock change rebuilt content")
		}
		switch side {
		case "left":
			if editor.Position().X >= terminal.Position().X {
				t.Fatal("left dock missing")
			}
		case "right":
			if editor.Position().X <= terminal.Position().X {
				t.Fatal("right dock missing")
			}
		case "bottom":
			if editor.Position().Y <= terminal.Position().Y {
				t.Fatal("bottom dock missing")
			}
		}
	}
	w.SetEditorDock("left")
	w.editorSplit.Resize(fyne.NewSize(400, 500))
	if w.EffectiveEditorDock() != "bottom" || w.editorDock != "left" {
		t.Fatal("narrow window overwrote left preference")
	}
	w.editorSplit.Resize(fyne.NewSize(1000, 600))
	if w.EffectiveEditorDock() != "left" || editor.Position().X >= terminal.Position().X {
		t.Fatal("wide restore lost left preference")
	}
}
