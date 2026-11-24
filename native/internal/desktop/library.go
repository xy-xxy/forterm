package desktop

import (
	"bytes"
	"errors"
	"forterm/native/internal/launch"
	"forterm/native/internal/settings"
	"forterm/native/internal/vault"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/dialog"
	"fyne.io/fyne/v2/widget"
	"os"
	"path/filepath"
	"strings"
)

func (a *App) initSettings(id string) {
	if a.diagnostic {
		directory, _ := os.MkdirTemp("", "forterm-offline-ui-")
		a.temporarySettings = directory
		a.Store = settings.Open(directory, diagnosticVault{})
		return
	}
	directory := os.Getenv("FORTERM_NATIVE_SETTINGS_DIR")
	if directory == "" {
		if strings.Contains(id, "smoke") || strings.Contains(id, "preview") {
			directory, _ = os.MkdirTemp("", "forterm-native-settings-")
			a.temporarySettings = directory
		} else {
			base, e := os.UserConfigDir()
			if e == nil {
				directory = filepath.Join(base, "FortermNative")
			}
		}
	}
	a.Store = settings.Open(directory, vault.New())
	for endpoint, fingerprint := range a.Store.ViewSnapshot().KnownHosts {
		a.trusted[endpoint] = fingerprint
	}
}
func (a *App) ShowLibrary() {
	if a.autoNavigationCollapsed {
		a.autoNavigationCollapsed = false
		a.navigationVisible = a.UI.Preferences().BoolWithFallback("navigationVisible", true)
		if a.chromeBody != nil {
			a.chromeBody.Refresh()
		}
	}
	if a.connections == nil {
		a.connections = newConnectionManager(a)
	}
	if a.connectionsTab == nil {
		a.connectionsTab = container.NewTabItem("连接管理", a.connections)
		a.miscTabs = append(a.miscTabs, a.connectionsTab)
		if a.Tabs == nil {
			a.Tabs = container.NewDocTabs()
		}
		a.Tabs.Append(a.connectionsTab)
	}
	a.refreshProfileTree()
	a.Tabs.Select(a.connectionsTab)
}

func (a *App) ProfileDialog(existing *settings.Profile, after func()) {
	a.newConnectionForm(existing, false, after).show()
}
func (a *App) ConnectProfile(id string) {
	if !a.profilePreparing.CompareAndSwap(false, true) {
		a.SetStatus("正在读取保存的连接，请稍候再操作")
		return
	}
	a.SetStatus("正在读取保存的连接与系统凭据…")
	go func() {
		defer a.profilePreparing.Store(false)
		chain, err := a.Store.LoginChain(id)
		if err != nil {
			a.postOpen(func() { dialog.ShowInformation("保存的连接不可用", err.Error(), a.Window) })
			return
		}
		defer chain.Clear()
		prepared := make([]launch.Connection, len(chain))
		for i, item := range chain {
			p := item.Profile
			c := launch.Connection{Protocol: p.Protocol, Host: p.Host, Port: p.Port, Username: p.Username, Title: p.Title, KeyPath: p.KeyPath, AuthMethod: p.Auth, ProfileID: p.ID}
			if p.HasPassword {
				if item.PasswordError != nil {
					fyne.Do(func() {
						dialog.ShowInformation("凭据不可用", "系统保存的密码不可用，请编辑连接重新保存，或使用新建连接输入。", a.Window)
					})
					return
				}
				c.Password = string(item.Password)
				c.HasPassword = true
			}
			if p.HasPassphrase && item.PassphraseError == nil {
				c.Passphrase = string(item.Passphrase)
			}
			prepared[i] = c
		}
		fyne.Do(func() {
			a.mu.Lock()
			closed := a.closed
			a.mu.Unlock()
			if closed {
				for i := range prepared {
					prepared[i].Password = ""
					prepared[i].Passphrase = ""
				}
				return
			}
			a.completeProfileConnect(prepared, len(prepared)-1)
		})
	}()
}

func (a *App) importProfiles(after func()) {
	dialog.ShowFileOpen(func(reader fyne.URIReadCloser, e error) {
		if e != nil {
			dialog.ShowInformation("导入未开始", "无法打开所选配置文件。", a.Window)
			return
		}
		if reader == nil {
			return
		}
		a.SetStatus("正在导入非秘密配置…")
		go func() {
			defer reader.Close()
			err := a.Store.Import(reader)
			a.postOpen(func() {
				if err != nil {
					dialog.ShowInformation("导入未完成", err.Error(), a.Window)
					return
				}
				if after != nil {
					after()
				}
				a.SetStatus("已导入非秘密配置；密码和TOTP密钥未导入")
			})
		}()
	}, a.Window)
}
func (a *App) exportProfiles() {
	a.SetStatus("正在准备无秘密的连接配置…")
	go func() {
		raw, e := a.Store.Export()
		a.postOpen(func() {
			if e != nil {
				dialog.ShowInformation("导出未开始", "无法准备连接配置。", a.Window)
				return
			}
			dialog.ShowFileSave(func(writer fyne.URIWriteCloser, err error) {
				if err != nil {
					dialog.ShowInformation("导出未开始", "无法打开目标文件。", a.Window)
					return
				}
				if writer == nil {
					return
				}
				a.SetStatus("正在写入导出文件…")
				go func() {
					_, e := bytes.NewReader(raw).WriteTo(writer)
					closed := writer.Close()
					a.postOpen(func() {
						if e != nil || closed != nil {
							dialog.ShowInformation("导出失败", "配置没有完整写入。", a.Window)
						} else {
							a.SetStatus("无秘密配置已导出")
						}
					})
				}()
			}, a.Window)
		})
	}()
}

func (a *App) completeProfileConnect(prepared []launch.Connection, index int) {
	for index >= 0 {
		c := prepared[index]
		if (c.AuthMethod == "" || c.AuthMethod == "password") && !c.HasPassword {
			entry := newFormEntry(true)
			title := c.Title
			if title == "" {
				title = c.Host
			}
			current := index
			input := newFormViewport(widget.NewForm(widget.NewFormItem("密码（仅本次，不保存）", entry)))
			entry.reveal = input.Reveal
			entry.viewport = input
			prompt := dialog.NewCustomConfirm("本次连接 · "+title, "继续", "取消", container.NewThemeOverride(input, connectionDialogTheme{}), func(ok bool) {
				if !ok {
					entry.SetText("")
					clearPrepared(prepared)
					return
				}
				prepared[current].Password = entry.Text
				prepared[current].HasPassword = true
				entry.SetText("")
				a.completeProfileConnect(prepared, current-1)
			}, a.Window)
			fitPopup(prompt, a.Window, fyne.NewSize(760, 320))
			prompt.Show()
			return
		}
		index--
	}
	destination := assembledConnection(prepared)
	a.Open(launch.Plan{Connection: &destination, Entry: "manual"})
	clearPrepared(prepared)
}

// Offline diagnostic windows never call the operating-system credential API.
type diagnosticVault struct{}

func (diagnosticVault) Get(string) ([]byte, error) {
	return nil, errors.New("离线诊断不访问系统凭据")
}
func (diagnosticVault) Set(string, []byte) error { return errors.New("离线诊断不保存凭据") }
func (diagnosticVault) Delete(string) error      { return nil }
