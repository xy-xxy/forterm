package desktop

import (
	"os"
	"path/filepath"
	"testing"
)

func TestOfflineDiagnosticIgnoresExistingConfigAndNeverUsesSystemVault(t *testing.T) {
	existing := t.TempDir()
	raw := []byte(`{"version":1,"profiles":[{"id":"existing","host":"do-not-read.example","title":"private"}]}`)
	path := filepath.Join(existing, "config.json")
	if e := os.WriteFile(path, raw, 0600); e != nil {
		t.Fatal(e)
	}
	t.Setenv("FORTERM_NATIVE_SETTINGS_DIR", existing)
	a := &App{diagnostic: true}
	a.initSettings("diagnostic")
	defer os.RemoveAll(a.temporarySettings)
	if a.temporarySettings == "" || a.temporarySettings == existing || len(a.Store.Snapshot().Profiles) != 0 {
		t.Fatal("diagnostic read an existing store")
	}
	got, _ := os.ReadFile(path)
	if string(got) != string(raw) {
		t.Fatal("diagnostic modified existing configuration")
	}
	v := diagnosticVault{}
	if _, e := v.Get("x"); e == nil {
		t.Fatal("diagnostic vault reads must be disabled")
	}
	if e := v.Set("x", []byte("synthetic")); e == nil {
		t.Fatal("diagnostic vault writes must be disabled")
	}
}
