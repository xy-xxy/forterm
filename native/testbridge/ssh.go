package testbridge

import (
	"context"
	"forterm/native/internal/launch"
	"forterm/native/internal/sshconn"
	"forterm/native/internal/testserver"
	terminal "forterm/native/internal/vtwidget"
	"io"
	"time"
)

func NewSSH() (*CapturedTerminal, func() []byte, func() int64, error) {
	server, e := testserver.New()
	if e != nil {
		return nil, nil, nil, e
	}
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	conn, e := sshconn.Dial(ctx, launch.Connection{Protocol: "ssh", Host: "127.0.0.1", Port: server.Port, Username: "native-user", Password: "native-test-password"}, sshconn.Callbacks{HostKey: func(context.Context, string, string) (bool, error) { return true, nil }})
	if e != nil {
		server.Close()
		return nil, nil, nil, e
	}
	shell, e := conn.Shell(24, 80)
	if e != nil {
		conn.Close()
		server.Close()
		return nil, nil, nil, e
	}
	output := &outputBuffer{}
	c := &CapturedTerminal{Terminal: terminal.New(), Bytes: make(chan []byte, 64), output: output}
	c.cleanup = func() { shell.Close(); conn.Close(); server.Close() }
	go func() { _ = c.Terminal.RunWithConnection(shell.In, io.TeeReader(shell.Out, output)) }()
	until := time.Now().Add(time.Second)
	for time.Now().Before(until) {
		if _, e := c.Terminal.Write(nil); e == nil {
			break
		}
		time.Sleep(time.Millisecond)
	}
	return c, func() []byte { input, _ := server.Snapshot(); return input }, func() int64 { return server.Auth.Load() }, nil
}
