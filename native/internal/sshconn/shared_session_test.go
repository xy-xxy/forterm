package sshconn

import (
	"context"
	"forterm/native/internal/launch"
	"forterm/native/internal/testserver"
	"io"
	"strings"
	"testing"
	"time"
)

func TestTwoRealPTYsShareAuthenticationAndOwnerLifetime(t *testing.T) {
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()
	c, e := Dial(ctx, launch.Connection{Protocol: "ssh", Host: "127.0.0.1", Port: server.Port, Username: "native-user", Password: "native-test-password"}, Callbacks{HostKey: func(context.Context, string, string) (bool, error) { return true, nil }})
	if e != nil {
		t.Fatal(e)
	}
	defer c.Close()
	first, e := c.Shell(24, 80)
	if e != nil {
		t.Fatal(e)
	}
	if !c.Retain() {
		t.Fatal("could not retain live transport")
	}
	second, e := c.Shell(30, 90)
	if e != nil {
		t.Fatal(e)
	}
	output := func(r io.Reader) <-chan string {
		ch := make(chan string, 64)
		go func() {
			buf := make([]byte, 1024)
			for {
				n, e := r.Read(buf)
				if n > 0 {
					ch <- string(buf[:n])
				}
				if e != nil {
					return
				}
			}
		}()
		return ch
	}
	one, two := output(first.Out), output(second.Out)
	_, _ = first.In.Write([]byte("printf 'ONE_REAL_PTY\\n'\n"))
	_, _ = second.In.Write([]byte("printf 'TWO_REAL_PTY\\n'\n"))
	await := func(ch <-chan string, marker, forbidden string) {
		t.Helper()
		text := ""
		for !strings.Contains(text, marker) {
			select {
			case s := <-ch:
				text += s
				if strings.Contains(text, forbidden) {
					t.Fatal("PTY output crossed channels")
				}
			case <-ctx.Done():
				t.Fatal("shell marker missing")
			}
		}
	}
	await(one, "ONE_REAL_PTY", "TWO_REAL_PTY")
	await(two, "TWO_REAL_PTY", "ONE_REAL_PTY")
	if e := first.Resize(35, 100); e != nil {
		t.Fatal(e)
	}
	if e := second.Resize(45, 110); e != nil {
		t.Fatal(e)
	}
	first.Close()
	c.Release()
	if c.Closed() {
		t.Fatal("closing original owner killed second transport")
	}
	_, _ = second.In.Write([]byte("printf 'SECOND_STILL_LIVE\\n'\n"))
	await(two, "SECOND_STILL_LIVE", "ONE_REAL_PTY")
	second.Close()
	c.Release()
	if !c.Closed() || server.Auth.Load() != 1 {
		t.Fatal("last reference/authentication lifecycle wrong")
	}
}
