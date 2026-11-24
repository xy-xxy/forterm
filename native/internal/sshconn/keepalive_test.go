package sshconn

import (
	"context"
	"forterm/native/internal/launch"
	"forterm/native/internal/testserver"
	"testing"
	"time"
)

func heartbeatFixture(t *testing.T, options KeepAlive) (*Connection, *testserver.Server) {
	t.Helper()
	server, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	t.Cleanup(server.Close)
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	c, e := Dial(ctx, launch.Connection{Protocol: "sftp", Host: "127.0.0.1", Port: server.Port, Username: "native-user", Password: "native-test-password"}, Callbacks{KeepAlive: &options, HostKey: func(_ context.Context, _ string, fingerprint string) (bool, error) {
		return fingerprint == server.Fingerprint, nil
	}})
	if e != nil {
		t.Fatal(e)
	}
	t.Cleanup(c.Close)
	return c, server
}
func TestConfiguredHeartbeatOnlyProbesOpenedSFTP(t *testing.T) {
	c, server := heartbeatFixture(t, KeepAlive{Enabled: true, IntervalSeconds: 5, SFTPProbe: true})
	time.Sleep(5250 * time.Millisecond)
	if server.Keepalive.Load() < 1 || server.SFTP.Load() != 0 || server.RealPath.Load() != 0 {
		t.Fatal("heartbeat opened an unrequested file channel")
	}
	if _, e := c.List(context.Background(), "/"); e != nil {
		t.Fatal(e)
	}
	until := time.Now().Add(6 * time.Second)
	for server.RealPath.Load() == 0 && time.Now().Before(until) {
		time.Sleep(20 * time.Millisecond)
	}
	if server.RealPath.Load() != 1 {
		t.Fatal("opened SFTP not probed exactly once")
	}
	input, _ := server.Snapshot()
	if len(input) != 0 || server.Auth.Load() != 1 {
		t.Fatal("heartbeat injected input or reauthenticated")
	}
	c.Close()
	select {
	case <-c.done:
	case <-time.After(time.Second):
		t.Fatal("heartbeat timer not stopped")
	}
}
func TestDisabledHeartbeatHasNoRequests(t *testing.T) {
	c, server := heartbeatFixture(t, KeepAlive{Enabled: false, IntervalSeconds: 5, SFTPProbe: true})
	if _, e := c.List(context.Background(), "/"); e != nil {
		t.Fatal(e)
	}
	time.Sleep(5250 * time.Millisecond)
	if server.Keepalive.Load() != 0 || server.RealPath.Load() != 0 {
		t.Fatal("disabled heartbeat sent requests")
	}
}
