//go:build !windows

package localfile

import "os"

// A same-directory hard link is an atomic exclusive commit: an existing target
// is never removed. The caller removes its private temporary pathname.
func CommitExclusive(temporary, target string) error { return os.Link(temporary, target) }
