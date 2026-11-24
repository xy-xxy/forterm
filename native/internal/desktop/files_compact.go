package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"path"
	"sort"
	"strings"
)

func (w *Workspace) applyFileFilter() {
	query := ""
	if w.fileFilter != nil {
		query = strings.ToLower(w.fileFilter.Text)
	}
	w.entries = w.entries[:0]
	for _, e := range w.allEntries {
		if !w.showHidden && strings.HasPrefix(e.Name, ".") {
			continue
		}
		if query != "" && !strings.Contains(strings.ToLower(e.Name), query) {
			continue
		}
		w.entries = append(w.entries, e)
	}
	sort.SliceStable(w.entries, func(i, j int) bool {
		a, b := w.entries[i], w.entries[j]
		if a.Directory != b.Directory {
			return a.Directory
		}
		switch w.fileSort {
		case "size":
			return a.Size > b.Size
		case "modified":
			return a.Modified.After(b.Modified)
		}
		return strings.ToLower(a.Name) < strings.ToLower(b.Name)
	})
	w.selected = -1
	w.Files.UnselectAll()
	w.Files.Refresh()
	if w.fileTree != nil {
		w.fileTree.syncDirectory(w.directory, w.allEntries)
	}
}
func (w *Workspace) compactFileTools() fyne.CanvasObject {
	w.fileFilter = widget.NewEntry()
	w.fileFilter.PlaceHolder = "筛选文件名"
	w.fileFilter.OnChanged = func(string) { w.applyFileFilter() }
	w.fileFilter.Hide()
	filter := action("", theme.SearchIcon(), func() {
		if w.fileFilter.Visible() {
			w.fileFilter.SetText("")
			w.fileFilter.Hide()
		} else {
			w.fileFilter.Show()
			w.Window.Canvas().Focus(w.fileFilter)
		}
	})
	var sortButton *widget.Button
	sortButton = action("", theme.ListIcon(), func() {
		menu := fyne.NewMenu("排序", fyne.NewMenuItem("名称", func() { w.fileSort = "name"; w.applyFileFilter() }), fyne.NewMenuItem("最近修改", func() { w.fileSort = "modified"; w.applyFileFilter() }), fyne.NewMenuItem("大小", func() { w.fileSort = "size"; w.applyFileFilter() }), fyne.NewMenuItemSeparator(), fyne.NewMenuItem("显示 / 隐藏点文件", func() { w.showHidden = !w.showHidden; w.applyFileFilter() }))
		position := w.App.UI.Driver().AbsolutePositionForObject(sortButton).Add(fyne.NewPos(0, sortButton.Size().Height))
		widget.ShowPopUpMenuAtPosition(menu, w.Window.Canvas(), position)
	})
	up := action("", theme.MoveUpIcon(), func() { w.Path.SetText(path.Dir(w.directory)); w.Refresh() })
	pathRow := container.NewBorder(nil, nil, up, container.NewHBox(filter, sortButton), w.Path)
	var more *widget.Button
	more = action("", theme.MoreHorizontalIcon(), func() {
		menu := fyne.NewMenu("文件操作", fyne.NewMenuItem("打开 / 在线编辑", w.OpenSelectedText), fyne.NewMenuItem("按绝对目录直接读取…", w.ConfirmExactDirectory), fyne.NewMenuItem("新建文本", w.NewText), fyne.NewMenuItem("批量上传…", w.UploadBatch), fyne.NewMenuItem("粘贴文件…", w.PasteFile), fyne.NewMenuItem("复制上传阶段信息", w.CopyUploadEvidence), fyne.NewMenuItemSeparator(), fyne.NewMenuItem("重命名…", w.RenameSelected), fyne.NewMenuItem("权限…", w.PermissionsSelected), fyne.NewMenuItem("复制路径", w.CopySelectedPath), fyne.NewMenuItem("删除…", w.DeleteSelected), fyne.NewMenuItemSeparator(), fyne.NewMenuItem("打开终端", w.StartShell))
		position := w.App.UI.Driver().AbsolutePositionForObject(more).Add(fyne.NewPos(0, more.Size().Height))
		widget.ShowPopUpMenuAtPosition(menu, w.Window.Canvas(), position)
	})
	toolbar := container.NewHScroll(container.NewHBox(action("上传", theme.UploadIcon(), w.Upload), action("下载", theme.DownloadIcon(), w.Download), action("", theme.FolderNewIcon(), w.MakeDirectory), action("", theme.ViewRefreshIcon(), w.Refresh), more))
	w.operationStatus.Hide()
	return container.NewVBox(pathRow, w.fileFilter, toolbar, w.operationStatus, w.operationCancel)
}
