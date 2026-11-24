// Tests a freshly-created synthetic saved profile and OS vault item. Existing
// user profiles, vault items and Electron configuration are never opened.
package main

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"forterm/native/internal/desktop"
	"forterm/native/internal/settings"
	"forterm/native/internal/testserver"
	"forterm/native/internal/vault"
	"fyne.io/fyne/v2"
	"os"
	"path/filepath"
	"time"
)

func main() {
	directory, e := os.MkdirTemp("", "forterm-library-ui-")
	if e != nil {
		panic(e)
	}
	defer os.RemoveAll(directory)
	server, e := testserver.New()
	if e != nil {
		panic(e)
	}
	defer server.Close()
	secrets := vault.New()
	store := settings.Open(directory, secrets)
	profile, e := store.UpsertProfile(settings.Profile{Title: "合成保存连接", Group: "验证分组", Protocol: "ssh", Host: "127.0.0.1", Port: server.Port, Username: "native-user", Auth: "password"}, settings.SecretUpdate{Action: "replace", Data: []byte("native-test-password")}, settings.SecretUpdate{}, false)
	if e != nil {
		panic(e)
	}
	defer store.DeleteProfile(profile.ID)
	store.TrustHost(fmt.Sprintf("127.0.0.1:%d", server.Port), server.Fingerprint)
	raw, _ := os.ReadFile(filepath.Join(directory, "config.json"))
	if bytes.Contains(raw, []byte("native-test-password")) {
		panic("plaintext credential reached JSON")
	}
	os.Setenv("FORTERM_NATIVE_SETTINGS_DIR", directory)
	defer os.Unsetenv("FORTERM_NATIVE_SETTINGS_DIR")
	app := desktop.New("cn.forterm.native.library-smoke")
	var failure error
	go func() {
		result := map[string]any{"platform": "macOS arm64", "scope": "new synthetic saved profile + OS keychain only"}
		defer func() {
			if failure != nil {
				result["error"] = failure.Error()
			}
			os.MkdirAll("evidence", 0755)
			data, _ := json.MarshalIndent(result, "", "  ")
			os.WriteFile("evidence/library-system-vault-gui.json", data, 0600)
			fyne.Do(func() { app.Close(); app.UI.Quit() })
		}()
		fyne.DoAndWait(func() { app.ConnectProfile(profile.ID) })
		ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
		defer cancel()
		ready := false
		for ctx.Err() == nil {
			fyne.DoAndWait(func() { w := app.ActiveWorkspace(); ready = w != nil && w.Conn != nil })
			if ready {
				break
			}
			time.Sleep(30 * time.Millisecond)
		}
		if !ready {
			failure = fmt.Errorf("saved profile did not authenticate through OS credential storage")
			return
		}
		if server.Auth.Load() != 1 {
			failure = fmt.Errorf("unexpected repeated authentication")
			return
		}
		exported, e := app.Store.Export()
		if e != nil || bytes.Contains(exported, []byte("native-test-password")) {
			failure = fmt.Errorf("unsafe export")
			return
		}
		result["savedProfileReloaded"] = true
		result["systemVaultUsedForExplicitConnect"] = true
		result["persistedHostFingerprintAccepted"] = true
		result["noSecretInMetadataOrExport"] = true
		result["authentications"] = server.Auth.Load()
		result["passed"] = true
	}()
	app.Window.ShowAndRun()
	if failure != nil {
		fmt.Fprintln(os.Stderr, failure)
		os.Exit(1)
	}
}
