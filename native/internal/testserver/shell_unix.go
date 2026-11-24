//go:build !windows

package testserver

import (
	"github.com/creack/pty"
	"golang.org/x/sys/unix"
	"io"
	"os"
	"os/exec"
	"strings"
)

func shell(rows, cols uint32) (io.ReadWriteCloser, error) {
	cmd := exec.Command("/bin/sh", "-i")
	for _, value := range os.Environ() {
		if !strings.HasPrefix(value, "ENV=") && !strings.HasPrefix(value, "BASH_ENV=") {
			cmd.Env = append(cmd.Env, value)
		}
	}
	cmd.Env = append(cmd.Env, "PS1=native> ")
	return pty.StartWithSize(cmd, &pty.Winsize{Rows: uint16(rows), Cols: uint16(cols)})
}
func resize(p io.ReadWriteCloser, rows, cols uint32) {
	if f, ok := p.(*os.File); ok {
		raw, err := f.SyscallConn()
		if err == nil {
			_ = raw.Control(func(fd uintptr) {
				_ = unix.IoctlSetWinsize(int(fd), unix.TIOCSWINSZ, &unix.Winsize{Row: uint16(rows), Col: uint16(cols)})
			})
		}
	}
}
