//go:build !windows

package testbridge

import (
	"errors"
	terminal "forterm/native/internal/vtwidget"
	"github.com/creack/pty"
	"io"
	"os/exec"
	"strings"
	"time"
)

func NewPTY() (*CapturedTerminal, error) {
	cmd := exec.Command("/bin/sh", "-c", "/bin/stty -echo -icanon min 1 time 0; printf 'READY'; /bin/dd bs=1 count=1 2>/dev/null | /usr/bin/od -An -tx1")
	cmd.Env = []string{"LC_ALL=C"}
	file, e := pty.Start(cmd)
	if e != nil {
		return nil, e
	}
	buffer := &outputBuffer{}
	c := &CapturedTerminal{Terminal: terminal.New(), Bytes: make(chan []byte, 64), output: buffer}
	c.cleanup = func() { file.Close(); _ = cmd.Process.Kill(); _ = cmd.Wait() }
	go func() { _ = c.Terminal.RunWithConnection(file, io.TeeReader(file, buffer)) }()
	until := time.Now().Add(2 * time.Second)
	for time.Now().Before(until) {
		if strings.Contains(c.Output(), "READY") {
			return c, nil
		}
		time.Sleep(time.Millisecond)
	}
	c.Close()
	return nil, errors.New("synthetic PTY did not become ready")
}
