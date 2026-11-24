package fileops

import (
	"context"
	"forterm/native/internal/ftpconn"
	"forterm/native/internal/launch"
	"forterm/native/internal/testftp"
	"os"
	"path/filepath"
	"runtime"
	"testing"
	"time"
)

func fixture(t *testing.T) (*Service, *testftp.Server) {
	t.Helper()
	server, e := testftp.New(t.TempDir())
	if e != nil {
		t.Fatal(e)
	}
	t.Cleanup(server.Close)
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	c, e := ftpconn.Dial(ctx, launch.Connection{Protocol: "ftp", Host: "127.0.0.1", Port: server.Port, Username: "native-ftp", Password: "synthetic-ftp-password", HasPassword: true})
	if e != nil {
		t.Fatal(e)
	}
	t.Cleanup(c.Close)
	return New(c, true, nil, nil), server
}
func TestSafeOperationPaths(t *testing.T) {
	for _, p := range []string{"/", "relative", "/a/../b", "/a//b", "/a/", "/a\\b", "/a\nDELE"} {
		if _, e := SafePath(p); e == nil {
			t.Fatal("unsafe path accepted", p)
		}
	}
}
func TestFTPDirectoryOperationsAndNoOverwrite(t *testing.T) {
	service, server := fixture(t)
	ctx := context.Background()
	if e := service.Mkdir(ctx, "/folder"); e != nil {
		t.Fatal(e)
	}
	os.WriteFile(filepath.Join(server.Root, "old"), []byte("old"), 0600)
	os.WriteFile(filepath.Join(server.Root, "existing"), []byte("keep"), 0600)
	if e := service.Rename(ctx, "/old", "/existing"); e == nil {
		t.Fatal("rename overwrote target")
	}
	if e := service.Rename(ctx, "/old", "/renamed"); e != nil {
		t.Fatal(e)
	}
	os.MkdirAll(filepath.Join(server.Root, "folder/sub"), 0755)
	os.WriteFile(filepath.Join(server.Root, "folder/sub/item"), []byte("data"), 0600)
	if e := service.Remove(ctx, "/folder"); e != nil {
		t.Fatal(e)
	}
	if _, e := os.Stat(filepath.Join(server.Root, "folder")); !os.IsNotExist(e) {
		t.Fatal("recursive delete incomplete")
	}
	if server.Auth.Load() != 1 {
		t.Fatal("operations reauthenticated")
	}
}
func TestNonemptyRaceDoesNotDeleteNewChildren(t *testing.T) {
	service, server := fixture(t)
	victim := filepath.Join(server.Root, "victim")
	os.Mkdir(victim, 0755)
	os.WriteFile(filepath.Join(victim, "planned"), []byte("old"), 0600)
	server.BeforeRmdir = func(local string) {
		if local == victim {
			os.WriteFile(filepath.Join(victim, "concurrent"), []byte("keep"), 0600)
		}
	}
	if e := service.Remove(context.Background(), "/victim"); e == nil {
		t.Fatal("nonempty race ignored")
	}
	if data, e := os.ReadFile(filepath.Join(victim, "concurrent")); e != nil || string(data) != "keep" {
		t.Fatal("unplanned child deleted")
	}
	if service.Snapshot().Count != 1 {
		t.Fatal("partial deletion progress missing")
	}
}
func TestFTPLinksDoNotTraverseOutside(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("symlink fixture permissions")
	}
	service, server := fixture(t)
	outside := t.TempDir()
	os.WriteFile(filepath.Join(outside, "keep"), []byte("outside"), 0600)
	os.Mkdir(filepath.Join(server.Root, "folder"), 0755)
	os.Symlink(outside, filepath.Join(server.Root, "folder/link"))
	if e := service.Remove(context.Background(), "/folder"); e == nil {
		t.Fatal("FTP link tree accepted")
	}
	if _, e := os.Lstat(filepath.Join(server.Root, "folder/link")); e != nil {
		t.Fatal("plan deleted a link before validating entire tree")
	}
	if e := service.Remove(context.Background(), "/folder/link/keep"); e == nil {
		t.Fatal("parent link traversal accepted")
	}
	if data, _ := os.ReadFile(filepath.Join(outside, "keep")); string(data) != "outside" {
		t.Fatal("external link target changed")
	}
}
func TestPermissionChangeKeepsSpecialBitsAndDetectsConflict(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("Unix special-mode fixture")
	}
	service, server := fixture(t)
	file := filepath.Join(server.Root, "mode")
	os.WriteFile(file, []byte("data"), 0600)
	os.Chmod(file, 0600|os.ModeSetuid)
	before, e := service.Permissions(context.Background(), "/mode")
	if e != nil || !before.Supported || before.Mode != 04600 {
		t.Fatal("permissions unavailable", before, e)
	}
	after, e := service.Chmod(context.Background(), "/mode", 0640, before.Mode)
	if e != nil || after.Mode != 04640 {
		t.Fatal("special bits lost", after, e)
	}
	if _, e = service.Chmod(context.Background(), "/mode", 0644, before.Mode); e == nil {
		t.Fatal("stale permission change accepted")
	}
}
