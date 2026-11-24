package desktop

import (
	"context"
	"errors"
	"forterm/native/internal/launch"
	"forterm/native/internal/sshconn"
	"forterm/native/internal/testserver"
	"strings"
	"testing"
	"time"
)

func TestRealSFTPRealpathRestrictionAndTimeoutPreserveTransport(t *testing.T) {
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	conn, e := sshconn.Dial(ctx, launch.Connection{Protocol: "sftp", Host: "127.0.0.1", Port: server.Port, Username: "native-user", Password: "native-test-password"}, sshconn.Callbacks{HostKey: func(_ context.Context, _ string, fp string) (bool, error) { return fp == server.Fingerprint, nil }})
	if e != nil {
		t.Fatal(e)
	}
	defer conn.Close()
	server.RealPathStatus.Store(8)
	path, _, notice, e := readDirectory(ctx, conn, "/")
	if e != nil || path != "/" || notice == "" {
		t.Fatal("real unsupported REALPATH did not retain explicit absolute LIST", e)
	}
	if _, _, _, e = readDirectory(ctx, conn, "."); e == nil {
		t.Fatal("relative unknown path accepted")
	}
	server.RealPathStatus.Store(3)
	if _, _, _, e = readDirectory(ctx, conn, "/"); e == nil || !strings.Contains(e.Error(), "REALPATH") {
		t.Fatal("permission denial hidden", e)
	}
	if exact, _, _, err := readExactDirectory(ctx, conn, "/"); err != nil || exact != "/" {
		t.Fatal("explicit exact-directory read did not isolate REALPATH rejection", err)
	}
	server.RealPathStatus.Store(0)
	server.RealPathDelay.Store(int64(200 * time.Millisecond))
	short, stop := context.WithTimeout(ctx, 20*time.Millisecond)
	_, _, _, e = readDirectory(short, conn, "/")
	stop()
	if !errors.Is(e, context.DeadlineExceeded) {
		t.Fatal("metadata timeout did not propagate", e)
	}
	server.RealPathDelay.Store(0)
	if _, _, _, e = readDirectory(ctx, conn, "/"); e != nil {
		t.Fatal("explicit action could not reopen file channel", e)
	}
	if conn.Closed() || server.Auth.Load() != 1 {
		t.Fatal("file errors closed or reauthenticated SSH")
	}
}
