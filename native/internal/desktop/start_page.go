package desktop

import "fyne.io/fyne/v2"

func (a *App) startPage() fyne.CanvasObject {
	a.connections = newConnectionManager(a)
	return a.connections
}
