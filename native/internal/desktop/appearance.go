package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/theme"
	"image/color"
	"os"
	"path/filepath"
	"runtime"
	"sync"
)

type desktopTheme struct{ light bool }

func rgb(hex uint32) color.Color {
	return color.NRGBA{R: uint8(hex >> 16), G: uint8(hex >> 8), B: uint8(hex), A: 255}
}

var darkColors = map[fyne.ThemeColorName]uint32{theme.ColorNameBackground: 0x121212, theme.ColorNameForeground: 0xd2d4d3, theme.ColorNameInputBackground: 0x151515, theme.ColorNameMenuBackground: 0x1b1b1b, theme.ColorNameButton: 0x242624, theme.ColorNameDisabledButton: 0x1b1b1b, theme.ColorNameDisabled: 0x93999f, theme.ColorNamePlaceHolder: 0x858b91, theme.ColorNamePrimary: 0x42a879, theme.ColorNameFocus: 0x244c39, theme.ColorNameHover: 0x242825, theme.ColorNameSelection: 0x203a2d, theme.ColorNameSeparator: 0x2a2c2b, theme.ColorNameInputBorder: 0x363936, theme.ColorNameScrollBar: 0x51575c, theme.ColorNameOverlayBackground: 0x1b1b1b, theme.ColorNameHeaderBackground: 0x1b1b1b}
var lightColors = map[fyne.ThemeColorName]uint32{theme.ColorNameBackground: 0xf8f9f8, theme.ColorNameForeground: 0x303630, theme.ColorNameInputBackground: 0xffffff, theme.ColorNameMenuBackground: 0xf0f2f0, theme.ColorNameButton: 0xe8ece8, theme.ColorNameDisabledButton: 0xedf1f4, theme.ColorNameDisabled: 0x65788a, theme.ColorNamePlaceHolder: 0x6e8193, theme.ColorNamePrimary: 0x28764e, theme.ColorNameFocus: 0xc6ded8, theme.ColorNameHover: 0xdce7ed, theme.ColorNameSelection: 0xc9e2df, theme.ColorNameSeparator: 0xdce1dc, theme.ColorNameInputBorder: 0xc9d0c9, theme.ColorNameScrollBar: 0xa4b3c1, theme.ColorNameOverlayBackground: 0xffffff, theme.ColorNameHeaderBackground: 0xf0f2f0}

func (t desktopTheme) Color(name fyne.ThemeColorName, _ fyne.ThemeVariant) color.Color {
	values := darkColors
	variant := theme.VariantDark
	if t.light {
		values = lightColors
		variant = theme.VariantLight
	}
	if value, ok := values[name]; ok {
		return rgb(value)
	}
	return theme.DefaultTheme().Color(name, variant)
}
func (t desktopTheme) Size(name fyne.ThemeSizeName) float32 {
	switch name {
	case theme.SizeNameText:
		return 13
	case theme.SizeNameCaptionText:
		return 11
	case theme.SizeNameHeadingText:
		return 15
	case theme.SizeNameSubHeadingText:
		return 14
	case theme.SizeNameInnerPadding:
		return 4
	case theme.SizeNamePadding:
		return 4
	case theme.SizeNameInputRadius, theme.SizeNameSelectionRadius:
		return 2
	case theme.SizeNameInputBorder, theme.SizeNameSeparatorThickness:
		return 1
	case theme.SizeNameInlineIcon:
		return 16
	case theme.SizeNameScrollBar:
		return 8
	case theme.SizeNameScrollBarSmall:
		return 4
	case theme.SizeNameSplitThickness:
		return 6
	}
	return theme.DefaultTheme().Size(name)
}

var fontOnce sync.Once
var fontRegular, fontMono fyne.Resource

func loadDesktopFonts() {
	if runtime.GOOS != "windows" {
		loadSystemUISans("")
		return
	}
	root := os.Getenv("SystemRoot")
	if root == "" {
		root = `C:\Windows`
	}
	for _, item := range []struct {
		name   string
		target *fyne.Resource
	}{{"segoeui.ttf", &fontRegular}, {"consola.ttf", &fontMono}} {
		if data, e := os.ReadFile(filepath.Join(root, "Fonts", item.name)); e == nil {
			*item.target = fyne.NewStaticResource(item.name, data)
		}
	}
	loadSystemUISans(root)
}
func (t desktopTheme) Font(style fyne.TextStyle) fyne.Resource {
	fontOnce.Do(loadDesktopFonts)
	if !style.Monospace && uiSansRegular != nil {
		if style.Bold && uiSansBold != nil {
			return uiSansBold
		}
		return uiSansRegular
	}
	if !style.Bold && !style.Italic {
		if style.Monospace && fontMono != nil {
			return fontMono
		}
		if !style.Monospace && fontRegular != nil {
			return fontRegular
		}
	}
	return theme.DefaultTheme().Font(style)
}
func (t desktopTheme) Icon(name fyne.ThemeIconName) fyne.Resource {
	return theme.DefaultTheme().Icon(name)
}
func (a *App) SetAppearance(mode string) {
	light := mode == "light"
	a.UI.Preferences().SetString("appearance", mode)
	a.UI.Settings().SetTheme(desktopTheme{light: light})
}

// Apply the selected font/theme before any controls can populate Fyne's
// measurement caches. The native driver installs its cache-reset listener only
// when runGL starts, so a late startup SetTheme cannot repair prior measurements.
func (a *App) ensureAppearance() {
	mode := a.UI.Preferences().StringWithFallback("appearance", "dark")
	current, ok := a.UI.Settings().Theme().(desktopTheme)
	if !ok || current.light != (mode == "light") {
		a.SetAppearance(mode)
	}
}
