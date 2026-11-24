package desktop

import (
	"encoding/json"
	"errors"
	"fmt"
	"forterm/native/internal/settings"
	ws "forterm/native/internal/workspace"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	ftest "fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/widget"
	"os"
	"path/filepath"
	"testing"
)

type buttonBenchmarkVault struct{}

func (buttonBenchmarkVault) Get(string) ([]byte, error) {
	return nil, errors.New("disabled synthetic vault")
}
func (buttonBenchmarkVault) Set(string, []byte) error { return errors.New("disabled synthetic vault") }
func (buttonBenchmarkVault) Delete(string) error      { return nil }

// Direct callback CPU time with a pure software Fyne driver, not OS input or
// GPU/DWM latency. All metadata is synthetic and no connect action is invoked.
func BenchmarkButtonCallbackPaths(b *testing.B) {
	for _, n := range []int{0, 100, 1000} {
		b.Run(fmt.Sprint(n), func(b *testing.B) {
			ui := ftest.NewApp()
			defer ui.Quit()
			directory := b.TempDir()
			data := settings.Data{Version: 1, KnownHosts: map[string]string{}}
			for i := 0; i < n; i++ {
				id := fmt.Sprintf("fixture-%04d", i)
				data.Profiles = append(data.Profiles, settings.Profile{ID: id, Group: fmt.Sprint(i % 20), Title: "Synthetic " + id, Host: id + ".invalid", Username: "fixture", Port: 22, Protocol: "ssh"})
				data.KnownHosts[id] = "SHA256:fixture"
			}
			raw, _ := json.Marshal(data)
			os.WriteFile(filepath.Join(directory, "config.json"), raw, 0600)
			a := &App{UI: ui, Window: ui.NewWindow("synthetic callbacks"), Store: settings.Open(directory, buttonBenchmarkVault{}), Tabs: container.NewDocTabs(container.NewTabItem("fixture", widget.NewLabel("empty"))), Status: widget.NewLabel(""), model: ws.New()}
			a.Window.Resize(fyne.NewSize(1150, 760))
			a.initChrome()
			actions := []struct {
				name string
				run  func()
			}{{"new-connection", a.ConnectDialog}, {"preferences", a.ShowPreferences}, {"navigation", a.ToggleNavigation}, {"library", a.ShowLibrary}}
			for _, action := range actions {
				b.Run(action.name, func(b *testing.B) {
					b.ReportAllocs()
					b.ResetTimer()
					for i := 0; i < b.N; i++ {
						action.run()
						for j := 0; j < 8; j++ {
							top := a.Window.Canvas().Overlays().Top()
							if top == nil {
								break
							}
							top.Hide()
						}
					}
				})
			}
		})
	}
}
