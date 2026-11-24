package desktop

import (
	"encoding/json"
	"os"
	"path/filepath"
	"testing"

	"fyne.io/fyne/v2"
	ftest "fyne.io/fyne/v2/test"
)

// Before runGL starts, the native settings listener that clears font caches
// does not exist. Model that lifecycle without loading or showing a native UI.
type coldSettings struct {
	fyne.Settings
	selected fyne.Theme
	sets     int
}

func (s *coldSettings) Theme() fyne.Theme      { return s.selected }
func (s *coldSettings) SetTheme(th fyne.Theme) { s.selected = th; s.sets++ }

type coldApp struct {
	fyne.App
	settings *coldSettings
}

func (a coldApp) Settings() fyne.Settings { return a.settings }

func TestColdBeforeRunGLFontMeasurementProbe(t *testing.T) {
	if os.Getenv("FORTERM_COLD_PROBE") != "legacy" {
		t.Skip("explicit fresh-process offline probe")
	}
	base := ftest.NewApp()
	defer base.Quit()
	settings := &coldSettings{Settings: base.Settings(), selected: base.Settings().Theme()}
	ui := coldApp{App: base, settings: settings}
	fyne.SetCurrentApp(ui)
	defer fyne.SetCurrentApp(base)
	texts := []string{"演示连接 0000", "演示连接 0004", "演示连接 0008", "演示连接 0012", "演示连接 0016"}
	type sample struct {
		Text                                 string
		Before, ImplicitAfter, ExplicitAfter fyne.Size
		ShortBy                              float32
	}
	samples := make([]sample, len(texts))
	for i, text := range texts {
		size, _ := ui.Driver().RenderedTextSize(text, 13, fyne.TextStyle{}, nil)
		samples[i] = sample{Text: text, Before: size}
	}
	settings.SetTheme(desktopTheme{})
	// This name has never been measured: it isolates a stale face cache from
	// stale per-string metrics, matching rows first loaded after startup.
	texts = append(texts, "演示连接 0020")
	samples = append(samples, sample{Text: "演示连接 0020"})
	source := desktopTheme{}.Font(fyne.TextStyle{})
	reproduced := false
	for i, text := range texts {
		samples[i].ImplicitAfter, _ = ui.Driver().RenderedTextSize(text, 13, fyne.TextStyle{}, nil)
		samples[i].ExplicitAfter, _ = ui.Driver().RenderedTextSize(text, 13, fyne.TextStyle{}, source)
		samples[i].ShortBy = samples[i].ExplicitAfter.Width - samples[i].ImplicitAfter.Width
		if samples[i].ShortBy > 1 {
			reproduced = true
		}
	}
	raw, _ := json.MarshalIndent(map[string]any{"scope": "native pre-runGL cache lifecycle modeled with windowless driver", "reproduced": reproduced, "freshUnmeasuredText": "演示连接 0020", "samples": samples}, "", "  ")
	t.Log(string(raw))
	if dir := os.Getenv("FORTERM_FONT_PROBE"); dir != "" {
		os.MkdirAll(dir, 0700)
		os.WriteFile(filepath.Join(dir, "before-runGL-font-mismatch.json"), raw, 0600)
	}
	if !reproduced {
		t.Fatal("cold font mismatch did not reproduce; do not infer it from native screenshots alone")
	}
}

func TestColdInitialAppearanceKeepsMeasureAndSelectedFontConsistent(t *testing.T) {
	base := ftest.NewApp()
	defer base.Quit()
	settings := &coldSettings{Settings: base.Settings(), selected: base.Settings().Theme()}
	ui := coldApp{App: base, settings: settings}
	fyne.SetCurrentApp(ui)
	defer fyne.SetCurrentApp(base)
	a := &App{UI: ui, Window: ui.NewWindow("windowless cold startup")}
	manager := newConnectionManager(a)
	if settings.sets != 1 {
		t.Fatalf("theme selected %d times before first controls", settings.sets)
	}
	type sample struct {
		Text               string
		Implicit, Explicit fyne.Size
	}
	var samples []sample
	source := desktopTheme{}.Font(fyne.TextStyle{})
	for _, text := range []string{"演示连接 0000", "演示连接 0004", "演示连接 0008", "演示连接 0012", "演示连接 0016", "演示连接 0020"} {
		implicit, _ := ui.Driver().RenderedTextSize(text, 13, fyne.TextStyle{}, nil)
		explicit, _ := ui.Driver().RenderedTextSize(text, 13, fyne.TextStyle{}, source)
		samples = append(samples, sample{text, implicit, explicit})
		if implicit != explicit {
			t.Fatalf("first measure disagrees with selected font: %+v", samples[len(samples)-1])
		}
	}
	a.ensureAppearance()
	a.ensureAppearance()
	if settings.sets != 1 {
		t.Fatal("startup theme was reapplied after first controls")
	}
	if manager.table == nil {
		t.Fatal("manager did not initialize")
	}
	raw, _ := json.MarshalIndent(map[string]any{"scope": "fixed startup with native pre-runGL listener semantics; windowless", "consistent": true, "themeSelections": settings.sets, "samples": samples}, "", "  ")
	t.Log(string(raw))
	if dir := os.Getenv("FORTERM_FONT_PROBE"); dir != "" {
		os.MkdirAll(dir, 0700)
		os.WriteFile(filepath.Join(dir, "fixed-before-runGL-font-measurements.json"), raw, 0600)
	}
}
