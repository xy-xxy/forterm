//go:build !windows

package sshconn

import (
	"context"
	"crypto/ed25519"
	"crypto/rand"
	"forterm/native/internal/launch"
	"forterm/native/internal/testserver"
	"golang.org/x/crypto/ssh"
	"golang.org/x/crypto/ssh/agent"
	"net"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestNativeAgentUsesExistingAgentSocket(t *testing.T) {
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer server.Close()
	public, private, _ := ed25519.GenerateKey(rand.Reader)
	key, _ := ssh.NewPublicKey(public)
	server.AuthorizedKey = key.Marshal()
	ring := agent.NewKeyring()
	ring.Add(agent.AddedKey{PrivateKey: private})
	directory, e := os.MkdirTemp("/tmp", "ft-agent-")
	if e != nil {
		t.Fatal(e)
	}
	defer os.RemoveAll(directory)
	socket := filepath.Join(directory, "agent.sock")
	listener, e := net.Listen("unix", socket)
	if e != nil {
		t.Fatal(e)
	}
	defer listener.Close()
	t.Setenv("SSH_AUTH_SOCK", socket)
	go func() {
		for {
			c, e := listener.Accept()
			if e != nil {
				return
			}
			go func() { defer c.Close(); _ = agent.ServeAgent(ring, c) }()
		}
	}()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	c, e := Dial(ctx, launch.Connection{Protocol: "ssh", Host: "127.0.0.1", Port: server.Port, Username: "native-user", AuthMethod: "agent"}, Callbacks{HostKey: func(_ context.Context, _ string, fingerprint string) (bool, error) {
		return fingerprint == server.Fingerprint, nil
	}})
	if e != nil {
		t.Fatal(e)
	}
	defer c.Close()
	if _, e = c.List(ctx, "/"); e != nil {
		t.Fatal("agent-authenticated SFTP unavailable", e)
	}
}
