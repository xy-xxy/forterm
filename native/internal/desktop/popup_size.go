package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/dialog"
)

// Scrollable dialog bodies retain fixed, reachable close/save buttons in short
// windows. This resizes the dialog, never the containing application window.
func fitPopup(p dialog.Dialog, w fyne.Window, preferred fyne.Size) {
	size := w.Canvas().Size()
	p.Resize(fyne.NewSize(min(preferred.Width, max(260, size.Width-40)), min(preferred.Height, max(180, size.Height-40))))
}
