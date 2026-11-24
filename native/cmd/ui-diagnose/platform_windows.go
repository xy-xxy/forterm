//go:build windows

package main

import (
	"golang.org/x/sys/windows"
	"os/exec"
	"syscall"
	"unsafe"
)

func hide(c *exec.Cmd) { c.SysProcAttr = &syscall.SysProcAttr{HideWindow: true} }

type memoryCounters struct {
	CB                                                                                                                                                                     uint32
	PageFaultCount                                                                                                                                                         uint32
	PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage, QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage uintptr
}

func resources() map[string]any {
	result := map[string]any{}
	var a, b, k, u windows.Filetime
	if windows.GetProcessTimes(windows.CurrentProcess(), &a, &b, &k, &u) == nil {
		kernel := uint64(k.HighDateTime)<<32 | uint64(k.LowDateTime)
		user := uint64(u.HighDateTime)<<32 | uint64(u.LowDateTime)
		result["processCPUSeconds"] = float64(kernel+user) / 1e7
	}
	var c memoryCounters
	c.CB = uint32(unsafe.Sizeof(c))
	p := windows.NewLazySystemDLL("psapi.dll").NewProc("GetProcessMemoryInfo")
	r, _, _ := p.Call(uintptr(windows.CurrentProcess()), uintptr(unsafe.Pointer(&c)), uintptr(c.CB))
	if r != 0 {
		result["workingSetBytes"] = uint64(c.WorkingSetSize)
		result["privateCommitBytes"] = uint64(c.PagefileUsage)
	}
	if version := windows.RtlGetVersion(); version != nil {
		result["windowsVersion"] = []uint32{version.MajorVersion, version.MinorVersion, version.BuildNumber}
	}
	return result
}
