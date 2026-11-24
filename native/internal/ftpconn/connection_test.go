package ftpconn

import (
	"bytes"
	"context"
	"forterm/native/internal/launch"
	"forterm/native/internal/testftp"
	"forterm/native/internal/transfers"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func fixture(t *testing.T) (*Connection, *testftp.Server) {
	t.Helper()
	server, e := testftp.New(t.TempDir())
	if e != nil {
		t.Fatal(e)
	}
	t.Cleanup(server.Close)
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	c, e := Dial(ctx, launch.Connection{Protocol: "ftp", Host: "127.0.0.1", Port: server.Port, Username: "native-ftp", Password: "synthetic-ftp-password", HasPassword: true})
	if e != nil {
		t.Fatal(e)
	}
	t.Cleanup(c.Close)
	return c, server
}
func TestRealFTPStreamAndProtocolIsolation(t *testing.T) {
	c, server := fixture(t)
	ctx := context.Background()
	dir := t.TempDir()
	source := filepath.Join(dir, "upload.bin")
	payload := bytes.Repeat([]byte("native FTP streamed 中文\x00"), 100000)
	os.WriteFile(source, payload, 0600)
	if e := c.RunTransfer(ctx, transfers.Spec{Direction: "upload", Local: source, Remote: "/file.bin"}, nil); e != nil {
		t.Fatal(e)
	}
	list, e := c.List(ctx, "/")
	if e != nil || len(list) != 1 || list[0].Name != "file.bin" || list[0].Size != int64(len(payload)) {
		t.Fatal("FTP listing incorrect", e, list)
	}
	target := filepath.Join(dir, "download.bin")
	if e = c.RunTransfer(ctx, transfers.Spec{Direction: "download", Local: target, Remote: "/file.bin"}, nil); e != nil {
		t.Fatal(e)
	}
	data, _ := os.ReadFile(target)
	if !bytes.Equal(data, payload) {
		t.Fatal("FTP bytes differ")
	}
	if _, e = c.Stat(ctx, "/absent"); !os.IsNotExist(e) {
		t.Fatal("FTP missing file not distinguished", e)
	}
	if server.Auth.Load() != 1 {
		t.Fatal("FTP transfer reauthenticated")
	}
	if _, e = c.List(ctx, "/\r\nDELE file.bin"); e == nil {
		t.Fatal("FTP command injection accepted")
	}
}
func TestFTPTimeoutDoesNotReuseCredentials(t *testing.T) {
	c, server := fixture(t)
	server.MetadataDelay.Store(int64(200 * time.Millisecond))
	ctx, cancel := context.WithTimeout(context.Background(), 50*time.Millisecond)
	defer cancel()
	if _, e := c.Stat(ctx, "/file"); e == nil {
		t.Fatal("FTP metadata timeout ignored")
	}
	if !c.Closed() {
		t.Fatal("FTP timeout did not close stale control connection")
	}
	if _, e := c.List(context.Background(), "/"); e == nil {
		t.Fatal("closed FTP connection auto-reopened")
	}
	if server.Auth.Load() != 1 {
		t.Fatal("temporary FTP password reused")
	}
}
