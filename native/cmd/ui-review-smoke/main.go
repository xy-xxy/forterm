// Foreground QA window with synthetic metadata only. No network or OS vault.
package main

import (
	"encoding/json"
	"fmt"
	"forterm/native/internal/desktop"
	"forterm/native/internal/settings"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	"fyne.io/fyne/v2/widget"
	"os"
	"path/filepath"
	"time"
)

func main() {
	dir := "evidence/performance-0.2.2/virtual-library-native"
	os.MkdirAll(dir, 0700)
	tmp, _ := os.MkdirTemp("", "forterm-review-")
	defer os.RemoveAll(tmp)
	data := settings.Data{Version: 1, KnownHosts: map[string]string{}}
	for i := 0; i < 1000; i++ {
		id := fmt.Sprintf("fixture-%04d", i)
		data.Profiles = append(data.Profiles, settings.Profile{ID: id, Title: fmt.Sprintf("演示连接 %04d", i), Host: id + ".invalid", Username: "fixture", Port: 22, Protocol: "ssh", Group: fmt.Sprintf("合成分组%02d", i/100)})
	}
	raw, _ := json.Marshal(data)
	os.WriteFile(filepath.Join(tmp, "config.json"), raw, 0600)
	app := desktop.NewDiagnostic("cn.forterm.native.ui-review.smoke")
	app.Store = settings.Open(tmp, nil)
	app.Window.Resize(fyne.NewSize(1000, 700))
	original := app.Window.Content()
	samples := []map[string]any{}
	measure := func(name string, run func()) {
		start := time.Now()
		run()
		samples = append(samples, map[string]any{"action": name, "callbackMS": float64(time.Since(start)) / 1e6})
		raw, _ := json.MarshalIndent(map[string]any{"scope": "actual Mac operator callbacks; synthetic1000profiles; no network/vault", "callbacks": samples}, "", "  ")
		os.WriteFile(filepath.Join(dir, "callbacks.json"), raw, 0600)
	}
	toolbar := container.NewHBox(widget.NewLabel("合成1000连接 · 不联网"), widget.NewButton("连接库", func() { measure("library", app.ShowLibrary) }), widget.NewButton("设置", func() { measure("preferences", app.ShowPreferences) }), widget.NewButton("小窗口", func() { app.Window.Resize(fyne.NewSize(700, 412)) }))
	app.Window.SetContent(container.NewBorder(toolbar, nil, nil, nil, original))
	app.Window.SetCloseIntercept(nil)
	go func() { time.Sleep(120 * time.Second); fyne.Do(app.UI.Quit) }()
	app.Window.ShowAndRun()
	app.Close()
}
