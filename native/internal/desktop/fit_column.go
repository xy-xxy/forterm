package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/theme"
)

// A vertically scrollable metric column follows its viewport width. A long
// sample/detail must not force horizontal clipping of the summary values.
type fitColumn struct{}

func (fitColumn) MinSize(objects []fyne.CanvasObject) fyne.Size {
	height := float32(0)
	count := 0
	for _, obj := range objects {
		if obj.Visible() {
			height += obj.MinSize().Height
			count++
		}
	}
	if count > 1 {
		height += float32(count-1) * theme.Padding()
	}
	return fyne.NewSize(1, height)
}
func (fitColumn) Layout(objects []fyne.CanvasObject, size fyne.Size) {
	y := float32(0)
	for _, obj := range objects {
		if !obj.Visible() {
			continue
		}
		obj.Resize(fyne.NewSize(size.Width, obj.MinSize().Height))
		height := obj.MinSize().Height
		obj.Move(fyne.NewPos(0, y))
		obj.Resize(fyne.NewSize(size.Width, height))
		y += height + theme.Padding()
	}
}
