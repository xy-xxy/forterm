// Native GUI and real loopback SSH/SFTP smoke. Never linked into the release core.
package main

import (
	"bytes"
	"context"
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"forterm/native/internal/codeedit"
	"forterm/native/internal/desktop"
	"forterm/native/internal/launch"
	"forterm/native/internal/sshconn"
	"forterm/native/internal/testserver"
	"fyne.io/fyne/v2"
	ftest "fyne.io/fyne/v2/test"
	"fyne.io/fyne/v2/widget"
	"image/png"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"
)

func main() {
	dir := os.Getenv("FORTERM_EVIDENCE")
	if dir == "" {
		dir = "evidence"
	}
	if err := os.MkdirAll(dir, 0755); err != nil {
		panic(err)
	}
	server, err := testserver.New()
	if err != nil {
		panic(err)
	}
	defer server.Close()
	server.ShellDelay.Store(int64(250 * time.Millisecond))
	id := os.Getenv("FORTERM_SMOKE_ID")
	if id == "" {
		id = "cn.forterm.native.smoke"
	}
	ui := desktop.New(id)
	// The focus probe needs a visible file entry, independent of prior QA prefs.
	ui.UI.Preferences().SetBool("filesVisible", true)
	if mode := os.Getenv("FORTERM_APPEARANCE"); mode != "" {
		ui.SetAppearance(mode)
	}
	work := desktop.NewWorkspace(ui, "运维 A")
	ui.Tabs.Append(work.Tab)
	ui.Tabs.Select(work.Tab)
	var finalErr error
	go func() {
		result := map[string]any{"realNativeWindow": true, "remote": "synthetic loopback only", "webEngine": false, "platform": "macOS arm64"}
		defer func() {
			if finalErr != nil {
				result["error"] = finalErr.Error()
			}
			data, _ := json.MarshalIndent(result, "", "  ")
			_ = os.WriteFile(filepath.Join(dir, "native-gui-smoke.json"), data, 0600)
			fyne.Do(func() { work.Close(); ui.Window.Close(); ui.UI.Quit() })
		}()
		ctx, cancel := context.WithTimeout(context.Background(), 100*time.Second)
		defer cancel()
		c, e := sshconn.Dial(ctx, launch.Connection{Protocol: "ssh", Host: "127.0.0.1", Port: server.Port, Username: "native-user", Password: "native-test-password"}, sshconn.Callbacks{HostKey: func(_ context.Context, _ string, fp string) (bool, error) { return fp == server.Fingerprint, nil }})
		if e != nil {
			finalErr = e
			return
		}
		defer c.Close()
		fyne.DoAndWait(func() { work.SetConnection(c); work.StartShell(); ui.Window.Canvas().Focus(work.Path); work.Refresh() })
		wait := func(check func() bool) bool {
			for ctx.Err() == nil {
				ok := false
				fyne.DoAndWait(func() { ok = check() })
				if ok {
					return true
				}
				time.Sleep(30 * time.Millisecond)
			}
			return false
		}
		if !wait(func() bool { _, e := work.Term.Write(nil); return e == nil }) {
			finalErr = fmt.Errorf("terminal readiness timeout")
			return
		}
		fyne.DoAndWait(func() {
			if ui.Window.Canvas().Focused() != work.Path {
				finalErr = fmt.Errorf("late shell readiness stole file entry focus")
			}
		})
		if finalErr != nil {
			return
		}
		result["lateShellReadinessPreservesFocus"] = true
		local, e := os.MkdirTemp("", "forterm-native-smoke-")
		if e != nil {
			finalErr = e
			return
		}
		defer os.RemoveAll(local)
		source := filepath.Join(local, "upload.txt")
		payload := []byte("Forterm native UTF-8 roundtrip 中文\n")
		if e = os.WriteFile(source, payload, 0600); e != nil {
			finalErr = e
			return
		}
		if e = c.Upload(ctx, source, "/native-roundtrip.txt", false); e != nil {
			finalErr = e
			return
		}
		if e = c.Download(ctx, "/native-roundtrip.txt", filepath.Join(local, "download.txt"), false); e != nil {
			finalErr = e
			return
		}
		got, _ := os.ReadFile(filepath.Join(local, "download.txt"))
		if string(got) != string(payload) {
			finalErr = fmt.Errorf("SFTP byte mismatch")
			return
		}
		result["sftpRoundtrip"] = true
		fyne.DoAndWait(func() { work.Path.SetText("/"); work.Refresh() })
		if !wait(func() bool { return work.Files.Length() > 0 }) {
			finalErr = fmt.Errorf("file list timeout")
			return
		}
		fyne.DoAndWait(func() { ui.Window.Canvas().Focus(nil) })
		if !wait(func() bool { return strings.Contains(work.Term.Text(), "native>") }) {
			finalErr = fmt.Errorf("shell startup prompt missing")
			return
		}
		initialInput, _ := server.Snapshot()
		if len(initialInput) != 0 {
			finalErr = fmt.Errorf("unexpected pre-command input: %d bytes, content not logged", len(initialInput))
			return
		}
		var nonce [8]byte
		_, _ = rand.Read(nonce[:])
		suffix := hex.EncodeToString(nonce[:])
		executionMarker := "NATIVE_EXEC_" + suffix
		command := fmt.Sprintf("printf '\\033[2J\\033[HNative SSH terminal\\n\\033[32mANSI GREEN\\033[0m  \\033[41m中文双列 AB\\033[0m\\nNATIVE_%%s_%%s\\n' 'EXEC' '%s'\n", suffix)
		if strings.Contains(command, executionMarker) {
			finalErr = fmt.Errorf("execution marker appears in echoed command")
			return
		}
		fyne.DoAndWait(func() { _, e = work.Term.Write([]byte(command)) })
		if e != nil {
			finalErr = e
			return
		}
		if !wait(func() bool {
			return strings.Contains(work.Term.Text(), executionMarker) && bytes.Contains(server.SnapshotOutput(), []byte("\x1b[32mANSI GREEN\x1b[0m"))
		}) {
			finalErr = fmt.Errorf("real shell execution/ANSI output missing")
			return
		}
		sent, _ := server.Snapshot()
		if !bytes.Equal(sent, []byte(command)) {
			finalErr = fmt.Errorf("SSH input differs from fixture command: got %d expected %d bytes; content not logged", len(sent), len(command))
			return
		}
		actualHash := sha256.Sum256(sent)
		result["exactShellInputSHA256"] = hex.EncodeToString(actualHash[:])
		expectedHash := sha256.Sum256([]byte(command))
		result["expectedShellInputSHA256"] = hex.EncodeToString(expectedHash[:])
		result["fixtureCommandHex"] = hex.EncodeToString([]byte(command))
		result["exactShellInputBytes"] = len(sent)
		result["nonEchoExecutionMarker"] = executionMarker
		result["rawANSIOutputConfirmed"] = true
		green := 0
		if !wait(func() bool {
			img := ui.Window.Canvas().Capture()
			position := ui.UI.Driver().AbsolutePositionForObject(work.Term)
			size := work.Term.Size()
			scale := float32(img.Bounds().Dx()) / ui.Window.Canvas().Size().Width
			green = 0
			for y := max(0, int(position.Y*scale)); y < min(img.Bounds().Dy(), int((position.Y+size.Height)*scale)); y++ {
				for x := max(0, int(position.X*scale)); x < min(img.Bounds().Dx(), int((position.X+size.Width)*scale)); x++ {
					r, g, b, _ := img.At(x, y).RGBA()
					if g > 30000 && float64(g) > float64(r)*1.2 && float64(g) > float64(b)*1.1 {
						green++
					}
				}
			}
			return green > 5
		}) {
			finalErr = fmt.Errorf("ANSI green not rendered inside native terminal viewport")
			return
		}
		result["terminalGreenPixels"] = green

		fyne.DoAndWait(func() { ui.Window.Resize(fyne.NewSize(1200, 760)) })
		time.Sleep(500 * time.Millisecond)
		_, sizes := server.Snapshot()
		result["resizeEvents"] = len(sizes)
		result["authentications"] = server.Auth.Load()
		result["sftpChannels"] = server.SFTP.Load()
		if server.Auth.Load() != 1 || server.SFTP.Load() != 1 || len(sizes) < 2 {
			finalErr = fmt.Errorf("transport reuse or resize failed")
			return
		}
		fyne.DoAndWait(func() { work.MonitorToggle.SetChecked(true); work.Monitor.Read() })
		if !wait(func() bool { return strings.Contains(work.Monitor.Text(), "MemTotal:") }) {
			finalErr = fmt.Errorf("monitor data missing")
			return
		}
		result["monitorExec"] = true
		fyne.DoAndWait(func() { work.Monitor.SetAutomatic(true) })
		if !wait(func() bool { return server.Exec.Load() >= 2 }) {
			finalErr = fmt.Errorf("automatic monitor did not run")
			return
		}
		fyne.DoAndWait(func() { work.MonitorToggle.SetChecked(false) })
		hiddenCount := server.Exec.Load()
		time.Sleep(10300 * time.Millisecond)
		if server.Exec.Load() != hiddenCount {
			finalErr = fmt.Errorf("hidden monitor kept polling")
			return
		}
		result["hiddenMonitorPaused"] = true
		fyne.DoAndWait(func() { work.MonitorToggle.SetChecked(true) })
		if !wait(func() bool { return server.Exec.Load() > hiddenCount }) {
			finalErr = fmt.Errorf("monitor did not resume")
			return
		}
		result["monitorResumed"] = true
		code := "\ufeffpackage main\r\n\r\nfunc main() {\r\n  // 中文与尾空格  \r\n  println(\"Forterm Native\")\r\n}\r\n"
		if _, e = c.CreateText(ctx, "/sample.go", code); e != nil {
			finalErr = e
			return
		}
		fyne.DoAndWait(func() { work.OpenText("/sample.go") })
		if !wait(func() bool { return work.ActiveDocument() != nil }) {
			finalErr = fmt.Errorf("native editor did not open")
			return
		}
		fyne.DoAndWait(func() {
			doc := work.ActiveDocument()
			if doc.Editor.Document.Serialize() != code || doc.Source != c {
				finalErr = fmt.Errorf("editor source or BOM/CRLF changed")
				return
			}
			doc.Editor.Document.SetPosition(3, 2, false)
			ui.Window.Canvas().Focus(doc.Editor)
			ui.Window.Canvas().Focused().TypedRune('X')
			if !doc.Editor.Document.Dirty() {
				finalErr = fmt.Errorf("editor dirty tracking missing")
				return
			}
			doc.Editor.Document.Undo()
			if doc.Editor.Document.Serialize() != code {
				finalErr = fmt.Errorf("editor undo changed bytes")
				return
			}
			doc.Editor.TypedRune('Y')
		})
		if finalErr != nil {
			return
		}
		fyne.DoAndWait(func() {
			work.SaveDocument(work.ActiveDocument())
			if !clickDialog(ui.Window, false) {
				finalErr = fmt.Errorf("save cancel button unreachable: %s (closed=%v, mixed=%v)", work.ActiveDocument().Label.Text, c.Closed(), work.ActiveDocument().Editor.Document.Format.Mixed)
			}
		})
		if finalErr != nil {
			return
		}
		unchanged, e := c.ReadText(ctx, "/sample.go")
		if e != nil || unchanged.Text != code {
			finalErr = fmt.Errorf("save cancel changed remote")
			return
		}
		fyne.DoAndWait(func() {
			work.SaveDocument(work.ActiveDocument())
			if !clickDialog(ui.Window, true) {
				finalErr = fmt.Errorf("save confirm button unreachable")
			}
		})
		if finalErr != nil {
			return
		}
		if !wait(func() bool { d := work.ActiveDocument(); return !d.Busy && !d.Editor.Document.Dirty() }) {
			finalErr = fmt.Errorf("confirmed save did not finish")
			return
		}
		persisted, e := c.ReadText(ctx, "/sample.go")
		if e != nil || !strings.Contains(persisted.Text, "Y// 中文") {
			finalErr = fmt.Errorf("confirmed editor save lost changes")
			return
		}
		result["editorSaveCancelConfirm"] = true
		fyne.DoAndWait(func() {
			d := work.ActiveDocument()
			d.Editor.TypedRune('Z')
			work.RequestClose(func() { finalErr = fmt.Errorf("dirty close bypassed confirmation") })
			if !clickDialog(ui.Window, false) {
				finalErr = fmt.Errorf("dirty close cancel unreachable")
			}
		})
		if finalErr != nil {
			return
		}
		result["dirtyCloseCancelPreservesWorkspace"] = true
		batch := make([]string, 25)
		for i := range batch {
			batch[i] = filepath.Join(local, fmt.Sprintf("batch-%02d.bin", i))
			data := []byte("native batch item")
			if i == 0 {
				data = make([]byte, 4<<20)
			}
			if e = os.WriteFile(batch[i], data, 0600); e != nil {
				finalErr = e
				return
			}
		}
		fyne.DoAndWait(func() { work.EnqueueUploads(batch[:21], "/"); work.EnqueueUploads(batch[21:], "/") })
		if !wait(func() bool { items := work.Transfers.Snapshot(); return len(items) == 25 && items[24].State == "done" }) {
			finalErr = fmt.Errorf("20+ batch/appended native queue did not finish")
			return
		}
		result["streamedLargeAnd25QueuedAppended"] = true
		result["nativeCodeEditor"] = true
		result["editorBOMCRLFUndoSourceBound"] = true
		focusMarker := "AFTER_FOCUS_" + suffix
		focusCommand := fmt.Sprintf("printf 'AFTER_%%s_%%s\\n' 'FOCUS' '%s'", suffix)
		fyne.DoAndWait(func() {
			editor := work.ActiveDocument().Editor
			ui.Window.Canvas().Focus(work.Path)
			ui.Window.Canvas().Focused().TypedRune('x')
			work.Path.SetText("/")
			ui.Window.Canvas().Focus(editor)
			work.Monitor.Read()
			if ui.Window.Canvas().Focused() != editor {
				finalErr = fmt.Errorf("monitor stole editor focus")
				return
			}
			ui.Window.Canvas().Focused().TypedRune('Q')
			ui.Window.Canvas().Focus(work.Term)
			for _, r := range focusCommand {
				ui.Window.Canvas().Focused().TypedRune(r)
			}
			ui.Window.Canvas().Focused().TypedKey(&fyne.KeyEvent{Name: fyne.KeyReturn})
		})
		if finalErr != nil {
			return
		}
		if !wait(func() bool { return strings.Contains(work.Term.Text(), focusMarker) }) {
			finalErr = fmt.Errorf("terminal execution after editor/files/monitor focus transitions failed")
			return
		}
		expected := []byte(command + focusCommand + "\r")
		actual, _ := server.Snapshot()
		if !bytes.Equal(actual, expected) {
			finalErr = fmt.Errorf("editor/file input leaked or terminal input altered: got %d expected %d bytes; content not logged", len(actual), len(expected))
			return
		}
		result["focusTransitionExactInput"] = true
		result["secondNonEchoExecutionMarker"] = focusMarker
		// Wait for the default protocol keepalive; it must not inject shell input.
		before, _ := server.Snapshot()
		deadline := time.Now().Add(18 * time.Second)
		for server.Keepalive.Load() == 0 && time.Now().Before(deadline) {
			time.Sleep(100 * time.Millisecond)
		}
		after, _ := server.Snapshot()
		result["keepaliveRequests"] = server.Keepalive.Load()
		result["noHeartbeatInput"] = string(before) == string(after)
		if server.Keepalive.Load() == 0 || string(before) != string(after) {
			finalErr = fmt.Errorf("keepalive missing or injected terminal input")
			return
		}
		if os.Getenv("FORTERM_MULTI_SOURCE") == "1" {
			serverB, e := testserver.New()
			if e != nil {
				finalErr = e
				return
			}
			defer serverB.Close()
			connB, e := sshconn.Dial(ctx, launch.Connection{Protocol: "sftp", Host: "127.0.0.1", Port: serverB.Port, Username: "native-user", Password: "native-test-password"}, sshconn.Callbacks{HostKey: func(_ context.Context, _ string, fp string) (bool, error) { return fp == serverB.Fingerprint, nil }})
			if e != nil {
				finalErr = e
				return
			}
			defer connB.Close()
			codeB := "package main\n\nfunc main() {\n  // 日志来源 B：独立草稿与连接\n  println(\"source B\")\n}\n"
			if _, e = connB.CreateText(ctx, "/sample.go", codeB); e != nil {
				finalErr = e
				return
			}
			var sourceB *desktop.Workspace
			fyne.DoAndWait(func() {
				sourceB = desktop.NewWorkspace(ui, "日志 B", "sftp")
				sourceB.SetConnection(connB)
				ui.Tabs.Append(sourceB.Tab)
				if e := work.AttachFileSource(sourceB.ID); e != nil {
					finalErr = e
					return
				}
				work.ActiveFileView().Refresh()
				work.ActiveFileView().OpenText("/sample.go")
				ui.Window.Resize(fyne.NewSize(1440, 960))
			})
			if finalErr != nil {
				return
			}
			if !wait(func() bool {
				return work.ActiveDocument() != nil && work.ActiveDocument().SourceID == sourceB.ID && work.ActiveFileView().Files.Length() > 0
			}) {
				finalErr = fmt.Errorf("multi-source native view/editor did not become ready")
				return
			}
			fyne.DoAndWait(func() {
				if work.ActiveDocument().Source != connB {
					finalErr = fmt.Errorf("source B editor misbound to terminal transport")
					return
				}
				for _, tab := range ui.Tabs.Items {
					if tab == sourceB.Tab {
						finalErr = fmt.Errorf("referenced SFTP tab still duplicated at top")
						return
					}
				}
				work.ActiveDocument().Editor.Document.SetPosition(3, 2, false)
				work.ActiveDocument().Editor.TypedRune('X')
			})
			if finalErr != nil {
				return
			}
			result["multiSourceSeparateSameNameDrafts"] = true
			result["referencedFileTopTabHidden"] = true
			result["sourceBAuthentications"] = serverB.Auth.Load()
		}
		if os.Getenv("FORTERM_DOCK_CYCLE") == "1" {
			var editorBefore *codeedit.Editor
			var textBefore string
			var versionBefore uint64
			var viewBefore map[string]int
			fyne.DoAndWait(func() {
				editorBefore = work.ActiveDocument().Editor
				textBefore = editorBefore.Document.Serialize()
				versionBefore = editorBefore.Document.Version
				viewBefore = editorBefore.ViewState()
			})
			for _, dock := range []string{"left", "right", "bottom"} {
				fyne.DoAndWait(func() { work.SetEditorDock(dock) })
				time.Sleep(50 * time.Millisecond)
				fyne.DoAndWait(func() {
					if work.ActiveDocument().Editor != editorBefore || editorBefore.Document.Serialize() != textBefore || editorBefore.Document.Version != versionBefore || editorBefore.ViewState()["caret"] != viewBefore["caret"] {
						finalErr = fmt.Errorf("editor dock change replaced/modified draft or caret")
					}
				})
				if finalErr != nil {
					return
				}
			}
			fyne.DoAndWait(func() { work.SetEditorDock("left"); ui.Window.Resize(fyne.NewSize(740, 600)) })
			time.Sleep(100 * time.Millisecond)
			fyne.DoAndWait(func() {
				if work.EffectiveEditorDock() != "bottom" || work.LayoutSnapshot()["editorDock"] != "left" {
					finalErr = fmt.Errorf("narrow fallback overwrote left preference")
				}
			})
			if finalErr != nil {
				return
			}
			fyne.DoAndWait(func() { ui.Window.Resize(fyne.NewSize(1600, 960)) })
			time.Sleep(100 * time.Millisecond)
			fyne.DoAndWait(func() {
				if work.EffectiveEditorDock() != "left" {
					finalErr = fmt.Errorf("left dock did not restore after widen")
				}
				work.SetEditorDock("right")
			})
			if finalErr != nil {
				return
			}
			result["editorLeftRightBottomSameDraftCaretHistory"] = true
		}
		if size := os.Getenv("FORTERM_WINDOW_SIZE"); size != "" {
			parts := strings.Split(size, "x")
			if len(parts) == 2 {
				width, _ := strconv.Atoi(parts[0])
				height, _ := strconv.Atoi(parts[1])
				if width >= 600 && height >= 400 {
					fyne.DoAndWait(func() { ui.Window.Resize(fyne.NewSize(float32(width), float32(height))) })
					time.Sleep(200 * time.Millisecond)
					result["requestedViewport"] = size
				}
			}
		}
		fyne.DoAndWait(func() {
			ui.SetStatus("本机合成连接 · 原生开发预览")
			img := ui.Window.Canvas().Capture()
			f, e := os.Create(filepath.Join(dir, "native-live-window.png"))
			if e == nil {
				e = png.Encode(f, img)
				f.Close()
			}
			if e != nil {
				finalErr = e
			}
		})
		if os.Getenv("FORTERM_PREVIEW_HOLD") == "1" {
			seconds := 150
			if value, err := strconv.Atoi(os.Getenv("FORTERM_PREVIEW_SECONDS")); err == nil && value > 0 && value <= 900 {
				seconds = value
			}
			deadline := time.Now().Add(time.Duration(seconds) * time.Second)
			for time.Now().Before(deadline) {
				fyne.DoAndWait(func() {
					state := work.LayoutSnapshot()
					state["selectedSource"] = work.ActiveFileView().ID
					state["fileCount"] = work.ActiveFileView().Files.Length()
					if doc := work.ActiveDocument(); doc != nil {
						state["editorText"] = doc.Editor.Document.Serialize()
					}
					data, _ := json.MarshalIndent(state, "", "  ")
					_ = os.WriteFile(filepath.Join(dir, "live-layout.json"), data, 0600)
				})
				time.Sleep(200 * time.Millisecond)
			}
		}
		result["passed"] = finalErr == nil
	}()
	ui.Window.ShowAndRun()
	if finalErr != nil {
		fmt.Fprintln(os.Stderr, finalErr)
		os.Exit(1)
	}
}

// Drives the actual native confirmation widgets, only in this explicitly named
// synthetic smoke binary. Production code never receives an auto-approval hook.
func clickDialog(window fyne.Window, approve bool) bool {
	top := window.Canvas().Overlays().Top()
	fmt.Printf("confirmation top=%T approve=%v\n", top, approve)
	if top == nil {
		return false
	}
	var found *widget.Button
	visited := map[fyne.CanvasObject]bool{}
	var walk func(fyne.CanvasObject, int)
	walk = func(obj fyne.CanvasObject, depth int) {
		if obj == nil || depth > 24 || visited[obj] || found != nil {
			return
		}
		visited[obj] = true
		if b, ok := obj.(*widget.Button); ok {
			title := strings.ToLower(b.Text)
			fmt.Printf("confirmation button=%q\n", title)
			positive := title == "confirm" || title == "确认" || title == "ok" || title == "确定" || title == "yes" || title == "是"
			negative := title == "cancel" || title == "取消" || title == "no" || title == "否" || title == "不"
			if approve && positive || !approve && negative {
				found = b
			}
			return
		}
		if c, ok := obj.(*fyne.Container); ok {
			for _, child := range c.Objects {
				walk(child, depth+1)
			}
		}
		if p, ok := obj.(*widget.PopUp); ok {
			walk(p.Content, depth+1)
			return
		}
		if w, ok := obj.(fyne.Widget); ok {
			for _, child := range ftest.WidgetRenderer(w).Objects() {
				walk(child, depth+1)
			}
		}
	}
	walk(top, 0)
	if found == nil {
		return false
	}
	ftest.Tap(found)
	return true
}
