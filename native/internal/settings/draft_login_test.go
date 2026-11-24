package settings

import (
	"encoding/json"
	"strings"
	"testing"
)

func TestDraftLoginKeepsRealmSecretAndSelectedJumpAtomic(t *testing.T) {
	v := &gatedVault{values: map[string][]byte{}}
	s := Open(t.TempDir(), v)
	jump, e := s.UpsertProfile(Profile{Protocol: "ssh", Host: "jump.invalid", Port: 22, Username: "fixture"}, SecretUpdate{Action: "replace", Data: []byte("synthetic-jump")}, SecretUpdate{Action: "keep"}, false)
	if e != nil {
		t.Fatal(e)
	}
	p, e := s.UpsertProfile(Profile{Protocol: "sftp", Host: "target.invalid", Port: 22, Username: "fixture", JumpID: jump.ID}, SecretUpdate{Action: "replace", Data: []byte("synthetic-root")}, SecretUpdate{Action: "keep"}, false)
	if e != nil {
		t.Fatal(e)
	}
	rows, e := s.DraftLogin(p, DraftLoginOptions{Password: true})
	if e != nil || len(rows) != 2 || string(rows[0].Password) != "synthetic-root" || string(rows[1].Password) != "synthetic-jump" {
		t.Fatal("draft chain credentials incorrect")
	}
	raw, _ := json.Marshal(rows)
	if strings.Contains(string(raw), "synthetic-root") {
		t.Fatal("draft serialized credentials")
	}
	rows.Clear()
	p.Host = "edited.invalid"
	if _, e = s.DraftLogin(p, DraftLoginOptions{Password: true}); e == nil {
		t.Fatal("reused saved password for modified host")
	}
	rows, e = s.DraftLogin(p, DraftLoginOptions{})
	if e != nil || rows[0].Profile.HasPassword {
		t.Fatal("explicit draft inherited unrequested old password")
	}
	rows.Clear()
	if _, e = s.DraftLogin(p, DraftLoginOptions{RequireUnchanged: true}); e == nil {
		t.Fatal("save-connect realm race was accepted")
	}
}
