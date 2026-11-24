//go:build windows

package main

import (
	"os/exec"
	"syscall"
)

func hide(command *exec.Cmd) { command.SysProcAttr = &syscall.SysProcAttr{HideWindow: true} }
