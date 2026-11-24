package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
)

type fileRow struct {
	icon   *widget.Icon
	detail *widget.Label
	widget.BaseWidget
	label     *widget.Label
	workspace *Workspace
	index     int
}

func newFileRow(w *Workspace) *fileRow {
	r := &fileRow{workspace: w, label: widget.NewLabel(""), icon: widget.NewIcon(theme.DocumentIcon()), detail: widget.NewLabel("")}
	r.label.Truncation = fyne.TextTruncateEllipsis
	r.detail.Importance = widget.LowImportance
	r.ExtendBaseWidget(r)
	return r
}
func (r *fileRow) CreateRenderer() fyne.WidgetRenderer {
	return widget.NewSimpleRenderer(container.NewPadded(container.NewBorder(nil, nil, r.icon, r.detail, r.label)))
}
func (r *fileRow) Tapped(*fyne.PointEvent) { r.workspace.Files.Select(r.index) }
func (r *fileRow) DoubleTapped(*fyne.PointEvent) {
	r.workspace.Files.Select(r.index)
	r.workspace.OpenSelectedText()
}
func (r *fileRow) TappedSecondary(event *fyne.PointEvent) {
	w := r.workspace
	w.Files.Select(r.index)
	menu := fyne.NewMenu("文件", fyne.NewMenuItem("打开/在线编辑", w.OpenSelectedText), fyne.NewMenuItem("下载", w.Download), fyne.NewMenuItem("复制路径", w.CopySelectedPath), fyne.NewMenuItemSeparator(), fyne.NewMenuItem("重命名", w.RenameSelected), fyne.NewMenuItem("权限", w.PermissionsSelected), fyne.NewMenuItem("删除", w.DeleteSelected))
	position := w.App.UI.Driver().AbsolutePositionForObject(r).Add(event.Position)
	widget.ShowPopUpMenuAtPosition(menu, w.Window.Canvas(), position)
}
