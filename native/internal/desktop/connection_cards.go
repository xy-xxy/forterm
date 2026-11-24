package desktop

import (
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/canvas"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/layout"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"image/color"
)

// These layouts report their full height at the current width. In particular,
// the scroll content cannot keep the old collapsed or wide-row minimum height.
type connectionRowLayout struct{ width float32 }

func (l *connectionRowLayout) MinSize(o []fyne.CanvasObject) fyne.Size {
	if l.width > 0 && l.width < 440 {
		return fyne.NewSize(200, o[0].MinSize().Height+o[1].MinSize().Height+20)
	}
	return fyne.NewSize(200, max(o[0].MinSize().Height, o[1].MinSize().Height)+24)
}
func (l *connectionRowLayout) Layout(o []fyne.CanvasObject, s fyne.Size) {
	l.width = s.Width
	if s.Width < 440 {
		h := o[0].MinSize().Height
		o[0].Move(fyne.NewPos(16, 8))
		o[0].Resize(fyne.NewSize(max(1, s.Width-32), h))
		o[1].Move(fyne.NewPos(16, h+12))
		o[1].Resize(fyne.NewSize(max(1, s.Width-32), o[1].MinSize().Height))
		return
	}
	left := min(float32(240), s.Width*0.48)
	o[0].Move(fyne.NewPos(16, (s.Height-o[0].MinSize().Height)/2))
	o[0].Resize(fyne.NewSize(left-28, o[0].MinSize().Height))
	o[1].Move(fyne.NewPos(left, 12))
	o[1].Resize(fyne.NewSize(max(1, s.Width-left-16), max(32, o[1].MinSize().Height)))
}
func connectionRow(title, description string, control fyne.CanvasObject) fyne.CanvasObject {
	left := &connectionCaption{title: title, note: description}
	left.ExtendBaseWidget(left)
	return container.New(&connectionRowLayout{}, left, control)
}

type connectionNoteTheme struct{ connectionDialogTheme }

func (connectionNoteTheme) Size(n fyne.ThemeSizeName) float32 {
	if n == theme.SizeNameText {
		return 12
	}
	return connectionDialogTheme{}.Size(n)
}

type connectionCard struct {
	widget.BaseWidget
	body *fyne.Container
}

func newConnectionCard(rows ...fyne.CanvasObject) *connectionCard {
	objects := []fyne.CanvasObject{}
	for i, row := range rows {
		if i > 0 {
			objects = append(objects, widget.NewSeparator())
		}
		objects = append(objects, row)
	}
	c := &connectionCard{body: container.NewVBox(objects...)}
	c.ExtendBaseWidget(c)
	return c
}
func (c *connectionCard) CreateRenderer() fyne.WidgetRenderer {
	bg := canvas.NewRectangle(color.Transparent)
	bg.CornerRadius = 7
	bg.StrokeWidth = 1
	return &connectionCardRenderer{c: c, bg: bg}
}

type connectionCardRenderer struct {
	c  *connectionCard
	bg *canvas.Rectangle
}

func (r *connectionCardRenderer) Layout(s fyne.Size) {
	r.bg.Resize(s)
	r.c.body.Move(fyne.NewPos(1, 1))
	r.c.body.Resize(fyne.NewSize(max(1, s.Width-2), max(0, s.Height-2)))
}
func (r *connectionCardRenderer) MinSize() fyne.Size {
	return fyne.NewSize(200, r.c.body.MinSize().Height+2)
}
func (r *connectionCardRenderer) Refresh() {
	t := theme.CurrentForWidget(r.c)
	v := fyne.CurrentApp().Settings().ThemeVariant()
	r.bg.FillColor = t.Color(theme.ColorNameOverlayBackground, v)
	r.bg.StrokeColor = t.Color(theme.ColorNameInputBorder, v)
	r.Layout(r.c.Size())
	r.bg.Refresh()
	r.c.body.Refresh()
}
func (r *connectionCardRenderer) Objects() []fyne.CanvasObject {
	return []fyne.CanvasObject{r.bg, r.c.body}
}
func (r *connectionCardRenderer) Destroy() {}

// The action area wraps on narrow windows instead of imposing a wide minimum
// on the entire modal. It always stays outside the vertical viewport.
type connectionActionsLayout struct{ width float32 }

func (l *connectionActionsLayout) widths(o []fyne.CanvasObject) (left, right float32) {
	for i, b := range o {
		if !b.Visible() {
			continue
		}
		if i < 2 {
			if left > 0 {
				left += 12
			}
			left += b.MinSize().Width
		} else {
			if right > 0 {
				right += 8
			}
			right += b.MinSize().Width
		}
	}
	return
}
func (l *connectionActionsLayout) MinSize(o []fyne.CanvasObject) fyne.Size {
	left, right := l.widths(o)
	h := float32(34)
	if l.width > 0 && left+right+32 > l.width {
		h = 80
	}
	return fyne.NewSize(300, h)
}
func (l *connectionActionsLayout) Layout(o []fyne.CanvasObject, s fyne.Size) {
	l.width = s.Width
	left, right := l.widths(o)
	y := float32(0)
	if left+right+32 > s.Width {
		y = 46
	}
	x := float32(0)
	end := s.Width
	for i := 0; i < 2; i++ {
		b := o[i]
		if !b.Visible() {
			continue
		}
		sz := b.MinSize()
		b.Move(fyne.NewPos(x, 0))
		b.Resize(fyne.NewSize(sz.Width, 34))
		x += sz.Width + 12
	}
	for i := len(o) - 1; i >= 2; i-- {
		b := o[i]
		if !b.Visible() {
			continue
		}
		sz := b.MinSize()
		end -= sz.Width
		b.Move(fyne.NewPos(end, y+(34-sz.Height)/2))
		b.Resize(sz)
		end -= 8
	}
}

// All form popups share this bounded shell. A parent resize remeasures the
// footer before assigning the remaining height to the scroll viewport.
type connectionShellLayout struct {
	canvas    fyne.Canvas
	preferred fyne.Size
}

func (l *connectionShellLayout) MinSize([]fyne.CanvasObject) fyne.Size {
	available := l.canvas.Size()
	return fyne.NewSize(min(l.preferred.Width, max(320, available.Width-32)), min(l.preferred.Height, max(180, available.Height-32)))
}
func (l *connectionShellLayout) Layout(o []fyne.CanvasObject, s fyne.Size) {
	o[0].Move(fyne.Position{})
	o[0].Resize(s)
}

type connectionPanelLayout struct{}

func (*connectionPanelLayout) MinSize([]fyne.CanvasObject) fyne.Size { return fyne.NewSize(320, 200) }
func (*connectionPanelLayout) Layout(o []fyne.CanvasObject, s fyne.Size) {
	header, footer, main := o[0], o[1], o[2]
	header.Move(fyne.NewPos(16, 4))
	header.Resize(fyne.NewSize(max(1, s.Width-32), 44))
	// Prime responsive footer children before calculating its new height.
	footer.Resize(fyne.NewSize(max(1, s.Width-32), footer.MinSize().Height))
	footer.Refresh()
	fh := footer.MinSize().Height
	footer.Move(fyne.NewPos(16, max(48, s.Height-fh-12)))
	footer.Resize(fyne.NewSize(max(1, s.Width-32), fh))
	main.Move(fyne.NewPos(0, 56))
	main.Resize(fyne.NewSize(s.Width, max(1, s.Height-fh-76)))
}

type connectionMainLayout struct{}

func (*connectionMainLayout) MinSize([]fyne.CanvasObject) fyne.Size { return fyne.NewSize(280, 48) }
func (*connectionMainLayout) Layout(o []fyne.CanvasObject, s fyne.Size) {
	if s.Width < 660 {
		o[0].Move(fyne.NewPos(8, 0))
		o[0].Resize(fyne.NewSize(max(1, s.Width-16), 40))
		o[1].Hide()
		o[2].Move(fyne.NewPos(0, 48))
		o[2].Resize(fyne.NewSize(s.Width, max(1, s.Height-48)))
		return
	}
	o[1].Show()
	o[0].Move(fyne.NewPos(12, 8))
	o[0].Resize(fyne.NewSize(136, max(1, s.Height-16)))
	o[1].Move(fyne.NewPos(156, 0))
	o[1].Resize(fyne.NewSize(1, s.Height))
	o[2].Move(fyne.NewPos(160, 0))
	o[2].Resize(fyne.NewSize(s.Width-160, s.Height))
}

type connectionNavLayout struct{}

func (*connectionNavLayout) MinSize([]fyne.CanvasObject) fyne.Size { return fyne.NewSize(120, 40) }
func (*connectionNavLayout) Layout(o []fyne.CanvasObject, s fyne.Size) {
	for i, b := range o {
		if s.Width > 200 {
			w := (s.Width - 12) / float32(len(o))
			b.Move(fyne.NewPos(float32(i)*(w+4), 0))
			b.Resize(fyne.NewSize(w, 36))
		} else {
			b.Move(fyne.NewPos(0, float32(i)*48))
			b.Resize(fyne.NewSize(s.Width, 40))
		}
	}
}

type connectionPopup struct {
	*widget.PopUp
	onClosed func()
	closed   bool
}

func (p *connectionPopup) Hide() {
	if p.closed {
		return
	}
	p.closed = true
	p.PopUp.Hide()
	if p.onClosed != nil {
		p.onClosed()
	}
}
func (p *connectionPopup) Dismiss()              { p.Hide() }
func (p *connectionPopup) SetOnClosed(fn func()) { p.onClosed = fn }
func (p *connectionPopup) SetDismissText(string) {}

var _ dialog.Dialog = (*connectionPopup)(nil)

func (f *connectionForm) buildCards(choose fyne.CanvasObject) fyne.CanvasObject {
	basicRows := []fyne.CanvasObject{}
	if !f.quick {
		basicRows = append(basicRows, connectionRow("名称", "为连接设置易辨认的名称", f.title), connectionRow("分组", "连接库中的分组", f.group))
	}
	basicRows = append(basicRows, connectionRow("协议", "选择终端或文件传输协议", f.protocol), connectionRow("地址 / 端口", "目标主机与服务端口", container.New(&addressPairLayout{}, f.host, f.port)))
	basic := newConnectionCard(basicRows...)
	authRows := []fyne.CanvasObject{}
	if !f.quick {
		authRows = append(authRows, connectionRow("认证方式", "按服务器允许的方式认证", f.auth))
	}
	authRows = append(authRows, connectionRow("登录用户 / 密码", "密码可仅用于本次连接", container.NewGridWithColumns(2, f.user, f.password)))
	if !f.quick {
		authRows = append(authRows, connectionRow("密码保存", "仅显式选择才写入系统安全存储", container.NewVBox(f.savePassword, f.clearPassword)))
	}
	authRows = append(authRows, container.NewPadded(f.hint))
	authentication := newConnectionCard(authRows...)
	sections := []fyne.CanvasObject{basic, authentication}
	if !f.quick {
		details := container.NewVBox(connectionRow("私钥文件", "仅用于私钥认证", container.NewBorder(nil, nil, nil, choose, f.key)), widget.NewSeparator(), connectionRow("私钥口令", "加密私钥的解密口令", f.phrase), connectionRow("口令保存", "系统安全存储", f.savePhrase))
		f.privateDetails = details
		f.privateToggle = widget.NewButtonWithIcon("私钥设置", theme.NavigateNextIcon(), func() { f.togglePrivate(!details.Visible()) })
		details.Hide()
		private := newConnectionCard(container.NewPadded(f.privateToggle), details)
		jump := newConnectionCard(connectionRow("跳板连接", "使用已保存的 SSH 连接作为跳板", f.jump))
		sections = append(sections, private, jump)
	}
	body := container.NewVBox(sections...)
	f.viewport = newFormViewport(body)
	for _, e := range []*formEntry{f.title, f.group, f.host, f.port, f.user, f.password, f.key, f.phrase} {
		e.reveal = f.viewport.Reveal
		e.viewport = f.viewport
	}
	for _, s := range []*formSelect{f.protocol, f.auth, f.jump} {
		s.reveal = f.viewport.Reveal
	}
	for _, c := range []*formCheck{f.savePassword, f.clearPassword, f.savePhrase} {
		c.reveal = f.viewport.Reveal
	}
	labels := []string{"基本信息", "身份认证", "私钥设置", "跳板连接"}
	icons := []fyne.Resource{theme.ComputerIcon(), theme.AccountIcon(), theme.SettingsIcon(), theme.FolderOpenIcon()}
	nav := container.New(&connectionNavLayout{})
	for i, section := range sections {
		index, target := i, section
		b := widget.NewButtonWithIcon(labels[i], icons[i], nil)
		b.Alignment = widget.ButtonAlignLeading
		b.Importance = widget.LowImportance
		b.OnTapped = func() {
			if index == 2 {
				f.togglePrivate(true)
			}
			f.viewport.Reflow()
			y := fyne.CurrentApp().Driver().AbsolutePositionForObject(target).Y - fyne.CurrentApp().Driver().AbsolutePositionForObject(f.viewport.scroll).Y + f.viewport.scroll.Offset.Y
			f.viewport.scroll.ScrollToOffset(fyne.NewPos(0, min(max(0, y-16), max(0, f.viewport.padded.MinSize().Height-f.viewport.scroll.Size().Height))))
			f.selectCategory(index)
		}
		f.categories = append(f.categories, b)
		nav.Add(b)
	}
	f.selectCategory(0)
	f.viewport.scroll.OnScrolled = func(pos fyne.Position) {
		idx := 0
		for i, section := range sections {
			if section.Position().Y+16 <= pos.Y+32 {
				idx = i
			}
		}
		if pos.Y > 0 && pos.Y >= f.viewport.padded.MinSize().Height-f.viewport.scroll.Size().Height-1 {
			idx = len(sections) - 1
		}
		f.selectCategory(idx)
	}
	return container.New(&connectionMainLayout{}, nav, widget.NewSeparator(), f.viewport)
}
func (f *connectionForm) selectCategory(index int) {
	for i, b := range f.categories {
		importance := widget.LowImportance
		if i == index {
			importance = widget.MediumImportance
		}
		if b.Importance != importance {
			b.Importance = importance
			b.Refresh()
		}
	}
}
func (f *connectionForm) togglePrivate(open bool) {
	if f.privateDetails == nil {
		return
	}
	if open {
		f.privateDetails.Show()
		f.privateToggle.SetIcon(theme.MenuDropDownIcon())
	} else {
		f.privateDetails.Hide()
		f.privateToggle.SetIcon(theme.NavigateNextIcon())
	}
	if f.viewport != nil {
		f.viewport.Reflow()
	}
}

type addressPairLayout struct{}

func (*addressPairLayout) MinSize([]fyne.CanvasObject) fyne.Size { return fyne.NewSize(180, 32) }
func (*addressPairLayout) Layout(o []fyne.CanvasObject, s fyne.Size) {
	port := min(float32(80), s.Width*.3)
	o[0].Move(fyne.Position{})
	o[0].Resize(fyne.NewSize(max(1, s.Width-port-8), s.Height))
	o[1].Move(fyne.NewPos(s.Width-port, 0))
	o[1].Resize(fyne.NewSize(port, s.Height))
}

func (f *connectionForm) makePopup(main fyne.CanvasObject, caption string, preferred fyne.Size) {
	close := newConnectionIcon("关闭连接窗口", theme.CancelIcon(), func() {
		if !f.saving {
			f.popup.Hide()
		}
	})
	title := widget.NewLabelWithStyle(caption, fyne.TextAlignCenter, fyne.TextStyle{Bold: true})
	header := container.NewStack(title, container.NewBorder(nil, nil, nil, close, layout.NewSpacer()))
	f.more = newConnectionIcon("更多保存方式：仅保存信息", theme.MenuDropDownIcon(), nil)
	f.more.outlined = true
	f.more.OnTapped = func() {
		if f.busy {
			return
		}
		item := fyne.NewMenuItem("仅保存信息", f.saveOnly.OnTapped)
		item.Disabled = f.saveOnly.Disabled()
		menu := widget.NewPopUpMenu(fyne.NewMenu("保存方式", item), f.app.Window.Canvas())
		menu.ShowAtRelativePosition(fyne.NewPos(f.more.Size().Width-menu.MinSize().Width, -menu.MinSize().Height-6), f.more)
	}
	actions := container.New(&connectionActionsLayout{}, f.test, f.stop, f.closeButton, f.connect, f.more)
	if f.quick {
		f.more.Hide()
	}
	f.status.Truncation = fyne.TextTruncateEllipsis
	f.status.Wrapping = fyne.TextWrapOff
	f.status.Importance = widget.LowImportance
	details := newConnectionIcon("查看完整连接状态", theme.InfoIcon(), func() { dialog.ShowInformation("连接状态", f.status.Text, f.app.Window) })
	details.Importance = widget.LowImportance
	f.iconButtons = []*connectionButton{close, details, f.more}
	footer := container.NewVBox(widget.NewSeparator(), container.NewBorder(nil, nil, nil, details, container.NewThemeOverride(f.status, connectionNoteTheme{})), actions)
	f.footer = footer
	panel := container.New(&connectionPanelLayout{}, header, footer, main)
	background := canvas.NewRectangle(color.Transparent)
	background.CornerRadius = 8
	themedPanel := container.NewStack(background, panel)
	root := container.New(&connectionShellLayout{canvas: f.app.Window.Canvas(), preferred: preferred}, themedPanel)
	content := container.NewThemeOverride(withConnectionTooltips(root, close, details, f.more), connectionDialogTheme{})
	f.popup = &connectionPopup{PopUp: widget.NewModalPopUp(content, f.app.Window.Canvas())}
	// Modal PopUp reflows with the canvas. The bounded panel itself lives inside.
	f.popup.Resize(root.MinSize())
}

// Captions have deliberate line spacing; nesting two padded Labels makes every
// settings row almost 90 dp high and obscures the card hierarchy.
type connectionCaption struct {
	widget.BaseWidget
	title, note string
}

func (c *connectionCaption) CreateRenderer() fyne.WidgetRenderer {
	title := canvas.NewText(c.title, color.Transparent)
	title.TextSize = 14
	title.TextStyle = fyne.TextStyle{Bold: true}
	note := canvas.NewText(c.note, color.Transparent)
	note.TextSize = 12
	return &captionRenderer{c: c, title: title, note: note}
}

type captionRenderer struct {
	c           *connectionCaption
	title, note *canvas.Text
}

func (r *captionRenderer) Layout(s fyne.Size) {
	r.title.Move(fyne.Position{})
	r.title.Resize(fyne.NewSize(s.Width, 18))
	r.note.Move(fyne.NewPos(0, 22))
	r.note.Resize(fyne.NewSize(s.Width, 16))
}
func (r *captionRenderer) MinSize() fyne.Size {
	h := float32(18)
	if r.c.note != "" {
		h = 38
	}
	return fyne.NewSize(100, h)
}
func (r *captionRenderer) Refresh() {
	th := theme.CurrentForWidget(r.c)
	v := fyne.CurrentApp().Settings().ThemeVariant()
	r.title.Color = th.Color(theme.ColorNameForeground, v)
	r.note.Color = th.Color(theme.ColorNamePlaceHolder, v)
	r.title.FontSource = th.Font(fyne.TextStyle{Bold: true})
	r.note.FontSource = th.Font(fyne.TextStyle{})
	r.Layout(r.c.Size())
	r.title.Refresh()
	r.note.Refresh()
}
func (r *captionRenderer) Objects() []fyne.CanvasObject { return []fyne.CanvasObject{r.title, r.note} }
func (r *captionRenderer) Destroy()                     {}
