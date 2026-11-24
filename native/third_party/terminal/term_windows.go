package terminal

import (
	"github.com/ActiveState/termtest/conpty"
	"io"
	"os"
	"path/filepath"
	"sync"
	"syscall"
)

type nativeConsole struct {
	*conpty.ConPty
	once sync.Once
	err  error
}

func (c *nativeConsole) Close() error { c.once.Do(func() { c.err = c.ConPty.Close() }); return c.err }
func (t *Terminal) updatePTYSize() {
	if t.pty == nil {
		return
	}
	_ = t.pty.(*nativeConsole).Resize(uint16(t.config.Columns), uint16(t.config.Rows))
}
func (t *Terminal) hangup() {}
func (t *Terminal) startPTY() (io.WriteCloser, io.Reader, io.Closer, error) {
	pty, err := conpty.New(80, 25)
	if err != nil {
		return nil, nil, nil, err
	}
	console := &nativeConsole{ConPty: pty}
	root := os.Getenv("SystemRoot")
	if root == "" {
		root = `C:\Windows`
	}
	executable := filepath.Join(root, "System32", "WindowsPowerShell", "v1.0", "powershell.exe")
	pid, _, err := pty.Spawn(executable, []string{}, &syscall.ProcAttr{Env: os.Environ(), Dir: t.startingDir()})
	if err != nil {
		console.Close()
		return nil, nil, nil, err
	}
	process, err := os.FindProcess(pid)
	if err != nil {
		console.Close()
		return nil, nil, nil, err
	}
	go func() {
		state, err := process.Wait()
		if err == nil {
			t.exitCode.Store(int32(state.ExitCode()))
			t.exitReady.Store(true)
		}
		// A local shell failure must not terminate the whole app and other sessions.
		_ = console.Close()
	}()
	return pty.InPipe(), pty.OutPipe(), console, nil
}
