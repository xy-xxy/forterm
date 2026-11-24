package fileops_test

import (
	"context"
	"forterm/native/internal/fileops"
	"forterm/native/internal/ftpconn"
	"forterm/native/internal/launch"
	"forterm/native/internal/remotefs"
	"forterm/native/internal/sshconn"
	"forterm/native/internal/testftp"
	"forterm/native/internal/testserver"
	"io"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func sshCopyFixture(t *testing.T) (*sshconn.Connection, *testserver.Server) {
	t.Helper()
	s, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	c, e := sshconn.Dial(ctx, launch.Connection{Protocol: "sftp", Host: "127.0.0.1", Port: s.Port, Username: "native-user", Password: "native-test-password"}, sshconn.Callbacks{HostKey: func(context.Context, string, string) (bool, error) { return true, nil }})
	if e != nil {
		t.Fatal(e)
	}
	t.Cleanup(func() { c.Close(); s.Close() })
	return c, s
}
func readCopyFile(t *testing.T, ctx context.Context, fs remotefs.Streams, name string) string {
	t.Helper()
	var text strings.Builder
	e := fs.ReadStream(ctx, name, func(r io.Reader, _ os.FileInfo) error { _, e := io.Copy(&text, r); return e })
	if e != nil {
		t.Fatal(e)
	}
	return text.String()
}
func TestSameAndCrossProtocolCopyMoveBytesAndNoReauthentication(t *testing.T) {
	src, server := sshCopyFixture(t)
	ftpServer, e := testftp.New(t.TempDir())
	if e != nil {
		t.Fatal(e)
	}
	defer ftpServer.Close()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	dst, e := ftpconn.Dial(ctx, launch.Connection{Protocol: "ftp", Host: "127.0.0.1", Port: ftpServer.Port, Username: "native-ftp", Password: "synthetic-ftp-password", HasPassword: true})
	if e != nil {
		t.Fatal(e)
	}
	defer dst.Close()
	payload := "synthetic 中文 bytes % #\n"
	local := filepath.Join(t.TempDir(), "file.txt")
	os.WriteFile(local, []byte(payload), 0600)
	if e = src.Upload(ctx, local, "/source.txt", false); e != nil {
		t.Fatal(e)
	}
	info, _ := src.Stat(ctx, "/source.txt")
	identity := fileops.IdentityOf(info)
	if e = fileops.CopyFile(ctx, fileops.CopyRequest{Source: src, Destination: src, SourcePath: "/source.txt", DestinationPath: "/same-copy.txt", SourceIdentity: identity}, nil); e != nil {
		t.Fatal(e)
	}
	if readCopyFile(t, ctx, src, "/same-copy.txt") != payload {
		t.Fatal("same-source copy bytes differ")
	}
	if e = fileops.CopyFile(ctx, fileops.CopyRequest{Source: src, Destination: dst, SourcePath: "/source.txt", DestinationPath: "/跨源 % #.txt", SourceIdentity: identity}, nil); e != nil {
		t.Fatal(e)
	}
	if readCopyFile(t, ctx, dst, "/跨源 % #.txt") != payload {
		t.Fatal("cross-source bytes differ")
	}
	if e = fileops.CopyFile(ctx, fileops.CopyRequest{Source: src, Destination: dst, SourcePath: "/source.txt", DestinationPath: "/跨源 % #.txt", SourceIdentity: identity}, nil); e == nil {
		t.Fatal("unconfirmed overwrite accepted")
	}
	if e = fileops.CopyFile(ctx, fileops.CopyRequest{Source: src, Destination: dst, SourcePath: "/source.txt", DestinationPath: "/moved.txt", SourceIdentity: identity, Move: true}, nil); e != nil {
		t.Fatal(e)
	}
	if _, e = src.Stat(ctx, "/source.txt"); !os.IsNotExist(e) {
		t.Fatal("cross move did not remove confirmed source")
	}
	if readCopyFile(t, ctx, dst, "/moved.txt") != payload {
		t.Fatal("move target differs")
	}
	if server.Auth.Load() != 1 || ftpServer.Auth.Load() != 1 {
		t.Fatal("file operation authenticated again")
	}
}
func TestCopyCancellationChangedSourceAndUnsafePathsPreserveSource(t *testing.T) {
	src, _ := sshCopyFixture(t)
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	local := filepath.Join(t.TempDir(), "file.txt")
	os.WriteFile(local, []byte("before"), 0600)
	src.Upload(ctx, local, "/source.txt", false)
	info, _ := src.Stat(ctx, "/source.txt")
	identity := fileops.IdentityOf(info)
	cancelled, stop := context.WithCancel(ctx)
	stop()
	r := fileops.CopyRequest{Source: src, Destination: src, SourcePath: "/source.txt", DestinationPath: "/copy.txt", SourceIdentity: identity, Move: true}
	if fileops.CopyFile(cancelled, r, nil) == nil {
		t.Fatal("cancelled move succeeded")
	}
	if readCopyFile(t, ctx, src, "/source.txt") != "before" {
		t.Fatal("cancel lost source")
	}
	os.WriteFile(local, []byte("changed-size"), 0600)
	src.Upload(ctx, local, "/source.txt", true)
	if fileops.CopyFile(ctx, r, nil) == nil {
		t.Fatal("stale source metadata accepted")
	}
	for _, target := range []string{"/", "/source.txt", "/x/../escape"} {
		r.DestinationPath = target
		if fileops.CopyFile(ctx, r, nil) == nil {
			t.Fatal("unsafe/self target accepted")
		}
	}
}
