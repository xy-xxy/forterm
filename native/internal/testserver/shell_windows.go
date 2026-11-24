//go:build windows

package testserver

import (
	"errors"
	"io"
)

func shell(rows, cols uint32) (io.ReadWriteCloser, error) {
	return nil, errors.New("Unix loopback shell fixture unavailable on Windows; use native ConPTY smoke there")
}
func resize(p io.ReadWriteCloser, rows, cols uint32) {}
