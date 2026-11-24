package launch

import (
	"encoding/json"
	"strings"
	"testing"
	"time"
)

func TestOpaqueParametersAndRedaction(t *testing.T) {
	p := Parse([]string{"-newtab", "private title", "-url", "ssh://user%2Bname:p%2525%40word@[::1]:2222"}, "xftp.exe")
	if p.Error != "" || p.Connection.Protocol != "sftp" || p.Connection.Username != "user+name" || p.Connection.Password != "p%25@word" || p.Connection.Port != 2222 {
		t.Fatal("opaque argument mapping failed")
	}
	b, _ := json.Marshal(p.Report())
	for _, secret := range []string{"private title", "user+name", "p%25@word", "::1"} {
		if strings.Contains(string(b), secret) {
			t.Fatal("report leaked input")
		}
	}
}
func TestBadInputNeverConnects(t *testing.T) {
	for _, a := range [][]string{{"-url"}, {"-unknown", "secret"}, {"-url", "ssh://x:y@host/path"}, {"-url", "ssh://x:%zz@host"}, {"-url", "ssh://x:y@host", "ssh://x:y@other"}} {
		p := Parse(a, "forterm")
		if p.Error == "" || p.Connection != nil {
			t.Fatal("invalid arguments allowed")
		}
	}
}
func TestModesAndGate(t *testing.T) {
	p := Parse([]string{"-newwin", "-url", "ssh://u:p@localhost"}, "forterm")
	if !p.NewWindow || p.Error != "" {
		t.Fatal("newwin failed")
	}
	g := NewGate()
	p = Parse([]string{"ssh://u:p@localhost"}, "xshell")
	now := time.Now()
	for i := 0; i < 11; i++ {
		ok := g.Accept(p, now.Add(time.Duration(i)*time.Millisecond))
		if ok != (i == 0) {
			t.Fatal("burst dedup failed")
		}
	}
	p.Connection.Password = "different"
	if !g.Accept(p, now) {
		t.Fatal("different credential merged")
	}
}

func TestExplicitEmptyPasswordIsPreserved(t *testing.T) {
	with := Parse([]string{"ssh://u:@localhost"}, "xshell")
	without := Parse([]string{"ssh://u@localhost"}, "xshell")
	if !with.Connection.HasPassword || without.Connection.HasPassword {
		t.Fatal("empty and absent password collapsed")
	}
	gate := NewGate()
	now := time.Now()
	if !gate.Accept(with, now) || !gate.Accept(without, now) {
		t.Fatal("different authentication forms merged")
	}
}
