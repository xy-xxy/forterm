package settings

import (
	"encoding/json"
	"errors"
	"forterm/native/internal/vault"
	"strings"
	"sync"
	"testing"
	"time"
)

type gatedVault struct {
	mu               sync.Mutex
	values           map[string][]byte
	pause            bool
	entered, release chan struct{}
}

func (v *gatedVault) Get(k string) ([]byte, error) {
	v.mu.Lock()
	defer v.mu.Unlock()
	p, ok := v.values[k]
	if !ok {
		return nil, vault.ErrNotFound
	}
	return append([]byte(nil), p...), nil
}
func (v *gatedVault) Set(k string, p []byte) error {
	v.mu.Lock()
	v.values[k] = append([]byte(nil), p...)
	pause := v.pause
	v.pause = false
	v.mu.Unlock()
	if pause {
		close(v.entered)
		<-v.release
	}
	return nil
}
func (v *gatedVault) Delete(k string) error {
	v.mu.Lock()
	defer v.mu.Unlock()
	delete(v.values, k)
	return nil
}
func TestViewDoesNotWaitForVaultButLoginUsesCommittedRealmCredentialPair(t *testing.T) {
	v := &gatedVault{values: map[string][]byte{}, entered: make(chan struct{}), release: make(chan struct{})}
	s := Open(t.TempDir(), v)
	p, e := s.UpsertProfile(Profile{Protocol: "ssh", Host: "old.invalid", Port: 22, Username: "fixture"}, SecretUpdate{Action: "replace", Data: []byte("synthetic-old")}, SecretUpdate{Action: "keep"}, false)
	if e != nil {
		t.Fatal(e)
	}
	v.mu.Lock()
	v.pause = true
	v.mu.Unlock()
	updated := p
	updated.Host = "new.invalid"
	mutation := make(chan error, 1)
	go func() {
		_, e := s.UpsertProfile(updated, SecretUpdate{Action: "replace", Data: []byte("synthetic-new")}, SecretUpdate{Action: "keep"}, false)
		mutation <- e
	}()
	select {
	case <-v.entered:
	case <-time.After(time.Second):
		t.Fatal("synthetic credential mutation did not pause")
	}
	released := false
	defer func() {
		if !released {
			close(v.release)
		}
	}()
	view := make(chan Data, 1)
	go func() { view <- s.ViewSnapshot() }()
	select {
	case d := <-view:
		if d.Profiles[0].Host != "old.invalid" {
			t.Fatal("uncommitted metadata leaked into view")
		}
	case <-time.After(time.Second):
		t.Fatal("UI view waited behind credential IO")
	}
	logins := make(chan LoginProfiles, 1)
	errs := make(chan error, 1)
	go func() { rows, e := s.LoginChain(p.ID); logins <- rows; errs <- e }()
	select {
	case <-logins:
		t.Fatal("login read credentials during a realm mutation")
	case <-time.After(20 * time.Millisecond):
	}
	close(v.release)
	released = true
	if e := <-mutation; e != nil {
		t.Fatal(e)
	}
	rows := <-logins
	defer rows.Clear()
	if e := <-errs; e != nil || rows[0].Profile.Host != "new.invalid" || string(rows[0].Password) != "synthetic-new" {
		t.Fatal("realm and credential did not commit coherently", e)
	}
	raw, e := json.Marshal(rows)
	if e != nil || strings.Contains(string(raw), "synthetic-new") {
		t.Fatal("ephemeral credentials entered JSON")
	}
	if s.ViewSnapshot().Profiles[0].Host != "new.invalid" {
		t.Fatal("committed view not published")
	}
}
func TestLoginSnapshotEmptyPasswordAndMissingChain(t *testing.T) {
	v := &gatedVault{values: map[string][]byte{}}
	s := Open(t.TempDir(), v)
	p, e := s.UpsertProfile(Profile{Protocol: "ssh", Host: "empty.invalid", Port: 22, Username: "fixture"}, SecretUpdate{Action: "replace", Data: []byte{}}, SecretUpdate{Action: "keep"}, false)
	if e != nil {
		t.Fatal(e)
	}
	rows, e := s.LoginChain(p.ID)
	if e != nil || !rows[0].Profile.HasPassword || rows[0].PasswordError != nil || len(rows[0].Password) != 0 {
		t.Fatal("explicit empty password lost", e)
	}
	rows.Clear()
	if _, e = s.LoginChain("missing"); e == nil {
		t.Fatal("missing identity accepted")
	}
	if _, _, e = s.AccountWithSecret("missing", "unknown"); e == nil || errors.Is(e, vault.ErrNotFound) {
		t.Fatal("unsupported account secret kind not rejected")
	}
}
