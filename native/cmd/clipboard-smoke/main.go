package main

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"forterm/native/internal/codeedit"
	"forterm/native/internal/desktop"
	"forterm/native/internal/launch"
	"forterm/native/internal/sshconn"
	"forterm/native/internal/testserver"
	"forterm/native/internal/uitest"
	"fyne.io/fyne/v2"
	"os"
	"strings"
	"time"
)

func main() {
	server, e := testserver.New()
	if e != nil {
		panic(e)
	}
	defer server.Close()
	app := desktop.New("cn.forterm.native.clipboard-smoke")
	w := desktop.NewWorkspace(app, "粘贴安全验证", "ssh")
	other := desktop.NewWorkspace(app, "其他工作区", "local")
	app.Tabs.Append(w.Tab)
	app.Tabs.Append(other.Tab)
	app.Tabs.Select(w.Tab)
	var failure error
	go func() {
		result := map[string]any{"platform": "macOS arm64", "syntheticLoopbackOnly": true}
		defer func() {
			if failure != nil {
				result["error"] = failure.Error()
			}
			os.MkdirAll("evidence", 0755)
			raw, _ := json.MarshalIndent(result, "", "  ")
			os.WriteFile("evidence/clipboard-native-gui.json", raw, 0600)
			fyne.Do(func() { app.Close(); app.UI.Quit() })
		}()
		ctx, cancel := context.WithTimeout(context.Background(), 25*time.Second)
		defer cancel()
		c, e := sshconn.Dial(ctx, launch.Connection{Protocol: "ssh", Host: "127.0.0.1", Port: server.Port, Username: "native-user", Password: "native-test-password"}, sshconn.Callbacks{HostKey: func(_ context.Context, _ string, fingerprint string) (bool, error) {
			return fingerprint == server.Fingerprint, nil
		}})
		if e != nil {
			failure = e
			return
		}
		defer c.Close()
		fyne.DoAndWait(func() { w.SetConnection(c); w.StartShell() })
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
		if !wait(func() bool { return w.Term.Connected() }) {
			failure = fmt.Errorf("shell not ready")
			return
		}
		text := strings.Repeat("# synthetic preview line with trailing spaces  \n", 140) + "printf 'PASTE_%s_%s\\n' 'EXEC' 'DONE'\n"
		fyne.DoAndWait(func() {
			app.Window.Canvas().Focus(w.Term)
			w.Term.OnPaste(text)
			object := uitest.FindObject(app.Window.Canvas().Overlays().Top(), func(obj fyne.CanvasObject) bool { _, ok := obj.(*codeedit.Editor); return ok })
			if object == nil || object.(*codeedit.Editor).Document.Serialize() != text {
				failure = fmt.Errorf("full clipboard preview missing or truncated")
				return
			}
			if !uitest.ClickDialog(app.Window, false) {
				failure = fmt.Errorf("multiline cancel unreachable")
			}
		})
		if failure != nil {
			return
		}
		input, _ := server.Snapshot()
		if len(input) != 0 {
			failure = fmt.Errorf("cancelled paste sent bytes")
			return
		}
		result["cancelSendsNothing"] = true
		fyne.DoAndWait(func() {
			app.Window.Canvas().Focus(w.Term)
			w.Term.OnPaste(text)
			if !uitest.ClickDialog(app.Window, true) {
				failure = fmt.Errorf("paste confirmation unreachable")
			}
		})
		if failure != nil {
			return
		}
		if !wait(func() bool { return bytes.Contains(server.SnapshotOutput(), []byte("PASTE_EXEC_DONE\r\n")) }) {
			failure = fmt.Errorf("approved paste did not execute actual terminal output")
			return
		}
		input, _ = server.Snapshot()
		expected := []byte(text)
		if !bytes.Equal(input, expected) && !bytes.Equal(input, append(append([]byte("\x1b[200~"), expected...), []byte("\x1b[201~")...)) {
			failure = fmt.Errorf("approved paste bytes differ or extra Enter injected")
			return
		}
		result["largeMultilineRawBytesNoExtraEnter"] = true
		before := append([]byte(nil), input...)
		fyne.DoAndWait(func() {
			app.Window.Canvas().Focus(w.Term)
			w.Term.OnPaste("must not reach old target\n")
			app.Tabs.Select(other.Tab)
			if !uitest.ClickDialog(app.Window, true) {
				failure = fmt.Errorf("stale-target confirmation unreachable")
			}
		})
		if failure != nil {
			return
		}
		time.Sleep(80 * time.Millisecond)
		input, _ = server.Snapshot()
		if !bytes.Equal(input, before) {
			failure = fmt.Errorf("paste followed changed active workspace")
			return
		}
		result["workspaceSwitchBlocksStaleApproval"] = true
		fyne.DoAndWait(func() {
			app.Tabs.Select(w.Tab)
			app.Window.Canvas().Focus(w.Term)
			w.Term.OnPaste(strings.Repeat("x", (1<<20)+1))
			w.Term.OnPaste("\x1b[201~")
		})
		input, _ = server.Snapshot()
		if !bytes.Equal(input, before) {
			failure = fmt.Errorf("oversize/control paste reached terminal")
			return
		}
		result["sizeAndControlBoundaries"] = true
		result["previewCharacters"] = len(text)
		result["passed"] = true
	}()
	app.Window.ShowAndRun()
	if failure != nil {
		fmt.Fprintln(os.Stderr, failure)
		os.Exit(1)
	}
}
