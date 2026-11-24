package localfile

import (
	"context"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestCancelledLocalOpenReturnsWithoutAccumulatingBlockedCalls(t *testing.T) {
	p := filepath.Join(t.TempDir(), "中文 %#.txt")
	os.WriteFile(p, []byte("fixture"), 0600)
	release := make(chan struct{})
	started := make(chan struct{})
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan error, 1)
	go func() {
		_, e := openContext(ctx, p, func(s string) (*os.File, error) { close(started); <-release; return os.Open(s) })
		done <- e
	}()
	<-started
	cancel()
	select {
	case e := <-done:
		if e == nil {
			t.Fatal("cancel returned success")
		}
	case <-time.After(time.Second):
		t.Fatal("local open blocks cancellation")
	}
	if _, e := OpenContext(context.Background(), p); e == nil {
		t.Fatal("blocked open request was duplicated")
	}
	close(release)
}
func TestDownloadTargetVersionRejectsCreatedChangedAndSymlinkTargets(t *testing.T) {
	directory := t.TempDir()
	p := filepath.Join(directory, "file.txt")
	empty, e := CaptureTarget(p)
	if e != nil {
		t.Fatal(e)
	}
	os.WriteFile(p, []byte("original"), 0600)
	if empty.Check() == nil {
		t.Fatal("new target bypassed original exclusive decision")
	}
	original, e := CaptureTarget(p)
	if e != nil {
		t.Fatal(e)
	}
	os.WriteFile(p, []byte("changed-size"), 0600)
	if original.Check() == nil {
		t.Fatal("overwrite confirmation applied to changed file")
	}
	link := filepath.Join(directory, "link")
	os.Symlink(p, link)
	if _, e = CaptureTarget(link); e == nil {
		t.Fatal("symlink overwrite approved")
	}
}
