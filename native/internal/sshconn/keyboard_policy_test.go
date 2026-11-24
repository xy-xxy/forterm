package sshconn

import (
	"context"
	"errors"
	"forterm/native/internal/launch"
	"forterm/native/internal/testserver"
	"golang.org/x/crypto/ssh"
	"strings"
	"testing"
	"time"
)

func TestKeyboardPasswordUsesInputOnceAndKeepsSecondFactorExplicit(t *testing.T) {
	for _, temporary := range []bool{false, true} {
		s, e := testserver.NewWithConfig(func(conf *ssh.ServerConfig) {
			conf.PasswordCallback = nil
			conf.KeyboardInteractiveCallback = func(_ ssh.ConnMetadata, ask ssh.KeyboardInteractiveChallenge) (*ssh.Permissions, error) {
				a, e := ask("", "", []string{"Password:"}, []bool{false})
				if e != nil || len(a) != 1 || a[0] != "native-test-password" {
					return nil, errors.New("rejected")
				}
				a, e = ask("", "Verification required", []string{"One-time code:"}, []bool{false})
				if e != nil || len(a) != 1 || a[0] != "synthetic-code" {
					return nil, errors.New("rejected")
				}
				return nil, nil
			}
		})
		if e != nil {
			t.Fatal(e)
		}
		asked := 0
		ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
		c, e := Dial(ctx, launch.Connection{Protocol: "ssh", Host: "127.0.0.1", Port: s.Port, Username: "native-user", Password: "native-test-password", HasPassword: true, Temporary: temporary}, Callbacks{HostKey: func(context.Context, string, string) (bool, error) { return true, nil }, Challenge: func(_ context.Context, title string, q []string, echo []bool) ([]string, error) {
			asked++
			if !strings.Contains(title, "服务器交互认证") {
				t.Error("server authentication not distinguished")
			}
			if q[0] == "Password:" {
				return []string{"native-test-password"}, nil
			}
			return []string{"synthetic-code"}, nil
		}})
		if e != nil {
			t.Fatal(e)
		}
		c.Close()
		cancel()
		s.Close()
		expected := 1
		if temporary {
			expected = 2
		}
		if asked != expected {
			t.Fatal("password reused or second factor bypassed")
		}
	}
}
func TestPasswordRejectionDoesNotAutoReplayInputIntoKeyboard(t *testing.T) {
	s, e := testserver.NewWithConfig(func(conf *ssh.ServerConfig) {
		conf.PasswordCallback = func(ssh.ConnMetadata, []byte) (*ssh.Permissions, error) { return nil, errors.New("rejected") }
		conf.KeyboardInteractiveCallback = func(_ ssh.ConnMetadata, ask ssh.KeyboardInteractiveChallenge) (*ssh.Permissions, error) {
			a, e := ask("", "", []string{"Password:"}, []bool{false})
			if e != nil || len(a) != 1 || a[0] != "synthetic-replacement" {
				return nil, errors.New("rejected")
			}
			return nil, nil
		}
	})
	if e != nil {
		t.Fatal(e)
	}
	defer s.Close()
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	asked := 0
	c, e := Dial(ctx, launch.Connection{Protocol: "ssh", Host: "127.0.0.1", Port: s.Port, Username: "native-user", Password: "synthetic-first", HasPassword: true}, Callbacks{HostKey: func(context.Context, string, string) (bool, error) { return true, nil }, Challenge: func(_ context.Context, title string, _ []string, _ []bool) ([]string, error) {
		asked++
		if !strings.Contains(title, "密码已提交") {
			t.Error("repeat is not identified as server request")
		}
		return []string{"synthetic-replacement"}, nil
	}})
	if e != nil {
		t.Fatal(e)
	}
	c.Close()
	if asked != 1 {
		t.Fatal("initial credential was replayed")
	}
}
