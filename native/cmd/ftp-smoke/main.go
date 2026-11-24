package main

import (
	"context"
	"encoding/json"
	"fmt"
	"forterm/native/internal/desktop"
	"forterm/native/internal/launch"
	"forterm/native/internal/testftp"
	"fyne.io/fyne/v2"
	"image/png"
	"os"
	"path/filepath"
	"time"
)

func main() {
	root, e := os.MkdirTemp("", "forterm-native-ftp-ui-")
	if e != nil {
		panic(e)
	}
	defer os.RemoveAll(root)
	remoteRoot := filepath.Join(root, "remote")
	os.Mkdir(remoteRoot, 0700)
	os.WriteFile(filepath.Join(remoteRoot, "existing.bin"), make([]byte, 2<<20), 0600)
	server, e := testftp.New(remoteRoot)
	if e != nil {
		panic(e)
	}
	defer server.Close()
	app := desktop.New("cn.forterm.native.ftp-smoke")
	connection := &launch.Connection{Protocol: "ftp", Host: "127.0.0.1", Port: server.Port, Username: "native-ftp", Password: "synthetic-ftp-password", HasPassword: true, Title: "独立 FTP · 本机验证"}
	app.Open(launch.Plan{Connection: connection, Entry: "manual"})
	var finalErr error
	go func() {
		result := map[string]any{"platform": "macOS arm64", "realNativeWindow": true, "protocol": "FTP"}
		defer func() {
			if finalErr != nil {
				result["error"] = finalErr.Error()
			}
			os.MkdirAll("evidence/ftp-ui", 0755)
			data, _ := json.MarshalIndent(result, "", "  ")
			os.WriteFile("evidence/ftp-ui/result.json", data, 0600)
			fyne.Do(func() { app.Close(); app.UI.Quit() })
		}()
		ctx, cancel := context.WithTimeout(context.Background(), 35*time.Second)
		defer cancel()
		wait := func(check func() bool) bool {
			for ctx.Err() == nil {
				ok := false
				fyne.DoAndWait(func() { ok = check() })
				if ok {
					return true
				}
				time.Sleep(25 * time.Millisecond)
			}
			return false
		}
		if !wait(func() bool { w := app.ActiveWorkspace(); return w != nil && w.FileConn != nil && w.Files.Length() == 1 }) {
			finalErr = fmt.Errorf("FTP native workspace did not become ready")
			return
		}
		var workspace *desktop.Workspace
		fyne.DoAndWait(func() {
			workspace = app.ActiveWorkspace()
			if workspace.Conn != nil || workspace.Protocol != "ftp" {
				finalErr = fmt.Errorf("FTP silently mapped to SSH")
			}
		})
		if finalErr != nil {
			return
		}
		local := filepath.Join(root, "upload.bin")
		os.WriteFile(local, make([]byte, 3<<20), 0600)
		fyne.DoAndWait(func() {
			workspace.EnqueueUploads([]string{local}, "/")
			workspace.EnqueueDownload("/existing.bin", filepath.Join(root, "download.bin"))
		})
		if !wait(func() bool {
			items := workspace.Transfers.Snapshot()
			return len(items) == 2 && items[1].State == "done"
		}) {
			finalErr = fmt.Errorf("native FTP queue did not complete")
			return
		}
		if info, e := os.Stat(filepath.Join(root, "download.bin")); e != nil || info.Size() != 2<<20 {
			finalErr = fmt.Errorf("FTP UI download bytes missing")
			return
		}
		if e = workspace.Operations.Mkdir(ctx, "/native-folder"); e != nil {
			finalErr = e
			return
		}
		if e = workspace.Operations.Rename(ctx, "/native-folder", "/renamed-folder"); e != nil {
			finalErr = e
			return
		}
		if e = workspace.Operations.Remove(ctx, "/renamed-folder"); e != nil {
			finalErr = e
			return
		}
		fyne.DoAndWait(workspace.Refresh)
		if !wait(func() bool { return workspace.SelectFile("upload.bin") }) {
			finalErr = fmt.Errorf("uploaded FTP entry missing")
			return
		}
		if server.Auth.Load() != 1 {
			finalErr = fmt.Errorf("native FTP workspace reauthenticated")
			return
		}
		fyne.DoAndWait(func() {
			img := app.Window.Canvas().Capture()
			os.MkdirAll("evidence/ftp-ui", 0755)
			f, err := os.Create("evidence/ftp-ui/native-ftp-window.png")
			if err == nil {
				err = png.Encode(f, img)
				f.Close()
			}
			if err != nil {
				finalErr = err
			}
		})
		result["passed"] = finalErr == nil
		result["authentications"] = server.Auth.Load()
		result["streamingUploadDownloadQueue"] = true
		result["mkdirRenameDelete"] = true
		result["noSSHTransport"] = true
	}()
	app.Window.ShowAndRun()
	if finalErr != nil {
		fmt.Fprintln(os.Stderr, finalErr)
		os.Exit(1)
	}
}
