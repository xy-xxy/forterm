//go:build windows

package localfile

import "golang.org/x/sys/windows"

func CommitExclusive(temporary, target string) error {
	from, e := windows.UTF16PtrFromString(temporary)
	if e != nil {
		return e
	}
	to, e := windows.UTF16PtrFromString(target)
	if e != nil {
		return e
	}
	// No MOVEFILE_REPLACE_EXISTING: safe on NTFS and removable filesystems.
	return windows.MoveFileEx(from, to, 0)
}
