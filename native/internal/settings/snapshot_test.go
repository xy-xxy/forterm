package settings

import (
	"reflect"
	"testing"
)

func TestSnapshotValueParityAndDeepIsolation(t *testing.T) {
	for _, d := range []Data{{}, defaults(), snapshotFixture(100)} {
		d.Accounts = append(d.Accounts, Account{ID: "fixture-account", Name: "fixture", Totp: TotpParameters{Algorithm: "SHA256", Digits: 8, Period: 45}, HasSeed: true})
		got := clone(d)
		old := legacyJSONSnapshot(d)
		if !reflect.DeepEqual(got, old) {
			t.Fatal("snapshot changed valid metadata or nil/empty semantics")
		}
		got.Accounts[0].Totp.Period = 60
		got.KnownHosts["added"] = "fixture"
		if len(got.Profiles) > 0 {
			got.Profiles[0].Title = "changed"
		}
		if d.Accounts[0].Totp.Period != 45 || d.KnownHosts["added"] != "" || len(d.Profiles) > 0 && d.Profiles[0].Title == "changed" {
			t.Fatal("snapshot aliases mutable store metadata")
		}
	}
}
