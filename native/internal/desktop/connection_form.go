package desktop

import (
	"context"
	"fmt"
	"image/color"
	"strconv"
	"strings"
	"time"

	"forterm/native/internal/connectcheck"
	"forterm/native/internal/launch"
	"forterm/native/internal/localfile"
	"forterm/native/internal/settings"
	"forterm/native/internal/sshconn"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
)

type connectionForm struct {
	app                                                   *App
	quick                                                 bool
	previous                                              *settings.Profile
	after                                                 func()
	ctx                                                   context.Context
	cancel                                                context.CancelFunc
	testCancel                                            context.CancelFunc
	busy, saving, closed                                  bool
	epoch                                                 uint64
	title, group, host, port, user, key, password, phrase *formEntry
	protocol, auth, jump                                  *formSelect
	jumpIDs                                               []string
	savePassword, clearPassword, savePhrase               *formCheck
	hint, status                                          *widget.Label
	test, stop, saveOnly, connect, closeButton            *connectionButton
	controls                                              []fyne.Disableable
	popup                                                 *connectionPopup
	more                                                  *connectionButton
	iconButtons                                           []*connectionButton
	viewport                                              *formViewport
	privateDetails                                        *fyne.Container
	privateToggle                                         *widget.Button
	categories                                            []*widget.Button
	footer                                                *fyne.Container
}

func (a *App) newConnectionForm(previous *settings.Profile, quick bool, after func()) *connectionForm {
	ctx, cancel := context.WithCancel(context.Background())
	f := &connectionForm{app: a, quick: quick, previous: previous, after: after, ctx: ctx, cancel: cancel}
	p := settings.Profile{Protocol: "ssh", Port: 22, Auth: "password"}
	if previous != nil {
		p = *previous
		if p.Auth == "" {
			p.Auth = "password"
		}
	}
	f.title = newFormEntry(false)
	f.group = newFormEntry(false)
	f.host = newFormEntry(false)
	f.port = newFormEntry(false)
	f.user = newFormEntry(false)
	f.key = newFormEntry(false)
	f.password = newFormEntry(true)
	f.phrase = newFormEntry(true)
	f.title.SetText(p.Title)
	f.group.SetText(p.Group)
	f.host.SetText(p.Host)
	f.port.SetText(strconv.Itoa(p.Port))
	f.user.SetText(p.Username)
	f.key.SetText(p.KeyPath)
	f.host.PlaceHolder = "主机名或 IP 地址"
	f.user.PlaceHolder = "登录用户"
	f.password.PlaceHolder = "输入本次密码"
	if p.HasPassword {
		f.password.PlaceHolder = "留空使用已保存密码"
	}
	f.phrase.PlaceHolder = "留空使用已保存口令"
	f.protocol = newFormSelect([]string{"ssh", "sftp", "ftp"})
	f.protocol.SetSelected(p.Protocol)
	f.auth = newFormSelect([]string{"password", "key", "agent", "keyboard"})
	f.auth.SetSelected(p.Auth)
	f.savePassword = newFormCheck("保存到系统安全存储")
	f.savePassword.SetChecked(p.HasPassword)
	f.clearPassword = newFormCheck("清除已保存密码")
	f.savePhrase = newFormCheck("保存私钥口令")
	f.savePhrase.SetChecked(p.HasPassphrase)
	labels := []string{"不使用跳板"}
	f.jumpIDs = []string{""}
	selected := 0
	if a.Store != nil {
		for _, candidate := range a.Store.ViewSnapshot().Profiles {
			if candidate.ID != p.ID && candidate.Protocol != "ftp" {
				f.jumpIDs = append(f.jumpIDs, candidate.ID)
				labels = append(labels, profileName(candidate))
				if candidate.ID == p.JumpID {
					selected = len(labels) - 1
				}
			}
		}
	}
	f.jump = newFormSelect(labels)
	f.jump.SetSelectedIndex(selected)
	f.hint = widget.NewLabel("")
	f.hint.Wrapping = fyne.TextWrapWord
	f.hint.Importance = widget.LowImportance
	f.status = widget.NewLabel("尚未测试连接")
	f.status.Wrapping = fyne.TextWrapWord
	updateHint := func() {
		defer func() {
			if f.viewport != nil {
				f.viewport.Reflow()
			}
		}()
		if quick {
			f.hint.SetText("仅用于此次连接，不保存密码。")
			return
		}
		if f.savePassword.Checked && !f.clearPassword.Checked {
			f.hint.SetText("已选择系统保存；存储失败时保留输入。")
		} else {
			f.hint.SetText("未保存密码仅用于测试或连接；仅保存信息会丢弃输入。取消系统保存会移除旧密码。")
		}
	}
	f.savePassword.OnChanged = func(bool) { updateHint() }
	f.clearPassword.OnChanged = func(bool) { updateHint() }
	updateHint()
	f.test = newConnectionButton("测试连接", theme.ConfirmIcon(), 120, f.startTest)
	f.stop = newConnectionButton("取消测试", nil, 104, func() {
		if f.testCancel != nil {
			f.testCancel()
			f.status.SetText("已请求取消，正在释放测试连接…")
		}
	})
	f.stop.Disable()
	f.stop.Hide()
	f.closeButton = newConnectionButton("取消", nil, 80, func() { f.popup.Hide() })
	f.connect = newConnectionButton("保存并连接", nil, 144, func() { f.save(true) })
	f.connect.Importance = widget.HighImportance
	f.saveOnly = newConnectionButton("仅保存信息", nil, 120, func() {
		if f.password.Text != "" && (!f.savePassword.Checked || f.clearPassword.Checked) {
			dialog.ShowConfirm("仅保存连接信息？", "本次密码未选择系统保存，将被丢弃；之后连接会再次询问。选择“取消”可返回，再用保存并连接。", func(ok bool) {
				if ok {
					f.save(false)
				}
			}, a.Window)
			return
		}
		f.save(false)
	})
	if quick {
		f.connect.SetText("连接")
		f.connect.OnTapped = f.connectQuick
	}
	choose := newConnectionButton("选择文件", theme.FolderOpenIcon(), 112, func() {
		dialog.ShowFileOpen(func(reader fyne.URIReadCloser, err error) {
			if err != nil || reader == nil {
				return
			}
			defer reader.Close()
			path, e := localfile.URIPath(reader.URI())
			if e != nil {
				f.status.SetText(e.Error())
				return
			}
			f.key.SetText(path)
		}, a.Window)
	})
	main := f.buildCards(choose)
	f.auth.OnChanged = func(value string) {
		if value == "key" {
			f.togglePrivate(true)
		}
	}
	if p.Auth == "key" {
		f.togglePrivate(true)
	}
	f.protocol.OnChanged = func(value string) {
		if value == "ftp" {
			if f.port.Text == "22" {
				f.port.SetText("21")
			}
			f.auth.SetSelected("password")
		} else if f.port.Text == "21" {
			f.port.SetText("22")
		}
	}
	f.controls = []fyne.Disableable{f.title, f.group, f.host, f.port, f.user, f.key, f.password, f.phrase, f.protocol, f.auth, f.jump, f.savePassword, f.clearPassword, f.savePhrase}
	caption := "新建连接"
	if previous != nil {
		caption = "编辑连接"
	}
	preferred := fyne.NewSize(920, 740)
	if quick {
		caption = "快速连接"
		preferred = fyne.NewSize(860, 600)
	}
	f.makePopup(main, caption, preferred)
	f.popup.SetOnClosed(f.dispose)
	if a.connectionForms == nil {
		a.connectionForms = map[*connectionForm]bool{}
	}
	a.connectionForms[f] = true
	return f
}
func (f *connectionForm) show() {
	f.popup.Show()
	if f.quick {
		f.app.Window.Canvas().Focus(f.host)
	} else {
		f.app.Window.Canvas().Focus(f.title)
	}
}
func (f *connectionForm) dispose() {
	if f.closed {
		return
	}
	f.closed = true
	for _, b := range append(f.iconButtons, f.test, f.stop, f.closeButton, f.connect, f.saveOnly) {
		b.hideTooltip()
	}
	f.cancel()
	if f.testCancel != nil {
		f.testCancel()
	}
	f.password.SetText("")
	f.phrase.SetText("")
	delete(f.app.connectionForms, f)
}
func (f *connectionForm) setBusy(value, saving bool) {
	f.busy = value
	f.saving = saving
	for _, c := range f.controls {
		if value {
			c.Disable()
		} else {
			c.Enable()
		}
	}
	for _, b := range []*connectionButton{f.test, f.connect, f.saveOnly, f.more} {
		if value {
			b.Disable()
		} else {
			b.Enable()
		}
	}
	if value && !saving {
		f.stop.Show()
		f.stop.Enable()
	} else {
		f.stop.Hide()
		f.stop.Disable()
	}
	if f.footer != nil {
		f.footer.Refresh()
		f.popup.Refresh()
	}
	if saving {
		f.iconButtons[0].Disable()
		f.closeButton.Disable()
	} else {
		f.iconButtons[0].Enable()
		f.closeButton.Enable()
	}
}
func (f *connectionForm) snapshot() (settings.Profile, draftSecrets, error) {
	p := settings.Profile{}
	if f.previous != nil {
		p = *f.previous
	}
	port, err := strconv.Atoi(f.port.Text)
	if err != nil {
		return p, draftSecrets{}, fmt.Errorf("端口须为1–65535")
	}
	p.Title, p.Group, p.Protocol, p.Host, p.Port, p.Username, p.Auth, p.KeyPath = f.title.Text, f.group.Text, f.protocol.Selected, strings.TrimSpace(f.host.Text), port, f.user.Text, f.auth.Selected, f.key.Text
	p.JumpID = ""
	if i := f.jump.SelectedIndex(); i >= 0 && i < len(f.jumpIDs) {
		p.JumpID = f.jumpIDs[i]
	}
	if f.quick {
		p.ID = ""
		p.Auth = "password"
		p.KeyPath = ""
		p.JumpID = ""
	}
	if p.Protocol == "ftp" {
		p.Auth = "password"
		p.KeyPath = ""
		p.JumpID = ""
	}
	if err = settings.ValidateProfile(p); err != nil {
		return p, draftSecrets{}, err
	}
	secrets := draftSecrets{password: f.password.Text, passphrase: f.phrase.Text, providedPassword: f.password.Text != "" || f.quick, providedPhrase: f.phrase.Text != ""}
	if !secrets.providedPassword && p.HasPassword && !f.clearPassword.Checked {
		secrets.inheritPassword = true
	}
	if !secrets.providedPhrase && p.HasPassphrase {
		secrets.inheritPhrase = true
	}
	return p, secrets, nil
}
func (f *connectionForm) connectQuick() {
	if f.busy {
		return
	}
	p, s, err := f.snapshot()
	if err != nil {
		f.status.SetText(err.Error())
		return
	}
	c := launch.Connection{Protocol: p.Protocol, Host: p.Host, Port: p.Port, Username: p.Username, Password: s.password, HasPassword: true, AuthMethod: "password"}
	s.clear()
	f.popup.Hide()
	f.app.Open(launch.Plan{Connection: &c, Entry: "manual"})
}
func (f *connectionForm) save(andConnect bool) {
	if f.busy || f.closed {
		return
	}
	p, s, err := f.snapshot()
	if err != nil {
		f.status.SetText(err.Error())
		return
	}
	pw := settings.SecretUpdate{Action: "keep"}
	if f.clearPassword.Checked || !f.savePassword.Checked {
		pw.Action = "clear"
	} else if f.password.Text != "" || f.previous == nil {
		pw.Action = "replace"
		pw.Data = []byte(f.password.Text)
	}
	phrase := settings.SecretUpdate{Action: "keep"}
	if !f.savePhrase.Checked || p.Protocol == "ftp" {
		phrase.Action = "clear"
	} else if f.phrase.Text != "" {
		phrase.Action = "replace"
		phrase.Data = []byte(f.phrase.Text)
	}
	f.setBusy(true, true)
	f.status.SetText("正在保存连接与所选密码策略…")
	go func() {
		defer clear(pw.Data)
		defer clear(phrase.Data)
		defer s.clear()
		saved, err := f.app.Store.UpsertProfile(p, pw, phrase, false)
		var prepared []launch.Connection
		if err == nil && andConnect {
			s.requireSame = true
			if !s.providedPassword {
				s.inheritPassword = saved.HasPassword
			}
			if !s.providedPhrase {
				s.inheritPhrase = saved.HasPassphrase
			}
			prepared, err = f.app.prepareConnectionDraft(saved, s)
		}
		fyne.Do(func() {
			if f.ctx.Err() != nil {
				clearPrepared(prepared)
				return
			}
			f.setBusy(false, false)
			if err != nil {
				f.status.SetText("未完成：" + err.Error() + "；输入仍保留，可修改后重试。")
				return
			}
			message := "已保存连接信息；没有保存密码，后续连接需输入。"
			if saved.HasPassword {
				message = "连接及密码已保存到系统安全存储。"
			} else if saved.HasPassphrase {
				message = "连接及私钥口令已保存到系统安全存储。"
			}
			f.app.SetStatus(message)
			f.app.refreshProfileTree()
			if f.after != nil {
				f.after()
			}
			f.popup.Hide()
			if andConnect {
				f.app.completeProfileConnect(prepared, len(prepared)-1)
			}
		})
	}()
}
func (f *connectionForm) startTest() {
	if f.app.diagnostic {
		f.status.SetText("离线预览不建立测试连接")
		return
	}
	if f.busy || f.closed {
		return
	}
	p, s, err := f.snapshot()
	if err != nil {
		f.status.SetText(err.Error())
		return
	}
	if (p.Auth == "password" || p.Auth == "") && !s.providedPassword && !s.inheritPassword {
		f.status.SetText("请先填写本次密码，或选择有已保存密码的连接。")
		return
	}
	f.setBusy(true, false)
	f.epoch++
	epoch := f.epoch
	ctx, cancel := context.WithTimeout(f.ctx, 45*time.Second)
	f.testCancel = cancel
	f.status.SetText("测试中（最多45秒，含指纹与认证确认）；不会保存连接或启动终端。")
	progress := func(text string) {
		fyne.Do(func() {
			if f.ctx.Err() == nil && ctx.Err() == nil && f.busy && f.epoch == epoch {
				f.status.SetText(text)
			}
		})
	}
	go func() {
		defer cancel()
		defer s.clear()
		prepared, err := f.app.prepareConnectionDraft(p, s)
		defer clearPrepared(prepared)
		var result connectcheck.Result
		if err == nil {
			for i := range prepared {
				if i > 0 && (prepared[i].AuthMethod == "password" || prepared[i].AuthMethod == "") && !prepared[i].HasPassword {
					answers, e := f.app.Challenge(ctx, "测试跳板 · 本次认证", []string{"跳板密码（仅此次测试，不保存）"}, []bool{false})
					if e != nil {
						err = e
						break
					}
					prepared[i].Password = answers[0]
					prepared[i].HasPassword = true
					clear(answers)
				}
			}
		}
		if err != nil {
			result = connectcheck.Result{Stage: "input", Message: err.Error()}
		} else if ctx.Err() != nil {
			result = connectcheck.Result{Stage: "cancelled", Message: "测试已取消或超时，未保存"}
		} else {
			result = connectcheck.Run(ctx, assembledConnection(prepared), sshconn.Callbacks{HostKey: f.app.ConfirmHost, Challenge: f.app.Challenge}, progress)
		}
		fyne.Do(func() {
			if f.ctx.Err() != nil || f.epoch != epoch {
				return
			}
			f.setBusy(false, false)
			f.testCancel = nil
			f.status.SetText(result.Message + "\n" + result.Files)
		})
	}()
}

// Shared readable scale for connection/authentication forms only. The workbench,
// terminal grid and code editor retain their independent persisted font sizes.
type connectionDialogTheme struct{}

func (connectionDialogTheme) Size(n fyne.ThemeSizeName) float32 {
	switch n {
	case theme.SizeNameText:
		return 14
	case theme.SizeNameInnerPadding, theme.SizeNamePadding:
		return 4
	case theme.SizeNameInlineIcon:
		return 16
	case theme.SizeNameInputRadius, theme.SizeNameButtonRadius:
		return 4
	}
	return fyne.CurrentApp().Settings().Theme().Size(n)
}
func (connectionDialogTheme) Color(n fyne.ThemeColorName, v fyne.ThemeVariant) color.Color {
	if v == theme.VariantDark {
		switch n {
		case theme.ColorNameInputBackground:
			return color.NRGBA{R: 39, G: 39, B: 39, A: 255}
		case theme.ColorNameInputBorder:
			return color.NRGBA{R: 72, G: 72, B: 72, A: 255}
		case theme.ColorNameOverlayBackground:
			return color.NRGBA{R: 32, G: 32, B: 32, A: 255}
		}
	}
	if n == theme.ColorNamePrimary {
		return color.NRGBA{R: 42, G: 166, B: 99, A: 255}
	}
	return fyne.CurrentApp().Settings().Theme().Color(n, v)
}
func (connectionDialogTheme) Font(s fyne.TextStyle) fyne.Resource {
	return fyne.CurrentApp().Settings().Theme().Font(s)
}
func (connectionDialogTheme) Icon(n fyne.ThemeIconName) fyne.Resource {
	return fyne.CurrentApp().Settings().Theme().Icon(n)
}
