package desktop

import (
	"fmt"
	"net"
	"sort"
	"strconv"
	"strings"

	"forterm/native/internal/settings"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/driver/desktop"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
)

// Only committed non-secret metadata is cached here. Layout and visible cell
// updates never read credentials, rebuild a full profile snapshot, or probe hosts.
type connectionManager struct {
	widget.BaseWidget
	app                                         *App
	profiles, rows                              []settings.Profile
	group, protocol, query, selectedID, hoverID string
	online                                      map[string]bool
	tableHost                                   *container.ThemeOverride
	table                                       *widget.Table
	search                                      *widget.Entry
	protocols                                   *fyne.Container
	protocolButtons                             map[string]*widget.Button
	more                                        *widget.Button
	tools                                       *container.Scroll
	summary, empty                              *widget.Label
	lastWidth                                   float32
	createdCells                                int
}

func newConnectionManager(a *App) *connectionManager {
	a.ensureAppearance()
	m := &connectionManager{app: a, online: map[string]bool{}, protocolButtons: map[string]*widget.Button{}}
	m.ExtendBaseWidget(m)
	m.search = widget.NewEntry()
	m.search.PlaceHolder = "搜索名称、地址、用户或分组"
	m.search.OnChanged = func(q string) { m.query = q; m.filter() }
	m.protocols = container.NewHBox()
	for _, p := range []string{"", "ssh", "sftp", "ftp"} {
		protocol := p
		title := strings.ToUpper(p)
		if p == "" {
			title = "全部连接"
		}
		b := action(title, nil, func() { m.protocol = protocol; m.filter() })
		m.protocolButtons[p] = b
		m.protocols.Add(b)
	}
	create := widget.NewButtonWithIcon("新建连接", theme.ContentAddIcon(), func() { a.ProfileDialog(nil, m.changed) })
	create.Importance = widget.HighImportance
	m.tools = container.NewHScroll(container.NewHBox(create, action("导入", theme.DownloadIcon(), func() { a.importProfiles(m.changed) }), action("导出", theme.UploadIcon(), a.exportProfiles), action("刷新", theme.ViewRefreshIcon(), m.reload)))
	m.more = action("更多", theme.MoreHorizontalIcon(), m.showToolsMenu)
	m.summary = widget.NewLabel("")
	m.summary.Importance = widget.LowImportance
	m.empty = widget.NewLabel("暂无连接\n新建连接或导入已有配置")
	m.empty.Alignment = fyne.TextAlignCenter
	m.empty.Importance = widget.LowImportance
	m.table = widget.NewTable(func() (int, int) { return len(m.rows), 6 }, func() fyne.CanvasObject { m.createdCells++; return newConnectionCell(m) }, func(id widget.TableCellID, obj fyne.CanvasObject) {
		if id.Row < 0 || id.Row >= len(m.rows) {
			return
		}
		obj.(*connectionCell).set(m.rows[id.Row], id.Col)
	})
	m.table.ShowHeaderRow = true
	m.table.HideSeparators = true
	headers := []string{"系统", "名称", "地址", "状态", "备注", "操作"}
	m.table.CreateHeader = func() fyne.CanvasObject {
		l := widget.NewLabelWithStyle("", fyne.TextAlignLeading, fyne.TextStyle{Bold: true})
		l.Truncation = fyne.TextTruncateEllipsis
		return l
	}
	m.table.UpdateHeader = func(id widget.TableCellID, obj fyne.CanvasObject) {
		if id.Col >= 0 && id.Col < len(headers) {
			if label := obj.(*widget.Label); label.Text != headers[id.Col] {
				label.SetText(headers[id.Col])
			}
		}
	}
	m.table.OnSelected = func(id widget.TableCellID) {
		if id.Row >= 0 && id.Row < len(m.rows) {
			m.selectedID = m.rows[id.Row].ID
			m.table.Refresh()
			m.updateSummary()
		}
	}
	m.table.OnHighlighted = func(id widget.TableCellID) {
		next := ""
		if id.Row >= 0 && id.Row < len(m.rows) {
			next = m.rows[id.Row].ID
		}
		if next != m.hoverID {
			m.hoverID = next
			m.table.Refresh()
		}
	}
	m.tableHost = container.NewThemeOverride(m.table, connectionTableTheme{})
	m.reload()
	return m
}
func (m *connectionManager) changed() { m.app.refreshProfileTree() }
func (m *connectionManager) reload() {
	if m.app.Store != nil {
		m.profiles = m.app.Store.ViewSnapshot().Profiles
	} else {
		m.profiles = nil
	}
	sort.SliceStable(m.profiles, func(i, j int) bool {
		a, b := m.profiles[i], m.profiles[j]
		if a.Group != b.Group {
			return a.Group < b.Group
		}
		if profileName(a) != profileName(b) {
			return profileName(a) < profileName(b)
		}
		return a.ID < b.ID
	})
	m.readStates()
	m.filter()
}
func (m *connectionManager) readStates() {
	m.online = map[string]bool{}
	for _, w := range m.app.sessions {
		if w.profileID != "" && w.Context.Err() == nil && ((w.Conn != nil && !w.Conn.Closed()) || (w.FileConn != nil && !w.FileConn.Closed())) {
			m.online[w.profileID] = true
		}
	}
}
func (m *connectionManager) refreshStates() {
	m.readStates()
	if m.table != nil {
		m.table.Refresh()
	}
}
func (m *connectionManager) filter() {
	m.rows = m.rows[:0]
	for _, p := range m.profiles {
		group := p.Group
		if group == "" {
			group = "默认分组"
		}
		if m.group != "" && group != m.group {
			continue
		}
		if m.protocol != "" && p.Protocol != m.protocol {
			continue
		}
		if m.query != "" && !containsFold(profileName(p)+" "+p.Host+" "+p.Username+" "+group, m.query) {
			continue
		}
		m.rows = append(m.rows, p)
	}
	for p, b := range m.protocolButtons {
		importance := widget.LowImportance
		if p == m.protocol {
			importance = widget.MediumImportance
		}
		if b.Importance != importance {
			b.Importance = importance
			b.Refresh()
		}
	}
	m.table.UnselectAll()
	m.table.Refresh()
	if len(m.rows) == 0 {
		if len(m.profiles) == 0 {
			m.empty.SetText("暂无连接\n新建连接或导入已有配置")
		} else {
			m.empty.SetText("没有匹配的连接\n调整搜索或协议筛选")
		}
		m.empty.Show()
	} else {
		m.empty.Hide()
	}
	m.updateSummary()
}
func (m *connectionManager) updateSummary() {
	group := m.group
	if group == "" {
		group = "所有分组"
	}
	text := fmt.Sprintf("%s  ·  %d 个连接", group, len(m.rows))
	for _, p := range m.rows {
		if p.ID == m.selectedID {
			text += "  ·  " + profileName(p)
			break
		}
	}
	if m.summary.Text != text {
		m.summary.SetText(text)
	}
}
func (m *connectionManager) selectGroup(group string) { m.group = group; m.selectedID = ""; m.filter() }
func (m *connectionManager) selectProfile(id string) {
	m.group = ""
	m.protocol = ""
	m.query = ""
	m.search.SetText("")
	m.filter()
	for i, p := range m.rows {
		if p.ID == id {
			m.selectedID = id
			m.table.Select(widget.TableCellID{Row: i, Col: 1})
			m.table.ScrollTo(widget.TableCellID{Row: i, Col: 1})
			break
		}
	}
}
func (m *connectionManager) resizeColumns(width float32) {
	if m.lastWidth == width {
		return
	}
	m.lastWidth = width
	extra := max(float32(0), width-900)
	for col, w := range []float32{72, 180 + extra*.4, 204 + extra*.25, 88, 150 + extra*.35, 200} {
		m.table.SetColumnWidth(col, w)
	}
}
func (m *connectionManager) CreateRenderer() fyne.WidgetRenderer {
	return &connectionManagerRenderer{m: m, bg: canvas.NewRectangle(theme.Color(theme.ColorNameBackground)), line: canvas.NewRectangle(theme.Color(theme.ColorNameSeparator))}
}

type connectionManagerRenderer struct {
	m        *connectionManager
	bg, line *canvas.Rectangle
}

func (r *connectionManagerRenderer) MinSize() fyne.Size { return fyne.NewSize(320, 240) }
func (r *connectionManagerRenderer) Layout(s fyne.Size) {
	m := r.m
	r.bg.Resize(s)
	m.protocols.Move(fyne.NewPos(12, 2))
	m.protocols.Resize(fyne.NewSize(max(0, s.Width-24), 34))
	searchWidth := max(float32(140), min(float32(320), s.Width-m.tools.Content.MinSize().Width-m.more.MinSize().Width-44))
	m.search.Move(fyne.NewPos(12, 42))
	m.search.Resize(fyne.NewSize(searchWidth, 32))
	m.tools.Move(fyne.NewPos(searchWidth+20, 42))
	moreWidth := m.more.MinSize().Width
	m.tools.Resize(fyne.NewSize(max(0, s.Width-searchWidth-moreWidth-40), 32))
	m.more.Move(fyne.NewPos(s.Width-moreWidth-12, 42))
	m.more.Resize(fyne.NewSize(moreWidth, 32))
	r.line.Move(fyne.NewPos(0, 83))
	r.line.Resize(fyne.NewSize(s.Width, 1))
	m.resizeColumns(s.Width)
	m.tableHost.Move(fyne.NewPos(0, 84))
	m.tableHost.Resize(fyne.NewSize(s.Width, max(0, s.Height-112)))
	m.empty.Move(fyne.NewPos(0, 126))
	m.empty.Resize(fyne.NewSize(s.Width, max(0, s.Height-180)))
	m.summary.Move(fyne.NewPos(8, max(84, s.Height-27)))
	m.summary.Resize(fyne.NewSize(max(0, s.Width-16), 26))
}
func (r *connectionManagerRenderer) Refresh() {
	r.bg.FillColor = theme.Color(theme.ColorNameBackground)
	r.line.FillColor = theme.Color(theme.ColorNameSeparator)
	r.bg.Refresh()
	r.line.Refresh()
	r.Layout(r.m.Size())
}
func (r *connectionManagerRenderer) Objects() []fyne.CanvasObject {
	return []fyne.CanvasObject{r.bg, r.m.protocols, r.m.search, r.m.tools, r.m.more, r.line, r.m.tableHost, r.m.empty, r.m.summary}
}
func (r *connectionManagerRenderer) Destroy() {}

// Templates are rebound by profile ID so recycled row actions never retain an
// earlier profile. Action buttons are allocated only for visible action cells.
type connectionCell struct {
	widget.BaseWidget
	manager *connectionManager
	profile settings.Profile
	column  int
	label   *widget.Label
	actions *fyne.Container
}

func newConnectionCell(m *connectionManager) *connectionCell {
	c := &connectionCell{manager: m, label: widget.NewLabel("")}
	c.label.Truncation = fyne.TextTruncateEllipsis
	c.ExtendBaseWidget(c)
	return c
}
func (c *connectionCell) set(p settings.Profile, col int) {
	c.profile = p
	c.column = col
	if col == 5 {
		if c.actions == nil {
			connect := action("连接", nil, func() { c.manager.app.ConnectProfile(c.profile.ID) })
			edit := action("编辑", nil, func() { p := c.profile; c.manager.app.ProfileDialog(&p, c.manager.changed) })
			more := action("更多", nil, func() { c.more() })
			c.actions = container.NewHBox(connect, edit, more)
		}
		c.actions.Show()
		c.label.Hide()
	} else {
		if c.actions != nil {
			c.actions.Hide()
		}
		c.label.Show()
		text := "—"
		switch col {
		case 1:
			text = profileName(p)
		case 2:
			text = net.JoinHostPort(p.Host, strconv.Itoa(p.Port))
		case 3:
			text = "未连接"
			if c.manager.online[p.ID] {
				text = "已连接"
			}
		}
		if c.label.Text != text {
			c.label.SetText(text)
		}
	}
	c.Refresh()
}
func (c *connectionCell) more() {
	p := c.profile
	a := c.manager.app
	menu := fyne.NewMenu("连接操作", fyne.NewMenuItem("复制地址", func() {
		a.Window.Clipboard().SetContent(net.JoinHostPort(p.Host, strconv.Itoa(p.Port)))
	}), fyne.NewMenuItem("复制用户", func() { a.Window.Clipboard().SetContent(p.Username) }), fyne.NewMenuItemSeparator(), fyne.NewMenuItem("删除连接…", func() {
		dialog.ShowConfirm("删除保存的连接？", profileName(p)+"\n不关闭已有会话；同时清理此连接保存的系统凭据。", func(ok bool) {
			if !ok {
				return
			}
			go func() {
				err := a.Store.DeleteProfile(p.ID)
				fyne.Do(func() {
					if err != nil {
						dialog.ShowInformation("删除结果", err.Error(), a.Window)
					}
					c.manager.changed()
				})
			}()
		}, a.Window)
	}))
	pos := a.UI.Driver().AbsolutePositionForObject(c).Add(fyne.NewPos(0, c.Size().Height))
	widget.ShowPopUpMenuAtPosition(menu, a.Window.Canvas(), pos)
}
func (c *connectionCell) CreateRenderer() fyne.WidgetRenderer {
	return &connectionCellRenderer{c: c, bg: canvas.NewRectangle(theme.Color(theme.ColorNameBackground)), line: canvas.NewRectangle(theme.Color(theme.ColorNameSeparator))}
}
func (c *connectionCell) selectCurrent() {
	for i, p := range c.manager.rows {
		if p.ID == c.profile.ID {
			c.manager.table.Select(widget.TableCellID{Row: i, Col: c.column})
			return
		}
	}
}

// Select on pointer-down; double-tap detection must not delay the row's
// selection/focus feedback. Authentication remains an explicit later action.
func (c *connectionCell) MouseDown(e *desktop.MouseEvent) {
	if e.Button == desktop.MouseButtonPrimary {
		c.selectCurrent()
		c.manager.app.Window.Canvas().Focus(c.manager.table)
	}
}
func (c *connectionCell) MouseUp(*desktop.MouseEvent)   {}
func (c *connectionCell) Tapped(*fyne.PointEvent)       { c.selectCurrent() }
func (c *connectionCell) DoubleTapped(*fyne.PointEvent) { c.manager.app.ConnectProfile(c.profile.ID) }

type connectionCellRenderer struct {
	c        *connectionCell
	bg, line *canvas.Rectangle
}

func (r *connectionCellRenderer) MinSize() fyne.Size { return fyne.NewSize(24, 36) }
func (r *connectionCellRenderer) Layout(s fyne.Size) {
	r.bg.Resize(s)
	r.line.Move(fyne.NewPos(0, max(0, s.Height-1)))
	r.line.Resize(fyne.NewSize(s.Width, 1))
	r.c.label.Move(fyne.NewPos(4, 0))
	r.c.label.Resize(fyne.NewSize(max(0, s.Width-8), s.Height))
	if r.c.actions != nil {
		r.c.actions.Move(fyne.NewPos(4, 2))
		r.c.actions.Resize(fyne.NewSize(max(0, s.Width-8), max(0, s.Height-4)))
	}
}
func (r *connectionCellRenderer) Refresh() {
	name := theme.ColorNameBackground
	if r.c.manager.hoverID == r.c.profile.ID {
		name = theme.ColorNameHover
	}
	if r.c.manager.selectedID == r.c.profile.ID {
		name = theme.ColorNameSelection
	}
	r.bg.FillColor = theme.Color(name)
	r.line.FillColor = theme.Color(theme.ColorNameSeparator)
	r.bg.Refresh()
	r.line.Refresh()
	r.Layout(r.c.Size())
}
func (r *connectionCellRenderer) Objects() []fyne.CanvasObject {
	out := []fyne.CanvasObject{r.bg, r.c.label}
	if r.c.actions != nil {
		out = append(out, r.c.actions)
	}
	return append(out, r.line)
}
func (r *connectionCellRenderer) Destroy() {}

func (c *connectionCell) AccessibilityLabel() string {
	if c.column == 5 {
		return "连接操作：" + profileName(c.profile)
	}
	names := []string{"系统", "名称", "地址", "状态", "备注"}
	if c.column >= 0 && c.column < len(names) {
		return names[c.column] + "：" + c.label.Text
	}
	return c.label.Text
}
func (c *connectionCell) AccessibilityRole() fyne.AccessibleRole {
	if c.column == 5 {
		return fyne.AccessibleRoleContainer
	}
	return fyne.AccessibleRoleText
}

func (m *connectionManager) showToolsMenu() {
	menu := fyne.NewMenu("连接管理操作", fyne.NewMenuItem("新建连接…", func() { m.app.ProfileDialog(nil, m.changed) }), fyne.NewMenuItem("导入配置…", func() { m.app.importProfiles(m.changed) }), fyne.NewMenuItem("导出配置…", m.app.exportProfiles), fyne.NewMenuItemSeparator(), fyne.NewMenuItem("刷新连接列表", m.reload))
	pos := m.app.UI.Driver().AbsolutePositionForObject(m.more).Add(fyne.NewPos(0, m.more.Size().Height))
	widget.ShowPopUpMenuAtPosition(menu, m.app.Window.Canvas(), pos)
}
func (m *connectionManager) AccessibilityLabel() string { return "连接管理" }
func (m *connectionManager) AccessibilityRole() fyne.AccessibleRole {
	return fyne.AccessibleRoleContainer
}
