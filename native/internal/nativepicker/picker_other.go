//go:build (!darwin && !windows) || !cgo

package nativepicker

func startPlatform(uint64, any, Request) error { return ErrUnavailable }
func cancelPlatform(uint64)                    {}
