package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/theme"
)

// Keep a real file viewport in the scrollable column. Fixed headers and a
// queue must never subtract the list down to a negative height. The surrounding
// scroll clips the column and exposes its scrollbar when the window is short.
type filePaneLayout struct{ tools, files, footer fyne.CanvasObject }

func (l *filePaneLayout) MinSize([]fyne.CanvasObject) fyne.Size {
	return fyne.NewSize(1, l.tools.MinSize().Height+100+l.footer.MinSize().Height+2*theme.Padding())
}
func (l *filePaneLayout) Layout(_ []fyne.CanvasObject, size fyne.Size) {
	gap := theme.Padding()
	top, bottom := l.tools.MinSize().Height, l.footer.MinSize().Height
	list := max(100, size.Height-top-bottom-2*gap)
	l.tools.Move(fyne.NewPos(0, 0))
	l.tools.Resize(fyne.NewSize(max(0, size.Width), top))
	l.files.Move(fyne.NewPos(0, top+gap))
	l.files.Resize(fyne.NewSize(max(0, size.Width), list))
	l.footer.Move(fyne.NewPos(0, top+gap+list+gap))
	l.footer.Resize(fyne.NewSize(max(0, size.Width), bottom))
}
func scrollableFilePane(tools, files, footer fyne.CanvasObject) fyne.CanvasObject {
	return container.NewVScroll(container.New(&filePaneLayout{tools, files, footer}, tools, files, footer))
}
