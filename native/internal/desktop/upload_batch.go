package desktop

import (
	"context"
	"fmt"
	"forterm/native/internal/localfile"
	"forterm/native/internal/nativepicker"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/widget"
	"os"
	"path/filepath"
	"strings"
)

func (w *Workspace) UploadBatch() {
	directory, ok := w.uploadDirectory()
	if !ok || w.pickerBusy {
		return
	}
	w.pickerBusy = true
	ctx, cancel := context.WithCancel(w.Context)
	err := w.picker(ctx, nativepicker.Request{Directory: true, Title: "选择包含待上传文件的文件夹（不递归）"}, func(result nativepicker.Result) {
		defer cancel()
		w.pickerBusy = false
		if ctx.Err() != nil || !w.fileAvailable() {
			return
		}
		if result.Err != nil {
			w.uploadError(result.Err.Error())
			return
		}
		if result.Cancelled || len(result.Paths) == 0 {
			return
		}
		w.readLocalUploadFolder(result.Paths[0], directory)
	})
	if err != nil {
		cancel()
		w.pickerBusy = false
		dialog.ShowConfirm("系统文件夹选择器不可用", err.Error()+"\n改用内置文件夹选择器？", func(ok bool) {
			if !ok {
				return
			}
			dialog.ShowFolderOpen(func(folder fyne.ListableURI, e error) {
				if e != nil || folder == nil {
					return
				}
				local, e := localfile.URIPath(folder)
				if e == nil {
					w.readLocalUploadFolder(local, directory)
				}
			}, w.Window)
		}, w.Window)
	}
}
func (w *Workspace) readLocalUploadFolder(directory, remoteDirectory string) {
	w.Status.SetText("读取本地文件列表…")
	go func() {
		entries, err := os.ReadDir(directory)
		paths := make([]string, 0, len(entries))
		if err == nil {
			for _, entry := range entries {
				info, e := entry.Info()
				if e == nil && info.Mode().IsRegular() {
					paths = append(paths, filepath.Join(directory, entry.Name()))
				}
			}
		}
		fyne.Do(func() {
			if w.Context.Err() != nil {
				return
			}
			if err != nil {
				w.Status.SetText("无法读取本地目录")
				return
			}
			w.ChooseUploads(paths, remoteDirectory)
		})
	}()
}
func (w *Workspace) ChooseUploads(paths []string, remoteDirectory string) {
	if len(paths) == 0 {
		dialog.ShowInformation("没有普通文件", "请选择包含普通文件的本地文件夹。不会递归上传目录或跟随链接。", w.Window)
		return
	}
	selected := make([]bool, len(paths))
	visible := make([]int, len(paths))
	for i := range visible {
		visible[i] = i
	}
	summary := widget.NewLabel("未选择文件")
	refreshSummary := func() {
		n := 0
		for _, v := range selected {
			if v {
				n++
			}
		}
		summary.SetText(fmt.Sprintf("已选%d / %d文件 · 加入同一连接的串行队列", n, len(paths)))
	}
	list := widget.NewList(func() int { return len(visible) }, func() fyne.CanvasObject { return widget.NewCheck("file", nil) }, func(id widget.ListItemID, obj fyne.CanvasObject) {
		index := visible[id]
		check := obj.(*widget.Check)
		check.OnChanged = nil
		check.SetText(filepath.Base(paths[index]))
		check.SetChecked(selected[index])
		check.OnChanged = func(v bool) { selected[index] = v; refreshSummary() }
	})
	filter := widget.NewEntry()
	filter.PlaceHolder = "筛选文件名（选择状态保留）"
	filter.OnChanged = func(query string) {
		visible = visible[:0]
		for i, p := range paths {
			if strings.Contains(strings.ToLower(filepath.Base(p)), strings.ToLower(query)) {
				visible = append(visible, i)
			}
		}
		list.Refresh()
	}
	tools := container.NewHBox(widget.NewButton("全选当前结果", func() {
		for _, i := range visible {
			selected[i] = true
		}
		refreshSummary()
		list.Refresh()
	}), widget.NewButton("清空选择", func() {
		for i := range selected {
			selected[i] = false
		}
		refreshSummary()
		list.Refresh()
	}))
	area := container.NewGridWrap(fyne.NewSize(max(240, min(560, w.Window.Canvas().Size().Width-60)), max(100, min(280, w.Window.Canvas().Size().Height-340))), list)
	destination := widget.NewLabel("远程目录：" + remoteDirectory)
	destination.Wrapping = fyne.TextWrapWord
	content := container.NewVBox(destination, filter, tools, summary, area)
	dialog.ShowCustomConfirm("批量上传", "加入队列", "取消", content, func(ok bool) {
		if !ok {
			return
		}
		var chosen []string
		for i, v := range selected {
			if v {
				chosen = append(chosen, paths[i])
			}
		}
		w.EnqueueUploads(chosen, remoteDirectory)
	}, w.Window)
}
