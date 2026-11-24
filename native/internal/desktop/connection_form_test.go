package desktop

import (
	"fmt"
	"forterm/native/internal/settings"
	"forterm/native/internal/testserver"
	"forterm/native/internal/vault"
	"forterm/native/internal/workspace"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	ftest "fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/widget"
	"strings"
	"sync"
	"testing"
)

type formVault struct {
	mu     sync.Mutex
	values map[string][]byte
	fail   bool
}

func (v *formVault) Get(key string) ([]byte, error) {
	v.mu.Lock()
	defer v.mu.Unlock()
	data, ok := v.values[key]
	if !ok {
		return nil, vault.ErrNotFound
	}
	return append([]byte(nil), data...), nil
}
func (v *formVault) Set(key string, data []byte) error {
	v.mu.Lock()
	defer v.mu.Unlock()
	if v.fail {
		return fmt.Errorf("synthetic vault rejected write")
	}
	v.values[key] = append([]byte(nil), data...)
	return nil
}
func (v *formVault) Delete(key string) error {
	v.mu.Lock()
	defer v.mu.Unlock()
	delete(v.values, key)
	return nil
}
func formFixture(t *testing.T) (*App, *formVault, func(func() bool)) {
	ui, pump := uploadRouteUI(t)
	v := &formVault{values: map[string][]byte{}}
	a := &App{UI: ui, Window: ui.NewWindow("synthetic connection form"), Tabs: container.NewDocTabs(), model: workspace.New(), Store: settings.Open(t.TempDir(), v), Status: widget.NewLabel(""), trusted: map[string]string{}}
	a.initChrome()
	a.Window.Resize(fyne.NewSize(1050, 650))
	t.Cleanup(a.Close)
	return a, v, pump
}
func fillSyntheticConnection(f *connectionForm, server *testserver.Server) {
	f.protocol.SetSelected("sftp")
	f.host.SetText("127.0.0.1")
	f.port.SetText(fmt.Sprint(server.Port))
	f.user.SetText("native-user")
	f.password.SetText("native-test-password")
}
func TestSavedProfilePasswordReachedExplicitConnectionWithoutSecondPrompt(t *testing.T) {
	a, _, pump := formFixture(t)
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	a.trusted[fmt.Sprintf("127.0.0.1:%d", server.Port)] = server.Fingerprint
	f := a.newConnectionForm(nil, false, nil)
	f.show()
	fillSyntheticConnection(f, server)
	f.savePassword.SetChecked(true)
	ftest.Tap(f.saveOnly)
	pump(func() bool { return f.closed })
	profiles := a.Store.Snapshot().Profiles
	if len(profiles) != 1 || !profiles[0].HasPassword {
		t.Fatal("explicit saved-password choice was not committed")
	}
	a.ConnectProfile(profiles[0].ID)
	pump(func() bool { return len(a.sessions) > 0 && a.sessions[0].FileConn != nil })
	if a.Window.Canvas().Overlays().Top() != nil || server.Auth.Load() != 1 {
		t.Fatal("saved password caused unexpected prompt/login")
	}
	raw, e := a.Store.Export()
	if e != nil || strings.Contains(string(raw), "native-test-password") {
		t.Fatal("password entered export")
	}
}
func TestSaveAndConnectUsesOneTimeInputAndVaultFailureRetainsForm(t *testing.T) {
	a, v, pump := formFixture(t)
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	a.trusted[fmt.Sprintf("127.0.0.1:%d", server.Port)] = server.Fingerprint
	f := a.newConnectionForm(nil, false, nil)
	f.show()
	fillSyntheticConnection(f, server)
	if f.savePassword.Checked {
		t.Fatal("new form silently opted into password storage")
	}
	ftest.Tap(f.connect)
	pump(func() bool { return f.closed && len(a.sessions) > 0 && a.sessions[0].FileConn != nil })
	if a.Store.Snapshot().Profiles[0].HasPassword || server.Auth.Load() != 1 {
		t.Fatal("one-time input was persisted or not forwarded")
	}
	g := a.newConnectionForm(nil, false, nil)
	g.show()
	fillSyntheticConnection(g, server)
	g.savePassword.SetChecked(true)
	v.mu.Lock()
	v.fail = true
	v.mu.Unlock()
	ftest.Tap(g.saveOnly)
	pump(func() bool { return !g.busy })
	if g.closed || g.password.Text == "" || !strings.Contains(g.status.Text, "输入仍保留") {
		t.Fatal("vault failure discarded input or reported success")
	}
	g.popup.Hide()
	if g.password.Text != "" {
		t.Fatal("cancel retained password entry")
	}
}
func TestConnectionTestIsSingleFlightDoesNotSaveOrOpenWorkspace(t *testing.T) {
	a, _, pump := formFixture(t)
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	a.trusted[fmt.Sprintf("127.0.0.1:%d", server.Port)] = server.Fingerprint
	f := a.newConnectionForm(nil, true, nil)
	f.show()
	fillSyntheticConnection(f, server)
	f.startTest()
	f.startTest()
	pump(func() bool { return !f.busy })
	if len(a.sessions) != 0 || len(a.Store.Snapshot().Profiles) != 0 || server.Auth.Load() != 1 || server.Exec.Load() != 0 {
		t.Fatal("test saved/opened/duplicated connection")
	}
	if !strings.Contains(f.status.Text, "SFTP") {
		t.Fatal("test did not report file capability scope")
	}
}
func TestConnectionFormsLargeButFitShortHighDensitySpace(t *testing.T) {
	a, _, _ := formFixture(t)
	for _, size := range []fyne.Size{fyne.NewSize(1050, 650), fyne.NewSize(700, 433)} {
		a.Window.Resize(size)
		for _, quick := range []bool{true, false} {
			f := a.newConnectionForm(nil, quick, nil)
			f.show()
			popup := a.Window.Canvas().Overlays().Top()
			if popup == nil {
				t.Fatal("form missing")
			}
			inner := findPickerObject(popup, func(o fyne.CanvasObject) bool { _, ok := o.(*widget.PopUp); return ok })
			body := inner.(*widget.PopUp).Content
			position := a.UI.Driver().AbsolutePositionForObject(body)
			if position.X < 0 || position.Y < 0 || position.X+body.Size().Width > size.Width+1 || position.Y+body.Size().Height > size.Height+1 {
				t.Fatalf("form exceeds window: %v %v in %v", position, body.Size(), size)
			}
			f.popup.Hide()
		}
	}
}
