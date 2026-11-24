package sshconn

import (
	"bytes"
	"context"
	"forterm/native/internal/testserver"
	"forterm/native/internal/transfers"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func TestStreamedFileBeyondPrototypeLimit(t *testing.T) {
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	c := dialFixture(t, server)
	dir := t.TempDir()
	source := filepath.Join(dir, "large.bin")
	payload := bytes.Repeat([]byte("Forterm streamed file payload\x00"), 150000)
	if e = os.WriteFile(source, payload, 0600); e != nil {
		t.Fatal(e)
	}
	last, total := int64(0), int64(0)
	progress := func(done, size int64) {
		if done < last {
			t.Error("progress went backwards")
		}
		last, total = done, size
	}
	if e = c.RunTransfer(context.Background(), transfers.Spec{Direction: "upload", Local: source, Remote: "/large.bin"}, progress); e != nil {
		t.Fatal(e)
	}
	if last != int64(len(payload)) || total != int64(len(payload)) {
		t.Fatal("progress did not confirm full file")
	}
	destination := filepath.Join(dir, "download.bin")
	if e = c.RunTransfer(context.Background(), transfers.Spec{Direction: "download", Local: destination, Remote: "/large.bin"}, nil); e != nil {
		t.Fatal(e)
	}
	data, e := os.ReadFile(destination)
	if e != nil || !bytes.Equal(data, payload) {
		t.Fatal("stream roundtrip differed", e)
	}
	if e = c.RunTransfer(context.Background(), transfers.Spec{Direction: "download", Local: destination, Remote: "/large.bin"}, nil); e == nil {
		t.Fatal("exclusive local commit overwrote")
	}
	if server.Auth.Load() != 1 || server.SFTP.Load() != 1 {
		t.Fatal("stream reauthenticated")
	}
}

func TestStreamStallAndMetadataTimeoutKeepTransport(t *testing.T) {
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	c := dialFixture(t, server)
	source := filepath.Join(t.TempDir(), "data.bin")
	if e = os.WriteFile(source, make([]byte, 2<<20), 0600); e != nil {
		t.Fatal(e)
	}
	server.WriteDelay.Store(int64(200 * time.Millisecond))
	e = c.transferWithBounds(context.Background(), transfers.Spec{Direction: "upload", Local: source, Remote: "/partial"}, nil, time.Second, 50*time.Millisecond)
	if e == nil || !strings.Contains(e.Error(), "停滞") {
		t.Fatal("stalled write did not abort", e)
	}
	if _, e = c.Monitor(context.Background()); e != nil {
		t.Fatal("write abort killed SSH exec", e)
	}
	if _, e = c.List(context.Background(), "/"); e != nil {
		t.Fatal("explicit file refresh did not recover", e)
	}
	server.WriteDelay.Store(0)
	server.StatDelay.Store(int64(200 * time.Millisecond))
	e = c.transferWithBounds(context.Background(), transfers.Spec{Direction: "download", Local: filepath.Join(t.TempDir(), "target"), Remote: "/partial"}, nil, 50*time.Millisecond, time.Second)
	if e == nil || !strings.Contains(e.Error(), "信息读取超时") {
		t.Fatal("metadata did not timeout", e)
	}
	if _, e = c.Monitor(context.Background()); e != nil {
		t.Fatal("metadata timeout killed exec", e)
	}
	if server.Auth.Load() != 1 {
		t.Fatal("timeout reauthenticated")
	}
}
func TestDownloadCancellationPreservesLocalTargetAndCleansPartial(t *testing.T) {
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	c := dialFixture(t, server)
	dir := t.TempDir()
	source := filepath.Join(dir, "source")
	os.WriteFile(source, make([]byte, 2<<20), 0600)
	if e = c.RunTransfer(context.Background(), transfers.Spec{Direction: "upload", Local: source, Remote: "/data"}, nil); e != nil {
		t.Fatal(e)
	}
	target := filepath.Join(dir, "target")
	os.WriteFile(target, []byte("preserve existing"), 0600)
	server.ReadDelay.Store(int64(200 * time.Millisecond))
	ctx, cancel := context.WithTimeout(context.Background(), 70*time.Millisecond)
	defer cancel()
	if e = c.RunTransfer(ctx, transfers.Spec{Direction: "download", Local: target, Remote: "/data", Overwrite: true}, nil); e == nil {
		t.Fatal("cancelled download succeeded")
	}
	data, _ := os.ReadFile(target)
	if string(data) != "preserve existing" {
		t.Fatal("cancelled download replaced target")
	}
	matches, _ := filepath.Glob(filepath.Join(dir, ".forterm-native-*.part"))
	if len(matches) != 0 {
		t.Fatal("download left temporary files")
	}
	if _, e = c.Monitor(context.Background()); e != nil {
		t.Fatal("cancelled file transfer killed exec")
	}
}
