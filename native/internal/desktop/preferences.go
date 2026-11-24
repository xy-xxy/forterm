package desktop

import (
	"forterm/native/internal/settings"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/driver/desktop"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"strconv"
)

func (a *App) ShowPreferences() {
	current := a.Store.ViewSnapshot().KeepAlive
	enabled := widget.NewCheck("启用SSH协议心跳", nil)
	enabled.SetChecked(current.Enabled)
	probe := widget.NewCheck("仅对已打开的SFTP通道做只读探测", nil)
	probe.SetChecked(current.SFTPProbe)
	interval := widget.NewEntry()
	interval.SetText(strconv.Itoa(current.IntervalSeconds))
	font := widget.NewEntry()
	font.SetText(strconv.Itoa(a.UI.Preferences().IntWithFallback("terminalFontSize", 13)))
	editorFont := widget.NewEntry()
	editorFont.SetText(strconv.Itoa(a.UI.Preferences().IntWithFallback("editorFontSize", 13)))
	themeSelect := widget.NewSelect([]string{"深色", "浅色"}, nil)
	themeSelect.SetSelected("深色")
	if a.UI.Preferences().StringWithFallback("appearance", "dark") == "light" {
		themeSelect.SetSelected("浅色")
	}
	notice := widget.NewLabel("心跳设置用于之后新建的连接；已有连接保持其创建时设置。\n心跳发送协议包，不向终端输入命令或按键。")
	notice.Wrapping = fyne.TextWrapWord
	autoCollapse := widget.NewCheck("连接成功后自动收起连接分组", nil)
	autoCollapse.SetChecked(a.UI.Preferences().BoolWithFallback("autoCollapseNavigation", true))
	form := widget.NewForm(widget.NewFormItem("导航", autoCollapse), widget.NewFormItem("外观", themeSelect), widget.NewFormItem("终端字号（9–32）", font), widget.NewFormItem("编辑字号（9–32）", editorFont), widget.NewFormItem("", enabled), widget.NewFormItem("间隔（5–600秒）", interval), widget.NewFormItem("", probe))
	content := container.NewVBox(form, notice, action("重置工作区布局", theme.ViewRefreshIcon(), func() {
		for _, w := range a.sessions {
			if w.Context.Err() != nil {
				continue
			}
			w.fileWidth = 280
			w.monitorWidth = 242
			w.editorDock = "right"
			w.editorDockRight = true
			w.editorBottomRatio = .54
			w.editorRightRatio = .5
			w.bodyHost.Refresh()
		}
		a.navigationWidth = 280
		a.UI.Preferences().SetFloat("navigationWidth", 280)
		a.chromeBody.Refresh()
		a.UI.Preferences().SetFloat("filesWidth", 280)
		a.UI.Preferences().SetFloat("monitorWidth", 242)
	}))
	popup := dialog.NewCustomConfirm("偏好设置", "保存", "取消", container.NewVScroll(content), func(ok bool) {
		if !ok {
			return
		}
		seconds, e := strconv.Atoi(interval.Text)
		if e != nil || seconds < 5 || seconds > 600 {
			dialog.ShowInformation("间隔无效", "心跳间隔须为5–600秒。", a.Window)
			return
		}
		terminalSize, e := strconv.Atoi(font.Text)
		editorSize, ee := strconv.Atoi(editorFont.Text)
		if e != nil || ee != nil || terminalSize < 9 || terminalSize > 32 || editorSize < 9 || editorSize > 32 {
			dialog.ShowInformation("字号无效", "字号须为9–32。", a.Window)
			return
		}
		a.SetStatus("正在保存偏好…")
		value := settings.KeepAlive{Enabled: enabled.Checked, IntervalSeconds: seconds, SFTPProbe: probe.Checked}
		mode := "dark"
		if themeSelect.Selected == "浅色" {
			mode = "light"
		}
		go func() {
			err := a.Store.SetKeepAlive(value)
			fyne.Do(func() {
				a.mu.Lock()
				closed := a.closed
				a.mu.Unlock()
				if closed {
					return
				}
				if err != nil {
					dialog.ShowInformation("设置未保存", err.Error(), a.Window)
					return
				}
				a.UI.Preferences().SetBool("autoCollapseNavigation", autoCollapse.Checked)
				a.UI.Preferences().SetInt("terminalFontSize", terminalSize)
				a.UI.Preferences().SetInt("editorFontSize", editorSize)
				a.SetAppearance(mode)
				for _, w := range a.sessions {
					if w.Context.Err() != nil {
						continue
					}
					w.Term.SetFontSize(float32(terminalSize))
					for _, d := range w.documents {
						d.Editor.FontSize = float32(editorSize)
						d.Editor.Refresh()
					}
				}
				a.SetStatus("偏好已保存；新连接使用新的心跳设置")
			})
		}()
	}, a.Window)
	fitPopup(popup, a.Window, fyne.NewSize(610, 470))
	popup.Show()
}
func (a *App) installShortcuts() {
	modifier := fyne.KeyModifierShortcutDefault | fyne.KeyModifierShift
	add := func(key fyne.KeyName, run func()) {
		a.Window.Canvas().AddShortcut(&desktop.CustomShortcut{KeyName: key, Modifier: modifier}, func(fyne.Shortcut) {
			if a.Window.Canvas().Overlays().Top() != nil {
				return
			}
			run()
		})
	}
	add(fyne.KeyN, a.ConnectDialog)
	add(fyne.KeyL, a.Local)
	add(fyne.KeyW, func() {
		if w := a.ActiveWorkspace(); w != nil {
			a.CloseWorkspace(w)
		}
	})
	add(fyne.KeyB, func() {
		if w := a.ActiveWorkspace(); w != nil {
			w.toggleFiles()
		}
	})
	cycle := func(delta int) {
		current := a.ActiveWorkspace()
		visible := a.model.Visible()
		items := []*Workspace{}
		index := 0
		for _, meta := range visible {
			w := a.session(meta.ID)
			if w == nil || w.Window != a.Window {
				continue
			}
			if w == current {
				index = len(items)
			}
			items = append(items, w)
		}
		if len(items) > 0 {
			a.Tabs.Select(items[(index+delta+len(items))%len(items)].Tab)
		}
	}
	add(fyne.KeyName("["), func() { cycle(-1) })
	add(fyne.KeyName("]"), func() { cycle(1) })
}
func (w *Workspace) toggleFiles() {
	w.filesPaneEnabled = !w.filesPaneEnabled
	w.App.UI.Preferences().SetBool("filesVisible", w.filesPaneEnabled)
	for id, view := range w.sourceViews {
		view.filesVisible = w.filesPaneEnabled && w.workspaceVisible && id == w.groupActive()
	}
	w.bodyHost.Refresh()
	if w.filesPaneEnabled {
		view := w.ActiveFileView()
		if view.refreshPending {
			view.refreshPending = false
			view.Refresh()
		}
	}
}
