package desktop

import (
	"context"
	"forterm/native/internal/nativepicker"
	"forterm/native/internal/remotefs"
	"forterm/native/internal/transfers"
	"fyne.io/fyne/v2"
	"os"
	"path/filepath"
	"testing"
	"time"
)

type treeFixture struct {
	directoryFixture
	lists   chan string
	release chan struct{}
}

func (f *treeFixture) Stat(context.Context, string) (os.FileInfo, error) { return fixtureDir{}, nil }
func (f *treeFixture) List(ctx context.Context, p string) ([]remotefs.Entry, error) {
	f.lists <- p
	if p == "/root/child" {
		select {
		case <-f.release:
		case <-ctx.Done():
			return nil, ctx.Err()
		}
	}
	return []remotefs.Entry{{Name: "deep.txt", Size: 10}}, nil
}
func TestLazyTreePreservesExpansionSelectionAndDropsOldRootReply(t *testing.T) {
	a, _, pump := formFixture(t)
	w := NewWorkspace(a, "tree-source", "sftp")
	ctx, cancel := context.WithCancel(w.Context)
	defer cancel()
	w.Context = ctx
	fs := &treeFixture{lists: make(chan string, 8), release: make(chan struct{})}
	w.FileConn = fs
	w.directory = "/root"
	w.Path.SetText("/root")
	w.directoryReady = true
	w.fileTree.syncDirectory("/root", []remotefs.Entry{{Name: "child", Directory: true, Mode: os.ModeDir}, {Name: "one.txt", Size: 1}})
	select {
	case <-fs.lists:
		t.Fatal("tree recursively queried before expansion")
	default:
	}
	w.fileTree.view.OpenBranch("/root/child")
	select {
	case p := <-fs.lists:
		if p != "/root/child" {
			t.Fatal(p)
		}
	case <-time.After(time.Second):
		t.Fatal("expand did not query")
	}
	w.fileTree.syncDirectory("/new-root", []remotefs.Entry{{Name: "new.txt", Size: 2}})
	close(fs.release)
	pump(func() bool { return w.fileTree.active == 0 })
	if w.fileTree.root != "/new-root" || w.fileTree.nodes["/root/child/deep.txt"] != nil {
		t.Fatal("late result polluted new root")
	}
	w.fileTree.view.Select("/new-root/new.txt")
	w.fileTree.syncDirectory("/new-root", []remotefs.Entry{{Name: "new.txt", Size: 2}})
	entry, path, ok := w.selectedRemoteEntry()
	if !ok || entry.Name != "new.txt" || path != "/new-root/new.txt" {
		t.Fatal("refresh lost selected path")
	}
}
func TestNativeUploadResultRemainsBoundAndCancellationQueuesNothing(t *testing.T) {
	a, _, pump := formFixture(t)
	w := NewWorkspace(a, "original", "sftp")
	a.Tabs.Append(w.Tab)
	a.Tabs.Select(w.Tab)
	w.FileConn = &missingTargetFS{}
	w.directory = "/captured"
	w.directoryReady = true
	w.Path.SetText("/captured")
	done := make(chan transfers.Spec, 4)
	w.Transfers = transfers.New(w.Context, func(_ context.Context, s transfers.Spec, _ func(int64, int64)) error { done <- s; return nil }, nil)
	defer w.Transfers.Close()
	var callback func(nativepicker.Result)
	a.FileDialogs = func(_ context.Context, _ fyne.Window, r nativepicker.Request, cb func(nativepicker.Result)) error {
		if !r.Multiple || r.Save {
			t.Error("wrong system picker request")
		}
		callback = cb
		return nil
	}
	w.Upload()
	callback(nativepicker.Result{Native: true, Cancelled: true})
	if len(w.Transfers.Snapshot()) != 0 {
		t.Fatal("cancel queued upload")
	}
	folder := t.TempDir()
	name := "中文 空格%20#文件.txt"
	local := filepath.Join(folder, name)
	os.WriteFile(local, []byte("fixture"), 0600)
	w.Upload()
	w.directory = "/changed"
	w.Path.SetText("/changed")
	callback(nativepicker.Result{Native: true, Paths: []string{local}})
	select {
	case s := <-done:
		if s.Remote != "/captured/"+name || s.Local != local {
			t.Fatal("native result changed source target/path decoding")
		}
	case <-time.After(time.Second):
		t.Fatal("native choice did not run")
	}
	pump(func() bool { return w.Transfers.Snapshot()[0].State == transfers.Done })
}
func TestAutoNavigationCollapseOnlyCurrentSuccessAndKeepsManualWidth(t *testing.T) {
	a, _, _ := formFixture(t)
	a.navigationWidth = 320
	a.navigationVisible = true
	w := NewWorkspace(a, "current", "ssh")
	a.Tabs.Append(w.Tab)
	a.Tabs.Select(w.Tab)
	w.collapseNavigationAfterConnect()
	if a.navigationVisible || a.navigationWidth != 320 {
		t.Fatal("success did not hide only connection navigation")
	}
	a.ToggleNavigation()
	if !a.navigationVisible || a.navigationWidth != 320 {
		t.Fatal("manual reopen lost width")
	}
	pending := NewWorkspace(a, "background", "ssh")
	a.Tabs.Append(pending.Tab)
	a.Tabs.Select(w.Tab)
	pending.collapseNavigationAfterConnect()
	if !a.navigationVisible {
		t.Fatal("background success collapsed current page")
	}
	other := NewWorkspace(a, "manual-intent", "ssh")
	a.Tabs.Append(other.Tab)
	a.Tabs.Select(other.Tab)
	a.ToggleNavigation()
	a.ToggleNavigation()
	other.collapseNavigationAfterConnect()
	if !a.navigationVisible {
		t.Fatal("async success overrode later manual intent")
	}
	a.UI.Preferences().SetBool("autoCollapseNavigation", false)
	off := NewWorkspace(a, "preference-off", "ssh")
	a.Tabs.Append(off.Tab)
	a.Tabs.Select(off.Tab)
	off.collapseNavigationAfterConnect()
	if !a.navigationVisible {
		t.Fatal("disabled preference ignored")
	}
}
func TestMonitorDefaultThreeSecondsAndNoRequestWhileOffOrHidden(t *testing.T) {
	a, _, _ := formFixture(t)
	w := NewWorkspace(a, "monitor", "ssh")
	p := w.Monitor
	if p.intervalSeconds != 3 || p.auto.Checked {
		t.Fatal("new default is not3 seconds/manual opt-in")
	}
	p.SetVisible(true)
	p.method.SetSelected("关闭监控")
	p.Read()
	if p.busy {
		t.Fatal("off monitor queried")
	}
	p.SetVisible(false)
	p.method.SetSelectedIndex(0)
	p.Read()
	if p.busy {
		t.Fatal("hidden monitor queried")
	}
	a.UI.Preferences().SetInt("monitorIntervalSeconds", 10)
	other := NewWorkspace(a, "explicit10", "ssh")
	if other.Monitor.intervalSeconds != 10 {
		t.Fatal("explicit refresh setting was overwritten")
	}
}
