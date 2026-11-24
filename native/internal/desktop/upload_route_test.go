package desktop

import (
	"context"
	"encoding/json"
	"fmt"
	"image/png"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"forterm/native/internal/launch"
	"forterm/native/internal/nativepicker"
	"forterm/native/internal/settings"
	"forterm/native/internal/testserver"
	"forterm/native/internal/transfers"
	"forterm/native/internal/workspace"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/container"
	ftest "fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/theme"
	"fyne.io/fyne/v2/widget"
)

// The software driver normally runs Do immediately. Serialize UI dispatch as
// the real driver does, so tests can exercise App.Open and network callbacks
// without data races or taking the user's foreground.
type uploadRouteDriver struct {
	fyne.Driver
	calls chan func()
}

func (d *uploadRouteDriver) DoFromGoroutine(fn func(), wait bool) {
	done := make(chan struct{})
	d.calls <- func() { fn(); close(done) }
	if wait {
		<-done
	}
}

type uploadRouteApp struct {
	fyne.App
	driver fyne.Driver
}

func (a uploadRouteApp) Driver() fyne.Driver { return a.driver }
func uploadRouteUI(t *testing.T) (fyne.App, func(func() bool)) {
	t.Helper()
	base := ftest.NewApp()
	d := &uploadRouteDriver{Driver: base.Driver(), calls: make(chan func(), 1024)}
	ui := uploadRouteApp{App: base, driver: d}
	fyne.SetCurrentApp(ui)
	t.Cleanup(func() { fyne.SetCurrentApp(base); base.Quit() })
	ui.Settings().SetTheme(theme.DefaultTheme())
	pump := func(check func() bool) {
		t.Helper()
		deadline := time.NewTimer(4 * time.Second)
		defer deadline.Stop()
		for !check() {
			select {
			case fn := <-d.calls:
				fn()
			case <-time.After(time.Millisecond):
			case <-deadline.C:
				t.Fatal("software UI/queue did not reach expected stage")
			}
		}
	}
	return ui, pump
}

func uploadRouteButton(t *testing.T, root fyne.CanvasObject, name string) *widget.Button {
	t.Helper()
	obj := findPickerObject(root, func(o fyne.CanvasObject) bool {
		b, ok := o.(*widget.Button)
		return ok && (strings.EqualFold(b.Text, name) || name == "Cancel" && b.Text == "取消" || name == "Open" && b.Text == "打开")
	})
	if obj == nil {
		t.Fatalf("button %q missing", name)
	}
	return obj.(*widget.Button)
}

type overwriteRouteFS struct {
	directoryFixture
	info os.FileInfo
}

func (f *overwriteRouteFS) Stat(context.Context, string) (os.FileInfo, error) { return f.info, nil }

func TestClosingBorrowedViewDismissesPendingOverwriteWithoutWriting(t *testing.T) {
	ui, pump := uploadRouteUI(t)
	local := filepath.Join(t.TempDir(), "local.txt")
	if err := os.WriteFile(local, []byte("fixture"), 0600); err != nil {
		t.Fatal(err)
	}
	info, _ := os.Stat(local)
	ctx, cancel := context.WithCancel(context.Background())
	window := ui.NewWindow("borrowed source confirmation")
	w := &Workspace{Context: ctx, cancel: cancel, owner: &Workspace{}, Window: window, FileConn: &overwriteRouteFS{info: info}}
	ran := make(chan struct{}, 1)
	q := transfers.New(context.Background(), func(context.Context, transfers.Spec, func(int64, int64)) error { ran <- struct{}{}; return nil }, nil)
	defer q.Close()
	w.Transfers = q
	q.Add(transfers.Spec{Direction: "upload", Local: local, Remote: "/uploads/existing.txt"}, w.transferBefore)
	pump(func() bool { return window.Canvas().Overlays().Top() != nil })
	w.closeBorrowedView()
	pump(func() bool {
		items := q.Snapshot()
		return items[0].State == transfers.Cancelled && window.Canvas().Overlays().Top() == nil
	})
	select {
	case <-ran:
		t.Fatal("closed confirmation wrote a file")
	default:
	}
}

func TestControllerXftpPickerAndAttachedSourceUseSameAuthenticatedQueue(t *testing.T) {
	runControllerUploadRoute(t, false)
}
func TestControllerXftpSeparateWindowConfirmsInVisibleOwner(t *testing.T) {
	runControllerUploadRoute(t, true)
}
func runControllerUploadRoute(t *testing.T, separate bool) {
	t.Helper()
	ui, pump := uploadRouteUI(t)
	srv, err := testserver.New()
	if err != nil {
		t.Fatal(err)
	}
	defer srv.Close()
	a := &App{UI: ui, Window: ui.NewWindow("controller route"), Tabs: container.NewDocTabs(), model: workspace.New(), Status: widget.NewLabel(""), gate: launch.NewGate(), Store: settings.Open(t.TempDir(), diagnosticVault{}), trusted: map[string]string{fmt.Sprintf("127.0.0.1:%d", srv.Port): srv.Fingerprint}}
	a.Tabs.OnSelected = func(tab *container.TabItem) {
		for _, w := range a.sessions {
			w.setWorkspaceVisible(w.Tab == tab)
		}
	}
	a.Window.SetContent(a.Tabs)
	a.Window.Resize(fyne.NewSize(1050, 650))
	rawURL := fmt.Sprintf("ssh://native-user:native-test-password@127.0.0.1:%d", srv.Port)
	args := []string{"--entry=xftp", "-newtab", "synthetic source", "-url", rawURL}
	if separate {
		args = []string{"--entry=xftp", "-newwin", "-url", rawURL}
	}
	plan := launch.Parse(args, "forterm-native.exe")
	if plan.Error != "" || plan.Connection.Protocol != "sftp" {
		t.Fatalf("unexpected controller plan: %s", plan.Error)
	}
	a.Open(plan)
	defer a.Close()
	source := a.sessions[0]
	pump(func() bool { return source.directoryReady })
	if err := source.FileConn.(interface {
		Mutate(context.Context, string, string, string, os.FileMode) error
	}).Mutate(context.Background(), "mkdir", "/uploads", "", 0700); err != nil {
		t.Fatal(err)
	}
	source.Path.SetText("/uploads")
	source.Refresh()
	pump(func() bool { return !source.busy && source.directory == "/uploads" })
	owner := NewWorkspace(a, "synthetic terminal owner", "ssh")
	a.Tabs.Append(owner.Tab)
	a.Tabs.Select(owner.Tab)
	if err := owner.AttachFileSource(source.ID); err != nil {
		t.Fatal(err)
	}
	view := owner.ActiveFileView()
	pump(func() bool { return view.directoryReady && !view.busy })
	if view == source || view.FileConn != source.FileConn || view.Transfers != source.Transfers || view.Window != owner.Window {
		t.Fatal("attachment changed source/queue/window")
	}
	for _, tab := range a.Tabs.Items {
		if tab == source.Tab {
			t.Fatal("source duplicate top tab visible")
		}
	}
	local := t.TempDir()
	name := "中文 空格%20.txt"
	payload := "controller attached payload"
	if err := os.WriteFile(filepath.Join(local, name), []byte(payload), 0600); err != nil {
		t.Fatal(err)
	}
	var selected func(nativepicker.Result)
	a.FileDialogs = func(_ context.Context, _ fyne.Window, _ nativepicker.Request, callback func(nativepicker.Result)) error {
		selected = callback
		return nil
	}
	ftest.Tap(uploadRouteButton(t, view.filePane, "上传"))
	selected(nativepicker.Result{Native: true, Cancelled: true})
	if len(source.Transfers.Snapshot()) != 0 {
		t.Fatal("cancel queued file")
	}
	choose := func() {
		ftest.Tap(uploadRouteButton(t, view.filePane, "上传"))
		selected(nativepicker.Result{Native: true, Paths: []string{filepath.Join(local, name)}})
	}
	choose()
	pump(func() bool {
		items := source.Transfers.Snapshot()
		return len(items) == 1 && (items[0].State == transfers.Done || items[0].State == transfers.Failed)
	})
	items := source.Transfers.Snapshot()
	if items[0].State != transfers.Done || items[0].Remote != "/uploads/"+name || !view.queueExpanded.Checked {
		t.Fatalf("attached upload result: %+v", items)
	}
	got, err := source.Conn.ReadText(context.Background(), items[0].Remote)
	if err != nil || got.Text != payload || srv.Auth.Load() != 1 {
		t.Fatalf("bytes/auth mismatch: %v auth=%d", err, srv.Auth.Load())
	}

	pump(func() bool {
		return len(view.transferItems) == 1 && view.transferItems[0].State == transfers.Done && !view.busy
	})
	if view.queuePane.Size().Height < view.queuePane.MinSize().Height {
		t.Fatalf("expanded queue clipped: allocated=%v required=%v", view.queuePane.Size(), view.queuePane.MinSize())
	}
	nextPayload := "replacement requires explicit confirmation"
	if err := os.WriteFile(filepath.Join(local, name), []byte(nextPayload), 0600); err != nil {
		t.Fatal(err)
	}
	choose()
	pump(func() bool {
		return owner.Window.Canvas().Overlays().Top() != nil && len(source.Transfers.Snapshot()) == 2 && source.Transfers.Snapshot()[1].Phase == transfers.PhaseOverwrite
	})
	if separate && source.Window.Canvas().Overlays().Top() != nil {
		t.Fatal("overwrite prompt is on hidden source window")
	}
	if !view.queueExpanded.Checked || a.Tabs.Selected() != owner.Tab {
		t.Fatal("pending confirmation not on active owner")
	}
	if evidenceDir := os.Getenv("FORTERM_ROUTE_EVIDENCE"); evidenceDir != "" {
		if err := os.MkdirAll(evidenceDir, 0700); err != nil {
			t.Fatal(err)
		}
		suffix := "tab"
		if separate {
			suffix = "window"
		}
		imageFile, err := os.Create(filepath.Join(evidenceDir, "overwrite-"+suffix+".png"))
		if err != nil {
			t.Fatal(err)
		}
		err = png.Encode(imageFile, owner.Window.Canvas().Capture())
		imageFile.Close()
		if err != nil {
			t.Fatal(err)
		}
		if err := os.WriteFile(filepath.Join(evidenceDir, "stages-"+suffix+".json"), []byte(view.UploadEvidenceJSON()), 0600); err != nil {
			t.Fatal(err)
		}
	}
	ftest.Tap(uploadRouteButton(t, owner.Window.Canvas().Overlays().Top(), "跳过"))
	pump(func() bool { return source.Transfers.Snapshot()[1].State == transfers.Cancelled })
	got, err = source.Conn.ReadText(context.Background(), items[0].Remote)
	if err != nil || got.Text != payload {
		t.Fatal("skip overwrote remote file")
	}
	choose()
	pump(func() bool {
		return owner.Window.Canvas().Overlays().Top() != nil && len(source.Transfers.Snapshot()) == 3 && source.Transfers.Snapshot()[2].Phase == transfers.PhaseOverwrite
	})
	ftest.Tap(uploadRouteButton(t, owner.Window.Canvas().Overlays().Top(), "覆盖"))
	pump(func() bool {
		return source.Transfers.Snapshot()[2].State == transfers.Done || source.Transfers.Snapshot()[2].State == transfers.Failed
	})
	got, err = source.Conn.ReadText(context.Background(), items[0].Remote)
	if err != nil || got.Text != nextPayload || srv.Auth.Load() != 1 {
		t.Fatalf("confirmed overwrite bytes/auth mismatch: %v auth=%d", err, srv.Auth.Load())
	}
	if evidenceDir := os.Getenv("FORTERM_ROUTE_EVIDENCE"); evidenceDir != "" {
		suffix := "tab"
		if separate {
			suffix = "window"
		}
		result, _ := json.MarshalIndent(map[string]any{"version": Version, "softwareDriverOnly": true, "syntheticLoopbackOnly": true, "controllerEntry": "xftp", "separateSourceWindow": separate, "pickerCancelQueueEmpty": true, "attachedSourceExactBytes": true, "overwritePromptOnVisibleOwner": true, "skipPreservedRemoteBytes": true, "confirmedOverwriteExactBytes": true, "authentications": srv.Auth.Load()}, "", "  ")
		if err := os.WriteFile(filepath.Join(evidenceDir, "result-"+suffix+".json"), result, 0600); err != nil {
			t.Fatal(err)
		}
	}
	if plan.Connection.Password != "" || len(a.Store.Snapshot().Profiles) != 0 {
		t.Fatal("temporary controller credentials retained in plan/store")
	}
	if !strings.Contains(view.UploadEvidenceJSON(), "queue_accepted") || strings.Contains(source.UploadEvidenceJSON(), "picker_callback") {
		t.Fatal("picker callback attributed to hidden original workspace")
	}
}
