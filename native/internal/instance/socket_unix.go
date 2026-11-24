//go:build !windows

package instance

import (
	"errors"
	"net"
	"os"
	"path/filepath"
)

func endpoint(dir string) (string, error) {
	p := filepath.Join(dir, "instance.sock")
	if len(p) > 100 {
		return "", errors.New("原生运行目录路径过长")
	}
	return p, nil
}
func listen(address string) (net.Listener, error) {
	if s, e := os.Lstat(address); e == nil {
		if s.Mode()&os.ModeSocket == 0 {
			return nil, errors.New("通信路径不是套接字")
		}
		if e = os.Remove(address); e != nil {
			return nil, e
		}
	}
	l, e := net.Listen("unix", address)
	if e == nil {
		e = os.Chmod(address, 0600)
	}
	return l, e
}
func dial(address string) (net.Conn, error) { return net.Dial("unix", address) }
