package settings

import (
	"bytes"
	"errors"
	"forterm/native/internal/vault"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

type memoryVault struct {
	items map[string][]byte
	fail  bool
}

func (v *memoryVault) Get(key string) ([]byte, error) {
	value, ok := v.items[key]
	if !ok {
		return nil, vault.ErrNotFound
	}
	return append([]byte(nil), value...), nil
}
func (v *memoryVault) Set(key string, value []byte) error {
	if v.fail {
		return errors.New("vault unavailable")
	}
	v.items[key] = append([]byte(nil), value...)
	return nil
}
func (v *memoryVault) Delete(key string) error { delete(v.items, key); return nil }
func TestProfilesPersistMetadataAndNeverExportSecrets(t *testing.T) {
	directory := t.TempDir()
	secrets := &memoryVault{items: map[string][]byte{}}
	store := Open(directory, secrets)
	profile := Profile{Title: "manual", Group: "servers/prod", Host: "host.example", Port: 22, Username: "user", Protocol: "ssh", Auth: "password"}
	if _, e := store.UpsertProfile(profile, SecretUpdate{Action: "replace", Data: []byte("synthetic-secret")}, SecretUpdate{}, true); e == nil || len(secrets.items) != 0 {
		t.Fatal("temporary credential saved")
	}
	p, e := store.UpsertProfile(profile, SecretUpdate{Action: "replace", Data: []byte("synthetic-secret")}, SecretUpdate{}, false)
	if e != nil {
		t.Fatal(e)
	}
	raw, e := os.ReadFile(filepath.Join(directory, "config.json"))
	if e != nil || bytes.Contains(raw, []byte("synthetic-secret")) {
		t.Fatal("plaintext secret reached metadata file")
	}
	exported, e := store.Export()
	if e != nil || bytes.Contains(exported, []byte("synthetic-secret")) || bytes.Contains(exported, []byte("hasPassword")) {
		t.Fatal("export contains secret or stale presence flags")
	}
	reopened := Open(directory, secrets)
	if len(reopened.Snapshot().Profiles) != 1 || !reopened.Snapshot().Profiles[0].HasPassword {
		t.Fatal("profile flags not persisted")
	}
	value, e := reopened.Secret(p.ID, "password")
	if e != nil || string(value) != "synthetic-secret" {
		t.Fatal("vault retrieval failed")
	}
	if e = reopened.Import(bytes.NewReader(exported)); e != nil {
		t.Fatal(e)
	}
	profiles := reopened.Snapshot().Profiles
	if len(profiles) != 2 || profiles[0].ID == profiles[1].ID || profiles[1].HasPassword {
		t.Fatal("import reused identity or secrets")
	}
	if e = reopened.Import(strings.NewReader(`{"version":1,"profiles":[{"password":"must not import"}]}`)); e == nil {
		t.Fatal("secret-bearing import accepted")
	}
}
func TestConfigFailureRollsBackSavedSecrets(t *testing.T) {
	directory := t.TempDir()
	secrets := &memoryVault{items: map[string][]byte{}}
	store := Open(directory, secrets)
	p, e := store.UpsertProfile(Profile{Host: "host", Port: 22, Username: "user", Protocol: "ssh"}, SecretUpdate{Action: "replace", Data: []byte("old")}, SecretUpdate{}, false)
	if e != nil {
		t.Fatal(e)
	}
	// A directory in place of the target prevents the final atomic rename, after
	// the vault update has succeeded; the old vault value must be restored.
	os.Remove(filepath.Join(directory, "config.json"))
	os.Mkdir(filepath.Join(directory, "config.json"), 0700)
	if _, e = store.UpsertProfile(p, SecretUpdate{Action: "replace", Data: []byte("new")}, SecretUpdate{}, false); e == nil {
		t.Fatal("metadata failure ignored")
	}
	value, _ := store.Secret(p.ID, "password")
	if string(value) != "old" {
		t.Fatal("credential not rolled back")
	}
}
func TestRFC6238AndSeedURI(t *testing.T) {
	raw := []byte("12345678901234567890")
	p := TotpParameters{Algorithm: "SHA1", Digits: 8, Period: 30}
	for _, vector := range []struct {
		at   int64
		want string
	}{{59, "94287082"}, {1111111109, "07081804"}, {1111111111, "14050471"}, {1234567890, "89005924"}} {
		code, _, e := GenerateTOTP(raw, p, time.Unix(vector.at, 0))
		if e != nil || code != vector.want {
			t.Fatal("RFC vector mismatch", code, e)
		}
	}
	seed, parameters, e := NormalizeTOTP("otpauth://totp/test?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&algorithm=SHA1&digits=8&period=30", TotpParameters{})
	if e != nil || !bytes.Equal(seed, raw) || parameters.Digits != 8 {
		t.Fatal("URI parse failed", e)
	}
}

func TestAccountVaultSeedRoundtripAndExport(t *testing.T) {
	secrets := &memoryVault{items: map[string][]byte{}}
	store := Open(t.TempDir(), secrets)
	a, e := store.UpsertAccount(Account{Name: "synthetic", PortalURL: "https://example.test", Username: "test", TotpEnabled: true, Totp: TotpParameters{Algorithm: "SHA512", Digits: 8, Period: 60}}, SecretUpdate{Action: "replace", Data: []byte("synthetic-password")}, SecretUpdate{Action: "replace", Data: []byte("otpauth://totp/public?secret=" + publicSeed)})
	if e != nil {
		t.Fatal(e)
	}
	raw, e := store.AccountSecret(a.ID, "totp")
	if e != nil || string(raw) != publicSeed {
		t.Fatal("saved canonical seed differs")
	}
	decoded, p, e := NormalizeTOTP(string(raw), a.Totp)
	code, _, e := GenerateTOTP(decoded, p, time.Unix(59, 0))
	if e != nil || code != "287082" {
		t.Fatal("stored key generates wrong standalone-default code", code, e)
	}
	exported, _ := store.Export()
	if bytes.Contains(exported, []byte(publicSeed)) || bytes.Contains(exported, []byte("synthetic-password")) {
		t.Fatal("account secrets exported")
	}
	if e = store.DeleteAccount(a.ID); e != nil || len(secrets.items) != 0 {
		t.Fatal("account vault cleanup failed", e)
	}
}

func TestKnownHostPersistenceAfterProfileMutation(t *testing.T) {
	s := Open(t.TempDir(), &memoryVault{items: map[string][]byte{}})
	_, e := s.UpsertProfile(Profile{Protocol: "ssh", Host: "test", Port: 22, Username: "user"}, SecretUpdate{}, SecretUpdate{}, false)
	if e != nil {
		t.Fatal(e)
	}
	if e = s.TrustHost("test:22", "SHA256:synthetic"); e != nil {
		t.Fatal(e)
	}
	if s.Snapshot().KnownHosts["test:22"] != "SHA256:synthetic" {
		t.Fatal("host trust did not survive clone")
	}
}
