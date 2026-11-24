// Actual picker/cancel/confirm/upload probe against synthetic loopback only.
package main

import (
	"context"
	"encoding/json"
	"fmt"
	"forterm/native/internal/desktop"
	"forterm/native/internal/launch"
	"forterm/native/internal/sshconn"
	"forterm/native/internal/testserver"
	"fyne.io/fyne/v2"
	"fyne.io/fyne/v2/storage"
	"os"
	"path/filepath"
	"strings"
	"time"
)

func main() {
	dir := os.Getenv("FORTERM_EVIDENCE")
	if dir == "" {
		dir = "/Users/xianyu/IdeaProjects/forterm/native/evidence/performance-0.2.2/picker-upload"
	}
	dir, _ = filepath.Abs(dir)
	dir = filepath.Join(dir, fmt.Sprintf("run-%d", os.Getpid()))
	if e := os.MkdirAll(dir, 0700); e != nil {
		panic(e)
	}
	server, e := testserver.New()
	if e != nil {
		panic(e)
	}
	defer server.Close()
	server.RealPathStatus.Store(3)
	local, e := os.MkdirTemp("", "forterm-upload-fixture-")
	if e != nil {
		panic(e)
	}
	defer os.RemoveAll(local)
	filename := "中文 空格%20.txt"
	payload := "synthetic picker upload 中文\n"
	os.WriteFile(filepath.Join(local, filename), []byte(payload), 0600)
	runID := fmt.Sprintf("%d", os.Getpid())
	app := desktop.New("cn.forterm.native.upload-picker.smoke." + runID)
	app.Window.SetTitle("Forterm Upload QA · " + runID)
	w := desktop.NewWorkspace(app, "合成上传来源", "sftp")
	app.Tabs.Append(w.Tab)
	app.Tabs.Select(w.Tab)
	app.Window.Resize(fyne.NewSize(1050, 720))
	go func() {
		result := map[string]any{"pid": os.Getpid(), "platform": "macOS arm64", "syntheticLoopbackOnly": true, "filename": filename}
		var failure error
		defer func() {
			if failure != nil {
				result["error"] = failure.Error()
			}
			result["passed"] = failure == nil
			b, _ := json.MarshalIndent(result, "", "  ")
			if err := os.WriteFile(filepath.Join(dir, "result.json"), b, 0600); err != nil {
				fmt.Fprintln(os.Stderr, "synthetic result write failed", err)
			}
			fyne.Do(func() { w.Close(); app.UI.Quit() })
		}()
		ctx, cancel := context.WithTimeout(context.Background(), 4*time.Minute)
		defer cancel()
		conn, e := sshconn.Dial(ctx, launch.Connection{Protocol: "sftp", Host: "127.0.0.1", Port: server.Port, Username: "native-user", Password: "native-test-password"}, sshconn.Callbacks{HostKey: func(_ context.Context, _ string, fp string) (bool, error) { return fp == server.Fingerprint, nil }})
		if e != nil {
			failure = e
			return
		}
		defer conn.Close()
		if e = conn.Mutate(ctx, "mkdir", "/uploads", "", 0700); e != nil {
			failure = e
			return
		}
		wait := func(fn func() bool) bool {
			for ctx.Err() == nil {
				ok := false
				fyne.DoAndWait(func() { ok = fn() })
				if ok {
					return true
				}
				time.Sleep(40 * time.Millisecond)
			}
			return false
		}
		fyne.DoAndWait(func() { w.SetConnection(conn); w.Path.SetText("/uploads"); w.Refresh() })
		if !wait(func() bool { return strings.Contains(w.Status.Text, "REALPATH") }) {
			failure = fmt.Errorf("REALPATH failure not visible")
			return
		}
		result["realpathDenialVisible"] = true
		ready, _ := json.Marshal(map[string]any{"pid": os.Getpid(), "reportDirectory": dir})
		os.WriteFile(filepath.Join(dir, "ready.json"), ready, 0600)
		fmt.Printf("UI_READY pid=%d report=%s\n", os.Getpid(), dir)
		fyne.DoAndWait(func() {
			app.SetStatus("步骤1：确认直接读取合成绝对目录（无写入）")
			w.ConfirmExactDirectory()
		})
		if !wait(func() bool { return w.LayoutSnapshot()["directoryReady"] == true }) {
			failure = fmt.Errorf("explicit directory confirmation did not complete")
			return
		}
		result["explicitConfirmedDirectoryWithoutRealpath"] = true
		uri, e := storage.ListerForURI(storage.NewFileURI(local))
		if e != nil {
			failure = e
			return
		}
		fyne.DoAndWait(func() {
			app.SetStatus("步骤2：请在文件选择框点取消，验证不会上传")
			w.UploadAt(uri)
		})
		if !wait(func() bool { return app.Window.Canvas().Overlays().Top() == nil }) {
			failure = fmt.Errorf("first picker did not close")
			return
		}
		if len(w.Transfers.Snapshot()) != 0 {
			failure = fmt.Errorf("cancel picker unexpectedly queued a file")
			return
		}
		result["pickerCancelQueueEmpty"] = true
		fyne.DoAndWait(func() { app.SetStatus("步骤3：选中 中文 空格%20.txt，点击打开"); w.UploadAt(uri) })
		if !wait(func() bool {
			items := w.Transfers.Snapshot()
			return len(items) > 0 && (items[0].State == "done" || items[0].State == "error")
		}) {
			failure = fmt.Errorf("selection did not produce queue result")
			return
		}
		items := w.Transfers.Snapshot()
		if items[0].State != "done" {
			failure = fmt.Errorf("queued upload failed: %s", items[0].Message)
			return
		}
		saved, e := conn.ReadText(ctx, "/uploads/"+filename)
		if e != nil || saved.Text != payload {
			failure = fmt.Errorf("uploaded bytes/destination mismatch: %v", e)
			return
		}
		result["selectedFileQueuedAndUploadedExactBytes"] = true
		result["authentications"] = server.Auth.Load()
		result["sourceBoundRemotePath"] = items[0].Remote
		result["passed"] = true
		proof, _ := json.MarshalIndent(result, "", "  ")
		os.WriteFile(filepath.Join(dir, "upload-committed.json"), proof, 0600)
		fyne.DoAndWait(func() { app.SetStatus("合成测试上传成功；保留截图窗口15秒") })
		time.Sleep(5 * time.Second)
	}()
	app.Window.ShowAndRun()
}
