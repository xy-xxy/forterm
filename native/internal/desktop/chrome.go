package desktop

import (
	"fmt"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/layout"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"strings"
)

func action(label string, icon fyne.Resource, tap func()) *widget.Button {
	b := widget.NewButtonWithIcon(label, icon, tap)
	b.Importance = widget.LowImportance
	return b
}

type surfaceWidget struct {
	widget.BaseWidget
	Content fyne.CanvasObject
	Color   fyne.ThemeColorName
	Panel   bool
}

func surface(content fyne.CanvasObject, name fyne.ThemeColorName) *surfaceWidget {
	s := &surfaceWidget{Content: content, Color: name}
	s.ExtendBaseWidget(s)
	return s
}
func paneSurface(content fyne.CanvasObject, name fyne.ThemeColorName) *surfaceWidget {
	s := surface(content, name)
	s.Panel = true
	return s
}
func (s *surfaceWidget) CreateRenderer() fyne.WidgetRenderer {
	return &surfaceRenderer{s: s, bg: canvas.NewRectangle(theme.Color(s.Color))}
}

type surfaceRenderer struct {
	s  *surfaceWidget
	bg *canvas.Rectangle
}

func (r *surfaceRenderer) Layout(size fyne.Size) {
	r.bg.Resize(size)
	r.s.Content.Move(fyne.NewPos(0, 0))
	r.s.Content.Resize(size)
}
func (r *surfaceRenderer) MinSize() fyne.Size { return r.s.Content.MinSize() }
func (r *surfaceRenderer) Refresh() {
	r.bg.FillColor = r.s.Theme().Color(r.s.Color, fyne.CurrentApp().Settings().ThemeVariant())
	if r.s.Panel {
		r.bg.CornerRadius = 0
		r.bg.StrokeWidth = 1
		r.bg.StrokeColor = r.s.Theme().Color(theme.ColorNameSeparator, fyne.CurrentApp().Settings().ThemeVariant())
	}
	r.bg.Refresh()
	r.s.Content.Refresh()
}
func (r *surfaceRenderer) Objects() []fyne.CanvasObject {
	return []fyne.CanvasObject{r.bg, r.s.Content}
}
func (r *surfaceRenderer) Destroy() {}
func (a *App) initChrome() {
	a.ensureAppearance()
	brand := widget.NewLabelWithStyle("Forterm", fyne.TextAlignLeading, fyne.TextStyle{Bold: true})
	brand.Importance = widget.HighImportance
	nav := action("", namedActionIcon("展开或收起连接导航", theme.MenuIcon()), a.ToggleNavigation)
	a.navButton = nav
	about := action("", namedActionIcon("关于 Forterm", theme.InfoIcon()), func() {
		dialog.ShowInformation("关于 Forterm", fmt.Sprintf("Forterm Native %s\n原生桌面工作区\n终端 · 文件 · 代码 · 监控\nWindows体验改进中，验证范围见发行说明。", Version), a.Window)
	})
	appearance := action("", namedActionIcon("切换深浅主题", theme.ColorPaletteIcon()), func() {
		if a.UI.Preferences().StringWithFallback("appearance", "dark") == "dark" {
			a.SetAppearance("light")
		} else {
			a.SetAppearance("dark")
		}
	})
	top := container.NewHBox(nav, brand, widget.NewSeparator(), action("连接管理", theme.FolderOpenIcon(), a.ShowLibrary), action("堡垒机", theme.StorageIcon(), a.ShowAccounts), action("快速连接", theme.ContentAddIcon(), a.ConnectDialog), action("本地终端", theme.ComputerIcon(), a.Local), layout.NewSpacer(), appearance, action("", namedActionIcon("偏好设置", theme.SettingsIcon()), a.ShowPreferences), about)
	search := widget.NewEntry()
	search.PlaceHolder = "筛选分组、连接或会话"
	a.navigationList = widget.NewList(func() int { return len(a.navigationSessions()) }, func() fyne.CanvasObject { return widget.NewLabel("session") }, func(id widget.ListItemID, obj fyne.CanvasObject) {
		sessions := a.navigationSessions()
		if id >= len(sessions) {
			return
		}
		s := sessions[id]
		label := obj.(*widget.Label)
		label.SetText(s.Title)
		label.Truncation = fyne.TextTruncateEllipsis
	})
	a.navigationList.OnSelected = func(id widget.ListItemID) {
		sessions := a.navigationSessions()
		if id >= len(sessions) {
			return
		}
		target := sessions[id]
		owner := a.model.OwnerFor(target.ID)
		if w := a.session(owner); w != nil {
			if target.ID != owner {
				w.SelectFileSource(target.ID)
			}
			if w.Window == a.Window {
				a.Tabs.Select(w.Tab)
			} else {
				w.Window.RequestFocus()
			}
		}
	}
	search.OnChanged = func(value string) { a.navigationQuery = value; a.navigationList.Refresh(); a.refreshProfileTree() }
	saved := a.profileSection()
	runtime := container.NewBorder(widget.NewLabelWithStyle("当前会话", fyne.TextAlignLeading, fyne.TextStyle{Bold: true}), nil, nil, nil, a.navigationList)
	sections := container.NewVSplit(saved, runtime)
	sections.Offset = .72
	sidebar := container.NewBorder(container.NewVBox(container.NewHBox(widget.NewLabelWithStyle("连接分组", fyne.TextAlignLeading, fyne.TextStyle{Bold: true}), layout.NewSpacer(), action("", theme.NavigateBackIcon(), a.ToggleNavigation)), search), nil, nil, nil, sections)

	a.navigation = surface(container.NewPadded(sidebar), theme.ColorNameMenuBackground)
	a.navigationVisible = a.UI.Preferences().BoolWithFallback("navigationVisible", true)
	a.navigationWidth = float32(a.UI.Preferences().FloatWithFallback("navigationWidth", 280))
	a.navDivider = newPaneDivider(func(delta float32) {
		a.navigationEpoch++
		a.navigationWidth = max(160, min(420, a.navigationWidth+delta))
		a.chromeBody.Refresh()
	}, func() { a.navigationWidth = 280; a.chromeBody.Refresh() }, func() { a.UI.Preferences().SetFloat("navigationWidth", float64(a.navigationWidth)) })
	a.navDivider.minimum = func() { a.navigationWidth = 160; a.chromeBody.Refresh() }
	a.navDivider.maximum = func() { a.navigationWidth = 420; a.chromeBody.Refresh() }
	a.chromeBody = container.New(&chromeBodyLayout{app: a}, a.navigation, a.navDivider, a.Tabs)
	footer := widget.NewLabel("就绪")
	footer.Importance = widget.LowImportance
	a.footer = footer
	topViewport := container.New(&fixedToolbarLayout{}, container.NewHScroll(top))
	a.Window.SetContent(container.NewBorder(surface(topViewport, theme.ColorNameMenuBackground), surface(container.NewBorder(nil, nil, footer, nil, widget.NewLabel("SSH  ·  SFTP  ·  FTP")), theme.ColorNameMenuBackground), nil, nil, a.chromeBody))
}
func (a *App) navigationSessions() []*Workspace {
	out := []*Workspace{}
	for _, w := range a.sessions {
		meta, ok := a.model.Get(w.ID)
		if ok && meta.Open && (a.navigationQuery == "" || containsFold(w.Title, a.navigationQuery)) {
			out = append(out, w)
		}
	}
	return out
}
func (a *App) ToggleNavigation() {
	a.navigationEpoch++
	a.autoNavigationCollapsed = false
	a.navigationVisible = !a.navigationVisible
	a.UI.Preferences().SetBool("navigationVisible", a.navigationVisible)
	a.chromeBody.Refresh()
	if !a.navigationVisible {
		a.Window.Canvas().Focus(a.navButton)
	}
}

type chromeBodyLayout struct{ app *App }

func (l *chromeBodyLayout) MinSize([]fyne.CanvasObject) fyne.Size { return fyne.NewSize(540, 320) }
func (l *chromeBodyLayout) Layout(_ []fyne.CanvasObject, size fyne.Size) {
	width := float32(0)
	if l.app.navigationVisible {
		width = max(140, min(min(420, l.app.navigationWidth), size.Width-360))
	}
	if width > 0 {
		l.app.navigation.Show()
		l.app.navigation.Move(fyne.NewPos(0, 0))
		l.app.navigation.Resize(fyne.NewSize(width, size.Height))
	} else {
		l.app.navigation.Hide()
	}
	gap := float32(0)
	if width > 0 {
		gap = 4
		l.app.navDivider.Show()
		l.app.navDivider.Move(fyne.NewPos(width, 0))
		l.app.navDivider.Resize(fyne.NewSize(gap, size.Height))
	} else {
		l.app.navDivider.Hide()
	}
	l.app.Tabs.Move(fyne.NewPos(width+gap, 0))
	l.app.Tabs.Resize(fyne.NewSize(size.Width-width-gap, size.Height))
}

func containsFold(text, query string) bool {
	return strings.Contains(strings.ToLower(text), strings.ToLower(query))
}

func (a *App) SetStatus(text string) {
	if a.footer != nil {
		a.footer.SetText(text)
	}
}

func (a *App) NavigationSnapshot() map[string]any {
	out := map[string]any{"navigationWidth": a.navigationWidth, "navigationVisible": a.navigationVisible}
	if a.navDivider != nil {
		p := a.UI.Driver().AbsolutePositionForObject(a.navDivider)
		out["navDividerX"] = p.X
		out["navDividerY"] = p.Y
		out["navDividerWidth"] = a.navDivider.Size().Width
		out["navDividerHeight"] = a.navDivider.Size().Height
	}
	return out
}

// The top navigation can scroll horizontally at high DPI without raising the
// window minimum to the sum of every action's width.
type fixedToolbarLayout struct{}

func (fixedToolbarLayout) MinSize([]fyne.CanvasObject) fyne.Size { return fyne.NewSize(1, 38) }
func (fixedToolbarLayout) Layout(objects []fyne.CanvasObject, s fyne.Size) {
	for _, o := range objects {
		o.Move(fyne.NewPos(4, 2))
		o.Resize(fyne.NewSize(max(0, s.Width-8), max(0, s.Height-4)))
	}
}

func namedActionIcon(name string, resource fyne.Resource) fyne.Resource {
	return theme.NewThemedResource(fyne.NewStaticResource(name, resource.Content()))
}
