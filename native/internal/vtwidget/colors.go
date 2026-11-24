package vtwidget

import (
	"github.com/charmbracelet/x/ansi"
	"image/color"
)

var dark16 = [16]uint32{0x18202c, 0xf38b91, 0x92d8ab, 0xe1cb8a, 0x82b5ed, 0xc7a9eb, 0x79dac3, 0xe1e8f1, 0x8193ab, 0xff9fa4, 0xb0ebbb, 0xf1db9b, 0xa0c8ff, 0xdfc1ff, 0x94f0da, 0xffffff}
var light16 = [16]uint32{0x1d2b3a, 0xb42335, 0x216e39, 0x8c5d05, 0x1d4ed8, 0x873ca0, 0x086f84, 0x536574, 0x617486, 0xd02b40, 0x24763e, 0x9a6200, 0x2458d3, 0xa43e9f, 0x147a8f, 0x243546}

func DefaultANSI(index int, light bool) color.Color {
	palette := dark16
	if light {
		palette = light16
	}
	if index < 0 || index > 15 {
		return ansi.IndexedColor(index)
	}
	v := palette[index]
	return color.NRGBA{R: uint8(v >> 16), G: uint8(v >> 8), B: uint8(v), A: 255}
}
func (t *Terminal) resolveColor(c color.Color, light bool) color.Color {
	index := -1
	switch value := c.(type) {
	case ansi.BasicColor:
		index = int(value)
	case ansi.IndexedColor:
		index = int(value)
	}
	if index >= 0 {
		resolved := t.model.IndexedColor(index)
		switch resolved.(type) {
		case ansi.BasicColor, ansi.IndexedColor:
			if index < 16 {
				return DefaultANSI(index, light)
			}
			return resolved
		default:
			return resolved
		}
	}
	return c // Application true-color values are preserved exactly.
}
