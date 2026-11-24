// Local native keyboard diagnostic; never linked into the release application.
package main

import (
	"encoding/json"
	"fmt"
	"forterm/native/internal/codeedit"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/app"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/widget"
	"os"
	"time"
)

func main() {
	a := app.NewWithID("cn.forterm.native.input-probe021")
	w := a.NewWindow("Forterm input diagnostic · synthetic only")
	entry := widget.NewEntry()
	editor := codeedit.New("probe.txt", "")
	w.SetContent(container.NewBorder(container.NewVBox(widget.NewLabel("Standard Fyne Entry"), entry, widget.NewLabel("Custom code editor")), nil, nil, nil, editor))
	w.Resize(fyne.NewSize(600, 400))
	go func() {
		end := time.After(240 * time.Second)
		tick := time.NewTicker(200 * time.Millisecond)
		defer tick.Stop()
		for {
			select {
			case <-end:
				fyne.Do(a.Quit)
				return
			case <-tick.C:
				fyne.Do(func() {
					data, _ := json.MarshalIndent(map[string]any{"entry": entry.Text, "editor": editor.Document.Serialize(), "focus": fmt.Sprintf("%T", w.Canvas().Focused())}, "", "  ")
					os.WriteFile("/Users/xianyu/IdeaProjects/forterm/native/evidence/input-probe-0.2.1/state.json", data, 0600)
				})
			}
		}
	}()
	w.ShowAndRun()
}
