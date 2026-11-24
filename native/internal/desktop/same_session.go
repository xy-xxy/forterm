package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/widget"
)

func (w *Workspace) showSessionMenu() {
	if w.Protocol == "ftp" {
		return
	}
	item := fyne.NewMenuItem("新建相同会话", func() { w.App.NewSameSession(w) })
	if w.Context.Err() != nil || (w.Protocol != "local" && (w.Conn == nil || w.Conn.Closed())) {
		item.Disabled = true
	}
	menu := fyne.NewMenu("终端会话", item)
	widget.ShowPopUpMenuAtPosition(menu, w.Window.Canvas(), fyne.NewPos(max(0, w.Window.Canvas().Size().Width-200), 80))
}
func (a *App) NewSameSession(source *Workspace) *Workspace {
	if a.diagnostic {
		dialog.ShowInformation("离线预览", "此操作需要已认证SSH或真实本地终端。", a.Window)
		return nil
	}
	if source.Context.Err() != nil {
		return nil
	}
	if source.Protocol == "local" {
		a.Local()
		return a.ActiveWorkspace()
	}
	if source.Protocol == "ftp" || source.Conn == nil || !source.Conn.Retain() {
		dialog.ShowInformation("不能创建相同会话", "原SSH连接已断开；没有重新登录或重用临时票据。请明确重新连接。", source.Window)
		return nil
	}
	w := NewWorkspace(a, source.Title+" · 新会话", "ssh")
	w.profileID = source.profileID
	a.Tabs.Append(w.Tab)
	a.Tabs.Select(w.Tab)
	w.SetConnection(source.Conn)
	w.Refresh()
	w.StartShell()
	w.Status.SetText("复用已认证SSH创建独立PTY；初始目录由新shell决定，未重放命令。")
	return w
}
func (w *Workspace) collapseNavigationAfterConnect() {
	if w.navigationHandled {
		return
	}
	w.navigationHandled = true
	a := w.App
	if w.Context.Err() != nil || a == nil || a.diagnostic || a.chromeBody == nil || w.Window != a.Window || a.Tabs.Selected() != w.Tab || a.navigationEpoch != w.navigationEpoch || !a.UI.Preferences().BoolWithFallback("autoCollapseNavigation", true) {
		return
	}
	if a.navigationVisible {
		a.navigationVisible = false
		a.autoNavigationCollapsed = true
		a.chromeBody.Refresh()
	}
}
