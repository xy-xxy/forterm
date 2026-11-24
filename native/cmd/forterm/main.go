package main

import (
	"fmt"
	"forterm/native/internal/desktop"
	"forterm/native/internal/instance"
	"forterm/native/internal/launch"
	"forterm/native/internal/uiresponse"
	"fyne.io/fyne/v2"
	"os"
	"path/filepath"
)

func main() {
	args := append([]string(nil), os.Args[1:]...)
	if uiresponse.Requested(args) {
		uiresponse.Run()
		return
	}
	entry := filepath.Base(os.Args[0])
	cache, e := os.UserCacheDir()
	if e != nil {
		fmt.Fprintln(os.Stderr, "无法定位原生运行目录")
		os.Exit(1)
	}
	directory := filepath.Join(cache, "FortermNative")
	if override := os.Getenv("FORTERM_NATIVE_DATA"); override != "" {
		directory = override
	}
	var gui *desktop.App
	queue := make(chan []string, 64)
	server, primary, e := instance.Start(directory, args, func(args []string) { queue <- args })
	if e != nil {
		fmt.Fprintln(os.Stderr, e)
		os.Exit(1)
	}
	if !primary {
		return
	}
	defer server.Close()
	gui = desktop.New("cn.forterm.native")
	plan := launch.Parse(args, entry)
	for i := range args {
		args[i] = ""
	}
	for i := 1; i < len(os.Args); i++ {
		os.Args[i] = ""
	}
	gui.Open(plan)
	go func() {
		for incoming := range queue {
			p := launch.Parse(incoming, entry)
			for i := range incoming {
				incoming[i] = ""
			}
			fyne.Do(func() { gui.Open(p) })
		}
	}()
	gui.Window.ShowAndRun()
	gui.Close()
}
