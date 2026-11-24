// Package uitest is linked only by explicitly named synthetic smoke binaries.
package uitest

import (
	"fmt"
	"fyne.io/fyne/v2"
	ftest "fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/widget"
	"strings"
)

func ClickDialog(window fyne.Window, approve bool) bool {
	top := window.Canvas().Overlays().Top()
	fmt.Printf("confirmation top=%T approve=%v\n", top, approve)
	if top == nil {
		return false
	}
	var found *widget.Button
	visited := map[fyne.CanvasObject]bool{}
	var walk func(fyne.CanvasObject, int)
	walk = func(obj fyne.CanvasObject, depth int) {
		if obj == nil || depth > 24 || visited[obj] || found != nil {
			return
		}
		visited[obj] = true
		if b, ok := obj.(*widget.Button); ok {
			title := strings.ToLower(b.Text)
			fmt.Printf("confirmation button=%q\n", title)
			positive := title == "confirm" || title == "确认" || title == "ok" || title == "确定" || title == "yes" || title == "是" || title == "粘贴" || title == "保存" || title == "应用"
			negative := title == "cancel" || title == "取消" || title == "no" || title == "否" || title == "不"
			if approve && positive || !approve && negative {
				found = b
			}
			return
		}
		if c, ok := obj.(*fyne.Container); ok {
			for _, child := range c.Objects {
				walk(child, depth+1)
			}
		}
		if p, ok := obj.(*widget.PopUp); ok {
			walk(p.Content, depth+1)
			return
		}
		if w, ok := obj.(fyne.Widget); ok {
			for _, child := range ftest.WidgetRenderer(w).Objects() {
				walk(child, depth+1)
			}
		}
	}
	walk(top, 0)
	if found == nil {
		return false
	}
	ftest.Tap(found)
	return true
}

func FindObject(root fyne.CanvasObject, match func(fyne.CanvasObject) bool) fyne.CanvasObject {
	visited := map[fyne.CanvasObject]bool{}
	var found fyne.CanvasObject
	var walk func(fyne.CanvasObject, int)
	walk = func(obj fyne.CanvasObject, depth int) {
		if obj == nil || depth > 24 || visited[obj] || found != nil {
			return
		}
		visited[obj] = true
		if match(obj) {
			found = obj
			return
		}
		if c, ok := obj.(*fyne.Container); ok {
			for _, child := range c.Objects {
				walk(child, depth+1)
			}
		}
		if p, ok := obj.(*widget.PopUp); ok {
			walk(p.Content, depth+1)
		}
		if w, ok := obj.(fyne.Widget); ok {
			for _, child := range ftest.WidgetRenderer(w).Objects() {
				walk(child, depth+1)
			}
		}
	}
	walk(root, 0)
	return found
}
