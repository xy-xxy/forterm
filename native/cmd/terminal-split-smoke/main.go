package main

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"forterm/native/internal/desktop"
	"forterm/native/internal/launch"
	"forterm/native/internal/sshconn"
	"forterm/native/internal/testserver"
	"fyne.io/fyne/v2"
	"image/png"
	"os"
	"strings"
	"time"
)

func main() {
	serverA, e := testserver.New()
	if e != nil {
		panic(e)
	}
	defer serverA.Close()
	serverB, e := testserver.New()
	if e != nil {
		panic(e)
	}
	defer serverB.Close()
	app := desktop.New("cn.forterm.native.terminal-split-smoke")
	first := desktop.NewWorkspace(app, "终端 A", "ssh")
	second := desktop.NewWorkspace(app, "终端 B", "ssh")
	app.Tabs.Append(first.Tab)
	app.Tabs.Append(second.Tab)
	app.Tabs.Select(first.Tab)
	var failure error
	go func() {
		result := map[string]any{"platform": "macOS arm64", "syntheticLoopbackOnly": true}
		defer func() {
			if failure != nil {
				result["error"] = failure.Error()
			}
			os.MkdirAll("evidence/terminal-split", 0755)
			data, _ := json.MarshalIndent(result, "", "  ")
			os.WriteFile("evidence/terminal-split/result.json", data, 0600)
			fyne.Do(func() { app.Close(); app.UI.Quit() })
		}()
		ctx, cancel := context.WithTimeout(context.Background(), 35*time.Second)
		defer cancel()
		dial := func(server *testserver.Server) (*sshconn.Connection, error) {
			return sshconn.Dial(ctx, launch.Connection{Protocol: "ssh", Host: "127.0.0.1", Port: server.Port, Username: "native-user", Password: "native-test-password"}, sshconn.Callbacks{HostKey: func(_ context.Context, _ string, fingerprint string) (bool, error) {
				return fingerprint == server.Fingerprint, nil
			}})
		}
		a, e := dial(serverA)
		if e != nil {
			failure = e
			return
		}
		defer a.Close()
		b, e := dial(serverB)
		if e != nil {
			failure = e
			return
		}
		defer b.Close()
		fyne.DoAndWait(func() { first.SetConnection(a); second.SetConnection(b); first.StartShell(); second.StartShell() })
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
		if !wait(func() bool { return first.Term.Connected() && second.Term.Connected() }) {
			failure = fmt.Errorf("dual shells did not become ready")
			return
		}
		commandA := "printf 'FIRST_%s_%s\\n' 'EXEC' 'DONE'\n"
		commandB := "printf 'SECOND_%s_%s\\n' 'EXEC' 'DONE'\n"
		fyne.DoAndWait(func() {
			first.SetTerminalSplit(second, true)
			first.Term.Write([]byte(commandA))
			app.Window.Canvas().Focus(second.Term)
			second.Term.Write([]byte(commandB))
		})
		if !wait(func() bool {
			return bytes.Contains(serverA.SnapshotOutput(), []byte("FIRST_EXEC_DONE\r\n")) && bytes.Contains(serverB.SnapshotOutput(), []byte("SECOND_EXEC_DONE\r\n"))
		}) {
			failure = fmt.Errorf("both split shells did not execute")
			return
		}
		inputA, _ := serverA.Snapshot()
		inputB, _ := serverB.Snapshot()
		if !bytes.Equal(inputA, []byte(commandA)) || !bytes.Equal(inputB, []byte(commandB)) {
			failure = fmt.Errorf("split input crossed sessions")
			return
		}
		fyne.DoAndWait(func() { first.SetTerminalSplit(second, false) })
		if !wait(func() bool { return first.Term.Size().Height > 0 && second.Term.Size().Height > 0 }) {
			failure = fmt.Errorf("vertical terminal split not laid out")
			return
		}
		fyne.DoAndWait(func() { first.SetTerminalSplit(second, true) })
		capture := func(name string) {
			fyne.DoAndWait(func() {
				os.MkdirAll("evidence/terminal-split", 0755)
				f, e := os.Create("evidence/terminal-split/" + name)
				if e == nil {
					e = png.Encode(f, app.Window.Canvas().Capture())
					f.Close()
				}
				if e != nil {
					failure = e
				}
			})
		}
		capture("dual-terminals.png")
		result["twoActualShellsInputIsolated"] = true
		result["bothDirectionsRetainOutput"] = true
		fyne.DoAndWait(func() { app.CloseWorkspace(second) })
		if a.Closed() {
			failure = fmt.Errorf("closing split peer closed first transport")
			return
		}
		fyne.DoAndWait(func() { first.SetTerminalSplit(nil, true) })
		// Real VT color output, not a picture. True-color and OSC4 are included so
		// palette readability fixes cannot silently recolor application overrides.
		board := "printf '\\033[2J\\033[HANSI 16 / APPLICATION COLORS\\n'\n"
		names := []string{"black", "red", "green", "yellow", "blue", "magenta", "cyan", "white", "bright black", "bright red", "bright green", "bright yellow", "bright blue", "bright magenta", "bright cyan", "bright white"}
		for i, name := range names {
			code := 30 + i
			if i >= 8 {
				code = 90 + i - 8
			}
			if i == 0 {
				board += fmt.Sprintf("printf '\\033[48;2;250;252;255m\\033[%dm %02d %-13s \\033[0m'\n", code, i, name)
			} else {
				board += fmt.Sprintf("printf '\\033[%dm %02d %-13s \\033[0m'\n", code, i, name)
			}
			if i%4 == 3 {
				board += "printf '\\n'\n"
			}
		}
		board += "printf '\\n\\033[38;2;63;143;191mTRUE RGB #3f8fbf\\033[0m\\nCOLOR_%s_%s\\n' 'EXEC' 'DONE'\n"
		board = strings.Join(strings.Split(strings.TrimSpace(board), "\n"), "; ") + "\n"
		fyne.DoAndWait(func() { first.Term.Write([]byte(board)) })
		if !wait(func() bool { return bytes.Contains(serverA.SnapshotOutput(), []byte("COLOR_EXEC_DONE\r\n")) }) {
			failure = fmt.Errorf("color board shell execution missing")
			return
		}
		time.Sleep(150 * time.Millisecond)
		fyne.DoAndWait(func() { app.SetAppearance("dark") })
		time.Sleep(150 * time.Millisecond)
		capture("ansi-dark.png")
		fyne.DoAndWait(func() { app.SetAppearance("light") })
		time.Sleep(200 * time.Millisecond)
		capture("ansi-light.png")
		if serverA.Auth.Load() != 1 || serverB.Auth.Load() != 1 {
			failure = fmt.Errorf("theme/split rebuilt authentication")
			return
		}
		result["colorBoardActuallyExecuted"] = true
		result["themeKeepsOutputAndConnections"] = true
		result["peerClosePreservesFirst"] = true
		result["passed"] = failure == nil
	}()
	app.Window.ShowAndRun()
	if failure != nil {
		fmt.Fprintln(os.Stderr, failure)
		os.Exit(1)
	}
}
