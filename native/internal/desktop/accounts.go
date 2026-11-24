package desktop

import (
	"context"
	"fmt"
	"forterm/native/internal/settings"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
	"strconv"
	"time"
)

func (a *App) ShowAccounts() {
	var accounts []settings.Account
	var list *widget.List
	refresh := func() { accounts = a.Store.ViewSnapshot().Accounts; list.Refresh() }
	list = widget.NewList(func() int { return len(accounts) }, func() fyne.CanvasObject { return newAccountRow(a, refresh) }, func(id widget.ListItemID, obj fyne.CanvasObject) {
		if id >= 0 && id < len(accounts) {
			obj.(*accountRow).set(accounts[id])
		}
	})
	refresh()
	notice := widget.NewLabel("门户登录待厂商适配；现有 Xshell / Xftp 入口可用。")
	notice.Importance = widget.LowImportance
	notice.Wrapping = fyne.TextWrapWord
	tools := container.NewHBox(widget.NewButtonWithIcon("新增账户", theme.ContentAddIcon(), func() { a.AccountDialog(nil, refresh) }), action("刷新", theme.ViewRefreshIcon(), refresh))
	headers := container.NewBorder(nil, nil, container.NewHBox(container.NewGridWrap(fyne.NewSize(170, 28), widget.NewLabel("账户名称")), container.NewGridWrap(fyne.NewSize(150, 28), widget.NewLabel("用户"))), container.NewGridWrap(fyne.NewSize(240, 28), widget.NewLabel("操作")), widget.NewLabel("入口地址"))
	rows := container.NewBorder(surface(headers, theme.ColorNameHeaderBackground), nil, nil, nil, list)
	viewport := container.NewHScroll(container.New(&minimumAccountWidth{}, rows))
	content := container.NewBorder(container.NewPadded(tools), container.NewPadded(notice), nil, nil, viewport)
	if a.accountsTab == nil {
		a.accountsTab = container.NewTabItem("堡垒机", content)
		a.miscTabs = append(a.miscTabs, a.accountsTab)
		a.Tabs.Append(a.accountsTab)
	} else {
		a.accountsTab.Content = content
		a.Tabs.Refresh()
	}
	a.Tabs.Select(a.accountsTab)
}

type minimumAccountWidth struct{}

func (minimumAccountWidth) MinSize([]fyne.CanvasObject) fyne.Size { return fyne.NewSize(850, 100) }
func (minimumAccountWidth) Layout(objects []fyne.CanvasObject, s fyne.Size) {
	for _, o := range objects {
		o.Move(fyne.NewPos(0, 0))
		o.Resize(s)
	}
}

type accountRow struct {
	widget.BaseWidget
	app                *App
	value              settings.Account
	after              func()
	name, user, portal *widget.Label
	code               *widget.Button
	content            *fyne.Container
}

func newAccountRow(a *App, after func()) *accountRow {
	r := &accountRow{app: a, after: after, name: widget.NewLabel(""), user: widget.NewLabel(""), portal: widget.NewLabel("")}
	for _, l := range []*widget.Label{r.name, r.user, r.portal} {
		l.Truncation = fyne.TextTruncateEllipsis
	}
	r.code = action("验证器", theme.ConfirmIcon(), func() { a.ShowTOTP(r.value) })
	edit := action("编辑", theme.DocumentCreateIcon(), func() { value := r.value; a.AccountDialog(&value, r.after) })
	remove := action("删除", theme.DeleteIcon(), func() {
		account := r.value
		dialog.ShowConfirm("移除账户配置？", account.Name+"\n同时移除此账户保存的密码和验证器key。", func(ok bool) {
			if !ok {
				return
			}
			go func() {
				err := a.Store.DeleteAccount(account.ID)
				fyne.Do(func() {
					if err != nil {
						dialog.ShowInformation("清理结果", err.Error(), a.Window)
					}
					r.after()
				})
			}()
		}, a.Window)
	})
	actions := container.NewGridWrap(fyne.NewSize(240, 36), container.NewHBox(r.code, edit, remove))
	left := container.NewHBox(container.NewGridWrap(fyne.NewSize(170, 36), r.name), container.NewGridWrap(fyne.NewSize(150, 36), r.user))
	r.content = container.NewBorder(nil, widget.NewSeparator(), left, actions, r.portal)
	r.ExtendBaseWidget(r)
	return r
}
func (r *accountRow) CreateRenderer() fyne.WidgetRenderer { return widget.NewSimpleRenderer(r.content) }
func (r *accountRow) set(value settings.Account) {
	r.value = value
	r.name.SetText(value.Name)
	r.user.SetText(value.Username)
	r.portal.SetText(value.PortalURL)
	if value.TotpEnabled && value.HasSeed {
		r.code.Enable()
	} else {
		r.code.Disable()
	}
}

func (a *App) AccountDialog(previous *settings.Account, after func()) {
	account := settings.Account{Totp: settings.TotpParameters{Algorithm: "SHA1", Digits: 6, Period: 30}}
	if previous != nil {
		account = *previous
	}
	name, portal, user := widget.NewEntry(), widget.NewEntry(), widget.NewEntry()
	name.SetText(account.Name)
	portal.SetText(account.PortalURL)
	portal.PlaceHolder = "https://bastion.example.com"
	user.SetText(account.Username)
	password, seed := widget.NewPasswordEntry(), widget.NewPasswordEntry()
	password.PlaceHolder = "留空保留原密码"
	seed.PlaceHolder = "Base32 key 或 otpauth://totp URI"
	clearPassword := widget.NewCheck("清除保存的密码", nil)
	clearSeed := widget.NewCheck("清除验证器key", nil)
	enabled := widget.NewCheck("启用本地TOTP验证码", nil)
	enabled.SetChecked(account.TotpEnabled)
	algorithm := widget.NewSelect([]string{"SHA1", "SHA256", "SHA512"}, nil)
	algorithm.SetSelected(account.Totp.Algorithm)
	digits := widget.NewSelect([]string{"6", "8"}, nil)
	digits.SetSelected(strconv.Itoa(account.Totp.Digits))
	period := widget.NewEntry()
	period.SetText(strconv.Itoa(account.Totp.Period))
	items := []*widget.FormItem{widget.NewFormItem("账户名称", name), widget.NewFormItem("入口地址", portal), widget.NewFormItem("用户", user), widget.NewFormItem("密码", password), widget.NewFormItem("", clearPassword), widget.NewFormItem("验证器key", seed), widget.NewFormItem("", clearSeed), widget.NewFormItem("", enabled), widget.NewFormItem("算法", algorithm), widget.NewFormItem("验证码位数", digits), widget.NewFormItem("周期（5–300秒）", period)}
	content := container.NewBorder(widget.NewLabel("密码与key仅存系统安全存储；不会导出或自动登录门户。"), nil, nil, nil, container.NewVScroll(widget.NewForm(items...)))
	popup := dialog.NewCustomConfirm("保存堡垒机账户", "保存", "取消", content, func(ok bool) {
		if !ok {
			password.SetText("")
			seed.SetText("")
			return
		}
		account.Name, account.PortalURL, account.Username = name.Text, portal.Text, user.Text
		account.TotpEnabled = enabled.Checked
		account.Totp.Algorithm = algorithm.Selected
		account.Totp.Digits, _ = strconv.Atoi(digits.Selected)
		account.Totp.Period, _ = strconv.Atoi(period.Text)
		pw, otp := settings.SecretUpdate{Action: "keep"}, settings.SecretUpdate{Action: "keep"}
		if clearPassword.Checked {
			pw.Action = "clear"
		} else if password.Text != "" {
			pw.Action = "replace"
			pw.Data = []byte(password.Text)
		}
		if clearSeed.Checked {
			otp.Action = "clear"
		} else if seed.Text != "" {
			otp.Action = "replace"
			otp.Data = []byte(seed.Text)
		}
		password.SetText("")
		seed.SetText("")
		go func() {
			_, err := a.Store.UpsertAccount(account, pw, otp)
			for i := range pw.Data {
				pw.Data[i] = 0
			}
			for i := range otp.Data {
				otp.Data[i] = 0
			}
			fyne.Do(func() {
				if err != nil {
					dialog.ShowInformation("未保存", err.Error(), a.Window)
				} else {
					a.SetStatus("账户配置与系统凭据已保存")
					if after != nil {
						after()
					}
				}
			})
		}()
	}, a.Window)
	fitPopup(popup, a.Window, fyne.NewSize(650, 650))
	popup.Show()
}
func (a *App) ShowTOTP(account settings.Account) {
	go func() {
		currentAccount, raw, err := a.Store.AccountWithSecret(account.ID, "totp")
		account = currentAccount
		if err != nil {
			fyne.Do(func() { dialog.ShowInformation("验证器不可用", err.Error(), a.Window) })
			return
		}
		seed, parameters, err := settings.NormalizeTOTP(string(raw), account.Totp)
		for i := range raw {
			raw[i] = 0
		}
		if err != nil {
			fyne.Do(func() { dialog.ShowInformation("验证器参数无效", err.Error(), a.Window) })
			return
		}
		fyne.Do(func() {
			ctx, cancel := context.WithCancel(context.Background())
			code := widget.NewLabelWithStyle("", fyne.TextAlignCenter, fyne.TextStyle{Monospace: true, Bold: true})
			remaining := widget.NewLabel("")
			current := ""
			update := func() {
				value, seconds, e := settings.GenerateTOTP(seed, parameters, time.Now())
				if e != nil {
					return
				}
				current = value
				code.SetText(value)
				remaining.SetText(fmt.Sprintf("剩余%d秒 · 本地生成，请确认系统时间准确", seconds))
			}
			update()
			content := container.NewVBox(widget.NewLabel(account.Name+" · "+account.Username), code, remaining, action("复制验证码", theme.ContentCopyIcon(), func() {
				value := current
				a.UI.Clipboard().SetContent(value)
				time.AfterFunc(time.Duration(parameters.Period)*time.Second, func() {
					fyne.Do(func() {
						if a.UI.Clipboard().Content() == value {
							a.UI.Clipboard().SetContent("")
						}
					})
				})
			}))
			popup := dialog.NewCustom("本地TOTP验证器", "关闭", content, a.Window)
			popup.SetOnClosed(func() {
				cancel()
				for i := range seed {
					seed[i] = 0
				}
				code.SetText("")
			})
			popup.Resize(fyne.NewSize(440, 220))
			popup.Show()
			go func() {
				tick := time.NewTicker(time.Second)
				defer tick.Stop()
				for {
					select {
					case <-ctx.Done():
						return
					case <-tick.C:
						fyne.Do(func() {
							if ctx.Err() == nil {
								update()
							}
						})
					}
				}
			}()
		})
	}()
}
