//go:build windows

// Minimal native entry points; arguments are forwarded as an argv array, never
// interpreted by cmd.exe or written to a command file.
package main

import (
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"unsafe"
)

func main() {
	executable, err := os.Executable()
	if err != nil {
		fail()
		return
	}
	name := strings.ToLower(filepath.Base(executable))
	mode := "xshell"
	if name == "xftp.exe" {
		mode = "xftp"
	}
	if name == "forterm-diagnose.exe" {
		mode = "diagnose"
	}
	args := append([]string{"--entry=" + mode}, os.Args[1:]...)
	command := exec.Command(filepath.Join(filepath.Dir(executable), "forterm.exe"), args...)
	command.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	if command.Start() != nil {
		fail()
	}
	// Preserve the launched application's lifetime and exit status. Returning
	// immediately after Start can look like a failed launch to an SSO controller.
	// With an existing instance, the child exits after forwarding its arguments.
	if err := command.Wait(); err != nil {
		if exit, ok := err.(*exec.ExitError); ok {
			os.Exit(exit.ExitCode())
		}
		os.Exit(1)
	}
}

func fail() {
	text, _ := syscall.UTF16PtrFromString("无法启动 Forterm。请将此入口与 forterm.exe 及 resources 目录放在同一文件夹。")
	title, _ := syscall.UTF16PtrFromString("Forterm")
	syscall.NewLazyDLL("user32.dll").NewProc("MessageBoxW").Call(0, uintptr(unsafe.Pointer(text)), uintptr(unsafe.Pointer(title)), 0x10)
	os.Exit(1)
}
