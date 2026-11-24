package sshconn

import (
	"bytes"
	"context"
	"crypto/rand"
	"encoding/hex"
	"fmt"
	"forterm/native/internal/testserver"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"sync"
	"testing"
	"time"
)

func TestFourCapabilitiesShareOneAuthenticatedTransport(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("fixture uses Unix shell")
	}
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	c := dialFixture(t, server)
	ctx := context.Background()
	doc, e := c.CreateText(ctx, "/edit.go", "package main\r\n")
	if e != nil {
		t.Fatal(e)
	}
	shell, e := c.Shell(24, 80)
	if e != nil {
		t.Fatal(e)
	}
	defer shell.Close()
	var nonce [8]byte
	_, _ = rand.Read(nonce[:])
	marker := "FOUR_EXEC_" + hex.EncodeToString(nonce[:])
	command := fmt.Sprintf("printf '\\033[32mFOUR_%%s_%%s\\033[0m\\n' 'EXEC' '%s'\n", hex.EncodeToString(nonce[:]))
	if strings.Contains(command, marker) {
		t.Fatal("shell output assertion could match echo")
	}
	output := make(chan bool, 1)
	go func() {
		var text []byte
		buf := make([]byte, 4096)
		for {
			n, err := shell.Out.Read(buf)
			text = append(text, buf[:n]...)
			if bytes.Contains(text, []byte("\x1b[32m"+marker+"\x1b[0m\r\n")) {
				output <- true
				return
			}
			if err != nil {
				return
			}
		}
	}()
	local := filepath.Join(t.TempDir(), "transfer")
	if e = os.WriteFile(local, []byte("concurrent upload"), 0600); e != nil {
		t.Fatal(e)
	}
	start := make(chan struct{})
	errs := make(chan error, 4)
	var wg sync.WaitGroup
	actions := []func() error{
		func() error { _, e := shell.In.Write([]byte(command)); return e },
		func() error { return c.Upload(ctx, local, "/transfer", false) },
		func() error {
			_, e := c.SaveText(ctx, "/edit.go", "package main\r\n// edited  \r\n", doc.Revision)
			return e
		},
		func() error { _, e := c.Monitor(ctx); return e },
	}
	for _, action := range actions {
		wg.Add(1)
		go func(action func() error) { defer wg.Done(); <-start; errs <- action() }(action)
	}
	close(start)
	wg.Wait()
	close(errs)
	for e := range errs {
		if e != nil {
			t.Fatal(e)
		}
	}
	select {
	case <-output:
	case <-time.After(3 * time.Second):
		t.Fatal("four-capability shell did not execute and return actual ANSI output")
	}
	input, _ := server.Snapshot()
	if !bytes.Equal(input, []byte(command)) {
		t.Fatal("four-capability shell input bytes changed")
	}

	saved, e := c.ReadText(ctx, "/edit.go")
	if e != nil || saved.Text != "package main\r\n// edited  \r\n" {
		t.Fatal("editor bytes incorrect", e)
	}
	if server.Auth.Load() != 1 || server.SFTP.Load() != 1 || server.Exec.Load() != 1 {
		t.Fatal("four capabilities reauthenticated or opened duplicate SFTP")
	}
}
