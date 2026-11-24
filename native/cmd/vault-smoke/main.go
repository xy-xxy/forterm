// Exercises only a newly-created synthetic OS vault item; never enumerates or
// reads existing user credentials. No secret values are printed or persisted.
package main

import (
	"bytes"
	"crypto/rand"
	"encoding/json"
	"fmt"
	"forterm/native/internal/settings"
	"forterm/native/internal/vault"
	"os"
	"runtime"
)

func main() {
	result := map[string]any{"platform": runtime.GOOS, "scope": "new synthetic OS-vault item only; no plaintext-file fallback"}
	defer func() {
		os.MkdirAll("evidence", 0755)
		raw, _ := json.MarshalIndent(result, "", "  ")
		os.WriteFile("evidence/system-vault-smoke.json", raw, 0600)
	}()
	v := vault.New()
	key := "synthetic-smoke/" + settings.ID()
	secret := make([]byte, 24)
	rand.Read(secret)
	defer func() {
		for i := range secret {
			secret[i] = 0
		}
	}()
	if e := v.Set(key, secret); e != nil {
		result["error"] = e.Error()
		fmt.Fprintln(os.Stderr, e)
		return
	}
	defer v.Delete(key)
	raw, e := v.Get(key)
	if e != nil || !bytes.Equal(raw, secret) {
		result["error"] = "OS vault roundtrip failed"
		return
	}
	for i := range raw {
		raw[i] = 0
	}
	if e = v.Delete(key); e != nil {
		result["error"] = e.Error()
		return
	}
	if _, e = v.Get(key); e == nil {
		result["error"] = "deleted synthetic item still retrievable"
		return
	}
	result["passed"] = true
	result["setGetDelete"] = true
}
