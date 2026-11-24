package connectcheck

import (
	"context"
	"forterm/native/internal/launch"
	"forterm/native/internal/sshconn"
	"forterm/native/internal/testserver"
	"strings"
	"testing"
	"time"
)

func TestCheckUsesAuthenticationOnlyAndReportsFileDenial(t *testing.T) {
	for _, deny := range []bool{false, true} {
		s, e := testserver.New()
		if e != nil {
			t.Fatal(e)
		}
		s.DenySFTP = deny
		s.DenyExec = true
		s.DenyShell = true
		ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
		result := Run(ctx, launch.Connection{Protocol: "sftp", Host: "127.0.0.1", Port: s.Port, Username: "native-user", Password: "native-test-password", HasPassword: true}, sshconn.Callbacks{HostKey: func(context.Context, string, string) (bool, error) { return true, nil }}, nil)
		cancel()
		s.Close()
		if !result.Authenticated || result.Success == deny || s.Auth.Load() != 1 || s.Exec.Load() != 0 {
			t.Fatalf("wrong auth/file capability result: %s", result.Stage)
		}
		if strings.Contains(result.Message, "native-test-password") {
			t.Fatal("secret leaked into result")
		}
	}
}
func TestCheckCancellationAndFingerprintRejection(t *testing.T) {
	s, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer s.Close()
	c := launch.Connection{Protocol: "ssh", Host: "127.0.0.1", Port: s.Port, Username: "native-user", Password: "native-test-password", HasPassword: true}
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	r := Run(ctx, c, sshconn.Callbacks{HostKey: func(context.Context, string, string) (bool, error) { return true, nil }}, nil)
	if r.Stage != "cancelled" || r.Success {
		t.Fatal("canceled check authenticated")
	}
	ctx, cancel = context.WithTimeout(context.Background(), 2*time.Second)
	defer cancel()
	r = Run(ctx, c, sshconn.Callbacks{HostKey: func(context.Context, string, string) (bool, error) { return false, nil }}, nil)
	if r.Stage != "fingerprint" || r.Authenticated {
		t.Fatal("fingerprint denial not distinguished")
	}
}
