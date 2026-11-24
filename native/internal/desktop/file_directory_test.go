package desktop

import (
	"context"
	"errors"
	"forterm/native/internal/remotefs"
	"forterm/native/internal/transfers"
	"github.com/pkg/sftp"
	"os"
	"strings"
	"testing"
	"time"
)

type directoryFixture struct {
	realErr              error
	real                 string
	listCalls, statCalls int
	listPath             string
	statDir              bool
	stall                bool
}

func (f *directoryFixture) RealPath(ctx context.Context, p string) (string, error) {
	if f.stall {
		<-ctx.Done()
		return "", ctx.Err()
	}
	return f.real, f.realErr
}
func (f *directoryFixture) List(_ context.Context, p string) ([]remotefs.Entry, error) {
	f.listCalls++
	f.listPath = p
	return []remotefs.Entry{{Name: "中文 file.txt"}}, nil
}
func (f *directoryFixture) Stat(context.Context, string) (os.FileInfo, error) {
	f.statCalls++
	if !f.statDir {
		return nil, os.ErrPermission
	}
	return fixtureDir{}, nil
}
func (*directoryFixture) RunTransfer(context.Context, transfers.Spec, func(int64, int64)) error {
	return errors.New("must not upload during directory resolution")
}
func (*directoryFixture) Close()       {}
func (*directoryFixture) Closed() bool { return false }
func (*directoryFixture) Mutate(context.Context, string, string, string, os.FileMode) error {
	return errors.New("must not mutate")
}
func (*directoryFixture) ChmodSupported() bool { return true }

type fixtureDir struct{}

func (fixtureDir) Name() string       { return "dir" }
func (fixtureDir) Size() int64        { return 0 }
func (fixtureDir) Mode() os.FileMode  { return os.ModeDir | 0700 }
func (fixtureDir) ModTime() time.Time { return time.Time{} }
func (fixtureDir) IsDir() bool        { return true }
func (fixtureDir) Sys() any           { return nil }
func TestDirectoryFailuresAreExplicitAndNeverChooseUnknownUploadTarget(t *testing.T) {
	for _, code := range []uint32{3, 4, 8} {
		for _, target := range []string{".", "/opt/forward"} {
			f := &directoryFixture{realErr: &sftp.StatusError{Code: code}, statDir: true}
			got, entries, notice, e := readDirectory(context.Background(), f, target)
			allowed := code == 8 && target == "/opt/forward"
			if allowed {
				if e != nil || got != target || len(entries) != 1 || notice == "" || f.listPath != target || f.statCalls != 1 {
					t.Fatalf("explicit unsupported path not safely confirmed: %q %v", got, e)
				}
			} else {
				if e == nil || !strings.Contains(e.Error(), "REALPATH") || f.listCalls != 0 || got != "" {
					t.Fatal("permission/error chose an unknown directory", code, target, e)
				}
			}
		}
	}
	f := &directoryFixture{real: "/canonical/dir"}
	got, _, _, e := readDirectory(context.Background(), f, ".")
	if e != nil || got != "/canonical/dir" || f.listPath != got {
		t.Fatal("canonical directory not used", e)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Millisecond)
	defer cancel()
	f = &directoryFixture{stall: true}
	_, _, _, e = readDirectory(ctx, f, "/target")
	if !errors.Is(e, context.DeadlineExceeded) || f.listCalls != 0 {
		t.Fatal("REALPATH timeout did not remain bounded", e)
	}
}
