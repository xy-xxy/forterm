//go:build windows

package vtwidget

import (
	"github.com/ActiveState/termtest/conpty"
	"io"
	"os"
	"path/filepath"
	"sync"
	"syscall"
)

type localPTY struct {
	*conpty.ConPty
	mu     sync.Mutex
	closed bool
}

func (p *localPTY) Close() error {
	p.mu.Lock()
	defer p.mu.Unlock()
	if p.closed {
		return nil
	}
	p.closed = true
	return p.ConPty.Close()
}
func (p *localPTY) Resize(cols, rows uint16) error {
	p.mu.Lock()
	defer p.mu.Unlock()
	if p.closed {
		return io.ErrClosedPipe
	}
	return p.ConPty.Resize(cols, rows)
}
func startLocal(rows, cols uint) (io.WriteCloser, io.Reader, io.Closer, error) {
	c, e := conpty.New(int16(min(cols, 32767)), int16(min(rows, 32767)))
	if e != nil {
		return nil, nil, nil, e
	}
	p := &localPTY{ConPty: c}
	root := os.Getenv("SystemRoot")
	if root == "" {
		root = `C:\Windows`
	}
	executable := filepath.Join(root, "System32", "WindowsPowerShell", "v1.0", "powershell.exe")
	pid, _, e := c.Spawn(executable, []string{}, &syscall.ProcAttr{Env: os.Environ()})
	if e != nil {
		p.Close()
		return nil, nil, nil, e
	}
	proc, e := os.FindProcess(pid)
	if e != nil {
		p.Close()
		return nil, nil, nil, e
	}
	go func() { _, _ = proc.Wait(); _ = p.Close() }()
	return c.InPipe(), c.OutPipe(), p, nil
}
