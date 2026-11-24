package desktop

import (
	"fmt"
	"forterm/native/internal/launch"
	"forterm/native/internal/testserver"
	"strings"
	"testing"
)

func TestSameSessionCreatesIndependentPageAndClosingOriginalKeepsClone(t *testing.T) {
	a, _, pump := formFixture(t)
	s, e := testserver.New()
	if e != nil {
		t.Fatal(e)
	}
	defer s.Close()
	a.trusted[fmt.Sprintf("127.0.0.1:%d", s.Port)] = s.Fingerprint
	c := launch.Connection{Protocol: "ssh", Host: "127.0.0.1", Port: s.Port, Username: "native-user", Password: "native-test-password", HasPassword: true}
	a.Open(launch.Plan{Entry: "manual", Connection: &c})
	pump(func() bool { return len(a.sessions) > 0 && a.sessions[0].shell != nil })
	original := a.sessions[0]
	clone := a.NewSameSession(original)
	if clone == nil {
		t.Fatal("clone missing")
	}
	pump(func() bool { return clone.shell != nil })
	if clone.Term == original.Term || clone.Tab == original.Tab || clone.Conn != original.Conn {
		t.Fatal("not independent page on same transport")
	}
	a.CloseWorkspace(original)
	if clone.Conn.Closed() || clone.Context.Err() != nil || s.Auth.Load() != 1 {
		t.Fatal("original close destroyed clone or authenticated again")
	}
	s.DenyNewShell.Store(true)
	denied := a.NewSameSession(clone)
	if denied == nil {
		t.Fatal("refused channel page should preserve error context")
	}
	pump(func() bool { return !denied.openingShell })
	if denied.shell != nil || clone.Conn.Closed() || s.Auth.Load() != 1 || !strings.Contains(denied.Status.Text, "shell") {
		t.Fatal("channel denial destroyed original or was hidden")
	}
}
