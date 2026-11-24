package main

import (
	"context"
	"encoding/json"
	"fmt"
	"forterm/native/internal/desktop"
	"forterm/native/internal/launch"
	"forterm/native/internal/sshconn"
	"forterm/native/internal/testserver"
	"forterm/native/internal/uitest"
	"fyne.io/fyne/v2"
	"os"
	"time"
)

func main() {
	a, e := testserver.New()
	if e != nil {
		panic(e)
	}
	defer a.Close()
	b, e := testserver.New()
	if e != nil {
		panic(e)
	}
	defer b.Close()
	app := desktop.New("cn.forterm.native.multi-source-smoke")
	owner := desktop.NewWorkspace(app, "工作区 A", "ssh")
	source := desktop.NewWorkspace(app, "独立来源 B", "sftp")
	other := desktop.NewWorkspace(app, "共享工作区 C", "local")
	app.Tabs.Append(owner.Tab)
	app.Tabs.Append(source.Tab)
	app.Tabs.Append(other.Tab)
	app.Tabs.Select(owner.Tab)
	var failure error
	go func() {
		result := map[string]any{"syntheticLoopbackOnly": true, "platform": "macOS arm64"}
		defer func() {
			if failure != nil {
				result["error"] = failure.Error()
			}
			os.MkdirAll("evidence/multi-source", 0755)
			data, _ := json.MarshalIndent(result, "", "  ")
			os.WriteFile("evidence/multi-source/result.json", data, 0600)
			fyne.Do(func() { app.Close(); app.UI.Quit() })
		}()
		ctx, cancel := context.WithTimeout(context.Background(), 45*time.Second)
		defer cancel()
		dial := func(s *testserver.Server) (*sshconn.Connection, error) {
			return sshconn.Dial(ctx, launch.Connection{Protocol: "sftp", Host: "127.0.0.1", Port: s.Port, Username: "native-user", Password: "native-test-password"}, sshconn.Callbacks{HostKey: func(_ context.Context, _ string, fp string) (bool, error) { return fp == s.Fingerprint, nil }})
		}
		connA, e := dial(a)
		if e != nil {
			failure = e
			return
		}
		defer connA.Close()
		connB, e := dial(b)
		if e != nil {
			failure = e
			return
		}
		defer connB.Close()
		if _, e = connA.CreateText(ctx, "/same.txt", "source A"); e != nil {
			failure = e
			return
		}
		for _, dir := range []string{"/one", "/two"} {
			if e = connB.Mutate(ctx, "mkdir", dir, "", 0); e != nil {
				failure = e
				return
			}
		}
		connB.CreateText(ctx, "/one/same.txt", "source B")
		connB.CreateText(ctx, "/two/second.txt", "second directory")
		wait := func(check func() bool) bool {
			for ctx.Err() == nil {
				ok := false
				fyne.DoAndWait(func() { ok = check() })
				if ok {
					return true
				}
				time.Sleep(20 * time.Millisecond)
			}
			return false
		}
		fyne.DoAndWait(func() {
			owner.SetConnection(connA)
			source.SetConnection(connB)
			owner.Refresh()
			owner.AttachFileSource(source.ID)
			owner.ActiveFileView().Path.SetText("/one")
			owner.ActiveFileView().Refresh()
		})
		if !wait(func() bool {
			return owner.ActiveFileView().Path.Text == "/one" && owner.ActiveFileView().SelectFile("same.txt")
		}) {
			failure = fmt.Errorf("owner A source view did not load /one")
			return
		}
		fyne.DoAndWait(func() {
			other.AttachFileSource(source.ID)
			other.ActiveFileView().Path.SetText("/two")
			other.ActiveFileView().Refresh()
		})
		if !wait(func() bool {
			return other.ActiveFileView().Path.Text == "/two" && other.ActiveFileView().SelectFile("second.txt")
		}) {
			failure = fmt.Errorf("owner C source view did not load /two")
			return
		}
		fyne.DoAndWait(func() {
			owner.SelectFileSource(owner.ID)
			owner.SelectFileSource(source.ID)
			if owner.ActiveFileView().Path.Text != "/one" || other.ActiveFileView().Path.Text != "/two" {
				failure = fmt.Errorf("shared source directory state leaked between owners")
			}
			app.Tabs.Select(owner.Tab)
			owner.ActiveFileView().OpenText("/one/same.txt")
		})
		if failure != nil {
			return
		}
		if !wait(func() bool { return owner.ActiveDocument() != nil && owner.ActiveDocument().SourceID == source.ID }) {
			failure = fmt.Errorf("source B draft missing")
			return
		}
		result["perOwnerSourceDirectoryState"] = true
		fyne.DoAndWait(func() {
			d := owner.ActiveDocument()
			d.Editor.Document.SelectAll()
			d.Editor.Document.Replace("edited B")
			owner.DetachFileSource(source.ID)
			if !uitest.ClickDialog(app.Window, true) {
				failure = fmt.Errorf("detach shared draft confirmation missing")
			}
		})
		if failure != nil {
			return
		}
		fyne.DoAndWait(func() {
			d := owner.ActiveDocument()
			if d.Source != connB || owner.ActiveFileView().ID != owner.ID {
				failure = fmt.Errorf("detach rebound draft source")
				return
			}
			owner.SaveDocument(d)
			if !uitest.ClickDialog(app.Window, true) {
				failure = fmt.Errorf("bound source save confirmation missing")
			}
		})
		if failure != nil {
			return
		}
		if !wait(func() bool { return !owner.ActiveDocument().Busy && !owner.ActiveDocument().Editor.Document.Dirty() }) {
			failure = fmt.Errorf("detached-source draft did not save")
			return
		}
		saved, e := connB.ReadText(ctx, "/one/same.txt")
		if e != nil || saved.Text != "edited B" {
			failure = fmt.Errorf("detached draft saved to wrong source")
			return
		}
		unchanged, e := connA.ReadText(ctx, "/same.txt")
		if e != nil || unchanged.Text != "source A" {
			failure = fmt.Errorf("source A overwritten by source B editor")
			return
		}
		result["detachedDraftStillBoundAndSavedToB"] = true
		fyne.DoAndWait(func() { owner.AttachFileSource(source.ID); app.CloseWorkspace(owner) })
		if owner.Context.Err() == nil { // a saved draft needs no discard; close should finish directly.
			fyne.DoAndWait(func() { uitest.ClickDialog(app.Window, true) })
		}
		if !wait(func() bool { return owner.Context.Err() != nil }) {
			failure = fmt.Errorf("owner close did not complete")
			return
		}
		if connB.Closed() {
			failure = fmt.Errorf("closing owner A closed shared source B")
			return
		}
		fyne.DoAndWait(func() {
			for _, tab := range app.Tabs.Items {
				if tab == source.Tab {
					failure = fmt.Errorf("source B restored despite owner C reference")
				}
			}
		})
		if failure != nil {
			return
		}
		if _, e = connB.List(ctx, "/two"); e != nil {
			failure = fmt.Errorf("shared source unavailable after owner close: %v", e)
			return
		}
		result["ownerCloseRetainsSharedBackend"] = true
		fyne.DoAndWait(func() { app.CloseWorkspace(other) })
		visible := false
		fyne.DoAndWait(func() {
			for _, tab := range app.Tabs.Items {
				if tab == source.Tab {
					visible = true
				}
			}
		})
		if !visible || connB.Closed() {
			failure = fmt.Errorf("last owner close failed to restore live source tab")
			return
		}
		result["lastReferenceRestoresSourceTab"] = true
		if b.Auth.Load() != 1 {
			failure = fmt.Errorf("shared source reauthenticated")
			return
		}
		result["sourceBAuthentications"] = b.Auth.Load()
		result["passed"] = true
	}()
	app.Window.ShowAndRun()
	if failure != nil {
		fmt.Fprintln(os.Stderr, failure)
		os.Exit(1)
	}
}
