//go:build !windows

package vtwidget

import (
	"github.com/creack/pty"
	"golang.org/x/sys/unix"
	"io"
	"os"
	"os/exec"
)

type localPTY struct{ *os.File }

func (p *localPTY) Resize(cols, rows uint16) error {
	raw, e := p.File.SyscallConn()
	if e != nil {
		return e
	}
	var resized error
	e = raw.Control(func(fd uintptr) {
		resized = unix.IoctlSetWinsize(int(fd), unix.TIOCSWINSZ, &unix.Winsize{Col: cols, Row: rows})
	})
	if e != nil {
		return e
	}
	return resized
}
func startLocal(rows, cols uint) (io.WriteCloser, io.Reader, io.Closer, error) {
	shell := os.Getenv("SHELL")
	if shell == "" {
		shell = "/bin/sh"
	}
	cmd := exec.Command(shell)
	cmd.Env = os.Environ()
	f, e := pty.StartWithSize(cmd, &pty.Winsize{Rows: uint16(rows), Cols: uint16(cols)})
	if e != nil {
		return nil, nil, nil, e
	}
	p := &localPTY{File: f}
	go func() { _ = cmd.Wait(); _ = p.Close() }()
	return p, p, p, nil
}
