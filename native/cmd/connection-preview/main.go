// Offline native visual review. No foreground is taken until explicitly launched.
package main

import (
	"encoding/json"
	"fmt"
	"forterm/native/internal/desktop"
	"forterm/native/internal/settings"
	"fyne.io/fyne/v2"
	"os"
	"path/filepath"
	"time"
)

func main() {
	dir, _ := os.MkdirTemp("", "forterm-connection-preview-")
	defer os.RemoveAll(dir)
	data := settings.Data{Version: 1, KnownHosts: map[string]string{}}
	groups := []string{"开发环境", "测试环境", "生产环境", "归档"}
	protocols := []string{"ssh", "sftp", "ftp"}
	for i := 0; i < 1000; i++ {
		data.Profiles = append(data.Profiles, settings.Profile{ID: fmt.Sprintf("fixture-%04d", i), Title: fmt.Sprintf("演示连接 %04d", i), Host: fmt.Sprintf("node-%04d.example.invalid", i), Port: 22, Username: "fixture", Protocol: protocols[i%3], Group: groups[i%4]})
	}
	raw, _ := json.Marshal(data)
	if err := os.WriteFile(filepath.Join(dir, "config.json"), raw, 0600); err != nil {
		panic(err)
	}
	a := desktop.NewDiagnostic(fmt.Sprintf("cn.forterm.connection-preview.dev8.%d", os.Getpid()))
	a.LoadSyntheticPreviewMetadata(dir)
	a.Window.SetTitle(fmt.Sprintf("Forterm UI Review · %s · PID %d", desktop.Version, os.Getpid()))
	a.Window.Resize(fyne.NewSize(1440, 900))
	var preview *desktop.Workspace
	a.Window.SetMainMenu(fyne.NewMainMenu(fyne.NewMenu("预览", fyne.NewMenuItem("连接管理", a.ShowLibrary), fyne.NewMenuItem("统一工作区", func() {
		if preview == nil || preview.Context.Err() != nil {
			preview = a.AddSyntheticWorkspacePreview()
		}
		a.Tabs.Select(preview.Tab)
	}), fyne.NewMenuItem("堡垒机账户", a.ShowAccounts), fyne.NewMenuItemSeparator(), fyne.NewMenuItem("深色", func() { a.SetAppearance("dark") }), fyne.NewMenuItem("浅色", func() { a.SetAppearance("light") }), fyne.NewMenuItem("1440 × 900", func() { a.Window.Resize(fyne.NewSize(1440, 900)) }), fyne.NewMenuItem("1050 × 650", func() { a.Window.Resize(fyne.NewSize(1050, 650)) }), fyne.NewMenuItem("700 × 433（150%逻辑空间）", func() { a.Window.Resize(fyne.NewSize(700, 433)) }))))
	evidence := os.Getenv("FORTERM_UI_PREVIEW_EVIDENCE")
	if evidence != "" {
		go func() {
			tick := time.NewTicker(time.Second)
			defer tick.Stop()
			for range tick.C {
				fyne.Do(func() {
					os.MkdirAll(evidence, 0700)
					report := a.ConnectionPageSnapshot()
					report["pid"] = os.Getpid()
					report["offlinePreview"] = true
					raw, _ := json.MarshalIndent(report, "", "  ")
					os.WriteFile(filepath.Join(evidence, "current.json"), raw, 0600)
				})
			}
		}()
	}
	a.Window.ShowAndRun()
	a.Close()
}
