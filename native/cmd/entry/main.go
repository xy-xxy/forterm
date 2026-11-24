package main

import (
	"os"
	"os/exec"
	"path/filepath"
	"strings"
)

func main() {
	ex, e := os.Executable()
	if e != nil {
		os.Exit(1)
	}
	name := strings.ToLower(strings.TrimSuffix(filepath.Base(ex), ".exe"))
	args := entryArguments(name, os.Args[1:])
	cmd := exec.Command(filepath.Join(filepath.Dir(ex), "forterm-native.exe"), args...)
	hide(cmd)
	if e = cmd.Run(); e != nil {
		if x, ok := e.(*exec.ExitError); ok {
			os.Exit(x.ExitCode())
		}
		os.Exit(1)
	}
}

func entryArguments(name string, input []string) []string {
	if name == "forterm-diagnose" && len(input) == 1 && input[0] == "--ui-check" {
		return []string{"--ui-check"}
	}
	mode := "xshell"
	if name == "xftp" {
		mode = "xftp"
	}
	if name == "forterm-diagnose" {
		mode = "diagnose"
	}
	return append([]string{"--entry=" + mode}, input...)
}
