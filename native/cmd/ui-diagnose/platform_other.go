//go:build !windows

package main

import (
	"os/exec"
	"runtime"
)

func hide(*exec.Cmd) {}
func resources() map[string]any {
	var m runtime.MemStats
	runtime.ReadMemStats(&m)
	return map[string]any{"goHeapBytes": m.HeapAlloc, "goSysBytes": m.Sys, "goroutines": runtime.NumGoroutine(), "processCPUSeconds": "not sampled on this platform"}
}
