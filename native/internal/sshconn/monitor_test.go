package sshconn

import (
	"context"
	"forterm/native/internal/launch"
	"forterm/native/internal/testserver"
	"io"
	"runtime"
	"strings"
	"testing"
	"time"
)

func dialFixture(t *testing.T, server *testserver.Server) *Connection {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	c, e := Dial(ctx, launch.Connection{Protocol: "ssh", Host: "127.0.0.1", Port: server.Port, Username: "native-user", Password: "native-test-password"}, Callbacks{HostKey: func(_ context.Context, _ string, fp string) (bool, error) { return fp == server.Fingerprint, nil }})
	if e != nil {
		t.Fatal(e)
	}
	t.Cleanup(c.Close)
	return c
}
func TestMonitorParallelChannelsAndNoInteractiveInput(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("real shell fixture runs Unix")
	}
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	c := dialFixture(t, server)
	shell, e := c.Shell(24, 80)
	if e != nil {
		t.Fatal(e)
	}
	defer shell.Close()
	go io.Copy(io.Discard, shell.Out)
	done := make(chan error, 1)
	go func() { _, e := c.List(context.Background(), "/"); done <- e }()
	text, e := c.Monitor(context.Background())
	if e != nil || !strings.Contains(text, "MemTotal:") {
		t.Fatal("monitor failed", e)
	}
	if e = <-done; e != nil {
		t.Fatal("SFTP affected", e)
	}
	input, _ := server.Snapshot()
	if len(input) != 0 {
		t.Fatal("monitor injected shell input")
	}
	if server.Auth.Load() != 1 || server.SFTP.Load() != 1 || server.Exec.Load() != 1 {
		t.Fatal("shared transport not reused")
	}
}
func TestMonitorPermissionDenialPreservesFiles(t *testing.T) {
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	server.DenyExec = true
	c := dialFixture(t, server)
	if _, e = c.Monitor(context.Background()); e == nil {
		t.Fatal("exec rejection ignored")
	}
	if _, e = c.List(context.Background(), "/"); e != nil {
		t.Fatal("file channel broken", e)
	}
	if server.Auth.Load() != 1 {
		t.Fatal("re-authenticated")
	}
}
func TestMonitorTimeoutDoesNotOverlapOrCloseTransport(t *testing.T) {
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	server.StallExec = true
	c := dialFixture(t, server)
	ctx, cancel := context.WithTimeout(context.Background(), 150*time.Millisecond)
	defer cancel()
	first := make(chan error, 1)
	go func() { _, e := c.Monitor(ctx); first <- e }()
	deadline := time.Now().Add(time.Second)
	for server.Exec.Load() == 0 && time.Now().Before(deadline) {
		time.Sleep(time.Millisecond)
	}
	if _, e = c.Monitor(context.Background()); e == nil {
		t.Fatal("overlapping monitor accepted")
	}
	select {
	case e := <-first:
		if e == nil {
			t.Fatal("timeout ignored")
		}
	case <-time.After(time.Second):
		t.Fatal("timeout not bounded")
	}
	if _, e = c.List(context.Background(), "/"); e != nil {
		t.Fatal("timeout closed shared transport", e)
	}
	if server.Exec.Load() != 1 {
		t.Fatal("overlapping exec opened")
	}
}
