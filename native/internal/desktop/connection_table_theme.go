package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/theme"
	"image/color"
)

// Fyne's Table uses theme padding as the space between cells. Keep that gap
// to one pixel without shrinking form controls or overriding the shared palette.
type connectionTableTheme struct{}

func (connectionTableTheme) Size(name fyne.ThemeSizeName) float32 {
	if name == theme.SizeNamePadding {
		return 1
	}
	return fyne.CurrentApp().Settings().Theme().Size(name)
}
func (connectionTableTheme) Color(name fyne.ThemeColorName, variant fyne.ThemeVariant) color.Color {
	return fyne.CurrentApp().Settings().Theme().Color(name, variant)
}
func (connectionTableTheme) Font(style fyne.TextStyle) fyne.Resource {
	return fyne.CurrentApp().Settings().Theme().Font(style)
}
func (connectionTableTheme) Icon(name fyne.ThemeIconName) fyne.Resource {
	return fyne.CurrentApp().Settings().Theme().Icon(name)
}
