package desktop

import (
	"testing"

	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	ftest "fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
)

func TestShortFileViewportKeepsListAndExpandedQueueInsideScroll(t *testing.T) {
	app := ftest.NewApp()
	defer app.Quit()
	app.Settings().SetTheme(theme.DefaultTheme())
	tools := container.NewVBox(widget.NewEntry(), widget.NewButton("upload", nil))
	files := widget.NewList(func() int { return 25 }, func() fyne.CanvasObject { return widget.NewLabel("file") }, func(int, fyne.CanvasObject) {})
	queue := widget.NewList(func() int { return 25 }, func() fyne.CanvasObject { return widget.NewProgressBar() }, func(int, fyne.CanvasObject) {})
	area := container.New(&queueViewportLayout{}, queue)
	footer := container.NewVBox(widget.NewLabel("25 completed"), widget.NewCheck("queue", nil), area, widget.NewLabel("status"))
	scroll := scrollableFilePane(tools, files, footer).(*container.Scroll)
	win := app.NewWindow("short file viewport")
	win.SetContent(scroll)
	for _, expanded := range []bool{false, true} {
		if expanded {
			area.Show()
		} else {
			area.Hide()
		}
		scroll.Resize(fyne.NewSize(196, 130))
		scroll.Refresh()
		if files.Size().Height < 100 || files.Position().Y < tools.Size().Height || footer.Position().Y < files.Position().Y+files.Size().Height {
			t.Fatal("short window compressed file list or overlapped its controls")
		}
		if scroll.Content.Size().Height <= scroll.Size().Height {
			t.Fatal("short file column has no scroll range")
		}
		if expanded && (queue.Size().Height <= 0 || queue.Position().Y < 0) {
			t.Fatal("expanded queue has invalid viewport")
		}
	}
}
