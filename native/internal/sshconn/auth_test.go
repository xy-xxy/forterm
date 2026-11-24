package sshconn

import (
	"context"
	"crypto/ed25519"
	"crypto/rand"
	"encoding/pem"
	"forterm/native/internal/launch"
	"forterm/native/internal/testserver"
	"golang.org/x/crypto/ssh"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestEncryptedPrivateKeyAuthentication(t *testing.T) {
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	public, private, e := ed25519.GenerateKey(rand.Reader)
	if e != nil {
		t.Fatal(e)
	}
	key, _ := ssh.NewPublicKey(public)
	server.AuthorizedKey = key.Marshal()
	block, e := ssh.MarshalPrivateKeyWithPassphrase(private, "synthetic", []byte("synthetic-key-passphrase"))
	if e != nil {
		t.Fatal(e)
	}
	file := filepath.Join(t.TempDir(), "id_ed25519")
	os.WriteFile(file, pem.EncodeToMemory(block), 0600)
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	c, e := Dial(ctx, launch.Connection{Protocol: "ssh", Host: "127.0.0.1", Port: server.Port, Username: "native-user", AuthMethod: "key", KeyPath: file, Passphrase: "synthetic-key-passphrase"}, Callbacks{HostKey: func(_ context.Context, _ string, fingerprint string) (bool, error) {
		return fingerprint == server.Fingerprint, nil
	}})
	if e != nil {
		t.Fatal(e)
	}
	defer c.Close()
	if _, e = c.List(ctx, "/"); e != nil {
		t.Fatal("encrypted-key authenticated transport unusable", e)
	}
}
func TestNativeJumpChainAndPerHopFingerprint(t *testing.T) {
	outer, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer outer.Close()
	outer.AllowForward = true
	target, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer target.Close()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	destination := launch.Connection{Protocol: "sftp", Host: "127.0.0.1", Port: target.Port, Username: "native-user", Password: "native-test-password"}
	destination.Jumps = []launch.Connection{{Protocol: "ssh", Host: "127.0.0.1", Port: outer.Port, Username: "native-user", Password: "native-test-password"}}
	confirmed := map[string]bool{}
	c, e := Dial(ctx, destination, Callbacks{HostKey: func(_ context.Context, endpoint, fingerprint string) (bool, error) {
		confirmed[endpoint] = true
		return fingerprint == outer.Fingerprint || fingerprint == target.Fingerprint, nil
	}})
	if e != nil {
		t.Fatal(e)
	}
	defer c.Close()
	if _, e = c.List(ctx, "/"); e != nil {
		t.Fatal(e)
	}
	if outer.Auth.Load() != 1 || target.Auth.Load() != 1 || outer.Forward.Load() != 1 || len(confirmed) != 2 {
		t.Fatal("jump transport or per-hop trust incorrect")
	}
}
