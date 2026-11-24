package fileops

import (
	"context"
	"forterm/native/internal/remotefs"
	"forterm/native/internal/transfers"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

type planFS struct {
	root    string
	changes []string
}

func (f *planFS) local(p string) string                                 { return filepath.Join(f.root, strings.TrimPrefix(p, "/")) }
func (f *planFS) Stat(_ context.Context, p string) (os.FileInfo, error) { return os.Lstat(f.local(p)) }
func (f *planFS) List(_ context.Context, p string) ([]remotefs.Entry, error) {
	entries, e := os.ReadDir(f.local(p))
	out := []remotefs.Entry{}
	for _, entry := range entries {
		info, _ := entry.Info()
		out = append(out, remotefs.Entry{Name: entry.Name(), Directory: entry.IsDir(), Mode: info.Mode(), ModeKnown: true, Size: info.Size(), Modified: info.ModTime()})
	}
	return out, e
}
func (f *planFS) RunTransfer(context.Context, transfers.Spec, func(int64, int64)) error { return nil }
func (f *planFS) Close()                                                                {}
func (f *planFS) Closed() bool                                                          { return false }
func (f *planFS) ChmodSupported() bool                                                  { return true }
func (f *planFS) RealPath(_ context.Context, p string) (string, error)                  { return p, nil }
func (f *planFS) Mutate(_ context.Context, kind, p, _ string, mode os.FileMode) error {
	if kind != "chmod" {
		return os.ErrPermission
	}
	f.changes = append(f.changes, p)
	return os.Chmod(f.local(p), mode)
}
func TestRecursivePermissionPlanPreservesFileExecuteAndSpecialBitsAndPostorder(t *testing.T) {
	f := &planFS{root: t.TempDir()}
	os.MkdirAll(f.local("/root/sub"), 0755)
	os.WriteFile(f.local("/root/sub/normal.txt"), []byte("fixture"), 0640)
	os.Symlink("/outside", f.local("/root/link"))
	plan, e := PlanPermissions(context.Background(), f, "/root", true, 0700, nil)
	if e != nil {
		t.Fatal(e)
	}
	if len(f.changes) != 0 || plan.Skipped != 1 || plan.Items[len(plan.Items)-1].Path != "/root" {
		t.Fatal("plan mutated or followed links/order incorrect")
	}
	if e = ApplyPermissionPlan(context.Background(), f, plan, false, nil, nil, nil); e != nil {
		t.Fatal(e)
	}
	info, _ := f.Stat(context.Background(), "/root/sub/normal.txt")
	if info.Mode().Perm() != 0640 {
		t.Fatal("directory mode added execute to file")
	}
	explicit := uint32(0600)
	plan, e = PlanPermissions(context.Background(), f, "/root", true, 0755, &explicit)
	if e != nil {
		t.Fatal(e)
	}
	os.WriteFile(f.local("/root/sub/normal.txt"), []byte("changed"), 0640)
	if ApplyPermissionPlan(context.Background(), f, plan, false, nil, nil, nil) == nil {
		t.Fatal("changed planned file accepted")
	}
	for _, p := range []string{"/", "/root/../escape", "/root/link/child"} {
		if _, e = PlanPermissions(context.Background(), f, p, true, 0755, nil); e == nil {
			t.Fatal("unsafe recursive root accepted")
		}
	}
}
