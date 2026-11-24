package main

import (
	"encoding/json"
	"fmt"
	"forterm/native/internal/desktop"
	"forterm/native/internal/settings"
	"forterm/native/internal/uitest"
	"fyne.io/fyne/v2"
	ftest "fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/widget"
	"os"
	"regexp"
	"time"
)

func main() {
	app := desktop.New("cn.forterm.native.account-smoke")
	var failure error
	go func() {
		result := map[string]any{"platform": "macOS arm64", "scope": "new public RFC TOTP seed in new synthetic OS-vault account only"}
		defer func() {
			if failure != nil {
				result["error"] = failure.Error()
			}
			os.MkdirAll("evidence", 0755)
			data, _ := json.MarshalIndent(result, "", "  ")
			os.WriteFile("evidence/account-totp-gui.json", data, 0600)
			fyne.Do(func() { app.Close(); app.UI.Quit() })
		}()
		account, e := app.Store.UpsertAccount(settings.Account{Name: "公开合成验证账户", PortalURL: "https://example.test", Username: "synthetic", TotpEnabled: true, Totp: settings.TotpParameters{Algorithm: "SHA1", Digits: 6, Period: 5}}, settings.SecretUpdate{}, settings.SecretUpdate{Action: "replace", Data: []byte("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ")})
		if e != nil {
			failure = e
			return
		}
		defer app.Store.DeleteAccount(account.ID)
		fyne.DoAndWait(func() { app.ShowTOTP(account) })
		var code string
		deadline := time.Now().Add(6 * time.Second)
		for time.Now().Before(deadline) {
			fyne.DoAndWait(func() {
				object := uitest.FindObject(app.Window.Canvas().Overlays().Top(), func(obj fyne.CanvasObject) bool {
					label, ok := obj.(*widget.Label)
					return ok && regexp.MustCompile(`^[0-9]{6}$`).MatchString(label.Text)
				})
				if object != nil {
					code = object.(*widget.Label).Text
				}
			})
			if code != "" {
				break
			}
			time.Sleep(30 * time.Millisecond)
		}
		if code == "" {
			failure = fmt.Errorf("TOTP dialog did not display six-digit code")
			return
		}
		seed, parameters, e := settings.NormalizeTOTP("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", account.Totp)
		if e != nil {
			failure = e
			return
		}
		valid := false
		for _, offset := range []time.Duration{-time.Second, 0, time.Second} {
			expected, _, _ := settings.GenerateTOTP(seed, parameters, time.Now().Add(offset))
			if expected == code {
				valid = true
			}
		}
		for i := range seed {
			seed[i] = 0
		}
		if !valid {
			failure = fmt.Errorf("TOTP UI code differs from stored public seed")
			return
		}
		var previousClipboard string
		fyne.DoAndWait(func() {
			previousClipboard = app.UI.Clipboard().Content()
			button := uitest.FindObject(app.Window.Canvas().Overlays().Top(), func(obj fyne.CanvasObject) bool {
				b, ok := obj.(*widget.Button)
				return ok && b.Text == "复制验证码"
			})
			if button == nil {
				failure = fmt.Errorf("copy code button unreachable")
				return
			}
			ftest.Tap(button.(*widget.Button))
			if !regexp.MustCompile(`^[0-9]{6}$`).MatchString(app.UI.Clipboard().Content()) {
				failure = fmt.Errorf("copied code invalid")
			}
		})
		if failure != nil {
			return
		}
		result["actualTOTPDisplayAndCopy"] = true
		fyne.DoAndWait(func() {
			button := uitest.FindObject(app.Window.Canvas().Overlays().Top(), func(obj fyne.CanvasObject) bool {
				b, ok := obj.(*widget.Button)
				return ok && (b.Text == "关闭" || b.Text == "Close")
			})
			if button != nil {
				ftest.Tap(button.(*widget.Button))
			}
			app.UI.Clipboard().SetContent(previousClipboard)
		})
		time.Sleep(1100 * time.Millisecond)
		fyne.DoAndWait(func() {
			if app.Window.Canvas().Overlays().Top() != nil {
				failure = fmt.Errorf("TOTP close left dialog active")
			}
		})
		result["closeStopsDisplayLifecycle"] = failure == nil
		result["passed"] = failure == nil
	}()
	app.Window.ShowAndRun()
	if failure != nil {
		fmt.Fprintln(os.Stderr, failure)
		os.Exit(1)
	}
}
