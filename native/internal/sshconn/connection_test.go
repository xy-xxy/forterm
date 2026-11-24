package sshconn

import (
	"context"
	"forterm/native/internal/launch"
	"forterm/native/internal/testserver"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"
	"time"
)

func TestSharedNativeSSHFiles(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("fixture starts Unix shell")
	}
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	conn, e := Dial(ctx, launch.Connection{Protocol: "ssh", Host: "127.0.0.1", Port: server.Port, Username: "native-user", Password: "native-test-password"}, Callbacks{HostKey: func(_ context.Context, _ string, fp string) (bool, error) { return fp == server.Fingerprint, nil }})
	if e != nil {
		t.Fatal(e)
	}
	defer conn.Close()
	shell, e := conn.Shell(24, 80)
	if e != nil {
		t.Fatal(e)
	}
	defer shell.Close()
	go func() { _, _ = shell.In.Write([]byte("printf 'NATIVE_SHELL_OK\\n'\n")) }()
	result := make(chan string, 1)
	go func() {
		buf := make([]byte, 4096)
		var text string
		for {
			n, e := shell.Out.Read(buf)
			text += string(buf[:n])
			if strings.Contains(text, "NATIVE_SHELL_OK\r\n") {
				result <- text
				return
			}
			if e != nil {
				return
			}
		}
	}()
	select {
	case <-result:
	case <-ctx.Done():
		t.Fatal("real shell output missing")
	}
	if e = shell.Resize(33, 101); e != nil {
		t.Fatal(e)
	}
	dir := t.TempDir()
	source := filepath.Join(dir, "small.txt")
	payload := []byte("native 中文 file\n")
	os.WriteFile(source, payload, 0600)
	if e = conn.Upload(ctx, source, "/small.txt", false); e != nil {
		t.Fatal(e)
	}
	if e = conn.Upload(ctx, source, "/small.txt", false); e == nil {
		t.Fatal("exclusive upload overwrote")
	}
	entries, e := conn.List(ctx, "/")
	if e != nil || len(entries) != 1 || entries[0].Name != "small.txt" {
		t.Fatal("directory mismatch", e)
	}
	dest := filepath.Join(dir, "download.txt")
	if e = conn.Download(ctx, "/small.txt", dest, false); e != nil {
		t.Fatal(e)
	}
	data, _ := os.ReadFile(dest)
	if string(data) != string(payload) {
		t.Fatal("download bytes differ")
	}
	os.WriteFile(dest, []byte("keep"), 0600)
	if e = conn.Download(ctx, "/small.txt", dest, false); e == nil {
		t.Fatal("download overwrote without approval")
	}
	data, _ = os.ReadFile(dest)
	if string(data) != "keep" {
		t.Fatal("target lost")
	}
	if server.Auth.Load() != 1 || server.SFTP.Load() != 1 {
		t.Fatal("did not reuse authenticated transport")
	}
	_, sizes := server.Snapshot()
	if len(sizes) == 0 {
		t.Fatal("no resize")
	}
}
func TestSFTPDenialPreservesSSH(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("Unix fixture")
	}
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	server.DenySFTP = true
	defer server.Close()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	conn, e := Dial(ctx, launch.Connection{Protocol: "ssh", Host: "127.0.0.1", Port: server.Port, Username: "native-user", Password: "native-test-password"}, Callbacks{HostKey: func(context.Context, string, string) (bool, error) { return true, nil }})
	if e != nil {
		t.Fatal(e)
	}
	defer conn.Close()
	if _, e = conn.List(ctx, "/"); e == nil {
		t.Fatal("SFTP denied")
	}
	shell, e := conn.Shell(24, 80)
	if e != nil {
		t.Fatal("terminal affected", e)
	}
	shell.Close()
	if server.Auth.Load() != 1 {
		t.Fatal("re-authenticated")
	}
}

func TestShellDenialPreservesFiles(t *testing.T) {
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	server.DenyShell = true
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	conn, e := Dial(ctx, launch.Connection{Protocol: "sftp", Host: "127.0.0.1", Port: server.Port, Username: "native-user", Password: "native-test-password"}, Callbacks{HostKey: func(_ context.Context, _ string, fp string) (bool, error) { return fp == server.Fingerprint, nil }})
	if e != nil {
		t.Fatal(e)
	}
	defer conn.Close()
	if _, e = conn.Shell(24, 80); e == nil {
		t.Fatal("shell must be denied")
	}
	if _, e = conn.List(ctx, "/"); e != nil {
		t.Fatal("file channel affected by shell denial", e)
	}
	if server.Auth.Load() != 1 || server.SFTP.Load() != 1 {
		t.Fatal("permission fallback reauthenticated")
	}
}

func TestOrdinaryTransferNoLongerHasTextSizeLimit(t *testing.T) {
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	conn, e := Dial(ctx, launch.Connection{Protocol: "sftp", Host: "127.0.0.1", Port: server.Port, Username: "native-user", Password: "native-test-password"}, Callbacks{HostKey: func(_ context.Context, _ string, fp string) (bool, error) { return fp == server.Fingerprint, nil }})
	if e != nil {
		t.Fatal(e)
	}
	defer conn.Close()
	file := filepath.Join(t.TempDir(), "large")
	if e = os.WriteFile(file, make([]byte, (1<<20)+1), 0600); e != nil {
		t.Fatal(e)
	}
	if e = conn.Upload(ctx, file, "/large-file", false); e != nil {
		t.Fatal("ordinary transfer incorrectly retained text limit", e)
	}
	entries, e := conn.List(ctx, "/")
	if e != nil || len(entries) != 1 {
		t.Fatal("large-file transfer missing", e)
	}
}
