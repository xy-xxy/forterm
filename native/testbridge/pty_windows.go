//go:build windows

package testbridge

import "errors"

func NewPTY() (*CapturedTerminal, error) { return nil, errors.New("Unix synthetic PTY fixture") }
