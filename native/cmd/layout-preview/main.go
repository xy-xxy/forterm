// A network-free native layout inspection window for real pointer/keyboard QA.
package main

import (
	"encoding/json"
	"forterm/native/internal/desktop"
	"fyne.io/fyne/v2"
	"os"
	"time"
)

func main() {
	app := desktop.New("cn.forterm.native.layout-preview-smoke-v2")
	w := desktop.NewWorkspace(app, "布局键盘验证", "local")
	app.Tabs.Append(w.Tab)
	app.Tabs.Select(w.Tab)
	app.Window.Resize(fyne.NewSize(1200, 800))
	app.SetStatus("隔离布局验证 · 分隔条点击后可立即用方向键 / Shift / Home / End / Enter")
	go func() {
		tick := time.NewTicker(100 * time.Millisecond)
		defer tick.Stop()
		stop := time.NewTimer(180 * time.Second)
		defer stop.Stop()
		for {
			select {
			case <-stop.C:
				fyne.Do(func() { app.Close(); app.UI.Quit() })
				return
			case <-tick.C:
				fyne.Do(func() {
					state := w.LayoutSnapshot()
					for key, value := range app.NavigationSnapshot() {
						state[key] = value
					}
					state["capturedAt"] = time.Now().Format(time.RFC3339Nano)
					raw, _ := json.MarshalIndent(state, "", "  ")
					os.MkdirAll("/Users/xianyu/IdeaProjects/forterm/native/evidence/layout-native", 0755)
					os.WriteFile("/Users/xianyu/IdeaProjects/forterm/native/evidence/layout-native/current.json", raw, 0600)
				})
			}
		}
	}()
	app.Window.ShowAndRun()
}
